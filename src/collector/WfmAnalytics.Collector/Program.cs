using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using WfmAnalytics.Collector;

if (args.Contains("--evidence-live", StringComparer.Ordinal))
{
    var duration = ReadIntOption(args, "--duration-seconds", 120);
    var interval = ReadIntOption(args, "--interval-ms", 1000);
    var outputPath = ReadStringOption(args, "--out") ?? Path.Combine("tmp", "wp03-live-evidence.json");
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var policy = ReadPolicy(args);
    var report = await LiveEvidenceRunner.RunAsync(new LiveEvidenceOptions(duration, interval, outputPath, queueRoot, policy));
    Console.WriteLine($"Wrote WP03 live evidence to {outputPath}");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        event_count = report.Batch.Events.Count,
        observed_applications = report.Batch.Events.SelectMany(item => item.Slices).Select(item => item.ApplicationId).Where(item => item is not null).Distinct().ToArray(),
        report.Queue.EncryptedPayloadCount,
        report.Queue.ReplayedPayloadCount,
        report.Queue.PlaintextLeakDetected,
        session_event_count = report.SessionEvents.Count,
        sample_gap_count = report.SampleGaps.Count,
        resource_sample_count = report.Resources.Count
    }, CollectorJson.Options));
    return 0;
}

if (args.Contains("--replay-evidence-queue", StringComparer.Ordinal))
{
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var evidence = LiveEvidenceRunner.ReplayQueue(queueRoot);
    Console.WriteLine(JsonSerializer.Serialize(evidence, CollectorJson.Options));
    return evidence.ReplayedPayloadCount == evidence.EncryptedPayloadCount && !evidence.PlaintextLeakDetected ? 0 : 1;
}

if (args.Contains("--export-evidence-artifacts", StringComparer.Ordinal))
{
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var outputDirectory = ReadStringOption(args, "--out") ?? Path.Combine("tmp", "wp03-artifact-export");
    var result = LiveEvidenceRunner.ExportArtifacts(queueRoot, outputDirectory);
    Console.WriteLine(JsonSerializer.Serialize(result, CollectorJson.Options));
    return 0;
}

if (args.Contains("--collector-health", StringComparer.Ordinal))
{
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var agentVersion = ReadStringOption(args, "--agent-version") ?? "0.1.0-live";
    var key = WindowsDpapiKeyStore.LoadOrCreateKey(Path.Combine(queueRoot, LiveEvidenceRunner.ProtectedKeyFileName));
    var queue = new FileBackedEncryptedQueue(queueRoot, new AesGcmPayloadProtector(key));
    var health = CollectorHealth.Create(queueRoot, agentVersion, queue);
    Console.WriteLine(JsonSerializer.Serialize(health, CollectorJson.Options));
    return health.Warnings.Count == 0 ? 0 : 1;
}

if (args.Contains("--upload-collector-health", StringComparer.Ordinal))
{
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var serverUrl = (ReadStringOption(args, "--server-url") ?? "http://127.0.0.1:5080").TrimEnd('/');
    var enrollmentId = ReadStringOption(args, "--enrollment-id") ?? "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
    var agentVersion = ReadStringOption(args, "--agent-version") ?? "0.1.0-live";
    var key = WindowsDpapiKeyStore.LoadOrCreateKey(Path.Combine(queueRoot, LiveEvidenceRunner.ProtectedKeyFileName));
    var queue = new FileBackedEncryptedQueue(queueRoot, new AesGcmPayloadProtector(key));
    var health = CollectorHealth.Create(queueRoot, agentVersion, queue);

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.Add("X-WFM-Enrollment-Id", enrollmentId);
    using var content = new StringContent(JsonSerializer.Serialize(health, CollectorJson.Options), Encoding.UTF8, "application/json");
    using var response = await client.PostAsync($"{serverUrl}/api/v1/device/health", content);
    var responseText = await response.Content.ReadAsStringAsync();
    Console.WriteLine(responseText);
    return response.IsSuccessStatusCode ? 0 : 1;
}

