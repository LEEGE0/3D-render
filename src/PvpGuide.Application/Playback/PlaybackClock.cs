using System.Runtime.ExceptionServices;

namespace PvpGuide.Application.Playback;

public sealed class PlaybackClock : IPlaybackTimeSource
{
    private const int MaxChangedNotificationDispatches = 32;
    private readonly Queue<(double TimeSeconds, bool IsPlaying)> _pendingChangedStates = [];
    private bool _isDispatchingChanged;
    private double _requestedTimeSeconds;
    private bool _requestedIsPlaying;
    private long _stateRequestVersion;

    public PlaybackClock(double durationSeconds, int framesPerSecond)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be finite and positive.");
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frames per second must be positive.");
        }

        DurationSeconds = durationSeconds;
        FramesPerSecond = framesPerSecond;
    }

    public double DurationSeconds { get; }

    public int FramesPerSecond { get; }

    public double CurrentTimeSeconds { get; private set; }

    public bool IsPlaying { get; private set; }

    public event EventHandler<PlaybackChangedEventArgs>? Changed;

    internal long StateRequestVersion => _stateRequestVersion;

    public bool Seek(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Time must be finite.");
        }

        return SetState(Math.Clamp(timeSeconds, 0, DurationSeconds), _requestedIsPlaying);
    }

    public bool Play() => SetState(
        _requestedTimeSeconds == DurationSeconds ? 0 : _requestedTimeSeconds,
        true);

    public bool Pause() => SetState(_requestedTimeSeconds, false);

    public bool Toggle() => _requestedIsPlaying ? Pause() : Play();

    public bool Stop() => SetState(0, false);

    public bool Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Delta must be finite and non-negative.");
        }

        if (!_requestedIsPlaying)
        {
            return false;
        }

        var nextTime = _requestedTimeSeconds + deltaSeconds;
        return nextTime >= DurationSeconds
            ? SetState(DurationSeconds, false)
            : SetState(nextTime, true);
    }

    private bool SetState(double currentTimeSeconds, bool isPlaying)
    {
        if (_requestedTimeSeconds == currentTimeSeconds && _requestedIsPlaying == isPlaying)
        {
            return false;
        }

        if (_isDispatchingChanged &&
            _pendingChangedStates.Count >= MaxChangedNotificationDispatches)
        {
            throw new InvalidOperationException("Playback state notification did not stabilize.");
        }

        _requestedTimeSeconds = currentTimeSeconds;
        _requestedIsPlaying = isPlaying;
        unchecked
        {
            _stateRequestVersion++;
        }

        _pendingChangedStates.Enqueue((currentTimeSeconds, isPlaying));
        if (_isDispatchingChanged)
        {
            return true;
        }

        _isDispatchingChanged = true;
        try
        {
            ExceptionDispatchInfo? observerFailure = null;
            for (var dispatch = 0; dispatch < MaxChangedNotificationDispatches; dispatch++)
            {
                if (_pendingChangedStates.Count == 0)
                {
                    observerFailure?.Throw();
                    return true;
                }

                var dispatchedState = _pendingChangedStates.Dequeue();
                CurrentTimeSeconds = dispatchedState.TimeSeconds;
                IsPlaying = dispatchedState.IsPlaying;
                try
                {
                    Changed?.Invoke(
                        this,
                        new PlaybackChangedEventArgs(
                            dispatchedState.TimeSeconds,
                            dispatchedState.IsPlaying));
                }
                catch (Exception exception)
                {
                    observerFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (_pendingChangedStates.Count == 0)
            {
                observerFailure?.Throw();
                return true;
            }

            _pendingChangedStates.Clear();
            _requestedTimeSeconds = CurrentTimeSeconds;
            _requestedIsPlaying = IsPlaying;
            throw new InvalidOperationException("Playback state notification did not stabilize.");
        }
        finally
        {
            _pendingChangedStates.Clear();
            _requestedTimeSeconds = CurrentTimeSeconds;
            _requestedIsPlaying = IsPlaying;
            _isDispatchingChanged = false;
        }
    }
}
