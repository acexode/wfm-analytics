namespace WfmAnalytics.Collector;

public sealed class BucketAccumulator
{
    public const int BucketDurationMs = 60_000;
    public const int MaxDetailedSlices = 60;

    private readonly string _bootId;
    private readonly string _sessionId;
    private readonly Guid _collectorInstanceId;
    private readonly TimeSpan _idleThreshold;
    private readonly List<ActivitySlice> _slices = [];
    private long _nextSequence;
    private DateTimeOffset? _bucketStart;
    private ActivityState? _currentState;
    private string? _currentApplicationId;
    private SensitiveObservation? _currentSensitive;
    private int _currentStartOffsetMs;
    private bool _detailUnavailable;
    private readonly CollectionPolicy _policy;

    public BucketAccumulator(
        string bootId,
        string sessionId,
        Guid collectorInstanceId,
        TimeSpan idleThreshold,
        CollectionPolicy? policy = null,
        long initialSequence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _bootId = bootId;
        _sessionId = sessionId;
        _collectorInstanceId = collectorInstanceId;
        _idleThreshold = idleThreshold;
        _policy = policy ?? CollectionPolicy.Minimum("minimum-default");
        _nextSequence = initialSequence;
    }

    public ActivityEnvelope? Observe(CollectorObservation observation)
    {
        var bucketStart = AlignToMinute(observation.TimestampUtc);
        _bucketStart ??= bucketStart;

        if (bucketStart != _bucketStart.Value)
        {
            var completed = CompleteBucket();
            BeginBucket(bucketStart);
            ApplyObservation(observation, 0);
            return completed;
        }

        var offset = ClampOffset(observation.TimestampUtc - _bucketStart.Value);
        ApplyObservation(observation, offset);
        return null;
    }

    public ActivityEnvelope? CompletePartial(DateTimeOffset healthyUntilUtc)
    {
        if (_bucketStart is null || _currentState is null)
        {
            return null;
        }

        var offset = ClampOffset(healthyUntilUtc - _bucketStart.Value);
        TrimSlicesTo(offset);
        if (offset > _currentStartOffsetMs)
        {
            AddSlice(_currentStartOffsetMs, offset, _currentApplicationId, _currentState.Value);
            TrimSlicesTo(offset);
        }

        return CreateEnvelope(offset);
    }

    private void TrimSlicesTo(int endOffsetMs)
    {
        if (_slices.Count == 0)
        {
            return;
        }

        var last = _slices[^1];
        if (last.EndOffsetMs <= endOffsetMs)
        {
            return;
        }

        if (last.StartOffsetMs >= endOffsetMs)
        {
            _slices.RemoveAt(_slices.Count - 1);
            _currentStartOffsetMs = endOffsetMs;
            return;
        }

        _slices[^1] = last with { EndOffsetMs = endOffsetMs };
        _currentStartOffsetMs = endOffsetMs;
    }

    private void BeginBucket(DateTimeOffset bucketStart)
    {
        _bucketStart = bucketStart;
        _slices.Clear();
        _currentState = null;
        _currentApplicationId = null;
        _currentSensitive = null;
        _currentStartOffsetMs = 0;
        _detailUnavailable = false;
    }

    private void ApplyObservation(CollectorObservation observation, int offsetMs)
    {
        var state = ResolveState(observation);
        var applicationId = state == ActivityState.Active
            ? ApplicationIdNormalizer.NormalizeExecutableName(observation.ApplicationId)
            : null;
        var sensitive = observation.Sensitive?.ApplyPolicy(_policy.SensitiveCapture);

        if (_detailUnavailable)
        {
            return;
        }

        if (_currentState is null)
        {
            _currentState = state;
            _currentApplicationId = applicationId;
            _currentSensitive = sensitive;
            _currentStartOffsetMs = offsetMs;
            return;
        }

        if (_currentState == state
            && string.Equals(_currentApplicationId, applicationId, StringComparison.Ordinal)
            && Equals(_currentSensitive, sensitive))
        {
            return;
        }

        if (offsetMs > _currentStartOffsetMs)
        {
            AddSlice(_currentStartOffsetMs, offsetMs, _currentApplicationId, _currentState.Value);
        }

        _currentState = state;
        _currentApplicationId = applicationId;
        _currentSensitive = sensitive;
        _currentStartOffsetMs = offsetMs;
    }

    private void AddSlice(int startOffsetMs, int endOffsetMs, string? applicationId, ActivityState state)
    {
        if (endOffsetMs <= startOffsetMs)
        {
            return;
        }

        if (_slices.Count >= MaxDetailedSlices - 1)
        {
            _slices.Add(new ActivitySlice(startOffsetMs, BucketDurationMs, null, ActivityState.DetailUnavailable));
            _currentStartOffsetMs = BucketDurationMs;
            _currentState = ActivityState.DetailUnavailable;
            _currentApplicationId = null;
            _detailUnavailable = true;
            return;
        }

        var sensitive = _policy.SensitiveCapture.AnyEnabled ? _currentSensitive : null;
        _slices.Add(new ActivitySlice(startOffsetMs, endOffsetMs, applicationId, state, sensitive));
    }

    private ActivityEnvelope CompleteBucket()
    {
        if (_currentState is not null && _currentStartOffsetMs < BucketDurationMs)
        {
            AddSlice(_currentStartOffsetMs, BucketDurationMs, _currentApplicationId, _currentState.Value);
        }

        return CreateEnvelope(BucketDurationMs);
    }

    private ActivityEnvelope CreateEnvelope(int bucketEndOffsetMs)
    {
        var bucketStart = _bucketStart ?? throw new InvalidOperationException("No bucket has been started.");
        var slices = _slices.Count == 0
            ? [new ActivitySlice(0, Math.Max(1, bucketEndOffsetMs), null, ActivityState.DetailUnavailable)]
            : _slices.ToArray();

        var envelope = new ActivityEnvelope(
            Guid.NewGuid(),
            _bootId,
            _sessionId,
            _collectorInstanceId,
            _nextSequence++,
            bucketStart,
            bucketStart.AddMilliseconds(bucketEndOffsetMs),
            slices.Any(slice => slice.State == ActivityState.DetailUnavailable),
            slices);

        _bucketStart = null;
        _slices.Clear();
        _currentState = null;
        _currentApplicationId = null;
        _currentSensitive = null;
        _currentStartOffsetMs = 0;
        _detailUnavailable = false;
        return envelope;
    }

    private ActivityState ResolveState(CollectorObservation observation)
    {
        if (observation.IsLocked)
        {
            return ActivityState.Locked;
        }

        return observation.TimeSinceLastInput >= _idleThreshold
            ? ActivityState.Inactive
            : ActivityState.Active;
    }

    private static DateTimeOffset AlignToMinute(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMinute);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static int ClampOffset(TimeSpan value)
    {
        return Math.Clamp((int)Math.Round(value.TotalMilliseconds), 0, BucketDurationMs);
    }
}
