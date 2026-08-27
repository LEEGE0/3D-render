namespace PvpGuide.Domain.Timeline;

public sealed class ActionKeyframe
{
    public ActionKeyframe(string id, double timeSeconds, string actionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Keyframe time must be finite and non-negative.");
        }

        Id = id;
        TimeSeconds = timeSeconds;
        ActionKey = actionKey;
    }

    public string Id { get; }

    public double TimeSeconds { get; }

    public string ActionKey { get; }
}
