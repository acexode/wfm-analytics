using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace WfmAnalytics.Server.Database;

public static class DevelopmentSeeder
{
    public static async Task SeedAsync(PostgresDatabase database, IHostEnvironment environment, string fixturePath, CancellationToken token = default)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Synthetic seeding requires Development.");
        var payload = await File.ReadAllTextAsync(fixturePath, token);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.GetProperty("synthetic").GetBoolean() || root.GetProperty("team_id").GetString() != "team-synthetic")
            throw new InvalidOperationException("Only the team-synthetic development fixture is accepted.");
        await using var connection = database.CreateConnection("Migration");
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var grant = new NpgsqlCommand("INSERT INTO platform.access_grants(principal_id,team_id,permission,valid_from) VALUES ('demo-manager','team-synthetic','analytics:daily:read','2026-01-01T00:00:00Z') ON CONFLICT DO NOTHING", connection, transaction);
        await grant.ExecuteNonQueryAsync(token);
        await using var report = new NpgsqlCommand("INSERT INTO analytics.synthetic_daily_reports(team_id,report_date,payload) VALUES ($1,$2,$3) ON CONFLICT (team_id,report_date) DO UPDATE SET payload=EXCLUDED.payload", connection, transaction);
        report.Parameters.AddWithValue("team-synthetic");
        report.Parameters.AddWithValue(DateOnly.Parse(root.GetProperty("report_date").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        report.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload);
        await report.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }
}
