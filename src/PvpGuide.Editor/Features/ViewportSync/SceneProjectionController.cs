using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.ViewportSync;

public interface ISceneProjectionConsumer
{
    void Apply(SceneSnapshot snapshot);
}

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
        if (_disposed || _lastProjectedRevision == eventArgs.Revision)
        {
            return;
        }

        var snapshot = _source.CreateSnapshot(_timeSeconds);
        _topConsumer.Apply(snapshot);
        _worldConsumer.Apply(snapshot);
        _lastProjectedRevision = eventArgs.Revision;
    }
}
