namespace PvpGuide.Domain.Timeline;

public sealed record EvaluatedLockOnState(
    string? SourceKeyframeId,
    bool Enabled,
    string? TargetActorId,
    double YawOffsetDegrees,
    LockOnTrackingMode TrackingMode);
