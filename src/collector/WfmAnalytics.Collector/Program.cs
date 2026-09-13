using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using WfmAnalytics.Collector;

if (args.Contains("--evidence-live", StringComparer.Ordinal))
{
    var duration = ReadIntOption(args, "--duration-seconds", 120);
    var interval = ReadIntOption(args, "--interval-ms", 1000);
    var outputPath = ReadStringOption(args, "--out") ?? Path.Combine("tmp", "wp03-live-evidence.json");
    var queueRoot = ReadStringOption(args, "--queue-root") ?? Path.Combine("tmp", "wp03-live-queue");
    var report = await LiveEvidenceRunner.RunAsync(new LiveEvidenceOptions(duration, interval, outputPath, queueRoot));
    Console.WriteLine($"Wrote WP03 live evidence to {outputPath}");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        event_count = report.Batch.Events.Count,
        observed_applications = report.Batch.Events.SelectMany(item => item.Slices).Select(item => item.ApplicationId).Where(item => item is not null).Distinct().ToArray(),
        report.Queue.EncryptedPayloadCount,
        report.Queue.ReplayedPayloadCount,
        report.Queue.PlaintextLeakDetected,
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
Console.WriteLine("Use --replay-evidence-queue --queue-root tmp/wp03-live-queue after a restart to verify queued payload replay.");
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

internal sealed record LiveSampleResult(
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("sample_interval_ms")] int SampleIntervalMs,
    [property: JsonPropertyName("batch")] CollectorBatch Batch);
