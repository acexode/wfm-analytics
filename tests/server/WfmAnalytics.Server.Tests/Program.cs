using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WfmAnalytics.Server.Bootstrap;
using WfmAnalytics.Server.Database;
using WfmAnalytics.Server.Identity;
using Npgsql;

var tests = new (string Name, Func<Task> Run)[]
{
    ("health endpoint is anonymous", HealthEndpointIsAnonymous),
    ("scoped endpoint rejects an anonymous request", ScopedEndpointRejectsAnonymousRequest),
    ("scoped endpoint rejects a principal without the required scope", ScopedEndpointRejectsWrongScope),
    ("scoped endpoint accepts the required synthetic scope in Development", ScopedEndpointAcceptsRequiredScope),
    ("development headers are ignored in Production", DevelopmentHeadersAreIgnoredInProduction),
    ("migration catalog is ordered and content-addressed", MigrationCatalogIsValid),
    ("unknown development principal is rejected", UnknownPrincipalRejected),
    ("readiness rejects missing database", ReadinessRejectsMissingDatabase),
    ("PostgreSQL migration, scope and report integration", DatabaseIntegration)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

return failures.Count == 0 ? 0 : 1;

static async Task HealthEndpointIsAnonymous()
{
    await WithServer("Development", async client =>
    {
        using var response = await client.GetAsync("/health");
        Equal(HttpStatusCode.OK, response.StatusCode);
    });
}

static async Task ScopedEndpointRejectsAnonymousRequest()
{
    await WithServer("Development", async client =>
    {
        using var response = await client.GetAsync("/api/demo/scoped");
        Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    });
}

static async Task ScopedEndpointRejectsWrongScope()
{
    await WithServer("Development", async client =>
    {
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-unscoped");
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.ScopesHeader, "analytics:demo:write");
        using var response = await client.GetAsync("/api/demo/scoped");
        Equal(HttpStatusCode.Forbidden, response.StatusCode);
    });
}

static async Task ScopedEndpointAcceptsRequiredScope()
{
    await WithServer("Development", async client =>
    {
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-manager");
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.ScopesHeader, "analytics:demo:read");
        using var response = await client.GetAsync("/api/demo/scoped");
        Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DemoResponse>();
        Equal("demo-manager", body?.PrincipalId ?? "");
        True(body?.Scopes.Contains("analytics:demo:read", StringComparer.Ordinal) == true, "Required scope was not returned.");
        Equal("synthetic-only", body?.DataClassification ?? "");
    });
}

static async Task DevelopmentHeadersAreIgnoredInProduction()
{
    await WithServer("Production", async client =>
    {
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-manager");
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.ScopesHeader, "analytics:demo:read");
        using var response = await client.GetAsync("/api/demo/scoped");
        Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    });
}

static Task MigrationCatalogIsValid()
{
    var catalog = new EmbeddedMigrationCatalog();
    Equal(7, catalog.Migrations.Count);
    Equal(1L, catalog.Migrations[0].Version);
    Equal(64, catalog.Migrations[0].Sha256.Length);
    True(catalog.Migrations[0].Sql.Contains("platform.schema_migrations", StringComparison.Ordinal), "Migration ledger is missing.");
    return Task.CompletedTask;
}

static async Task WithServer(string environment, Func<HttpClient, Task> assertion)
{
    await using var app = ServerApplication.Build(environmentName: environment, urls: "http://127.0.0.1:0");
    await app.StartAsync();
    try
    {
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        var address = addresses?.Single() ?? throw new InvalidOperationException("Test server did not publish an address.");
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        await assertion(client);
    }
    finally
    {
        await app.StopAsync();
    }
}

static async Task UnknownPrincipalRejected()
{
    await WithServer("Development", async client =>
    {
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "unknown-manager");
        client.DefaultRequestHeaders.Add(DevelopmentIdentity.ScopesHeader, "analytics:demo:read");
        using var response = await client.GetAsync("/api/demo/scoped");
        Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    });
}

static async Task ReadinessRejectsMissingDatabase()
{
    var previous = Environment.GetEnvironmentVariable("ConnectionStrings__Primary");
    Environment.SetEnvironmentVariable("ConnectionStrings__Primary", "Host=127.0.0.1;Port=1;Database=unavailable;Timeout=1");
    try
    {
        await WithServer("Development", async client =>
        {
            using var response = await client.GetAsync("/health/ready");
            Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        });
    }
    finally { Environment.SetEnvironmentVariable("ConnectionStrings__Primary", previous); }
}

