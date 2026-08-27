using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Commands;

internal sealed class ReplaceTransformCommand(
    string actorId,
    TransformKeyframe before,
    TransformKeyframe after) : ISceneEditCommand
{
    private readonly string _actorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
    private readonly TransformKeyframe _before = before ?? throw new ArgumentNullException(nameof(before));
    private readonly TransformKeyframe _after = after ?? throw new ArgumentNullException(nameof(after));

    public bool Execute(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ReplaceTransformKeyframe(_actorId, _before, _after);
    }

    public bool Undo(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.ReplaceTransformKeyframe(_actorId, _after, _before);
    }
}
