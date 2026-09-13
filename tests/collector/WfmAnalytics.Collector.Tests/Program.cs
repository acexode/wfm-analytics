using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WfmAnalytics.Collector;

var tests = new (string Name, Action Run)[]
{
    ("normalizes application identifiers without path content", NormalizesApplicationIdentifiers),
    ("idle threshold starts inactive at boundary", IdleThresholdBoundary),
    ("locked state overrides active input", LockedStateOverridesInput),
    ("bucket slices are ordered and capped", BucketSlicesAreOrderedAndCapped),
    ("partial buckets stop at last healthy sample", PartialBucketsStopAtLastHealthySample),
    ("encrypted queue replays exact envelope without plaintext leak", EncryptedQueueReplay),
    ("encrypted queue enforces retention cap with loss manifest", EncryptedQueueRetentionCap),
    ("DPAPI queue key survives process-style reload on Windows", DpapiQueueKeySurvivesReload),
    ("encrypted artifact store hides screenshot bytes and exports on demand", EncryptedArtifactStoreExport),
    ("sensitive fields are dropped unless policy enables them", SensitiveFieldsRequirePolicy),
    ("session event tracker records observed start end and lock transitions", SessionEventTrackerRecordsTransitions),
    ("session event tracker records WTS state transitions", SessionEventTrackerRecordsWtsStateTransitions),
    ("windows session probe maps service session changes", WindowsSessionProbeMapsServiceChanges),
    ("service session change tracker emits auditable event", ServiceSessionChangeTrackerEmitsEvent),
    ("collector batch JSON uses accepted snake_case contract names", CollectorBatchJsonUsesContractNames),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
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

static void NormalizesApplicationIdentifiers()
{
    Equal("notepad.exe", ApplicationIdNormalizer.NormalizeExecutableName(@"C:\Users\Example\Documents\Notepad.EXE") ?? "");
    Equal(null, ApplicationIdNormalizer.NormalizeExecutableName("bad name with spaces.exe"));
}

static void IdleThresholdBoundary()
{
    var envelope = BuildEnvelope(
        new("2026-09-13T09:00:00Z", "word.exe", false, TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(59))),
        new("2026-09-13T09:00:05Z", "word.exe", false, TimeSpan.FromMinutes(5)));

    Equal(ActivityState.Active, envelope.Slices[0].State);
    Equal(0, envelope.Slices[0].StartOffsetMs);
    Equal(5000, envelope.Slices[0].EndOffsetMs);
    Equal(ActivityState.Inactive, envelope.Slices[1].State);
}

static void LockedStateOverridesInput()
{
    var envelope = BuildEnvelope(
        new("2026-09-13T09:00:00Z", "teams.exe", false, TimeSpan.FromSeconds(1)),
        new("2026-09-13T09:00:10Z", "teams.exe", true, TimeSpan.FromSeconds(1)));

    Equal(ActivityState.Active, envelope.Slices[0].State);
    Equal(ActivityState.Locked, envelope.Slices[1].State);
    Equal(null, envelope.Slices[1].ApplicationId);
}

static void BucketSlicesAreOrderedAndCapped()
{
    var accumulator = NewAccumulator();
    for (var second = 0; second < 60; second++)
    {
        var app = second % 2 == 0 ? "a.exe" : "b.exe";
        accumulator.Observe(Observation($"2026-09-13T09:00:{second:00}Z", app, false, TimeSpan.Zero));
    }

    var envelope = accumulator.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:01:00Z")) ?? throw new InvalidOperationException("Missing envelope.");
    Equal(60, envelope.Slices.Count);
    True(envelope.Coarsened, "Application churn should coarsen at the slice cap.");
    Equal(ActivityState.DetailUnavailable, envelope.Slices[^1].State);

    var previousEnd = 0;
    foreach (var slice in envelope.Slices)
    {
        Equal(previousEnd, slice.StartOffsetMs);
        True(slice.EndOffsetMs > slice.StartOffsetMs, "Slice offsets must be increasing.");
        previousEnd = slice.EndOffsetMs;
    }
}

