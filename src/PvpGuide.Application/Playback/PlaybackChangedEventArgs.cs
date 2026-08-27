namespace PvpGuide.Application.Playback;

public sealed class PlaybackChangedEventArgs(double currentTimeSeconds, bool isPlaying) : EventArgs
{
    public double CurrentTimeSeconds { get; } = currentTimeSeconds;

    public bool IsPlaying { get; } = isPlaying;
}
