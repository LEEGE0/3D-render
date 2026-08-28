namespace PvpGuide.Domain.Timeline;

[Flags]
public enum TrajectoryAnchorKind
{
    None = 0,
    ActorTransform = 1,
    ActorLockOn = 2,
    ActiveTargetTransform = 4,
}
