using PvpGuide.Domain.Actors;

namespace PvpGuide.Domain.Timeline;

public static class LockOnFacingEvaluator
{
    public const double CoincidenceEpsilon = 1e-6;

    private const double CoincidenceEpsilonSquared = CoincidenceEpsilon * CoincidenceEpsilon;
    private const double StationaryRelativeVelocitySquaredEpsilon = double.Epsilon;
    private const double MachineEpsilon = 2.2204460492503131e-16;

    public static EvaluatedActorFacing Evaluate(
        ActorTrack actor,
        IReadOnlyDictionary<string, ActorTrack> actorsById,
        double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorsById);

        var authored = actor.Evaluate(timeSeconds);
        var lockOn = actor.EvaluateLockOn(timeSeconds);
        if (!lockOn.Enabled)
        {
            return new EvaluatedActorFacing(
                NormalizeYaw(authored.YawDegrees),
                FacingResolutionKind.AuthoredDisabled,
                lockOn.SourceKeyframeId);
        }

        if (lockOn.TargetActorId is not { } targetActorId ||
            !actorsById.TryGetValue(targetActorId, out var target))
        {
            return new EvaluatedActorFacing(
                NormalizeYaw(authored.YawDegrees),
                FacingResolutionKind.TargetUnavailableFallback,
                lockOn.SourceKeyframeId);
        }

        if (lockOn.TrackingMode == LockOnTrackingMode.KeyframeOnly)
        {
            return new EvaluatedActorFacing(
                NormalizeYaw(authored.YawDegrees),
                FacingResolutionKind.AuthoredKeyframeOnly,
                lockOn.SourceKeyframeId);
        }

        var sourceKeyframe = actor.GetLockOnKeyframe(lockOn.SourceKeyframeId!);
        if (lockOn.TrackingMode == LockOnTrackingMode.Snap)
        {
            var sourceActor = actor.Evaluate(sourceKeyframe.TimeSeconds);
            var sourceTarget = target.Evaluate(sourceKeyframe.TimeSeconds);
            var sourceRelative = RelativePosition.Between(sourceActor.Position, sourceTarget.Position);
            if (IsCoincident(sourceRelative))
            {
                return new EvaluatedActorFacing(
                    NormalizeYaw(sourceActor.YawDegrees),
                    FacingResolutionKind.CoincidentAuthoredFallback,
                    lockOn.SourceKeyframeId);
            }

            return new EvaluatedActorFacing(
                ResolveTargetYaw(sourceRelative, lockOn.YawOffsetDegrees),
                FacingResolutionKind.SnapTarget,
                lockOn.SourceKeyframeId);
        }

        var currentTarget = target.Evaluate(timeSeconds);
        var currentRelative = RelativePosition.Between(authored.Position, currentTarget.Position);
        if (!IsCoincident(currentRelative))
        {
            return new EvaluatedActorFacing(
                ResolveTargetYaw(currentRelative, lockOn.YawOffsetDegrees),
                FacingResolutionKind.ContinuousTarget,
                lockOn.SourceKeyframeId);
        }

        if (TryFindPreviousDirection(
                actor,
                target,
                sourceKeyframe.TimeSeconds,
                timeSeconds,
                out var previousRelative))
        {
            return new EvaluatedActorFacing(
                ResolveTargetYaw(previousRelative, lockOn.YawOffsetDegrees),
                FacingResolutionKind.CoincidentPrevious,
                lockOn.SourceKeyframeId);
        }

