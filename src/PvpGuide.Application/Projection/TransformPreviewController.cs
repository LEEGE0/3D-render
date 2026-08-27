using PvpGuide.Application.Editing;
using PvpGuide.Application.Sessions;

namespace PvpGuide.Application.Projection;

public sealed class TransformPreviewController : IDisposable
{
    private readonly DocumentSession _session;
    private readonly ITransformPreviewConsumer _topConsumer;
    private readonly ITransformPreviewConsumer _worldConsumer;
    private bool _disposed;

    public TransformPreviewController(
        DocumentSession session,
        ITransformPreviewConsumer topConsumer,
        ITransformPreviewConsumer worldConsumer)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(topConsumer);
        ArgumentNullException.ThrowIfNull(worldConsumer);
        if (ReferenceEquals(topConsumer, worldConsumer))
        {
            throw new ArgumentException("Top and world consumers must be distinct instances.", nameof(worldConsumer));
        }

        _session = session;
        _topConsumer = topConsumer;
        _worldConsumer = worldConsumer;
        _session.PreviewChanged += OnPreviewChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.PreviewChanged -= OnPreviewChanged;
        _disposed = true;
    }

    private void OnPreviewChanged(object? sender, TransformPreviewChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        _topConsumer.ApplyPreview(eventArgs.Preview);
        _worldConsumer.ApplyPreview(eventArgs.Preview);
    }
}
