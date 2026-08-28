using System.Collections.Frozen;
using PvpGuide.Domain.Actors;

namespace PvpGuide.Domain.Timeline;

internal static class MovementTrajectoryEvaluator
{
    public static TrajectorySamplePlan CreatePlan(
        double durationSeconds,
        int documentFramesPerSecond,
        IReadOnlyList<ActorTrack> actors,
        TrajectorySamplingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(settings);

        var uniformRate = Math.Min(documentFramesPerSecond, settings.MaximumUniformRate);
        var orderedTimes = new SortedSet<double>();
        if (durationSeconds == 0)
        {
            orderedTimes.Add(0);
        }
        else
        {
            var sampleCountEstimate = Math.Ceiling(durationSeconds * uniformRate);
            if (!double.IsFinite(sampleCountEstimate) || sampleCountEstimate > int.MaxValue)
            {
                throw new InvalidOperationException("The sampling policy would create too many uniform samples.");
            }

            for (var sampleIndex = 0; sampleIndex < (int)sampleCountEstimate; sampleIndex++)
            {
                var timeSeconds = (double)sampleIndex / uniformRate;
                if (timeSeconds >= durationSeconds)
                {
                    break;
                }

                orderedTimes.Add(timeSeconds);
            }

            orderedTimes.Add(0);
            orderedTimes.Add(durationSeconds);
        }

        foreach (var actor in actors)
        {
            foreach (var transform in actor.TransformKeyframes)
            {
                orderedTimes.Add(transform.TimeSeconds);
            }

            foreach (var lockOn in actor.LockOnKeyframes)
            {
                orderedTimes.Add(lockOn.TimeSeconds);
            }
        }

        return new TrajectorySamplePlan(settings.PolicyVersion, uniformRate, orderedTimes);
    }

    public static MovementTrajectorySet Evaluate(
        string documentId,
        long revision,
        long motionRevision,
        double durationSeconds,
        IReadOnlyList<ActorTrack> actors,
        TrajectorySamplePlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasValidFingerprint())
        {
            throw new ArgumentException("Sampling plan fingerprint does not match its payload.", nameof(plan));
        }