static void EncryptedQueueReplay()
{
    var envelope = BuildEnvelope(new RawObservation("2026-09-13T09:00:00Z", "crm.exe", false, TimeSpan.Zero));
    var key = RandomNumberGenerator.GetBytes(32);
    var root = Path.Combine(Path.GetTempPath(), "wfm-collector-tests", Guid.NewGuid().ToString("N"));
    var queue = new FileBackedEncryptedQueue(root, new AesGcmPayloadProtector(key));

    var pending = queue.Enqueue(envelope);
    var ciphertext = File.ReadAllBytes(pending.CiphertextPath);
    True(!Encoding.UTF8.GetString(ciphertext).Contains("crm.exe", StringComparison.Ordinal), "Queue payload leaked plaintext application ID.");

    var listed = queue.ListPending().Single();
    var replay = queue.Read(listed);
    Equal(envelope.EventId, replay.EventId);
    Equal(envelope.CollectorInstanceId, replay.CollectorInstanceId);
    Equal(envelope.Sequence, replay.Sequence);
    Equal("crm.exe", replay.Slices.Single().ApplicationId ?? "");
    Equal(ActivityState.Active, replay.Slices.Single().State);

    queue.Acknowledge(listed);
    True(queue.ListPending().Count == 0, "Acknowledged payload remained pending.");
}

static void EncryptedQueueRetentionCap()
{
    var root = Path.Combine(Path.GetTempPath(), "wfm-collector-retention-tests", Guid.NewGuid().ToString("N"));
    var queue = new FileBackedEncryptedQueue(
        root,
        new AesGcmPayloadProtector(RandomNumberGenerator.GetBytes(32)),
        new QueueRetentionOptions(TimeSpan.FromDays(7), 1));

    queue.Enqueue(BuildEnvelope(new RawObservation("2026-09-13T09:00:00Z", "old.exe", false, TimeSpan.Zero)));

    Equal(0, queue.ListPending().Count);
    var loss = queue.ListLosses().Single();
    Equal(1, loss.PayloadCount);
    True(loss.PayloadBytes > 0, "Loss manifest did not record dropped bytes.");
    Equal("queue_retention_cap", loss.Reason);
    var health = queue.GetHealth(DateTimeOffset.Parse("2026-09-13T10:00:00Z"));
    Equal(1, health.LossRecordCount);
    Equal(1, health.LostPayloadCount);
}

static void PartialBucketsStopAtLastHealthySample()
{
    var accumulator = NewAccumulator();
    var start = DateTimeOffset.Parse("2026-09-13T09:00:00Z");
    for (var sample = 0; sample < 60; sample++)
    {
        var app = sample % 2 == 0 ? "a.exe" : "b.exe";
        var timestamp = start.AddMilliseconds(sample * 500).ToString("O");
        accumulator.Observe(Observation(timestamp, app, false, TimeSpan.Zero));
    }

    var envelope = accumulator.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:00:30Z")) ?? throw new InvalidOperationException("Missing envelope.");
    Equal(DateTimeOffset.Parse("2026-09-13T09:00:30Z"), envelope.BucketEnd);
    True(envelope.Slices.All(slice => slice.EndOffsetMs <= 30000), "Partial bucket claimed time after the last healthy sample.");
}

static void CollectorBatchJsonUsesContractNames()
{
    var envelope = BuildEnvelope(new RawObservation("2026-09-13T09:00:00Z", "crm.exe", false, TimeSpan.Zero));
    var batch = new CollectorBatch("1.0", Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "0.1.0", "test-1", [envelope]);
    var json = JsonSerializer.Serialize(batch, CollectorJson.Options);

    True(json.Contains("\"schema_version\"", StringComparison.Ordinal), "Batch schema_version was not snake_case.");
    True(json.Contains("\"event_id\"", StringComparison.Ordinal), "Envelope event_id was not snake_case.");
    True(json.Contains("\"start_offset_ms\"", StringComparison.Ordinal), "Slice start_offset_ms was not snake_case.");
    True(json.Contains("\"state\":\"active\"", StringComparison.Ordinal), "Activity state did not use contract spelling.");
}

