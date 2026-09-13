using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record LiveEvidenceOptions(
    int DurationSeconds,
    int IntervalMs,
    string OutputPath,
    string QueueRoot,
    CollectionPolicy? Policy = null);

public sealed record LiveEvidenceReport(
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("sample_interval_ms")] int SampleIntervalMs,
    [property: JsonPropertyName("boot_id")] string BootId,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("collector_instance_id")] Guid CollectorInstanceId,
    [property: JsonPropertyName("policy")] CollectionPolicy Policy,
    [property: JsonPropertyName("batch")] CollectorBatch Batch,
    [property: JsonPropertyName("queue")] QueueEvidence Queue,
    [property: JsonPropertyName("artifacts")] ArtifactEvidence Artifacts,
    [property: JsonPropertyName("session_events")] IReadOnlyList<WindowsSessionEvent> SessionEvents,
    [property: JsonPropertyName("resources")] IReadOnlyList<ResourceSample> Resources,
    [property: JsonPropertyName("sample_gaps")] IReadOnlyList<SampleGap> SampleGaps,
    [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes);

public sealed record QueueEvidence(
    [property: JsonPropertyName("queue_root")] string QueueRoot,
    [property: JsonPropertyName("key_protection")] string KeyProtection,
    [property: JsonPropertyName("encrypted_payload_count")] int EncryptedPayloadCount,
    [property: JsonPropertyName("replayed_payload_count")] int ReplayedPayloadCount,
    [property: JsonPropertyName("replay_identity_matches")] bool ReplayIdentityMatches,
    [property: JsonPropertyName("plaintext_leak_detected")] bool PlaintextLeakDetected,
    [property: JsonPropertyName("leaked_tokens")] IReadOnlyList<string> LeakedTokens,
    [property: JsonPropertyName("ciphertext_bytes")] long CiphertextBytes,
    [property: JsonPropertyName("loss_record_count")] int LossRecordCount,
    [property: JsonPropertyName("lost_payload_count")] int LostPayloadCount,
    [property: JsonPropertyName("oldest_pending_end_utc")] DateTimeOffset? OldestPendingEndUtc);

public sealed record ArtifactEvidence(
    [property: JsonPropertyName("encrypted_artifact_count")] int EncryptedArtifactCount,
    [property: JsonPropertyName("ciphertext_bytes")] long CiphertextBytes,
    [property: JsonPropertyName("plaintext_leak_detected")] bool PlaintextLeakDetected,
    [property: JsonPropertyName("manifest_path")] string ManifestPath);

public sealed record ResourceSample(
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("elapsed_ms")] long ElapsedMs,
    [property: JsonPropertyName("working_set_bytes")] long WorkingSetBytes,
    [property: JsonPropertyName("private_memory_bytes")] long PrivateMemoryBytes,
    [property: JsonPropertyName("total_processor_ms")] long TotalProcessorMs);

public sealed record SampleGap(
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("gap_ms")] long GapMs,
    [property: JsonPropertyName("expected_interval_ms")] int ExpectedIntervalMs);

public static class LiveEvidenceRunner
{
    public const string ProtectedKeyFileName = "queue-key.dpapi";

    public static async Task<LiveEvidenceReport> RunAsync(LiveEvidenceOptions options, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Live evidence requires a real interactive Windows session.");
        }

