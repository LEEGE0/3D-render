using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class ActionKeyframeSelectionChangedEventArgs(
    string? actorId,
    string? keyframeId,
    ActionKeyframe? keyframe) : EventArgs
{
    public string? ActorId { get; } = actorId;

    public string? KeyframeId { get; } = keyframeId;

    public ActionKeyframe? Keyframe { get; } = keyframe;
}
