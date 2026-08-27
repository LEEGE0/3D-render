using PvpGuide.Domain;
using PvpGuide.Application.Playback;

namespace PvpGuide.Application.Projection;

public sealed class SceneProjectionController : IDisposable
{
    private readonly ISceneSnapshotSource _source;
    private readonly IPlaybackTimeSource _playback;
    private readonly ISceneProjectionConsumer _topConsumer;
    private readonly ISceneProjectionConsumer _worldConsumer;
    private (long Revision, double TimeSeconds)? _lastProjectedKey;
    private bool _disposed;

    public SceneProjectionController(
        ISceneSnapshotSource source,
        IPlaybackTimeSource playback,
        ISceneProjectionConsumer topConsumer,
        ISceneProjectionConsumer worldConsumer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(topConsumer);
        ArgumentNullException.ThrowIfNull(worldConsumer);
        if (ReferenceEquals(topConsumer, worldConsumer))
        {
            throw new ArgumentException("Top and world consumers must be distinct instances.", nameof(worldConsumer));
        }

        _source = source;
        _playback = playback;
        _topConsumer = topConsumer;
        _worldConsumer = worldConsumer;
        _source.Changed += OnDocumentChanged;
        _playback.Changed += OnPlaybackChanged;
    }

    public void ProjectCurrent()
    {
        if (_disposed)
        {
            return;
        }

        ProjectAtCurrentTime();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Changed -= OnDocumentChanged;
        _playback.Changed -= OnPlaybackChanged;
        _disposed = true;
    }

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        ProjectRevisionAtCurrentTime(eventArgs.Revision);
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        ProjectAtCurrentTime();
    }

    private void ProjectAtCurrentTime()
    {
        var snapshot = _source.CreateSnapshot(_playback.CurrentTimeSeconds);
        ProjectSnapshot(snapshot);
    }

    private void ProjectRevisionAtCurrentTime(long revision)
    {
        var timeSeconds = _playback.CurrentTimeSeconds;
        if (_lastProjectedKey == (revision, timeSeconds))
        {
            return;
        }

        var snapshot = _source.CreateSnapshot(timeSeconds);
        ProjectSnapshot(snapshot);
    }

    private void ProjectSnapshot(SceneSnapshot snapshot)
    {
        if (_lastProjectedKey == (snapshot.Revision, snapshot.TimeSeconds))
        {
            return;
        }

        _topConsumer.Apply(snapshot);
        _worldConsumer.Apply(snapshot);
        _lastProjectedKey = (snapshot.Revision, snapshot.TimeSeconds);
    }
}