        if (options.DurationSeconds <= 0 || options.IntervalMs <= 0)
        {
            throw new ArgumentException("Duration and interval must be positive.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? ".");
        Directory.CreateDirectory(options.QueueRoot);

        var bootId = CollectorRuntimeIdentity.CreateBootId();
        var sessionId = CollectorRuntimeIdentity.CreateSessionId();
        var collectorInstanceId = Guid.NewGuid();
        var policy = options.Policy ?? CollectionPolicy.Minimum("manual-live-test");
        var collector = new BucketAccumulator(bootId, sessionId, collectorInstanceId, TimeSpan.FromMinutes(5), policy);
        var completed = new List<ActivityEnvelope>();
        var resources = new List<ResourceSample>();
        var gaps = new List<SampleGap>();
        var started = DateTimeOffset.UtcNow;
        var sessionEvents = new SessionEventTracker(sessionId, started);
        var stopwatch = Stopwatch.StartNew();
        var previousSampleAt = started;
        using var process = Process.GetCurrentProcess();
        var key = WindowsDpapiKeyStore.LoadOrCreateKey(Path.Combine(options.QueueRoot, ProtectedKeyFileName));
        var protector = new AesGcmPayloadProtector(key);
        var artifactStore = new EncryptedArtifactStore(options.QueueRoot, protector);

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(options.DurationSeconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var gapMs = (long)(now - previousSampleAt).TotalMilliseconds;
            if (gapMs > options.IntervalMs * 2L)
            {
                gaps.Add(new SampleGap(previousSampleAt, now, gapMs, options.IntervalMs));
            }

            previousSampleAt = now;
            var observation = WindowsProbe.Observe(now, stopwatch.Elapsed, policy);
            if (policy.SensitiveCapture.Clipboard || policy.SensitiveCapture.Screenshots)
            {
                observation = observation with
                {
                    Sensitive = MergeSensitiveObservation(
                        observation.Sensitive,
                        policy.SensitiveCapture.Clipboard ? WindowsClipboardReader.TryReadText() : null,
                        CaptureScreenshotArtifact(policy, artifactStore, now))
                };
            }

            sessionEvents.Observe(observation);
            var envelope = collector.Observe(observation);
            if (envelope is not null)
            {
                completed.Add(envelope);
            }

            process.Refresh();
            resources.Add(new ResourceSample(now, stopwatch.ElapsedMilliseconds, process.WorkingSet64, process.PrivateMemorySize64, (long)process.TotalProcessorTime.TotalMilliseconds));
            await Task.Delay(options.IntervalMs, cancellationToken);
        }

        var partial = collector.CompletePartial(DateTimeOffset.UtcNow);
        if (partial is not null)
        {
            completed.Add(partial);
        }

        var ended = DateTimeOffset.UtcNow;
        sessionEvents.Complete(ended);
        var batch = new CollectorBatch("1.0", Guid.NewGuid(), "0.1.0-live-evidence", policy.PolicyVersion, completed);
        var queue = WriteAndInspectQueue(options.QueueRoot, completed, protector);
        var artifacts = InspectArtifacts(options.QueueRoot, artifactStore);
        var notes = new[]
        {
            "Manual live evidence only. This report supports WP03 but does not authorize employee deployment.",
            policy.SensitiveCapture.AnyEnabled
                ? "Expanded sensitive capture policy is enabled for this manual evidence run. Treat the report and queue as sensitive data."
                : "Minimum capture policy is enabled; window titles, URLs, text, screenshots, clipboard and full paths are not collected.",
            policy.SensitiveCapture.Screenshots
                ? "Screenshots are encrypted as local artifacts and referenced from slice sensitive.screenshot_ref. Use --export-evidence-artifacts to decrypt a review copy."
                : "Screenshot capture is disabled.",
            "Lock detection uses interactive desktop accessibility and must be confirmed on the controlled company Windows device."
        };

        var report = new LiveEvidenceReport(started, ended, options.IntervalMs, bootId, sessionId, collectorInstanceId, policy, batch, queue, artifacts, sessionEvents.Events, resources, gaps, notes);
        await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(report, CollectorJson.Options), cancellationToken);
        return report;
    }

    public static ArtifactExportResult ExportArtifacts(string queueRoot, string outputDirectory)
    {
        var key = WindowsDpapiKeyStore.LoadOrCreateKey(Path.Combine(queueRoot, ProtectedKeyFileName));
        var store = new EncryptedArtifactStore(queueRoot, new AesGcmPayloadProtector(key));
        return store.ExportAll(outputDirectory);
    }

    private static SensitiveObservation MergeSensitiveObservation(SensitiveObservation? current, string? clipboardText, string? screenshotRef)
    {
        current ??= SensitiveObservation.Empty;
        return current with
        {
            ClipboardText = clipboardText ?? current.ClipboardText,
            ScreenshotRef = screenshotRef ?? current.ScreenshotRef
        };
    }

    private static string? CaptureScreenshotArtifact(CollectionPolicy policy, EncryptedArtifactStore artifactStore, DateTimeOffset now)
    {
        if (!policy.SensitiveCapture.Screenshots)
        {
            return null;
        }

        var bytes = WindowsScreenshotCapture.TryCaptureDesktopBmp();
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        var artifact = artifactStore.Store("screenshot", now, "image/bmp", ".bmp", bytes);
        return $"artifact:{artifact.ArtifactId:N}";
    }

