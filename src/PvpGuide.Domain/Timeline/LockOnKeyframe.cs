namespace PvpGuide.Domain.Timeline;

public sealed class LockOnKeyframe
{
    public LockOnKeyframe(string id, double timeSeconds, bool enabled, string? targetActorId)
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

        Id = id;
        TimeSeconds = timeSeconds;
        Enabled = enabled;
        TargetActorId = targetActorId;
    }

    public string Id { get; }

    public double TimeSeconds { get; }

    public bool Enabled { get; }

    public string? TargetActorId { get; }
}
