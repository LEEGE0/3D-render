namespace PvpGuide.Application.Playback;

public interface IPlaybackTimeSource
{
    double CurrentTimeSeconds { get; }

    event EventHandler<PlaybackChangedEventArgs> Changed;
}
