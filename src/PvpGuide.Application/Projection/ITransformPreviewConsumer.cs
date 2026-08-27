using PvpGuide.Application.Editing;

namespace PvpGuide.Application.Projection;

public interface ITransformPreviewConsumer
{
    void ApplyPreview(TransformPreview? preview);
}
