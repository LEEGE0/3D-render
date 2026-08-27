namespace PvpGuide.Domain.Timeline;

public sealed class LockOnKeyframe
{
    public LockOnKeyframe(string id, double timeSeconds, bool enabled, string? targetActorId)
        : this(id, timeSeconds, enabled, targetActorId, 0, LockOnTrackingMode.Continuous)
    {
    }

    public LockOnKeyframe(
        string id,
        double timeSeconds,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Keyframe time must be finite and non-negative.");
        }

        if (targetActorId is not null && string.IsNullOrWhiteSpace(targetActorId))
        {
            throw new ArgumentException("A lock-on target must be null or a non-empty actor ID.", nameof(targetActorId));
        }

        if (enabled && targetActorId is null)
        {
            throw new ArgumentException("Enabled lock-on keyframes require a target actor.", nameof(targetActorId));
        }

        if (!double.IsFinite(yawOffsetDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(yawOffsetDegrees), "Yaw offset must be finite.");
        }

        Id = id;
        TimeSeconds = timeSeconds;
        Enabled = enabled;
        TargetActorId = targetActorId;
        YawOffsetDegrees = NormalizeYawOffset(yawOffsetDegrees);
        TrackingMode = trackingMode;
    }

    public string Id { get; }

    public double TimeSeconds { get; }

    public bool Enabled { get; }

    public string? TargetActorId { get; }

    public double YawOffsetDegrees { get; }

    public LockOnTrackingMode TrackingMode { get; }

    internal static double NormalizeYawOffset(double yawOffsetDegrees)
    {
        var normalized = yawOffsetDegrees % 360;
        if (normalized >= 180)
        {
            return normalized - 360;
        }

        return normalized < -180 ? normalized + 360 : normalized;
    }
}