        return new EvaluatedActorFacing(
            NormalizeYaw(authored.YawDegrees),
            FacingResolutionKind.CoincidentAuthoredFallback,
            lockOn.SourceKeyframeId);
    }

    private static bool TryFindPreviousDirection(
        ActorTrack actor,
        ActorTrack target,
        double sourceTimeSeconds,
        double currentTimeSeconds,
        out RelativePosition previousRelative)
    {
        var alignedTimes = new SortedSet<double>
        {
            sourceTimeSeconds,
            currentTimeSeconds,
        };

        AddTransformTimes(actor, sourceTimeSeconds, currentTimeSeconds, alignedTimes);
        AddTransformTimes(target, sourceTimeSeconds, currentTimeSeconds, alignedTimes);

        var times = alignedTimes.ToArray();
        for (var index = times.Length - 2; index >= 0; index--)
        {
            var left = EvaluateRelative(actor, target, times[index]);
            var right = EvaluateRelative(actor, target, times[index + 1]);

            if (!IsCoincident(right))
            {
                previousRelative = right;
                return true;
            }

            if (IsCoincident(left))
            {
                continue;
            }

            previousRelative = FindValidToCoincidentBoundary(
                left,
                right,
                times[index + 1] - times[index]);
            return true;
        }

        previousRelative = default;
        return false;
    }

    private static void AddTransformTimes(
        ActorTrack actor,
        double sourceTimeSeconds,
        double currentTimeSeconds,
        ISet<double> alignedTimes)
    {
        foreach (var keyframe in actor.TransformKeyframes)
        {
            if (keyframe.TimeSeconds >= sourceTimeSeconds && keyframe.TimeSeconds <= currentTimeSeconds)
            {
                alignedTimes.Add(keyframe.TimeSeconds);
            }
        }
    }

    private static RelativePosition EvaluateRelative(ActorTrack actor, ActorTrack target, double timeSeconds) =>
        RelativePosition.Between(actor.Evaluate(timeSeconds).Position, target.Evaluate(timeSeconds).Position);

    private static RelativePosition FindValidToCoincidentBoundary(
        RelativePosition left,
        RelativePosition right,
        double durationSeconds)
    {
        // Parameterize backward from the coincident right endpoint. This makes the
        // forward valid-to-coincident boundary the first exit root in [0, duration).
        var relativeVelocityX = (left.X - right.X) / durationSeconds;
        var relativeVelocityZ = (left.Z - right.Z) / durationSeconds;
        var a =
            (relativeVelocityX * relativeVelocityX) +
            (relativeVelocityZ * relativeVelocityZ);
        if (a <= StationaryRelativeVelocitySquaredEpsilon)
        {
            return left;
        }

        var b = 2 * ((right.X * relativeVelocityX) + (right.Z * relativeVelocityZ));
        var c = right.SquaredLength - CoincidenceEpsilonSquared;
        var fourAC = 4 * a * c;
        var discriminant = (b * b) - fourAC;
        var discriminantTolerance =
            32 * MachineEpsilon * (Math.Abs(b * b) + Math.Abs(fourAC));

        if (double.IsNaN(discriminant) || discriminant < -discriminantTolerance)
        {
            return left;
        }

        if (discriminant < 0)
        {
            discriminant = 0;
        }

        var squareRoot = Math.Sqrt(discriminant);
        var firstRoot = -b / (2 * a);
        var secondRoot = firstRoot;
        if (squareRoot > 0)
        {
            var stableNumerator = -0.5 * (b + Math.CopySign(squareRoot, b));
            firstRoot = stableNumerator / a;
            secondRoot = c / stableNumerator;
        }

        var parameterTolerance = 32 * MachineEpsilon * Math.Max(1, durationSeconds);
        var found = false;
        var boundaryParameter = 0d;

        SelectEntryRoot(firstRoot);
        SelectEntryRoot(secondRoot);
        if (!found)
        {
            return left;
        }

        var clampedParameter = Math.Max(0, boundaryParameter);
        return new RelativePosition(
            right.X + (relativeVelocityX * clampedParameter),
            right.Z + (relativeVelocityZ * clampedParameter));

        void SelectEntryRoot(double root)
        {
            if (!double.IsFinite(root) ||
                root < -parameterTolerance ||
                root >= durationSeconds)
            {
                return;
            }

            var derivative = (2 * a * root) + b;
            var derivativeTolerance =
                32 * MachineEpsilon * (Math.Abs(2 * a * root) + Math.Abs(b));
            if (derivative < -derivativeTolerance)
            {
                return;
            }

            if (!found || root < boundaryParameter)
            {
                boundaryParameter = root;
                found = true;
            }
        }
    }

    private static bool IsCoincident(RelativePosition relative) =>
        relative.SquaredLength <= CoincidenceEpsilonSquared;

    private static double ResolveTargetYaw(RelativePosition relative, double offsetDegrees)
    {
        var targetYaw = Math.Atan2(relative.Z, relative.X) * (180 / Math.PI);
        return NormalizeYaw(targetYaw + offsetDegrees);
    }

    private static double NormalizeYaw(double yawDegrees)
    {
        var normalized = yawDegrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized == 0 ? 0 : normalized;
    }

    private readonly record struct RelativePosition(double X, double Z)
    {
        public double SquaredLength => (X * X) + (Z * Z);

        public static RelativePosition Between(Position3 actor, Position3 target) =>
            new(target.X - actor.X, target.Z - actor.Z);
    }
}