static async Task DatabaseIntegration()
{
    var testConnection = Environment.GetEnvironmentVariable("WFM_TEST_DATABASE");
    if (string.IsNullOrWhiteSpace(testConnection))
        throw new InvalidOperationException("Set WFM_TEST_DATABASE to an isolated migration-capable PostgreSQL database; database checks cannot be skipped.");
    var oldPrimary = Environment.GetEnvironmentVariable("ConnectionStrings__Primary");
    var oldMigration = Environment.GetEnvironmentVariable("ConnectionStrings__Migration");
    Environment.SetEnvironmentVariable("ConnectionStrings__Primary", testConnection);
    Environment.SetEnvironmentVariable("ConnectionStrings__Migration", testConnection);
    try
    {
        await using var app = ServerApplication.Build(environmentName: "Development");
        var database = app.Services.GetRequiredService<PostgresDatabase>();
        await database.ApplyMigrationsAsync();
        await database.ApplyMigrationsAsync();
        True(await database.IsReadyAsync(), "Database did not become ready after repeat migration.");
        await ClearActivityReportsAsync(testConnection);
        await DevelopmentSeeder.SeedAsync(database, app.Environment, "fixtures/daily-report.json");
        await DevelopmentSeeder.SeedAsync(database, app.Environment, "fixtures/daily-report.json");
        await WithServer("Development", async client =>
        {
            client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-manager");
            using var response = await client.GetAsync("/api/v1/teams/team-synthetic/daily?date=2026-09-13");
            Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            True(payload.RootElement.GetProperty("synthetic").GetBoolean(), "Report must be synthetic.");
            True(payload.RootElement.GetProperty("employees")[0].GetProperty("output").GetProperty("completed_count").ValueKind == System.Text.Json.JsonValueKind.Null, "Missing output cannot become zero.");
            using var denied = await client.GetAsync("/api/v1/teams/other-team/daily?date=2026-09-13");
            Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var missing = await client.GetAsync("/api/v1/teams/team-synthetic/daily?date=2026-09-14");
            Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var invalid = await client.GetAsync("/api/v1/teams/team-synthetic/daily?date=bad");
            Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        });
        await WithServer("Development", async client =>
        {
            client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-unscoped");
            client.DefaultRequestHeaders.Add(DevelopmentIdentity.ScopesHeader, "analytics:daily:read");
            using var response = await client.GetAsync("/api/v1/teams/team-synthetic/daily?date=2026-09-13");
            Equal(HttpStatusCode.Forbidden, response.StatusCode);
        });
        await WithServer("Development", AssertActivityIngestion);
        await WithServer("Development", AssertDeviceHealthIngestion);
        await using var production = ServerApplication.Build(environmentName: "Production");
        try
        {
            await DevelopmentSeeder.SeedAsync(database, production.Environment, "fixtures/daily-report.json");
            throw new Exception("Production seed was allowed.");
        }
        catch (InvalidOperationException) { }
        // Deliberate checksum corruption in the explicitly supplied test database; always restore it.
        await using var connection = new NpgsqlConnection(testConnection);
        await connection.OpenAsync();
        var checksum = app.Services.GetRequiredService<IMigrationCatalog>().Migrations[0].Sha256;
        try
        {
            await using var corrupt = new NpgsqlCommand("UPDATE platform.schema_migrations SET sha256=repeat('0',64) WHERE version=1", connection);
            await corrupt.ExecuteNonQueryAsync();
            True(!await database.IsReadyAsync(), "Readiness accepted changed migration checksum.");
            try { await database.ApplyMigrationsAsync(); throw new Exception("Changed migration checksum was accepted."); }
            catch (InvalidOperationException) { }
        }
        finally
        {
            await using var restore = new NpgsqlCommand("UPDATE platform.schema_migrations SET sha256=$1 WHERE version=1", connection);
            restore.Parameters.AddWithValue(checksum);
            await restore.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Primary", oldPrimary);
        Environment.SetEnvironmentVariable("ConnectionStrings__Migration", oldMigration);
    }
}

static async Task ClearActivityReportsAsync(string testConnection)
{
    await using var connection = new NpgsqlConnection(testConnection);
    await connection.OpenAsync();
    await using var clear = new NpgsqlCommand("DELETE FROM analytics.activity_daily_reports; DELETE FROM ingestion.activity_dirty_days; DELETE FROM ingestion.activity_envelopes; DELETE FROM ingestion.ingestion_receipts; DELETE FROM ingestion.device_health_reports;", connection);
    await clear.ExecuteNonQueryAsync();
}

static async Task AssertActivityIngestion(HttpClient client)
{
    var enrollment = "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
    var eventId = Guid.NewGuid().ToString();
    var secondEventId = Guid.NewGuid().ToString();
    var collectorInstanceId = Guid.NewGuid().ToString();
    var bucketStart = DateTimeOffset.UtcNow.AddMinutes(-2);
    bucketStart = new DateTimeOffset(bucketStart.Year, bucketStart.Month, bucketStart.Day, bucketStart.Hour, bucketStart.Minute, 0, TimeSpan.Zero);
    var bucketEnd = bucketStart.AddMinutes(1);
    var reportDate = bucketStart.ToString("yyyy-MM-dd");
    var first = ActivityBatchJson(eventId, collectorInstanceId, sequence: 0, app: "teams.exe", bucketStart, bucketEnd);
    client.DefaultRequestHeaders.Add("X-WFM-Enrollment-Id", enrollment);

    using var invalidJson = await client.PostAsync("/api/v1/activity/batches", Json("{"));
    Equal(HttpStatusCode.BadRequest, invalidJson.StatusCode);

    using var unsupportedSchema = await client.PostAsync("/api/v1/activity/batches", Json("""{"schema_version":"2.0","batch_id":"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa","agent_version":"x","policy_version":"x","events":[]}"""));
    Equal(HttpStatusCode.BadRequest, unsupportedSchema.StatusCode);

    using var oversized = await client.PostAsync("/api/v1/activity/batches", Json(new string('x', 513 * 1024)));
    Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

    var future = ActivityBatchJson(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), sequence: 0, app: "future.exe", DateTimeOffset.UtcNow.AddMinutes(10), DateTimeOffset.UtcNow.AddMinutes(11));
    using var futureResponse = await client.PostAsync("/api/v1/activity/batches", Json(future));
    Equal(HttpStatusCode.BadRequest, futureResponse.StatusCode);

    var old = ActivityBatchJson(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), sequence: 0, app: "old.exe", DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.UtcNow.AddDays(-8).AddMinutes(1));
    using var oldResponse = await client.PostAsync("/api/v1/activity/batches", Json(old));
    Equal(HttpStatusCode.BadRequest, oldResponse.StatusCode);

    using var accepted = await client.PostAsync("/api/v1/activity/batches", Json(first));
    Equal(HttpStatusCode.OK, accepted.StatusCode);
    using (var payload = System.Text.Json.JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()))
    {
        Equal("accepted", payload.RootElement.GetProperty("outcomes")[0].GetProperty("status").GetString() ?? "");
    }

    using var duplicate = await client.PostAsync("/api/v1/activity/batches", Json(first));
    Equal(HttpStatusCode.OK, duplicate.StatusCode);
    using (var payload = System.Text.Json.JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()))
    {
        Equal("already_accepted", payload.RootElement.GetProperty("outcomes")[0].GetProperty("status").GetString() ?? "");
    }

    var changedSameEvent = ActivityBatchJson(eventId, collectorInstanceId, sequence: 0, app: "chrome.exe", bucketStart, bucketEnd);
    using var conflict = await client.PostAsync("/api/v1/activity/batches", Json(changedSameEvent));
    Equal(HttpStatusCode.OK, conflict.StatusCode);
    using (var payload = System.Text.Json.JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()))
    {
        Equal("rejected", payload.RootElement.GetProperty("outcomes")[0].GetProperty("status").GetString() ?? "");
        Equal("event_checksum_conflict", payload.RootElement.GetProperty("outcomes")[0].GetProperty("rejection_reason").GetString() ?? "");
    }

    var sequenceConflict = ActivityBatchJson(secondEventId, collectorInstanceId, sequence: 0, app: "word.exe", bucketStart, bucketEnd);
    using var sequenceConflictResponse = await client.PostAsync("/api/v1/activity/batches", Json(sequenceConflict));
    Equal(HttpStatusCode.OK, sequenceConflictResponse.StatusCode);
    using (var payload = System.Text.Json.JsonDocument.Parse(await sequenceConflictResponse.Content.ReadAsStringAsync()))
    {
        Equal("rejected", payload.RootElement.GetProperty("outcomes")[0].GetProperty("status").GetString() ?? "");
        Equal("sequence_conflict", payload.RootElement.GetProperty("outcomes")[0].GetProperty("rejection_reason").GetString() ?? "");
    }

    client.DefaultRequestHeaders.Add(DevelopmentIdentity.PrincipalHeader, "demo-manager");
    using var report = await client.GetAsync($"/api/v1/teams/team-synthetic/daily?date={reportDate}");
    Equal(HttpStatusCode.OK, report.StatusCode);
    using (var payload = System.Text.Json.JsonDocument.Parse(await report.Content.ReadAsStringAsync()))
    {
        True(!payload.RootElement.GetProperty("synthetic").GetBoolean(), "Ingested activity report must not be the static synthetic fixture.");
        Equal("employee-live-test", payload.RootElement.GetProperty("employees")[0].GetProperty("employee_id").GetString() ?? "");
        True(payload.RootElement.GetProperty("employees")[0].GetProperty("categories").GetProperty("active_seconds").GetInt32() >= 60, "Accepted activity was not aggregated into active seconds.");
        Equal("source_not_connected", payload.RootElement.GetProperty("employees")[0].GetProperty("output").GetProperty("unavailable_reason").GetString() ?? "");
    }
}

