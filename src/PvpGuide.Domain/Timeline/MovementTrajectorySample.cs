namespace PvpGuide.Domain.Timeline;

public sealed record MovementTrajectorySample
{
    public MovementTrajectorySample(
        double timeSeconds,
        Position3 position,
        double freeYawDegrees,
        EvaluatedActorFacing lockOnFacing,
        TrajectoryAnchorKind anchorKind)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Sample time must be finite and non-negative.");
        }

        if (!double.IsFinite(freeYawDegrees) || freeYawDegrees < 0 || freeYawDegrees >= 360)
        {
            throw new ArgumentException("Free yaw must be finite and in [0, 360).", nameof(freeYawDegrees));
        }

        ArgumentNullException.ThrowIfNull(lockOnFacing);
        if (!double.IsFinite(lockOnFacing.YawDegrees) ||
            lockOnFacing.YawDegrees < 0 ||
            lockOnFacing.YawDegrees >= 360)
        {
            throw new ArgumentException("Lock-on yaw must be finite and in [0, 360).", nameof(lockOnFacing));
        }

        const TrajectoryAnchorKind allKinds =
            TrajectoryAnchorKind.ActorTransform |
            TrajectoryAnchorKind.ActorLockOn |
            TrajectoryAnchorKind.ActiveTargetTransform;
        if ((anchorKind & ~allKinds) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorKind), "Anchor flags must be defined.");
        }

        TimeSeconds = timeSeconds;
        Position = position;
        FreeYawDegrees = freeYawDegrees == 0 ? 0 : freeYawDegrees;
        LockOnFacing = lockOnFacing;
        AnchorKind = anchorKind;
    }

    public double TimeSeconds { get; }

    public Position3 Position { get; }

    public double FreeYawDegrees { get; }

    public EvaluatedActorFacing LockOnFacing { get; }

    public TrajectoryAnchorKind AnchorKind { get; }
}
