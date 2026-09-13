using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record WindowsSessionEvent(
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("detail")] string? Detail = null);

public sealed class SessionEventTracker
{
    private readonly string _sessionId;
    private readonly List<WindowsSessionEvent> _events = [];
    private bool? _previousLocked;

    public SessionEventTracker(string sessionId, DateTimeOffset startedAt)
    {
        _sessionId = sessionId;
        _events.Add(new WindowsSessionEvent("session_observed_start", startedAt, sessionId, "collector_process"));
    }

    public IReadOnlyList<WindowsSessionEvent> Events => _events;

    public void Observe(CollectorObservation observation)
    {
        if (_previousLocked is null)
        {
            _previousLocked = observation.IsLocked;
            _events.Add(new WindowsSessionEvent(
                observation.IsLocked ? "lock_state_initial_locked" : "lock_state_initial_unlocked",
                observation.TimestampUtc,
                _sessionId,
                "interactive_probe"));
            return;
        }

        if (_previousLocked == observation.IsLocked)
        {
            return;
        }

        _previousLocked = observation.IsLocked;
        _events.Add(new WindowsSessionEvent(
            observation.IsLocked ? "workstation_locked" : "workstation_unlocked",
            observation.TimestampUtc,
            _sessionId,
            "interactive_probe"));
    }

    public void Complete(DateTimeOffset endedAt)
    {
        _events.Add(new WindowsSessionEvent("session_observed_end", endedAt, _sessionId, "collector_process"));
    }
}
