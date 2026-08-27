namespace PvpGuide.Domain.Timeline;

public sealed class TransformKeyframe
{
    public TransformKeyframe(string id, double timeSeconds, Position3 position, double yawDegrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Keyframe time must be finite and non-negative.");
        }

        if (!double.IsFinite(yawDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(yawDegrees), "Yaw must be finite.");
        }

        Id = id;
        TimeSeconds = timeSeconds;
        Position = position;
        YawDegrees = NormalizeYaw(yawDegrees);
    }

    public string Id { get; }

    public double TimeSeconds { get; }

    public Position3 Position { get; }

    public double YawDegrees { get; }

    internal static double NormalizeYaw(double yawDegrees)
    {
        var normalized = yawDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
