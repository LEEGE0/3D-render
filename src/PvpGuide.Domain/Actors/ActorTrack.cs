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

        var copiedTransforms = CopySortedUnique(transformKeyframes, static frame => frame.TimeSeconds, static frame => frame.Id, nameof(transformKeyframes));
        if (copiedTransforms.Length == 0)
        {
            throw new ArgumentException("An actor must have at least one transform keyframe.", nameof(transformKeyframes));
        }

        var copiedActions = CopySortedUnique(actionKeyframes, static frame => frame.TimeSeconds, static frame => frame.Id, nameof(actionKeyframes));
        var copiedLockOns = CopySortedUnique(lockOnKeyframes, static frame => frame.TimeSeconds, static frame => frame.Id, nameof(lockOnKeyframes));

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

    public TransformKeyframe GetTransformKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        return _transformKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
            ?? throw new ArgumentException($"Transform keyframe '{keyframeId}' does not exist.", nameof(keyframeId));
    }

    public ActionKeyframe GetActionKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        return _actionKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
            ?? throw new ArgumentException($"Action keyframe '{keyframeId}' does not exist.", nameof(keyframeId));
    }

    public LockOnKeyframe GetLockOnKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        return _lockOnKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
            ?? throw new ArgumentException($"Lock-on keyframe '{keyframeId}' does not exist.", nameof(keyframeId));
    }

    public ActorTrack AddActionKeyframe(ActionKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes.Append(keyframe),
            _lockOnKeyframes);
    }

    public ActorTrack UpdateActionKeyframe(ActionKeyframe expectedCurrent, ActionKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Id != expectedCurrent.Id)
        {
            throw new ArgumentException("Replacement identity must remain unchanged.", nameof(replacement));
        }

        var current = GetActionKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes.Select(frame => frame.Id == current.Id ? replacement : frame),
            _lockOnKeyframes);
    }

    public ActorTrack RemoveActionKeyframe(ActionKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var current = GetActionKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes.Where(frame => frame.Id != current.Id),
            _lockOnKeyframes);
    }

    public ActorTrack AddLockOnKeyframe(LockOnKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes,
            _lockOnKeyframes.Append(keyframe));
    }

    public ActorTrack UpdateLockOnKeyframe(LockOnKeyframe expectedCurrent, LockOnKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Id != expectedCurrent.Id)
        {
            throw new ArgumentException("Replacement identity must remain unchanged.", nameof(replacement));
        }

        var current = GetLockOnKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes,
            _lockOnKeyframes.Select(frame => frame.Id == current.Id ? replacement : frame));
    }

    public ActorTrack RemoveLockOnKeyframe(LockOnKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var current = GetLockOnKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes,
            _actionKeyframes,
            _lockOnKeyframes.Where(frame => frame.Id != current.Id));
    }

    public EvaluatedActionState EvaluateAction(double timeSeconds) =>
        EvaluateHeld(_actionKeyframes, timeSeconds, static frame => frame.TimeSeconds) is { } frame
            ? new(frame.Id, frame.ActionKey)
            : new(null, null);

    public EvaluatedLockOnState EvaluateLockOn(double timeSeconds) =>
        EvaluateHeld(_lockOnKeyframes, timeSeconds, static frame => frame.TimeSeconds) is { } frame
            ? new(frame.Id, frame.Enabled, frame.TargetActorId, frame.YawOffsetDegrees, frame.TrackingMode)
            : new(null, false, null, 0, LockOnTrackingMode.Continuous);

    public ActorTrack ReplaceTransformKeyframe(
        TransformKeyframe expectedCurrent,
        TransformKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.TimeSeconds != expectedCurrent.TimeSeconds)
        {
            throw new ArgumentException("Replacement time must remain unchanged.", nameof(replacement));
        }

        return UpdateTransformKeyframe(expectedCurrent, replacement);
    }

    public ActorTrack UpdateTransformKeyframe(
        TransformKeyframe expectedCurrent,
        TransformKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.Id != expectedCurrent.Id)
        {
            throw new ArgumentException("Replacement identity must remain unchanged.", nameof(replacement));
        }

        var current = GetTransformKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);

        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes.Select(frame => frame.Id == current.Id ? replacement : frame),
            _actionKeyframes,
            _lockOnKeyframes);
    }

    public ActorTrack RemoveTransformKeyframe(TransformKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var current = GetTransformKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        if (_transformKeyframes.Count == 1)
        {
            throw new InvalidOperationException("An actor must keep at least one transform keyframe.");
        }

        return new ActorTrack(
            ActorId,
            DisplayName,
            Role,
            _transformKeyframes.Where(frame => frame.Id != current.Id),
            _actionKeyframes,
            _lockOnKeyframes);
    }

    public EvaluatedTransform Evaluate(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Evaluation time must be finite.");
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

    private static T? EvaluateHeld<T>(
        IReadOnlyList<T> frames,
        double timeSeconds,
        Func<T, double> getTime)
        where T : class
    {
        if (!double.IsFinite(timeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Evaluation time must be finite.");
        }

        for (var index = frames.Count - 1; index >= 0; index--)
        {
            var frame = frames[index];
            if (getTime(frame) <= timeSeconds)
            {
                return frame;
            }
        }

        return null;
    }

    private static T[] CopySortedUnique<T>(
        IEnumerable<T> source,
        Func<T, double> getTime,
        Func<T, string> getId,
        string parameterName)
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

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyframe in copied)
        {
            if (!ids.Add(getId(keyframe)))
            {
                throw new ArgumentException("Keyframe IDs must be unique within a track.", parameterName);
            }
        }

        return copied;
    }

    private static bool SameTransform(TransformKeyframe left, TransformKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Position == right.Position &&
        TransformKeyframe.NormalizeYaw(left.YawDegrees) == TransformKeyframe.NormalizeYaw(right.YawDegrees);

    private static bool SameAction(ActionKeyframe left, ActionKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.ActionKey == right.ActionKey;

    private static bool SameLockOn(LockOnKeyframe left, LockOnKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Enabled == right.Enabled &&
        left.TargetActorId == right.TargetActorId &&
        left.YawOffsetDegrees == right.YawOffsetDegrees &&
        left.TrackingMode == right.TrackingMode;

    private static void ValidateExpected(TransformKeyframe current, TransformKeyframe expected)
    {
        if (!SameTransform(current, expected))
        {
            throw new InvalidOperationException("The transform keyframe changed after the edit began.");
        }
    }

    private static void ValidateExpected(ActionKeyframe current, ActionKeyframe expected)
    {
        if (!SameAction(current, expected))
        {
            throw new InvalidOperationException("The action keyframe changed after the edit began.");
        }
    }

    private static void ValidateExpected(LockOnKeyframe current, LockOnKeyframe expected)
    {
        if (!SameLockOn(current, expected))
        {
            throw new InvalidOperationException("The lock-on keyframe changed after the edit began.");
        }
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
