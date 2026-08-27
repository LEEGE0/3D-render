namespace PvpGuide.Domain.Timeline;

public enum FacingResolutionKind
{
    AuthoredDisabled,
    AuthoredKeyframeOnly,
    SnapTarget,
    ContinuousTarget,
    CoincidentPrevious,
    CoincidentAuthoredFallback,
    TargetUnavailableFallback,
}
