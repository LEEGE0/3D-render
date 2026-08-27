using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class LockOnKeyframeSelectionChangedEventArgs(
    string? actorId,
    string? keyframeId,
    LockOnKeyframe? keyframe) : EventArgs
{
    public string? ActorId { get; } = actorId;

    public string? KeyframeId { get; } = keyframeId;

    public LockOnKeyframe? Keyframe { get; } = keyframe;
}
