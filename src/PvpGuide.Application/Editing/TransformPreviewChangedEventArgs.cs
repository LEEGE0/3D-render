namespace PvpGuide.Application.Editing;

public sealed class TransformPreviewChangedEventArgs(TransformPreview? preview) : EventArgs
{
    public TransformPreview? Preview { get; } = preview;
}
