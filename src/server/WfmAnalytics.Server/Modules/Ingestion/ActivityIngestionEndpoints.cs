using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using WfmAnalytics.Server.Database;
using WfmAnalytics.Server.Modules.Analytics;

namespace WfmAnalytics.Server.Modules.Ingestion;

public static class ActivityIngestionEndpoints
{
    private const string EnrollmentHeader = "X-WFM-Enrollment-Id";
    private const int MaxBatchBytes = 512 * 1024;
    private static readonly TimeSpan ReceiptRetention = TimeSpan.FromDays(37);
    private static readonly TimeSpan LateWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan FutureWindow = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapActivityIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/activity/batches", async (HttpRequest request, PostgresDatabase database, ActivityDailyAggregator aggregator, CancellationToken token) =>
        {
            if (!Guid.TryParse(request.Headers[EnrollmentHeader], out var enrollmentId))
            {
                return Results.BadRequest(new { code = "missing_or_invalid_enrollment" });
            }

            using var body = await TryReadJsonBodyAsync(request, token);
            if (body is null)
            {
                return Results.BadRequest(new { code = "invalid_json_or_oversized_batch" });
            }

            var root = body.RootElement;
            if (!TryReadBatch(root, out var batchId, out var events, out var error))
            {
                return Results.BadRequest(new { code = error });
            }

            await using var connection = database.CreateConnection();
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            var outcomes = new List<ActivityOutcome>();
            foreach (var activityEvent in events)
            {
                outcomes.Add(await IngestEventAsync(connection, transaction, enrollmentId, activityEvent, token));
            }

            await transaction.CommitAsync(token);
            if (outcomes.Any(outcome => outcome.AcceptedNew))
            {
                await aggregator.ProcessDirtyDaysAsync(token);
            }

            return Results.Ok(new ActivityBatchResponse("1.0", batchId, outcomes));
        }).AllowAnonymous();

        endpoints.MapPost("/api/v1/device/health", async (HttpRequest request, PostgresDatabase database, CancellationToken token) =>
        {
            if (!Guid.TryParse(request.Headers[EnrollmentHeader], out var enrollmentId))
            {
                return Results.BadRequest(new { code = "missing_or_invalid_enrollment" });
            }

            using var body = await TryReadJsonBodyAsync(request, token);
            if (body is null)
            {
                return Results.BadRequest(new { code = "invalid_json_or_oversized_health" });
            }

            if (!TryReadHealth(body.RootElement, out var report, out var error))
            {
                return Results.BadRequest(new { code = error });
            }

            await using var connection = database.CreateConnection();
            await connection.OpenAsync(token);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO ingestion.device_health_reports(
                    enrollment_id,
                    checked_at,
                    agent_version,
                    queue_pending_payload_count,
                    queue_pending_payload_bytes,
                    queue_loss_record_count,
                    queue_lost_payload_count,
                    session_state,
                    warnings,
                    payload)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
                """, connection);
            insert.Parameters.AddWithValue(enrollmentId);
            insert.Parameters.AddWithValue(report.CheckedAt);
            insert.Parameters.AddWithValue(report.AgentVersion);
            insert.Parameters.AddWithValue(report.QueuePendingPayloadCount);
            insert.Parameters.AddWithValue(report.QueuePendingPayloadBytes);
            insert.Parameters.AddWithValue(report.QueueLossRecordCount);
            insert.Parameters.AddWithValue(report.QueueLostPayloadCount);
            insert.Parameters.AddWithValue((object?)report.SessionState ?? DBNull.Value);
            insert.Parameters.AddWithValue(report.Warnings);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, body.RootElement.GetRawText());
            await insert.ExecuteNonQueryAsync(token);

            return Results.Ok(new DeviceHealthResponse("1.0", enrollmentId, "accepted"));
        }).AllowAnonymous();

        return endpoints;
    }

    private static async Task<JsonDocument?> TryReadJsonBodyAsync(HttpRequest request, CancellationToken token)
    {
        if (request.ContentLength is > MaxBatchBytes)
        {
            return null;
        }

        try
        {
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, token);
            if (buffer.Length is 0 or > MaxBatchBytes)
            {
                return null;
            }

            buffer.Position = 0;
            return await JsonDocument.ParseAsync(buffer, cancellationToken: token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadBatch(JsonElement root, out Guid batchId, out ActivityEvent[] events, out string error)
    {
        batchId = Guid.Empty;
        events = [];
        error = "invalid_batch";
        if (!root.TryGetProperty("schema_version", out var schema) || schema.GetString() != "1.0")
        {
            error = "unsupported_schema_version";
            return false;
        }

        if (!TryString(root, "agent_version", out _) || !TryString(root, "policy_version", out _))
        {
            error = "missing_batch_metadata";
            return false;
        }

        if (!root.TryGetProperty("batch_id", out var batchElement) || !Guid.TryParse(batchElement.GetString(), out batchId))
        {
            error = "invalid_batch_id";
            return false;
        }

        if (!root.TryGetProperty("events", out var eventArray) || eventArray.ValueKind != JsonValueKind.Array)
        {
            error = "missing_events";
            return false;
        }

        var parsed = new List<ActivityEvent>();
        foreach (var element in eventArray.EnumerateArray())
        {
            if (!TryReadEvent(element, out var activityEvent, out error))
            {
                return false;
            }

            parsed.Add(activityEvent);
        }

        if (parsed.Count is < 1 or > 200)
        {
            error = "event_count_out_of_range";
            return false;
        }

        events = parsed.ToArray();
        return true;
    }

    private static bool TryReadEvent(JsonElement element, out ActivityEvent activityEvent, out string error)
    {
        activityEvent = default;
        error = "invalid_event";
        if (!TryGuid(element, "event_id", out var eventId)
            || !TryGuid(element, "collector_instance_id", out var collectorInstanceId)
            || !TryString(element, "boot_id", out var bootId)
            || !TryString(element, "session_id", out var sessionId)
            || !TryInt64(element, "sequence", out var sequence)
            || !TryDateTimeOffset(element, "bucket_start", out var bucketStart)
            || !TryDateTimeOffset(element, "bucket_end", out var bucketEnd)
            || !element.TryGetProperty("coarsened", out var coarsenedElement)
            || coarsenedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !element.TryGetProperty("slices", out var slices)
            || slices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (sequence < 0 || bucketEnd <= bucketStart || bucketEnd - bucketStart > TimeSpan.FromMinutes(1))
        {
            error = "invalid_event_time";
            return false;
        }

        if (bucketEnd < now.Subtract(LateWindow))
        {
            error = "event_too_old";
            return false;
        }

        if (bucketEnd > now.Add(FutureWindow))
        {
            error = "event_from_future";
            return false;
        }

        if (slices.GetArrayLength() is < 1 or > 60)
        {
            error = "invalid_slice_count";
            return false;
        }

        var bucketMilliseconds = (int)Math.Round((bucketEnd - bucketStart).TotalMilliseconds);
        var previousEnd = 0;
        foreach (var slice in slices.EnumerateArray())
        {
            if (!slice.TryGetProperty("start_offset_ms", out var startElement)
                || !startElement.TryGetInt32(out var start)
                || !slice.TryGetProperty("end_offset_ms", out var endElement)
                || !endElement.TryGetInt32(out var end)
                || !slice.TryGetProperty("state", out var stateElement)
                || !IsAcceptedState(stateElement.GetString())
                || start < previousEnd
                || end <= start
                || end > bucketMilliseconds)
            {
                error = "invalid_slice_bounds";
                return false;
            }

            previousEnd = end;
        }

        var raw = element.GetRawText();
        activityEvent = new ActivityEvent(eventId, bootId, sessionId, collectorInstanceId, sequence, bucketStart, bucketEnd, coarsenedElement.GetBoolean(), raw, Sha256(raw));
        return true;
    }

    private static bool TryReadHealth(JsonElement root, out DeviceHealthReport report, out string error)
    {
        report = default;
        error = "invalid_device_health";
        if (!TryDateTimeOffset(root, "checked_at", out var checkedAt)
            || !TryString(root, "agent_version", out var agentVersion)
            || !root.TryGetProperty("queue", out var queue)
            || queue.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryInt32(queue, "pending_payload_count", out var pendingCount)
            || !TryInt64(queue, "pending_payload_bytes", out var pendingBytes)
            || !TryInt32(queue, "loss_record_count", out var lossRecordCount)
            || !TryInt32(queue, "lost_payload_count", out var lostPayloadCount)
            || pendingCount < 0
            || pendingBytes < 0
            || lossRecordCount < 0
            || lostPayloadCount < 0)
        {
            error = "invalid_queue_health";
            return false;
        }

        string? sessionState = null;
        if (root.TryGetProperty("session", out var session)
            && session.ValueKind == JsonValueKind.Object
            && session.TryGetProperty("connect_state", out var state)
            && state.ValueKind == JsonValueKind.String)
        {
            sessionState = state.GetString();
        }

        var warnings = Array.Empty<string>();
        if (root.TryGetProperty("warnings", out var warningElement) && warningElement.ValueKind == JsonValueKind.Array)
        {
            warnings = warningElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Take(50)
                .ToArray();
        }

        report = new DeviceHealthReport(checkedAt, agentVersion, pendingCount, pendingBytes, lossRecordCount, lostPayloadCount, sessionState, warnings);
        return true;
    }

    private static async Task<ActivityOutcome> IngestEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid enrollmentId, ActivityEvent activityEvent, CancellationToken token)
    {
        await using (var existing = new NpgsqlCommand("SELECT payload_checksum FROM ingestion.ingestion_receipts WHERE enrollment_id=$1 AND event_id=$2", connection, transaction))
        {
            existing.Parameters.AddWithValue(enrollmentId);
            existing.Parameters.AddWithValue(activityEvent.EventId);
            var checksum = await existing.ExecuteScalarAsync(token) as string;
            if (checksum == activityEvent.PayloadChecksum)
            {
                return new ActivityOutcome(activityEvent.EventId, "already_accepted", null, false);
            }

            if (checksum is not null)
            {
                return new ActivityOutcome(activityEvent.EventId, "rejected", "event_checksum_conflict", false);
            }
        }

        await using (var sequence = new NpgsqlCommand("SELECT event_id, payload_checksum FROM ingestion.ingestion_receipts WHERE enrollment_id=$1 AND collector_instance_id=$2 AND sequence=$3", connection, transaction))
        {
            sequence.Parameters.AddWithValue(enrollmentId);
            sequence.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            sequence.Parameters.AddWithValue(activityEvent.Sequence);
            await using var reader = await sequence.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var existingEventId = reader.GetGuid(0);
                var existingChecksum = reader.GetString(1);
                await reader.CloseAsync();
                return existingEventId == activityEvent.EventId && existingChecksum == activityEvent.PayloadChecksum
                    ? new ActivityOutcome(activityEvent.EventId, "already_accepted", null, false)
                    : new ActivityOutcome(activityEvent.EventId, "rejected", "sequence_conflict", false);
            }
        }

        await using (var receipt = new NpgsqlCommand("INSERT INTO ingestion.ingestion_receipts(enrollment_id,event_id,collector_instance_id,sequence,payload_checksum,receipt_expires_at) VALUES ($1,$2,$3,$4,$5,$6)", connection, transaction))
        {
            receipt.Parameters.AddWithValue(enrollmentId);
            receipt.Parameters.AddWithValue(activityEvent.EventId);
            receipt.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            receipt.Parameters.AddWithValue(activityEvent.Sequence);
            receipt.Parameters.AddWithValue(activityEvent.PayloadChecksum);
            receipt.Parameters.AddWithValue(DateTimeOffset.UtcNow.Add(ReceiptRetention));
            await receipt.ExecuteNonQueryAsync(token);
        }

        await using (var insert = new NpgsqlCommand("INSERT INTO ingestion.activity_envelopes(enrollment_id,event_id,boot_id,session_id,collector_instance_id,sequence,bucket_start,bucket_end,coarsened,payload,payload_checksum) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)", connection, transaction))
        {
            insert.Parameters.AddWithValue(enrollmentId);
            insert.Parameters.AddWithValue(activityEvent.EventId);
            insert.Parameters.AddWithValue(activityEvent.BootId);
            insert.Parameters.AddWithValue(activityEvent.SessionId);
            insert.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            insert.Parameters.AddWithValue(activityEvent.Sequence);
            insert.Parameters.AddWithValue(activityEvent.BucketStart);
            insert.Parameters.AddWithValue(activityEvent.BucketEnd);
            insert.Parameters.AddWithValue(activityEvent.Coarsened);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, activityEvent.RawJson);
            insert.Parameters.AddWithValue(activityEvent.PayloadChecksum);
            await insert.ExecuteNonQueryAsync(token);
        }

        await using (var dirty = new NpgsqlCommand("""
            INSERT INTO ingestion.activity_dirty_days(enrollment_id,report_date,dirty_generation,last_event_end,updated_at)
            SELECT $1, $2, 1, $3, CURRENT_TIMESTAMP
            WHERE EXISTS (SELECT 1 FROM platform.development_enrollments WHERE enrollment_id=$1)
            ON CONFLICT (enrollment_id,report_date) DO UPDATE
            SET dirty_generation=ingestion.activity_dirty_days.dirty_generation + 1,
                last_event_end=GREATEST(ingestion.activity_dirty_days.last_event_end, EXCLUDED.last_event_end),
                updated_at=CURRENT_TIMESTAMP
            """, connection, transaction))
        {
            dirty.Parameters.AddWithValue(enrollmentId);
            dirty.Parameters.AddWithValue(DateOnly.FromDateTime(activityEvent.BucketStart.UtcDateTime.Date));
            dirty.Parameters.AddWithValue(activityEvent.BucketEnd);
            await dirty.ExecuteNonQueryAsync(token);
        }

        return new ActivityOutcome(activityEvent.EventId, "accepted", null, true);
    }

    private static bool TryGuid(JsonElement element, string property, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(property, out var child) && Guid.TryParse(child.GetString(), out value);
    }

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        return element.TryGetProperty(property, out var child) && child.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = child.GetString() ?? "");
    }

    private static bool TryInt64(JsonElement element, string property, out long value)
    {
        value = 0;
        return element.TryGetProperty(property, out var child) && child.TryGetInt64(out value);
    }

    private static bool TryInt32(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out var child) && child.TryGetInt32(out value);
    }

    private static bool TryDateTimeOffset(JsonElement element, string property, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(property, out var child) && DateTimeOffset.TryParse(child.GetString(), out value);
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static bool IsAcceptedState(string? value) => value is "active" or "inactive" or "locked" or "detail_unavailable";

    private readonly record struct ActivityEvent(Guid EventId, string BootId, string SessionId, Guid CollectorInstanceId, long Sequence, DateTimeOffset BucketStart, DateTimeOffset BucketEnd, bool Coarsened, string RawJson, string PayloadChecksum);
    private readonly record struct DeviceHealthReport(DateTimeOffset CheckedAt, string AgentVersion, int QueuePendingPayloadCount, long QueuePendingPayloadBytes, int QueueLossRecordCount, int QueueLostPayloadCount, string? SessionState, string[] Warnings);
    private sealed record ActivityBatchResponse(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("batch_id")] Guid BatchId,
        [property: JsonPropertyName("outcomes")] IReadOnlyList<ActivityOutcome> Outcomes);

    private sealed record ActivityOutcome(
        [property: JsonPropertyName("event_id")] Guid EventId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("rejection_reason")] string? RejectionReason,
        [property: JsonIgnore] bool AcceptedNew);

    private sealed record DeviceHealthResponse(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("enrollment_id")] Guid EnrollmentId,
        [property: JsonPropertyName("status")] string Status);
}