if (args.Contains("--upload-evidence-queue", StringComparer.Ordinal))
{
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var serverUrl = (ReadStringOption(args, "--server-url") ?? "http://127.0.0.1:5080").TrimEnd('/');
    var enrollmentId = ReadStringOption(args, "--enrollment-id") ?? "dddddddd-dddd-4ddd-8ddd-dddddddddddd";
    var agentVersion = ReadStringOption(args, "--agent-version") ?? "0.1.0-live";
    var policyVersion = ReadStringOption(args, "--policy-version") ?? "manual-live-test";

    var key = WindowsDpapiKeyStore.LoadOrCreateKey(Path.Combine(queueRoot, LiveEvidenceRunner.ProtectedKeyFileName));
    var queue = new FileBackedEncryptedQueue(queueRoot, new AesGcmPayloadProtector(key));
    var pending = queue.ListPending().Take(200).ToArray();
    if (pending.Length == 0)
    {
        Console.WriteLine(JsonSerializer.Serialize(new UploadResult(0, 0, 0, []), CollectorJson.Options));
        return 0;
    }

    var envelopes = pending.Select(queue.Read).ToArray();
    var batch = new CollectorBatch("1.0", Guid.NewGuid(), agentVersion, policyVersion, envelopes);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.Add("X-WFM-Enrollment-Id", enrollmentId);
    using var content = new StringContent(JsonSerializer.Serialize(batch, CollectorJson.Options), Encoding.UTF8, "application/json");
    using var response = await client.PostAsync($"{serverUrl}/api/v1/activity/batches", content);
    var responseText = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(responseText);
        return 1;
    }

    var upload = JsonSerializer.Deserialize<UploadResponse>(responseText, CollectorJson.Options)
        ?? throw new InvalidOperationException("Upload response could not be parsed.");
    var pendingByEvent = pending.Zip(envelopes).ToDictionary(item => item.Second.EventId, item => item.First);
    var acknowledged = 0;
    foreach (var outcome in upload.Outcomes)
    {
        if ((outcome.Status is "accepted" or "already_accepted") && pendingByEvent.TryGetValue(outcome.EventId, out var item))
        {
            queue.Acknowledge(item);
            acknowledged++;
        }
    }

    var result = new UploadResult(pending.Length, upload.Outcomes.Count, acknowledged, upload.Outcomes);
    Console.WriteLine(JsonSerializer.Serialize(result, CollectorJson.Options));
    return acknowledged == pending.Length ? 0 : 1;
}

if (args.Contains("--sample-live", StringComparer.Ordinal))
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("--sample-live requires a real interactive Windows session.");
        return 2;
    }

    var duration = ReadIntOption(args, "--duration-seconds", 30);
    var interval = ReadIntOption(args, "--interval-ms", 1000);
    var outputPath = ReadStringOption(args, "--out");
    if (duration <= 0 || interval <= 0)
    {
        Console.Error.WriteLine("Duration and interval must be positive.");
        return 2;
    }

    var collector = new BucketAccumulator(
        bootId: Environment.MachineName,
        sessionId: Environment.UserName,
        collectorInstanceId: Guid.NewGuid(),
        idleThreshold: TimeSpan.FromMinutes(5));

    var started = DateTimeOffset.UtcNow;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var completed = new List<ActivityEnvelope>();
    while (stopwatch.Elapsed < TimeSpan.FromSeconds(duration))
    {
        var envelope = collector.Observe(WindowsProbe.Observe(DateTimeOffset.UtcNow, stopwatch.Elapsed));
        if (envelope is not null)
        {
            completed.Add(envelope);
        }

        await Task.Delay(interval);
    }

    var partial = collector.CompletePartial(DateTimeOffset.UtcNow);
    if (partial is not null)
    {
        completed.Add(partial);
    }

    var batch = new CollectorBatch("1.0", Guid.NewGuid(), "0.1.0-live", "manual-live-test", completed);
    var summary = new LiveSampleResult(started, DateTimeOffset.UtcNow, interval, batch);
    var json = JsonSerializer.Serialize(summary, CollectorJson.Options);
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.WriteLine(json);
    }
    else
    {
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Wrote live sample to {outputPath}");
    }

    return 0;
}

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var collector = new BucketAccumulator(
        bootId: "manual-boot",
        sessionId: Environment.UserName,
        collectorInstanceId: Guid.NewGuid(),
        idleThreshold: TimeSpan.FromMinutes(5));

    var now = DateTimeOffset.UtcNow;
    collector.Observe(new CollectorObservation(now, TimeSpan.Zero, "explorer.exe", IsLocked: false, TimeSpan.FromSeconds(1)));
    var envelope = collector.CompletePartial(now.AddSeconds(10)) ?? throw new InvalidOperationException("No envelope produced.");

    var key = RandomNumberGenerator.GetBytes(32);
    var queueRoot = Path.Combine(Path.GetTempPath(), "wfm-collector-self-test", Guid.NewGuid().ToString("N"));
    var queue = new FileBackedEncryptedQueue(queueRoot, new AesGcmPayloadProtector(key));
    var pending = queue.Enqueue(envelope);
    var replay = queue.Read(pending);

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        replay.Sequence,
        replay.BucketStart,
        replay.BucketEnd,
        replay.Coarsened,
        SliceCount = replay.Slices.Count
    }, CollectorJson.Options));
    return 0;
}