        foreach (var timeSeconds in plan.OrderedTimes)
        {
            if (timeSeconds > durationSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(plan),
                    "Sampling plan times must be within the document duration.");
            }
        }

        var evaluation = TrajectoryEvaluationContext.Create(actors, plan);
        var actorTrajectories = new Dictionary<string, ActorMovementTrajectory>(actors.Count, StringComparer.Ordinal);
        long totalSegmentSteps = 0;
        foreach (var actorMetadata in evaluation.Actors)
        {
            var trajectory = EvaluateActor(actorMetadata, evaluation.ActorsById, plan);
            actorTrajectories.Add(actorMetadata.Actor.ActorId, trajectory);
            totalSegmentSteps += trajectory.SegmentSteps;
        }

        return new MovementTrajectorySet(
            documentId,
            revision,
            motionRevision,
            plan.Fingerprint,
            actorTrajectories,
            totalSegmentSteps);
    }

    private static ActorMovementTrajectory EvaluateActor(
        ActorEvaluationMetadata metadata,
        IReadOnlyDictionary<string, ActorTrack> actorsById,
        TrajectorySamplePlan plan)
    {
        var actor = metadata.Actor;
        if (plan.OrderedTimes.Count == 0)
        {
            return new ActorMovementTrajectory(actor.ActorId, [], 0);
        }

        var sampleTimes = plan.OrderedTimes.ToHashSet();
        var transformCursors = new Dictionary<string, ForwardTransformCursor>(StringComparer.Ordinal)
        {
            [actor.ActorId] = new ForwardTransformCursor(actor.TransformKeyframes),
        };
        foreach (var (targetActorId, target) in metadata.ReferencedTargets)
        {
            transformCursors.TryAdd(
                targetActorId,
                new ForwardTransformCursor(target.TransformKeyframes));
        }

        var lockOnCursor = new ForwardLockOnCursor(actor.LockOnKeyframes);
        var facingSweep = new MovementTrajectoryFacingSweep(actorsById);
        var samples = new List<MovementTrajectorySample>(plan.OrderedTimes.Count);
        long canonicalVisits = 0;

        foreach (var timeSeconds in metadata.CanonicalTimes)
        {
            canonicalVisits++;
            var authored = transformCursors[actor.ActorId].Evaluate(timeSeconds);
            var lockOn = lockOnCursor.Evaluate(timeSeconds);
            var targetTransform = lockOn.TargetActorId is { } targetActorId &&
                                  transformCursors.TryGetValue(targetActorId, out var targetCursor)
                ? targetCursor.Evaluate(timeSeconds)
                : (EvaluatedTransform?)null;
            var facing = facingSweep.Advance(authored, lockOn, targetTransform);
            if (!sampleTimes.Contains(timeSeconds))
            {
                continue;
            }

            var anchorKind = TrajectoryAnchorKind.None;
            if (metadata.TransformTimesByActorId[actor.ActorId].Contains(timeSeconds))
            {
                anchorKind |= TrajectoryAnchorKind.ActorTransform;
            }

            if (metadata.LockOnTimes.Contains(timeSeconds))
            {
                anchorKind |= TrajectoryAnchorKind.ActorLockOn;
            }

            if (lockOn.Enabled &&
                lockOn.TargetActorId is { } activeTargetId &&
                metadata.TransformTimesByActorId.TryGetValue(activeTargetId, out var activeTargetTransformTimes) &&
                activeTargetTransformTimes.Contains(timeSeconds))
            {
                anchorKind |= TrajectoryAnchorKind.ActiveTargetTransform;
            }

            samples.Add(new MovementTrajectorySample(
                timeSeconds,
                authored.Position,
                LockOnFacingEvaluator.NormalizeYaw(authored.YawDegrees),
                facing,
                anchorKind));
        }

        var segmentSteps = canonicalVisits +
                           lockOnCursor.SegmentSteps +
                           facingSweep.SegmentSteps +
                           transformCursors.Values.Sum(cursor => cursor.SegmentSteps);
        return new ActorMovementTrajectory(actor.ActorId, samples, segmentSteps);
    }

    private sealed class TrajectoryEvaluationContext
    {
        private TrajectoryEvaluationContext(
            IReadOnlyDictionary<string, ActorTrack> actorsById,
            IReadOnlyList<ActorEvaluationMetadata> actors)
        {
            ActorsById = actorsById;
            Actors = actors;
        }

        public IReadOnlyDictionary<string, ActorTrack> ActorsById { get; }

        public IReadOnlyList<ActorEvaluationMetadata> Actors { get; }

        public static TrajectoryEvaluationContext Create(
            IReadOnlyList<ActorTrack> actors,
            TrajectorySamplePlan plan)
        {
            var actorsById = actors.ToFrozenDictionary(
                actor => actor.ActorId,
                StringComparer.Ordinal);
            var transformTimesByActorId = actors.ToFrozenDictionary(
                actor => actor.ActorId,
                actor => (IReadOnlySet<double>)actor.TransformKeyframes
                    .Select(frame => frame.TimeSeconds)
                    .ToFrozenSet(),
                StringComparer.Ordinal);

            var actorMetadata = new List<ActorEvaluationMetadata>(actors.Count);
            foreach (var actor in actors)
            {
                var referencedTargets = actor.LockOnKeyframes
                    .Select(frame => frame.TargetActorId)
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Where(actorsById.ContainsKey)
                    .ToFrozenDictionary(
                        targetActorId => targetActorId,
                        targetActorId => actorsById[targetActorId],
                        StringComparer.Ordinal);

                var canonicalTimes = new SortedSet<double>(plan.OrderedTimes);
                if (plan.OrderedTimes.Count > 0)
                {
                    var lastSampleTime = plan.OrderedTimes[^1];
                    AddTimesThrough(
                        actor.TransformKeyframes.Select(frame => frame.TimeSeconds),
                        lastSampleTime,
                        canonicalTimes);
                    AddTimesThrough(
                        actor.LockOnKeyframes.Select(frame => frame.TimeSeconds),
                        lastSampleTime,
                        canonicalTimes);
                    foreach (var target in referencedTargets.Values)
                    {
                        AddTimesThrough(
                            target.TransformKeyframes.Select(frame => frame.TimeSeconds),
                            lastSampleTime,
                            canonicalTimes);
                    }
                }

                actorMetadata.Add(new ActorEvaluationMetadata(
                    actor,
                    referencedTargets,
                    transformTimesByActorId,
                    actor.LockOnKeyframes.Select(frame => frame.TimeSeconds).ToFrozenSet(),
                    Array.AsReadOnly(canonicalTimes.ToArray())));
            }

            return new TrajectoryEvaluationContext(actorsById, actorMetadata);
        }

        private static void AddTimesThrough(
            IEnumerable<double> source,
            double lastSampleTime,
            ISet<double> destination)
        {
            foreach (var timeSeconds in source)
            {
                if (timeSeconds > lastSampleTime)
                {
                    break;
                }

                destination.Add(timeSeconds);
            }
        }
    }

    private sealed record ActorEvaluationMetadata(
        ActorTrack Actor,
        IReadOnlyDictionary<string, ActorTrack> ReferencedTargets,
        IReadOnlyDictionary<string, IReadOnlySet<double>> TransformTimesByActorId,
        IReadOnlySet<double> LockOnTimes,
        IReadOnlyList<double> CanonicalTimes);

    private sealed class MovementTrajectoryFacingSweep
    {
        private readonly IReadOnlyDictionary<string, ActorTrack> _actorsById;
        private string? _sourceKeyframeId;
        private LockOnFacingEvaluator.RelativePosition _previousRelative;
        private LockOnFacingEvaluator.RelativePosition _latestValidRelative;
        private bool _hasPreviousRelative;
        private bool _hasLatestValidRelative;
        private EvaluatedActorFacing? _snapFacing;

        public MovementTrajectoryFacingSweep(
            IReadOnlyDictionary<string, ActorTrack> actorsById)
        {
            _actorsById = actorsById;
        }

        public long SegmentSteps { get; private set; }

        public EvaluatedActorFacing Advance(
            EvaluatedTransform authored,
            EvaluatedLockOnState lockOn,
            EvaluatedTransform? targetTransform)
        {
            var sourceChanged = !string.Equals(
                _sourceKeyframeId,
                lockOn.SourceKeyframeId,
                StringComparison.Ordinal);
            if (sourceChanged)
            {
                _sourceKeyframeId = lockOn.SourceKeyframeId;
                _hasPreviousRelative = false;
                _hasLatestValidRelative = false;
                _snapFacing = null;
            }

            if (!lockOn.Enabled)
            {
                return Authored(authored, FacingResolutionKind.AuthoredDisabled, lockOn.SourceKeyframeId);
            }

            if (lockOn.TargetActorId is not { } targetActorId ||
                !_actorsById.ContainsKey(targetActorId) ||
                targetTransform is null)
            {
                return Authored(authored, FacingResolutionKind.TargetUnavailableFallback, lockOn.SourceKeyframeId);
            }

            if (lockOn.TrackingMode == LockOnTrackingMode.KeyframeOnly)
            {
                return Authored(authored, FacingResolutionKind.AuthoredKeyframeOnly, lockOn.SourceKeyframeId);
            }

            var currentRelative = LockOnFacingEvaluator.RelativePosition.Between(
                authored.Position,
                targetTransform.Value.Position);
            if (lockOn.TrackingMode == LockOnTrackingMode.Snap)
            {
                if (sourceChanged || _snapFacing is null)
                {
                    _snapFacing = LockOnFacingEvaluator.IsCoincident(currentRelative)
                        ? Authored(authored, FacingResolutionKind.CoincidentAuthoredFallback, lockOn.SourceKeyframeId)
                        : new EvaluatedActorFacing(
                            LockOnFacingEvaluator.ResolveTargetYaw(
                                currentRelative,
                                lockOn.YawOffsetDegrees,
                                authored.YawDegrees),
                            FacingResolutionKind.SnapTarget,
                            lockOn.SourceKeyframeId);
                }

                return _snapFacing;
            }

            var segmentResolved = false;
            LockOnFacingEvaluator.RelativePosition latestValid = default;
            if (_hasPreviousRelative)
            {
                SegmentSteps++;
                segmentResolved = LockOnFacingEvaluator.TryResolveContinuousSegment(
                    _previousRelative,
                    currentRelative,
                    out latestValid);
            }

            if (segmentResolved)
            {
                _latestValidRelative = latestValid;
                _hasLatestValidRelative = true;
            }
            else if (!_hasPreviousRelative && !LockOnFacingEvaluator.IsCoincident(currentRelative))
            {
                _latestValidRelative = currentRelative;
                _hasLatestValidRelative = true;
            }

            _previousRelative = currentRelative;
            _hasPreviousRelative = true;
            if (!LockOnFacingEvaluator.IsCoincident(currentRelative))
            {
                return new EvaluatedActorFacing(
                    LockOnFacingEvaluator.ResolveTargetYaw(
                        currentRelative,
                        lockOn.YawOffsetDegrees,
                        authored.YawDegrees),
                    FacingResolutionKind.ContinuousTarget,
                    lockOn.SourceKeyframeId);
            }

            if (_hasLatestValidRelative)
            {
                return new EvaluatedActorFacing(
                    LockOnFacingEvaluator.ResolveTargetYaw(
                        _latestValidRelative,
                        lockOn.YawOffsetDegrees,
                        authored.YawDegrees),
                    FacingResolutionKind.CoincidentPrevious,
                    lockOn.SourceKeyframeId);
            }

            return Authored(authored, FacingResolutionKind.CoincidentAuthoredFallback, lockOn.SourceKeyframeId);
        }

        private static EvaluatedActorFacing Authored(
            EvaluatedTransform authored,
            FacingResolutionKind resolutionKind,
            string? sourceKeyframeId) =>
            new(
                LockOnFacingEvaluator.NormalizeYaw(authored.YawDegrees),
                resolutionKind,
                sourceKeyframeId);
    }

    private sealed class ForwardTransformCursor
    {
        private readonly IReadOnlyList<TransformKeyframe> _keyframes;
        private int _leftIndex;

        public ForwardTransformCursor(IReadOnlyList<TransformKeyframe> keyframes)
        {
            _keyframes = keyframes;
        }

        public long SegmentSteps { get; private set; }

        public EvaluatedTransform Evaluate(double timeSeconds)
        {
            while (_leftIndex + 1 < _keyframes.Count &&
                   _keyframes[_leftIndex + 1].TimeSeconds <= timeSeconds)
            {
                _leftIndex++;
                SegmentSteps++;
            }

            var left = _keyframes[_leftIndex];
            if (timeSeconds <= left.TimeSeconds || _leftIndex == _keyframes.Count - 1)
            {
                return new EvaluatedTransform(left.Position, left.YawDegrees);
            }

            var right = _keyframes[_leftIndex + 1];
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

    private sealed class ForwardLockOnCursor
    {
        private readonly IReadOnlyList<LockOnKeyframe> _keyframes;
        private int _index = -1;

        public ForwardLockOnCursor(IReadOnlyList<LockOnKeyframe> keyframes)
        {
            _keyframes = keyframes;
        }

        public long SegmentSteps { get; private set; }

        public EvaluatedLockOnState Evaluate(double timeSeconds)
        {
            while (_index + 1 < _keyframes.Count &&
                   _keyframes[_index + 1].TimeSeconds <= timeSeconds)
            {
                _index++;
                SegmentSteps++;
            }

            if (_index < 0)
            {
                return new EvaluatedLockOnState(
                    null,
                    false,
                    null,
                    0,
                    LockOnTrackingMode.Continuous);
            }

            var frame = _keyframes[_index];
            return new EvaluatedLockOnState(
                frame.Id,
                frame.Enabled,
                frame.TargetActorId,
                frame.YawOffsetDegrees,
                frame.TrackingMode);
        }
    }
}
