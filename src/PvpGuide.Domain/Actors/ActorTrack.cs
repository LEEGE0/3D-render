using System.Collections.ObjectModel;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain.Actors;

public sealed class ActorTrack
{
    private readonly ReadOnlyCollection<TransformKeyframe> _transformKeyframes;
    private readonly ReadOnlyCollection<ActionKeyframe> _actionKeyframes;
    private readonly ReadOnlyCollection<LockOnKeyframe> _lockOnKeyframes;

    public ActorTrack(string actorId, IEnumerable<TransformKeyframe> keyframes)
        : this(actorId, actorId, actorId, keyframes, [], [])
    {
    }

    public ActorTrack(
        string actorId,
        string displayName,
        string role,
        IEnumerable<TransformKeyframe> transformKeyframes,
        IEnumerable<ActionKeyframe> actionKeyframes,
        IEnumerable<LockOnKeyframe> lockOnKeyframes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(transformKeyframes);
        ArgumentNullException.ThrowIfNull(actionKeyframes);
        ArgumentNullException.ThrowIfNull(lockOnKeyframes);

        var copiedTransforms = CopySortedUnique(transformKeyframes, static frame => frame.TimeSeconds, nameof(transformKeyframes));
        var copiedActions = CopySortedUnique(actionKeyframes, static frame => frame.TimeSeconds, nameof(actionKeyframes));
        var copiedLockOns = CopySortedUnique(lockOnKeyframes, static frame => frame.TimeSeconds, nameof(lockOnKeyframes));

        ActorId = actorId;
        DisplayName = displayName;
        Role = role;
        _transformKeyframes = Array.AsReadOnly(copiedTransforms);
        _actionKeyframes = Array.AsReadOnly(copiedActions);
        _lockOnKeyframes = Array.AsReadOnly(copiedLockOns);
    }

    public string ActorId { get; }

    public string DisplayName { get; }

    public string Role { get; }

    public IReadOnlyList<TransformKeyframe> TransformKeyframes => _transformKeyframes;

    public IReadOnlyList<ActionKeyframe> ActionKeyframes => _actionKeyframes;

    public IReadOnlyList<LockOnKeyframe> LockOnKeyframes => _lockOnKeyframes;

    public IReadOnlyList<TransformKeyframe> Keyframes => _transformKeyframes;

    public EvaluatedTransform Evaluate(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Evaluation time must be finite.");
        }

        if (_transformKeyframes.Count == 0)
        {
            throw new InvalidOperationException("An empty actor track cannot be evaluated.");
        }

        if (timeSeconds <= _transformKeyframes[0].TimeSeconds)
        {
            return ToTransform(_transformKeyframes[0]);
        }

        if (timeSeconds >= _transformKeyframes[^1].TimeSeconds)
        {
            return ToTransform(_transformKeyframes[^1]);
        }

        for (var index = 1; index < _transformKeyframes.Count; index++)
        {
            var right = _transformKeyframes[index];
            if (timeSeconds <= right.TimeSeconds)
            {
                return Interpolate(_transformKeyframes[index - 1], right, timeSeconds);
            }
        }

        throw new InvalidOperationException("The actor track interpolation interval was not found.");
    }

    private static EvaluatedTransform ToTransform(TransformKeyframe keyframe) =>
        new(keyframe.Position, keyframe.YawDegrees);

    private static T[] CopySortedUnique<T>(IEnumerable<T> source, Func<T, double> getTime, string parameterName)
    {
        var copied = source.ToArray();
        if (copied.Any(item => item is null))
        {
            throw new ArgumentException("Tracks cannot contain null keyframes.", parameterName);
        }

        Array.Sort(copied, (left, right) => getTime(left).CompareTo(getTime(right)));
        for (var index = 1; index < copied.Length; index++)
        {
            if (getTime(copied[index - 1]) == getTime(copied[index]))
            {
                throw new ArgumentException("Keyframe times must be unique within a track.", parameterName);
            }
        }

        return copied;
    }

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
