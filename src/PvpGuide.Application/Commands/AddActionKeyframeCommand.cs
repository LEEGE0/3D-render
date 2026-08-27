using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Commands;

internal sealed class AddActionKeyframeCommand(string actorId, ActionKeyframe keyframe) : ISceneEditCommand
{
    private readonly string _actorId = string.IsNullOrWhiteSpace(actorId)
        ? throw new ArgumentException("Actor ID is required.", nameof(actorId))
        : actorId;
    private readonly ActionKeyframe _keyframe = keyframe ?? throw new ArgumentNullException(nameof(keyframe));

    public bool Execute(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.AddActionKeyframe(_actorId, _keyframe);
        return true;
    }

    public bool Undo(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.RemoveActionKeyframe(_actorId, _keyframe);
        return true;
    }
}
