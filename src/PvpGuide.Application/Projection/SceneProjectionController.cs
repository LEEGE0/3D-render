using PvpGuide.Domain;

namespace PvpGuide.Application.Projection;

public sealed class SceneProjectionController : IDisposable
{
    private readonly ISceneSnapshotSource _source;
    private readonly ISceneProjectionConsumer _topConsumer;
    private readonly ISceneProjectionConsumer _worldConsumer;
    private readonly double _timeSeconds;
    private long? _lastProjectedRevision;
    private bool _disposed;

    public SceneProjectionController(
        ISceneSnapshotSource source,
        ISceneProjectionConsumer topConsumer,
        ISceneProjectionConsumer worldConsumer,
        double timeSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(topConsumer);
        ArgumentNullException.ThrowIfNull(worldConsumer);
        if (ReferenceEquals(topConsumer, worldConsumer))
        {
            throw new ArgumentException("Top and world consumers must be distinct instances.", nameof(worldConsumer));
        }

        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Projection time must be finite.");
        }

        _source = source;
        _topConsumer = topConsumer;
        _worldConsumer = worldConsumer;
        _timeSeconds = timeSeconds;
        _source.Changed += OnDocumentChanged;
    }

    public void ProjectCurrent()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = _source.CreateSnapshot(_timeSeconds);
        ProjectSnapshot(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Changed -= OnDocumentChanged;
        _disposed = true;
    }

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        ProjectRevision(eventArgs.Revision);
    }

    private void ProjectRevision(long revision)
    {
        if (_lastProjectedRevision == revision)
        {
            return;
        }

        var snapshot = _source.CreateSnapshot(_timeSeconds);
        ProjectSnapshot(snapshot);
    }

    private void ProjectSnapshot(SceneSnapshot snapshot)
    {
        if (_lastProjectedRevision == snapshot.Revision)
        {
            return;
        }

        _topConsumer.Apply(snapshot);
        _worldConsumer.Apply(snapshot);
        _lastProjectedRevision = snapshot.Revision;
    }
}
