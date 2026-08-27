using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class TransformKeyframeSelectionChangedEventArgs(
    string? actorId,
    string? keyframeId,
    TransformKeyframe? keyframe) : EventArgs
{
    public string? ActorId { get; } = actorId;

    public string? KeyframeId { get; } = keyframeId;

    public TransformKeyframe? Keyframe { get; } = keyframe;
}
