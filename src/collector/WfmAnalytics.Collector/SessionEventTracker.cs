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
    private string? _previousConnectState;

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
        }

        if (observation.Session is not null)
        {
            ObserveSessionState(observation);
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

    private void ObserveSessionState(CollectorObservation observation)
    {
        var state = observation.Session!.ConnectState;
        if (_previousConnectState is null)
        {
            _previousConnectState = state;
            _events.Add(new WindowsSessionEvent(
                $"session_state_initial_{state}",
                observation.TimestampUtc,
                observation.Session.SessionId,
                observation.Session.Source));
            return;
        }

        if (string.Equals(_previousConnectState, state, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _previousConnectState = state;
        _events.Add(new WindowsSessionEvent(
            state is "active" or "connected" ? "session_connected" : "session_disconnected",
            observation.TimestampUtc,
            observation.Session.SessionId,
            observation.Session.Source,
            $"connect_state={state}"));
    }

    public void Complete(DateTimeOffset endedAt)
    {
        _events.Add(new WindowsSessionEvent("session_observed_end", endedAt, _sessionId, "collector_process"));
    }
}
