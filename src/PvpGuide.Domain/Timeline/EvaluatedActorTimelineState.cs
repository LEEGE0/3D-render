namespace PvpGuide.Domain.Timeline;

public sealed record EvaluatedActorTimelineState(
    EvaluatedActionState Action,
    EvaluatedLockOnState LockOn);
