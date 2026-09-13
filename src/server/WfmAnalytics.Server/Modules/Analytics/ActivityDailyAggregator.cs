using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using WfmAnalytics.Server.Database;

namespace WfmAnalytics.Server.Modules.Analytics;

public sealed class ActivityDailyAggregator(PostgresDatabase database)
{
    public async Task<int> ProcessDirtyDaysAsync(CancellationToken token = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(token);

        var targets = new List<DirtyTarget>();
        await using (var command = new NpgsqlCommand("""
            SELECT DISTINCT enrollment.team_id, enrollment.team_name, enrollment.timezone, dirty.report_date
            FROM ingestion.activity_dirty_days dirty
            JOIN platform.development_enrollments enrollment ON enrollment.enrollment_id=dirty.enrollment_id
            ORDER BY dirty.report_date, enrollment.team_id
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                targets.Add(new DirtyTarget(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3))));
            }
        }

        foreach (var target in targets)
        {
            await RecomputeAsync(connection, target, token);
        }

        return targets.Count;
    }

    private static async Task RecomputeAsync(NpgsqlConnection connection, DirtyTarget target, CancellationToken token)
    {
        var employees = new Dictionary<string, EmployeeAccumulator>(StringComparer.Ordinal);
        DateTimeOffset? lastReceivedAt = null;
        var dayStart = new DateTimeOffset(target.ReportDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        await using (var command = new NpgsqlCommand("""
            SELECT enrollment.employee_id,
                   enrollment.display_name,
                   envelope.bucket_start,
                   envelope.bucket_end,
                   envelope.received_at,
                   envelope.payload::text
            FROM ingestion.activity_envelopes envelope
            JOIN platform.development_enrollments enrollment ON enrollment.enrollment_id=envelope.enrollment_id
            WHERE enrollment.team_id=$1
              AND envelope.bucket_start >= $2
              AND envelope.bucket_start < $3
            ORDER BY enrollment.employee_id, envelope.bucket_start, envelope.sequence
            """, connection))
        {
            command.Parameters.AddWithValue(target.TeamId);
            command.Parameters.AddWithValue(dayStart);
            command.Parameters.AddWithValue(dayEnd);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var employeeId = reader.GetString(0);
                var employee = employees.TryGetValue(employeeId, out var existing)
                    ? existing
                    : employees[employeeId] = new EmployeeAccumulator(employeeId, reader.GetString(1));
                var bucketStart = reader.GetFieldValue<DateTimeOffset>(2);
                var bucketEnd = reader.GetFieldValue<DateTimeOffset>(3);
                var receivedAt = reader.GetFieldValue<DateTimeOffset>(4);
                lastReceivedAt = lastReceivedAt is null || receivedAt > lastReceivedAt ? receivedAt : lastReceivedAt;
                employee.AddEnvelope(bucketStart, bucketEnd, reader.GetString(5));
            }
        }

        var existingRevision = 0L;
        await using (var revision = new NpgsqlCommand("SELECT report_revision FROM analytics.activity_daily_reports WHERE team_id=$1 AND report_date=$2", connection))
        {
            revision.Parameters.AddWithValue(target.TeamId);
            revision.Parameters.AddWithValue(target.ReportDate);
            var value = await revision.ExecuteScalarAsync(token);
            if (value is long current)
            {
                existingRevision = current;
            }
        }

        var computedAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            schema_version = "1.0",
            synthetic = false,
            team_id = target.TeamId,
            team_name = target.TeamName,
            report_date = target.ReportDate.ToString("yyyy-MM-dd"),
            timezone = target.Timezone,
            report_revision = existingRevision + 1,
            definition_version = "1.0",
            computed_at = computedAt,
            units = "seconds",
            freshness = new
            {
                status = lastReceivedAt is null ? "unknown" : "fresh",
                last_received_at = lastReceivedAt
            },
            employees = employees.Values
                .OrderBy(employee => employee.EmployeeId, StringComparer.Ordinal)
                .Select(employee => employee.ToReportRow())
                .ToArray()
        });

        await using (var upsert = new NpgsqlCommand("""
            INSERT INTO analytics.activity_daily_reports(team_id,report_date,payload,report_revision,computed_at)
            VALUES ($1,$2,$3,$4,$5)
            ON CONFLICT (team_id,report_date) DO UPDATE
            SET payload=EXCLUDED.payload,
                report_revision=EXCLUDED.report_revision,
                computed_at=EXCLUDED.computed_at
            """, connection))
        {
            upsert.Parameters.AddWithValue(target.TeamId);
            upsert.Parameters.AddWithValue(target.ReportDate);
            upsert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload);
            upsert.Parameters.AddWithValue(existingRevision + 1);
            upsert.Parameters.AddWithValue(computedAt);
            await upsert.ExecuteNonQueryAsync(token);
        }

        await using (var clear = new NpgsqlCommand("""
            DELETE FROM ingestion.activity_dirty_days dirty
            USING platform.development_enrollments enrollment
            WHERE enrollment.enrollment_id=dirty.enrollment_id
              AND enrollment.team_id=$1
              AND dirty.report_date=$2
            """, connection))
        {
            clear.Parameters.AddWithValue(target.TeamId);
            clear.Parameters.AddWithValue(target.ReportDate);
            await clear.ExecuteNonQueryAsync(token);
        }
    }

    private sealed class EmployeeAccumulator(string employeeId, string displayName)
    {
        public string EmployeeId { get; } = employeeId;
        private string DisplayName { get; } = displayName;
        private int ActiveSeconds { get; set; }
        private int InactiveSeconds { get; set; }
        private int ApprovedNonDesktopSeconds { get; set; }
        private int ConflictSeconds { get; set; }
        private int UnknownSeconds { get; set; }

        public void AddEnvelope(DateTimeOffset bucketStart, DateTimeOffset bucketEnd, string rawJson)
        {
            var bucketMilliseconds = Math.Max(0, (int)Math.Round((bucketEnd - bucketStart).TotalMilliseconds));
            var explainedMilliseconds = 0;
            using var document = JsonDocument.Parse(rawJson);
            foreach (var slice in document.RootElement.GetProperty("slices").EnumerateArray())
            {
                var start = slice.GetProperty("start_offset_ms").GetInt32();
                var end = slice.GetProperty("end_offset_ms").GetInt32();
                var milliseconds = Math.Max(0, end - start);
                explainedMilliseconds += milliseconds;
                switch (slice.GetProperty("state").GetString())
                {
                    case "active":
                        ActiveSeconds += ToSeconds(milliseconds);
                        break;
                    case "inactive":
                    case "locked":
                        InactiveSeconds += ToSeconds(milliseconds);
                        break;
                    case "detail_unavailable":
                        UnknownSeconds += ToSeconds(milliseconds);
                        break;
                    default:
                        UnknownSeconds += ToSeconds(milliseconds);
                        break;
                }
            }

            if (explainedMilliseconds < bucketMilliseconds)
            {
                UnknownSeconds += ToSeconds(bucketMilliseconds - explainedMilliseconds);
            }
        }

        public object ToReportRow()
        {
            var eligibleSeconds = ActiveSeconds + InactiveSeconds + ApprovedNonDesktopSeconds + ConflictSeconds + UnknownSeconds;
            var coverage = eligibleSeconds == 0 ? (double?)null : (double)(ActiveSeconds + InactiveSeconds) / eligibleSeconds;
            return new
            {
                employee_id = EmployeeId,
                display_name = DisplayName,
                eligible_seconds = eligibleSeconds,
                categories = new
                {
                    active_seconds = ActiveSeconds,
                    inactive_seconds = InactiveSeconds,
                    approved_non_desktop_seconds = ApprovedNonDesktopSeconds,
                    conflict_seconds = ConflictSeconds,
                    unknown_seconds = UnknownSeconds
                },
                telemetry_coverage = new
                {
                    value = coverage,
                    unavailable_reason = coverage is null ? "no_eligible_time" : null
                },
                output = new
                {
                    completed_count = (int?)null,
                    unavailable_reason = "source_not_connected"
                }
            };
        }

        private static int ToSeconds(int milliseconds) => Math.Max(0, (int)Math.Round(milliseconds / 1000.0, MidpointRounding.AwayFromZero));
    }

    private sealed record DirtyTarget(string TeamId, string TeamName, string Timezone, DateOnly ReportDate);
}
