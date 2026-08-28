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

        var actorsById = actors.ToDictionary(actor => actor.ActorId, StringComparer.Ordinal);
        var actorTrajectories = new Dictionary<string, ActorMovementTrajectory>(actors.Count, StringComparer.Ordinal);
        long totalSegmentSteps = 0;
        foreach (var actor in actors)
        {
            var trajectory = EvaluateActor(actor, actorsById, plan);
            actorTrajectories.Add(actor.ActorId, trajectory);
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
        ActorTrack actor,
        IReadOnlyDictionary<string, ActorTrack> actorsById,
        TrajectorySamplePlan plan)
    {
        if (plan.OrderedTimes.Count == 0)
        {
            return new ActorMovementTrajectory(actor.ActorId, [], 0);
        }

        var lastSampleTime = plan.OrderedTimes[^1];
        var canonicalTimes = new SortedSet<double>(plan.OrderedTimes);
        foreach (var candidate in actorsById.Values)
        {
            foreach (var transform in candidate.TransformKeyframes)
            {
                if (transform.TimeSeconds <= lastSampleTime)
                {
                    canonicalTimes.Add(transform.TimeSeconds);
                }
            }
        }

        foreach (var lockOn in actor.LockOnKeyframes)
        {
            if (lockOn.TimeSeconds <= lastSampleTime)
            {
                canonicalTimes.Add(lockOn.TimeSeconds);
            }
        }

        var sampleTimes = plan.OrderedTimes.ToHashSet();
        var actorTransformTimes = actor.TransformKeyframes
            .Select(frame => frame.TimeSeconds)
            .ToHashSet();
        var transformTimesByActorId = actorsById.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.TransformKeyframes.Select(frame => frame.TimeSeconds).ToHashSet(),
            StringComparer.Ordinal);
        var actorLockOnTimes = actor.LockOnKeyframes
            .Select(frame => frame.TimeSeconds)
            .ToHashSet();
        var transformCursors = actorsById.ToDictionary(
            pair => pair.Key,
            pair => new ForwardTransformCursor(pair.Value.TransformKeyframes),
            StringComparer.Ordinal);
        var lockOnCursor = new ForwardLockOnCursor(actor.LockOnKeyframes);
        var facingSweep = new MovementTrajectoryFacingSweep(actorsById);
        var samples = new List<MovementTrajectorySample>(plan.OrderedTimes.Count);
        long segmentSteps = 0;
        var firstCanonical = true;

        foreach (var timeSeconds in canonicalTimes)
        {
            var authored = transformCursors[actor.ActorId].Evaluate(timeSeconds);
            var lockOn = lockOnCursor.Evaluate(timeSeconds);
            var targetTransform = lockOn.TargetActorId is { } targetActorId &&
                                  transformCursors.TryGetValue(targetActorId, out var targetCursor)
                ? targetCursor.Evaluate(timeSeconds)
                : (EvaluatedTransform?)null;
            var facing = facingSweep.Advance(authored, lockOn, targetTransform);

            if (!firstCanonical)
            {
                segmentSteps++;
            }

            firstCanonical = false;
            if (!sampleTimes.Contains(timeSeconds))
            {
                continue;
            }

            var anchorKind = TrajectoryAnchorKind.None;
            if (actorTransformTimes.Contains(timeSeconds))
            {
                anchorKind |= TrajectoryAnchorKind.ActorTransform;
            }

            if (actorLockOnTimes.Contains(timeSeconds))
            {
                anchorKind |= TrajectoryAnchorKind.ActorLockOn;
            }

            if (lockOn.Enabled &&
                lockOn.TargetActorId is { } activeTargetId &&
                transformTimesByActorId.TryGetValue(activeTargetId, out var activeTargetTransformTimes) &&
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

        return new ActorMovementTrajectory(actor.ActorId, samples, segmentSteps);
    }

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

            if (_hasPreviousRelative &&
                LockOnFacingEvaluator.TryResolveContinuousSegment(
                    _previousRelative,
                    currentRelative,
                    out var latestValid))
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

        public EvaluatedTransform Evaluate(double timeSeconds)
        {
            while (_leftIndex + 1 < _keyframes.Count &&
                   _keyframes[_leftIndex + 1].TimeSeconds <= timeSeconds)
            {
                _leftIndex++;
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

        public EvaluatedLockOnState Evaluate(double timeSeconds)
        {
            while (_index + 1 < _keyframes.Count &&
                   _keyframes[_index + 1].TimeSeconds <= timeSeconds)
            {
                _index++;
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