static void SensitiveFieldsRequirePolicy()
{
    var sensitive = new SensitiveObservation(
        WindowTitle: "Customer File - Jane Doe",
        BrowserUrl: "https://example.invalid/private",
        TypedText: "secret typed text",
        ScreenshotRef: "screenshot-1",
        ClipboardText: "clipboard secret",
        FullPath: @"C:\Users\Example\Sensitive\tool.exe");

    var minimum = new BucketAccumulator("boot", "session", Guid.NewGuid(), TimeSpan.FromMinutes(5), CollectionPolicy.Minimum("minimum"));
    minimum.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:00Z"), TimeSpan.Zero, "tool.exe", false, TimeSpan.Zero, sensitive));
    var minimumEnvelope = minimum.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:00:10Z")) ?? throw new InvalidOperationException("Missing envelope.");
    Equal(null, minimumEnvelope.Slices.Single().Sensitive);

    var expandedPolicy = new CollectionPolicy("expanded", new SensitiveCaptureSettings(WindowTitles: true, BrowserUrls: false, TypedText: false, Screenshots: false, Clipboard: false, FullPaths: true));
    var expanded = new BucketAccumulator("boot", "session", Guid.NewGuid(), TimeSpan.FromMinutes(5), expandedPolicy);
    expanded.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:00Z"), TimeSpan.Zero, "tool.exe", false, TimeSpan.Zero, sensitive));
    var expandedEnvelope = expanded.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:00:10Z")) ?? throw new InvalidOperationException("Missing envelope.");
    Equal("Customer File - Jane Doe", expandedEnvelope.Slices.Single().Sensitive?.WindowTitle ?? "");
    Equal(@"C:\Users\Example\Sensitive\tool.exe", expandedEnvelope.Slices.Single().Sensitive?.FullPath ?? "");
    Equal(null, expandedEnvelope.Slices.Single().Sensitive?.BrowserUrl);
    Equal(null, expandedEnvelope.Slices.Single().Sensitive?.TypedText);
    Equal(null, expandedEnvelope.Slices.Single().Sensitive?.ClipboardText);

    var clipboardScreenshotPolicy = new CollectionPolicy("expanded-assets", new SensitiveCaptureSettings(WindowTitles: false, BrowserUrls: false, TypedText: false, Screenshots: true, Clipboard: true, FullPaths: false));
    var clipboardScreenshot = new BucketAccumulator("boot", "session", Guid.NewGuid(), TimeSpan.FromMinutes(5), clipboardScreenshotPolicy);
    clipboardScreenshot.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:00Z"), TimeSpan.Zero, "tool.exe", false, TimeSpan.Zero, sensitive));
    var clipboardScreenshotEnvelope = clipboardScreenshot.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:00:10Z")) ?? throw new InvalidOperationException("Missing envelope.");
    Equal("screenshot-1", clipboardScreenshotEnvelope.Slices.Single().Sensitive?.ScreenshotRef ?? "");
    Equal("clipboard secret", clipboardScreenshotEnvelope.Slices.Single().Sensitive?.ClipboardText ?? "");
    Equal(null, clipboardScreenshotEnvelope.Slices.Single().Sensitive?.WindowTitle);
    Equal(null, clipboardScreenshotEnvelope.Slices.Single().Sensitive?.FullPath);
}

static void SessionEventTrackerRecordsTransitions()
{
    var tracker = new SessionEventTracker("windows-session-1", DateTimeOffset.Parse("2026-09-13T09:00:00Z"));
    tracker.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:01Z"), TimeSpan.FromSeconds(1), "teams.exe", false, TimeSpan.Zero));
    tracker.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:05Z"), TimeSpan.FromSeconds(5), null, true, TimeSpan.Zero));
    tracker.Observe(new CollectorObservation(DateTimeOffset.Parse("2026-09-13T09:00:10Z"), TimeSpan.FromSeconds(10), "teams.exe", false, TimeSpan.Zero));
    tracker.Complete(DateTimeOffset.Parse("2026-09-13T09:00:15Z"));

    Equal(5, tracker.Events.Count);
    Equal("session_observed_start", tracker.Events[0].EventType);
    Equal("lock_state_initial_unlocked", tracker.Events[1].EventType);
    Equal("workstation_locked", tracker.Events[2].EventType);
    Equal("workstation_unlocked", tracker.Events[3].EventType);
    Equal("session_observed_end", tracker.Events[4].EventType);
    Equal("windows-session-1", tracker.Events[4].SessionId);
}

static void SessionEventTrackerRecordsWtsStateTransitions()
{
    var tracker = new SessionEventTracker("windows-session-1", DateTimeOffset.Parse("2026-09-13T09:00:00Z"));
    tracker.Observe(new CollectorObservation(
        DateTimeOffset.Parse("2026-09-13T09:00:01Z"),
        TimeSpan.FromSeconds(1),
        "teams.exe",
        false,
        TimeSpan.Zero,
        Session: new WindowsSessionSnapshot("windows-session-1", 1, "active", "employee", "domain")));
    tracker.Observe(new CollectorObservation(
        DateTimeOffset.Parse("2026-09-13T09:00:05Z"),
        TimeSpan.FromSeconds(5),
        null,
        false,
        TimeSpan.Zero,
        Session: new WindowsSessionSnapshot("windows-session-1", 1, "disconnected", "employee", "domain")));

    Equal("session_state_initial_active", tracker.Events[2].EventType);
    Equal("session_disconnected", tracker.Events[3].EventType);
    Equal("wts", tracker.Events[3].Source);
}

static void WindowsSessionProbeMapsServiceChanges()
{
    Equal("session_logon", WindowsSessionProbe.EventTypeFromServiceSessionChange(0x5));
    Equal("session_logoff", WindowsSessionProbe.EventTypeFromServiceSessionChange(0x6));
    Equal("session_locked", WindowsSessionProbe.EventTypeFromServiceSessionChange(0x7));
    Equal("session_unlocked", WindowsSessionProbe.EventTypeFromServiceSessionChange(0x8));
    Equal("session_change_unknown", WindowsSessionProbe.EventTypeFromServiceSessionChange(0xff));
}