Console.WriteLine("WFM collector prototype. Use --self-test for a local encrypted queue smoke test.");
Console.WriteLine("Use --sample-live --duration-seconds 30 --out tmp/live-sample.json to capture foreground app slices.");
Console.WriteLine("Use --evidence-live --duration-seconds 120 --out tmp/wp03-live-evidence.json for the guided WP03 evidence report.");
Console.WriteLine("Add --allow-window-titles, --allow-browser-urls, --allow-typed-text, --allow-screenshots, --allow-clipboard, or --allow-full-paths to test an expanded capture policy.");
Console.WriteLine("Use --replay-evidence-queue --queue-root tmp/wp03-live-queue after a restart to verify queued payload replay.");
Console.WriteLine("Use --export-evidence-artifacts --queue-root tmp/wp03-live-queue --out tmp/screenshots-review to decrypt screenshot artifacts for manual review.");
Console.WriteLine("Use --collector-health --queue-root tmp/wp03-live-queue to inspect pending queue, loss manifest, Windows session state and deployment-hardening readiness.");
Console.WriteLine("Use --upload-collector-health --queue-root tmp/wp03-live-queue --server-url http://127.0.0.1:5080 --enrollment-id dddddddd-dddd-4ddd-8ddd-dddddddddddd to send health to the development API.");
Console.WriteLine("Use --upload-evidence-queue --queue-root tmp/wp03-live-queue --server-url http://127.0.0.1:5080 --enrollment-id dddddddd-dddd-4ddd-8ddd-dddddddddddd to send queued envelopes to the development API.");
return 0;

static int ReadIntOption(string[] args, string name, int defaultValue)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.Ordinal));
    if (index < 0 || index + 1 >= args.Length)
    {
        return defaultValue;
    }

    return int.TryParse(args[index + 1], out var parsed) ? parsed : defaultValue;
}

static string? ReadStringOption(string[] args, string name)
{
    var index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.Ordinal));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static CollectionPolicy ReadPolicy(string[] args)
{
    var settings = new SensitiveCaptureSettings(
        WindowTitles: args.Contains("--allow-window-titles", StringComparer.Ordinal),
        BrowserUrls: args.Contains("--allow-browser-urls", StringComparer.Ordinal),
        TypedText: args.Contains("--allow-typed-text", StringComparer.Ordinal),
        Screenshots: args.Contains("--allow-screenshots", StringComparer.Ordinal),
        Clipboard: args.Contains("--allow-clipboard", StringComparer.Ordinal),
        FullPaths: args.Contains("--allow-full-paths", StringComparer.Ordinal));

    return new CollectionPolicy(settings.AnyEnabled ? "manual-expanded-sensitive-test" : "manual-live-test", settings);
}

internal sealed record LiveSampleResult(
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("sample_interval_ms")] int SampleIntervalMs,
    [property: JsonPropertyName("batch")] CollectorBatch Batch);

internal sealed record UploadResult(
    [property: JsonPropertyName("pending_count")] int PendingCount,
    [property: JsonPropertyName("outcome_count")] int OutcomeCount,
    [property: JsonPropertyName("acknowledged_count")] int AcknowledgedCount,
    [property: JsonPropertyName("outcomes")] IReadOnlyList<UploadOutcome> Outcomes);

internal sealed record UploadResponse(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("batch_id")] Guid BatchId,
    [property: JsonPropertyName("outcomes")] IReadOnlyList<UploadOutcome> Outcomes);

internal sealed record UploadOutcome(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason);
