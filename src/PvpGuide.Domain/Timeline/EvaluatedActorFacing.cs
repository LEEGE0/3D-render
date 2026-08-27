namespace PvpGuide.Domain.Timeline;

public sealed record EvaluatedActorFacing(
    double YawDegrees,
    FacingResolutionKind ResolutionKind,
    string? SourceLockOnKeyframeId);