static async Task AssertDeviceHealthIngestion(HttpClient client)
{
    client.DefaultRequestHeaders.Add("X-WFM-Enrollment-Id", "dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    using var accepted = await client.PostAsync("/api/v1/device/health", Json("""
    {
      "checked_at": "2026-09-13T09:00:00Z",
      "machine_name": "TEST-DEVICE",
      "user_name": "test-user",
      "process_id": 1234,
      "process_session_id": "windows-session-1",
      "agent_version": "0.1.0-test",
      "queue": {
        "checked_at": "2026-09-13T09:00:00Z",
        "queue_root": "tmp\\test",
        "pending_payload_count": 2,
        "pending_payload_bytes": 1024,
        "oldest_pending_end_utc": "2026-09-13T08:59:00Z",
        "newest_pending_end_utc": "2026-09-13T09:00:00Z",
        "loss_record_count": 1,
        "lost_payload_count": 3,
        "lost_payload_bytes": 2048,
        "within_byte_limit": true,
        "within_age_limit": true
      },
      "session": {
        "session_id": "windows-session-1",
        "session_number": 1,
        "connect_state": "active",
        "user_name": "test-user",
        "domain_name": "TEST",
        "source": "wts"
      },
      "tamper_resistance": [],
      "warnings": ["local_queue_loss_manifest_present"]
    }
    """));
    Equal(HttpStatusCode.OK, accepted.StatusCode);
    using var payload = System.Text.Json.JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
    Equal("accepted", payload.RootElement.GetProperty("status").GetString() ?? "");

    using var missingQueue = await client.PostAsync("/api/v1/device/health", Json("""{"checked_at":"2026-09-13T09:00:00Z","agent_version":"x"}"""));
    Equal(HttpStatusCode.BadRequest, missingQueue.StatusCode);
}

static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

static string ActivityBatchJson(string eventId, string collectorInstanceId, long sequence, string app, DateTimeOffset bucketStart, DateTimeOffset bucketEnd)
{
    return $$"""
    {
      "schema_version": "1.0",
      "batch_id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      "agent_version": "0.1.0-test",
      "policy_version": "test",
      "events": [
        {
          "event_id": "{{eventId}}",
          "boot_id": "test-boot",
          "session_id": "test-session",
          "collector_instance_id": "{{collectorInstanceId}}",
          "sequence": {{sequence}},
          "bucket_start": "{{bucketStart:O}}",
          "bucket_end": "{{bucketEnd:O}}",
          "coarsened": false,
          "slices": [
            {
              "start_offset_ms": 0,
              "end_offset_ms": 60000,
              "application_id": "{{app}}",
              "state": "active"
            }
          ]
        }
      ]
    }
    """;
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record DemoResponse(string PrincipalId, string[] Scopes, string DataClassification);
