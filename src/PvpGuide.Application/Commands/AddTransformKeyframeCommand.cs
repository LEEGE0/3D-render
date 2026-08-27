using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Commands;

internal sealed class AddTransformKeyframeCommand(string actorId, TransformKeyframe keyframe) : ISceneEditCommand
{
    private readonly string _actorId = string.IsNullOrWhiteSpace(actorId)
        ? throw new ArgumentException("Actor ID is required.", nameof(actorId))
        : actorId;
    private readonly TransformKeyframe _keyframe = keyframe ?? throw new ArgumentNullException(nameof(keyframe));

    public bool Execute(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.AddKeyframe(_actorId, _keyframe);
        return true;
    }

    public bool Undo(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.RemoveTransformKeyframe(_actorId, _keyframe);
        return true;
    }
}