    public static QueueEvidence ReplayQueue(string queueRoot)
    {
        var keyPath = Path.Combine(queueRoot, ProtectedKeyFileName);
        var key = WindowsDpapiKeyStore.LoadOrCreateKey(keyPath);
        var queue = new FileBackedEncryptedQueue(queueRoot, new AesGcmPayloadProtector(key));
        var pending = queue.ListPending();
        var replayed = pending.Select(queue.Read).ToArray();
        var ciphertextFiles = pending.Select(payload => payload.CiphertextPath).ToArray();
        var tokens = replayed
            .SelectMany(envelope => envelope.Slices)
            .Select(slice => slice.ApplicationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var leakedTokens = FindPlaintextTokens(ciphertextFiles, tokens);
        var health = queue.GetHealth();
        return new QueueEvidence(
            queueRoot,
            WindowsDpapiKeyStore.ProtectionDescription,
            pending.Count,
            replayed.Length,
            ReplayIdentityMatches(replayed, replayed),
            leakedTokens.Count > 0,
            leakedTokens,
            ciphertextFiles.Sum(path => new FileInfo(path).Length),
            health.LossRecordCount,
            health.LostPayloadCount,
            health.OldestPendingEndUtc);
    }

    private static QueueEvidence WriteAndInspectQueue(string queueRoot, IReadOnlyList<ActivityEnvelope> envelopes, AesGcmPayloadProtector protector)
    {
        var queue = new FileBackedEncryptedQueue(queueRoot, protector);
        foreach (var envelope in envelopes)
        {
            queue.Enqueue(envelope);
        }

        var pending = queue.ListPending();
        var replayed = pending.Select(queue.Read).ToArray();
        var replayIdentityMatches = ReplayIdentityMatches(envelopes, replayed);

        var tokens = envelopes
            .SelectMany(envelope => envelope.Slices)
            .Select(slice => slice.ApplicationId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var ciphertextFiles = pending.Select(payload => payload.CiphertextPath).ToArray();
        var leakedTokens = FindPlaintextTokens(ciphertextFiles, tokens);
        var ciphertextBytes = ciphertextFiles.Sum(path => new FileInfo(path).Length);
        var health = queue.GetHealth();
        return new QueueEvidence(
            queueRoot,
            WindowsDpapiKeyStore.ProtectionDescription,
            pending.Count,
            replayed.Length,
            replayIdentityMatches,
            leakedTokens.Count > 0,
            leakedTokens,
            ciphertextBytes,
            health.LossRecordCount,
            health.LostPayloadCount,
            health.OldestPendingEndUtc);
    }

    private static ArtifactEvidence InspectArtifacts(string queueRoot, EncryptedArtifactStore store)
    {
        var artifacts = store.List();
        var ciphertextBytes = artifacts.Sum(artifact => new FileInfo(artifact.CiphertextPath).Length);
        var leak = artifacts.Any(artifact => ContainsAscii(File.ReadAllBytes(artifact.CiphertextPath), "BM"));
        return new ArtifactEvidence(
            artifacts.Count,
            ciphertextBytes,
            leak,
            Path.GetFullPath(Path.Combine(queueRoot, "artifacts", "manifest.jsonl")));
    }

    private static bool ReplayIdentityMatches(IReadOnlyList<ActivityEnvelope> expected, IReadOnlyList<ActivityEnvelope> replayed)
    {
        return expected.Count == replayed.Count
            && expected.Select(envelope => envelope.EventId).SequenceEqual(replayed.Select(envelope => envelope.EventId))
            && expected.Select(envelope => envelope.CollectorInstanceId).SequenceEqual(replayed.Select(envelope => envelope.CollectorInstanceId))
            && expected.Select(envelope => envelope.Sequence).SequenceEqual(replayed.Select(envelope => envelope.Sequence));
    }

    private static IReadOnlyList<string> FindPlaintextTokens(IReadOnlyList<string> ciphertextFiles, IReadOnlyList<string> tokens)
    {
        var leaked = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ciphertextFiles)
        {
            var bytes = File.ReadAllBytes(path);
            foreach (var token in tokens)
            {
                if (ContainsAscii(bytes, token))
                {
                    leaked.Add(token);
                }
            }
        }

        return leaked.ToArray();
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> haystack, string needle)
    {
        var needleBytes = Encoding.ASCII.GetBytes(needle);
        return haystack.IndexOf(needleBytes) >= 0;
    }
}
