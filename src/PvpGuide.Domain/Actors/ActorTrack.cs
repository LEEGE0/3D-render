using System.Collections.ObjectModel;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain.Actors;

public sealed class ActorTrack
{
    private readonly IReadOnlyList<TransformKeyframe> _keyframes;

    public ActorTrack(string actorId, IEnumerable<TransformKeyframe> keyframes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(keyframes);

        var copiedKeyframes = keyframes.ToArray();
        Array.Sort(copiedKeyframes, static (left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        for (var index = 1; index < copiedKeyframes.Length; index++)
        {
            if (copiedKeyframes[index - 1].TimeSeconds == copiedKeyframes[index].TimeSeconds)
            {
                throw new ArgumentException("Keyframe times must be unique.", nameof(keyframes));
            }
        }

        ActorId = actorId;
        _keyframes = Array.AsReadOnly(copiedKeyframes);
    }

    public string ActorId { get; }

    public IReadOnlyList<TransformKeyframe> Keyframes => _keyframes;

    public EvaluatedTransform Evaluate(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Evaluation time must be finite.");
        }

        if (_keyframes.Count == 0)
        {
            throw new InvalidOperationException("An empty actor track cannot be evaluated.");
        }

        if (timeSeconds <= _keyframes[0].TimeSeconds)
        {
            return ToTransform(_keyframes[0]);
        }

        if (timeSeconds >= _keyframes[^1].TimeSeconds)
        {
            return ToTransform(_keyframes[^1]);
        }

        for (var index = 1; index < _keyframes.Count; index++)
        {
            var right = _keyframes[index];
            if (timeSeconds <= right.TimeSeconds)
            {
                return Interpolate(_keyframes[index - 1], right, timeSeconds);
            }
        }

        throw new InvalidOperationException("The actor track interpolation interval was not found.");
    }

    private static EvaluatedTransform ToTransform(TransformKeyframe keyframe) =>
        new(keyframe.Position, keyframe.YawDegrees);

    private static EvaluatedTransform Interpolate(
        TransformKeyframe left,
        TransformKeyframe right,
        double timeSeconds)
    {
        var ratio = (timeSeconds - left.TimeSeconds) / (right.TimeSeconds - left.TimeSeconds);
        var position = new Position3(
            left.Position.X + ((right.Position.X - left.Position.X) * ratio),
            left.Position.Y + ((right.Position.Y - left.Position.Y) * ratio),
            left.Position.Z + ((right.Position.Z - left.Position.Z) * ratio));
        var yawDelta = TransformKeyframe.NormalizeYaw(right.YawDegrees - left.YawDegrees);
        if (yawDelta > 180)
        {
            yawDelta -= 360;
        }

        return new EvaluatedTransform(
            position,
            TransformKeyframe.NormalizeYaw(left.YawDegrees + (yawDelta * ratio)));
    }
}
