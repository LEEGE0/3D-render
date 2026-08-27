using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Editing;

public sealed class TransformPreview
{
    public TransformPreview(string actorId, string keyframeId, Position3 position, double yawDegrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        var validated = new TransformKeyframe(keyframeId, 0, position, yawDegrees);
        ActorId = actorId;
        KeyframeId = validated.Id;
        Position = validated.Position;
        YawDegrees = validated.YawDegrees;
    }

    public string ActorId { get; }

    public string KeyframeId { get; }

    public Position3 Position { get; }

    public double YawDegrees { get; }
}
