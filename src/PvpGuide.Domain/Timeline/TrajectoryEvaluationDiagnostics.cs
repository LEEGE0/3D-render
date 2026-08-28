namespace PvpGuide.Domain.Timeline;

public sealed class TrajectoryEvaluationDiagnostics
{
    private TrajectoryEvaluationDiagnostics(
        int actorCount,
        int planSampleCount,
        long totalSampleCount,
        long evaluatorSegmentSteps,
        int transformKeyCount,
        int lockOnKeyCount,
        int canonicalAnchorTimeCount)
    {
        ActorCount = actorCount;
        PlanSampleCount = planSampleCount;
        TotalSampleCount = totalSampleCount;
        EvaluatorSegmentSteps = evaluatorSegmentSteps;
        TransformKeyCount = transformKeyCount;
        LockOnKeyCount = lockOnKeyCount;
        CanonicalKeyCount = checked(transformKeyCount + lockOnKeyCount);
        CanonicalAnchorTimeCount = canonicalAnchorTimeCount;
    }

    public int ActorCount { get; }

    public int PlanSampleCount { get; }

    public long TotalSampleCount { get; }

    public long EvaluatorSegmentSteps { get; }

    public int TransformKeyCount { get; }

    public int LockOnKeyCount { get; }

    public int CanonicalKeyCount { get; }

    public int CanonicalAnchorTimeCount { get; }

    public static TrajectoryEvaluationDiagnostics Create(
        SceneDocument document,
        TrajectorySamplePlan plan,
        MovementTrajectorySet trajectories)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(trajectories);

        if (!string.Equals(document.DocumentId, trajectories.DocumentId, StringComparison.Ordinal) ||
            document.Revision != trajectories.Revision ||
            document.MotionRevision != trajectories.MotionRevision)
        {
            throw new ArgumentException(
                "Trajectory diagnostics require a result from the supplied document revision.",
                nameof(trajectories));
        }

        if (!string.Equals(plan.Fingerprint, trajectories.SamplingPolicyFingerprint, StringComparison.Ordinal) ||
            trajectories.UniformRate != plan.UniformRate)
        {
            throw new ArgumentException(
                "Trajectory diagnostics require the sampling plan that produced the result.",
                nameof(plan));
        }

        var documentActorIds = document.Actors
            .Select(actor => actor.ActorId)
            .ToHashSet(StringComparer.Ordinal);
        if (!documentActorIds.SetEquals(trajectories.Actors.Keys))
        {
            throw new ArgumentException(
                "Trajectory diagnostics require results for every document actor and no others.",
                nameof(trajectories));
        }

        var transformKeyCount = checked(document.Actors.Sum(actor => actor.TransformKeyframes.Count));
        var lockOnKeyCount = checked(document.Actors.Sum(actor => actor.LockOnKeyframes.Count));
        var canonicalAnchorTimeCount = document.Actors
            .SelectMany(actor => actor.TransformKeyframes.Select(frame => frame.TimeSeconds)
                .Concat(actor.LockOnKeyframes.Select(frame => frame.TimeSeconds)))
            .Distinct()
            .Count();
        var totalSampleCount = document.Actors.Sum(
            actor => (long)trajectories.Actors[actor.ActorId].Samples.Count);

        return new TrajectoryEvaluationDiagnostics(
            document.Actors.Count,
            plan.OrderedTimes.Count,
            totalSampleCount,
            trajectories.SegmentSteps,
            transformKeyCount,
            lockOnKeyCount,
            canonicalAnchorTimeCount);
    }
}
