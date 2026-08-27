namespace PvpGuide.Application.Playback;

public sealed class PlaybackClock
{
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

    public bool Seek(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Time must be finite.");
        }

        return SetState(Math.Clamp(timeSeconds, 0, DurationSeconds), IsPlaying);
    }

    public bool Play() => SetState(
        CurrentTimeSeconds == DurationSeconds ? 0 : CurrentTimeSeconds,
        true);

    public bool Pause() => SetState(CurrentTimeSeconds, false);

    public bool Toggle() => IsPlaying ? Pause() : Play();

    public bool Stop() => SetState(0, false);

    public bool Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Delta must be finite and non-negative.");
        }

        if (!IsPlaying)
        {
            return false;
        }

        var nextTime = CurrentTimeSeconds + deltaSeconds;
        return nextTime >= DurationSeconds
            ? SetState(DurationSeconds, false)
            : SetState(nextTime, true);
    }

    private bool SetState(double currentTimeSeconds, bool isPlaying)
    {
        if (CurrentTimeSeconds == currentTimeSeconds && IsPlaying == isPlaying)
        {
            return false;
        }

        CurrentTimeSeconds = currentTimeSeconds;
        IsPlaying = isPlaying;
        Changed?.Invoke(this, new PlaybackChangedEventArgs(CurrentTimeSeconds, IsPlaying));
        return true;
    }
}