static void ServiceSessionChangeTrackerEmitsEvent()
{
    var at = DateTimeOffset.Parse("2026-09-13T09:00:00Z");
    var sessionEvent = WindowsServiceSessionChangeTracker.FromServiceControlReason(0x5, 7, at);

    Equal("session_logon", sessionEvent.EventType);
    Equal("windows-session-7", sessionEvent.SessionId);
    Equal("windows_service_control", sessionEvent.Source);
    True(sessionEvent.Detail?.Contains("reason=0x5", StringComparison.Ordinal) == true, "Session change detail did not include the raw service-control reason.");
}

static void DpapiQueueKeySurvivesReload()
{
    if (!OperatingSystem.IsWindows())
    {
        Console.WriteLine("SKIP DPAPI queue key survives process-style reload on Windows");
        return;
    }

    var root = Path.Combine(Path.GetTempPath(), "wfm-collector-dpapi-tests", Guid.NewGuid().ToString("N"));
    var keyPath = Path.Combine(root, LiveEvidenceRunner.ProtectedKeyFileName);
    var firstKey = WindowsDpapiKeyStore.LoadOrCreateKey(keyPath);
    var envelope = BuildEnvelope(new RawObservation("2026-09-13T09:00:00Z", "reload.exe", false, TimeSpan.Zero));
    var firstQueue = new FileBackedEncryptedQueue(root, new AesGcmPayloadProtector(firstKey));
    var pending = firstQueue.Enqueue(envelope);

    var secondKey = WindowsDpapiKeyStore.LoadOrCreateKey(keyPath);
    var secondQueue = new FileBackedEncryptedQueue(root, new AesGcmPayloadProtector(secondKey));
    var replay = secondQueue.Read(pending);

    Equal(envelope.EventId, replay.EventId);
    Equal("reload.exe", replay.Slices.Single().ApplicationId ?? "");
}

static void EncryptedArtifactStoreExport()
{
    var root = Path.Combine(Path.GetTempPath(), "wfm-artifact-tests", Guid.NewGuid().ToString("N"));
    var exportRoot = Path.Combine(root, "export");
    var store = new EncryptedArtifactStore(root, new AesGcmPayloadProtector(RandomNumberGenerator.GetBytes(32)));
    var bytes = Encoding.ASCII.GetBytes("BM fake screenshot bytes");
    var artifact = store.Store("screenshot", DateTimeOffset.Parse("2026-09-13T09:00:00Z"), "image/bmp", ".bmp", bytes);

    var ciphertext = File.ReadAllBytes(artifact.CiphertextPath);
    True(!Encoding.ASCII.GetString(ciphertext).Contains("BM fake", StringComparison.Ordinal), "Artifact ciphertext leaked screenshot header/content.");
    Equal(bytes.Length, store.Read(artifact).Length);

    var export = store.ExportAll(exportRoot);
    Equal(1, export.ExportedCount);
    True(File.Exists(export.Files.Single()), "Exported artifact file was not written.");
    True(Encoding.ASCII.GetString(File.ReadAllBytes(export.Files.Single())).StartsWith("BM fake", StringComparison.Ordinal), "Exported artifact did not decrypt original bytes.");
}

static ActivityEnvelope BuildEnvelope(params RawObservation[] observations)
{
    var accumulator = NewAccumulator();
    foreach (var observation in observations)
    {
        accumulator.Observe(Observation(observation.Timestamp, observation.ApplicationId, observation.IsLocked, observation.TimeSinceLastInput));
    }

    return accumulator.CompletePartial(DateTimeOffset.Parse("2026-09-13T09:01:00Z"))
        ?? throw new InvalidOperationException("Missing envelope.");
}

static BucketAccumulator NewAccumulator()
{
    return new BucketAccumulator("boot", "session", Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), TimeSpan.FromMinutes(5));
}

static CollectorObservation Observation(string timestamp, string? applicationId, bool isLocked, TimeSpan timeSinceLastInput)
{
    var utc = DateTimeOffset.Parse(timestamp).ToUniversalTime();
    return new CollectorObservation(utc, utc - DateTimeOffset.Parse("2026-09-13T09:00:00Z"), applicationId, isLocked, timeSinceLastInput);
}

static void Equal<T>(T expected, T actual)
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

internal sealed record RawObservation(string Timestamp, string? ApplicationId, bool IsLocked, TimeSpan TimeSinceLastInput);
