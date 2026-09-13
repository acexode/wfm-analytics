using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public enum ActivityState
{
    Active,
    Inactive,
    Locked,
    DetailUnavailable
}

public sealed record CollectorObservation(
    DateTimeOffset TimestampUtc,
    TimeSpan MonotonicElapsed,
    string? ApplicationId,
    bool IsLocked,
    TimeSpan TimeSinceLastInput,
    SensitiveObservation? Sensitive = null,
    WindowsSessionSnapshot? Session = null);

public sealed record ActivitySlice(
    [property: JsonPropertyName("start_offset_ms")]
    int StartOffsetMs,
    [property: JsonPropertyName("end_offset_ms")]
    int EndOffsetMs,
    [property: JsonPropertyName("application_id")]
    string? ApplicationId,
    [property: JsonPropertyName("state")]
    ActivityState State,
    [property: JsonPropertyName("sensitive")]
    SensitiveObservation? Sensitive = null);

public sealed record ActivityEnvelope(
    [property: JsonPropertyName("event_id")]
    Guid EventId,
    [property: JsonPropertyName("boot_id")]
    string BootId,
    [property: JsonPropertyName("session_id")]
    string SessionId,
    [property: JsonPropertyName("collector_instance_id")]
    Guid CollectorInstanceId,
    [property: JsonPropertyName("sequence")]
    long Sequence,
    [property: JsonPropertyName("bucket_start")]
    DateTimeOffset BucketStart,
    [property: JsonPropertyName("bucket_end")]
    DateTimeOffset BucketEnd,
    [property: JsonPropertyName("coarsened")]
    bool Coarsened,
    [property: JsonPropertyName("slices")]
    IReadOnlyList<ActivitySlice> Slices);

public sealed record CollectorBatch(
    [property: JsonPropertyName("schema_version")]
    string SchemaVersion,
    [property: JsonPropertyName("batch_id")]
    Guid BatchId,
    [property: JsonPropertyName("agent_version")]
    string AgentVersion,
    [property: JsonPropertyName("policy_version")]
    string PolicyVersion,
    [property: JsonPropertyName("events")]
    IReadOnlyList<ActivityEnvelope> Events);
