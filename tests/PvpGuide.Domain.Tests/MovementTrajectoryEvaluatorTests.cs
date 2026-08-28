using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class MovementTrajectoryEvaluatorTests
{
    [Fact]
    public void Sample_plan_uses_integer_division_grid_exact_anchors_and_a_stable_fingerprint()
    {
        var document = SceneDocument.Create(
            "grid-document",
            "Grid",
            null,
            1,
            60,
            [
                Track("host",
                    [
                        Frame("host-0", 0, 0, 0, 0),
                        Frame("host-anchor", 0.35, 3.5, 0, 35),
                        Frame("host-1", 1, 10, 0, 90),
                    ]),
            ]);

        var first = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("lock-on-motion/v1", 30));
        var second = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("lock-on-motion/v1", 30));

        Assert.Equal(30, first.UniformRate);
        Assert.Equal(32, first.OrderedTimes.Count);
        Assert.Equal(0d, first.OrderedTimes[0]);
        Assert.Contains(10d / 30, first.OrderedTimes);
        Assert.Contains(0.35, first.OrderedTimes);
        Assert.Equal(1d, first.OrderedTimes[^1]);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.OrderedTimes, second.OrderedTimes);
    }

    public static TheoryData<int, double, double[]> RationalGridCases => new()
    {
        { 24, 0.1, [0d, 1d / 24, 2d / 24, 0.1] },
        { 29, 0.07, [0d, 1d / 29, 2d / 29, 0.07] },
        { 60, 0.05, [0d, 1d / 30, 0.05] },
    };

    [Theory]
    [MemberData(nameof(RationalGridCases))]
    public void Sample_plan_is_deterministic_for_supported_document_rates(
        int framesPerSecond,
        double durationSeconds,
        double[] expected)
    {
        var document = SceneDocument.Create(
            $"rate-{framesPerSecond}",
            "Rate",
            null,
            durationSeconds,
            framesPerSecond,
            [Track("host", [Frame("start", 0, 0, 0, 0)])]);

        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 30));

        Assert.Equal(Math.Min(framesPerSecond, 30), plan.UniformRate);
        Assert.Equal(expected, plan.OrderedTimes);
    }

    [Fact]
    public void Exact_and_nearly_equal_anchor_times_remain_distinct()
    {
        var exactGridTime = 0.5;
        var nearbyAnchorTime = Math.BitIncrement(exactGridTime);
        var document = SceneDocument.Create(
            "near-grid",
            "Near grid",
            null,
            1,
            30,
            [
                Track("host", [Frame("host-grid", exactGridTime, 0, 0, 0)]),
                Track("target", [Frame("target-near", nearbyAnchorTime, 1, 0, 0)]),
            ]);

        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 30));

        var exactIndex = plan.OrderedTimes.IndexOf(exactGridTime);
        Assert.True(exactIndex >= 0);
        Assert.Equal(nearbyAnchorTime, plan.OrderedTimes[exactIndex + 1]);
    }

    [Fact]
    public void Zero_duration_plan_contains_only_zero()
    {
        var document = SceneDocument.Create(
            "zero-duration",
            "Zero",
            null,
            0,
            60,
            [Track("host", [Frame("only", 0, 0, 0, 0)])]);

        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 30));

        Assert.Equal([0d], plan.OrderedTimes);
    }

    [Fact]
    public void Fingerprint_changes_for_each_policy_component_and_preserves_double_bits()
    {
        var original = new TrajectorySamplePlan("v1", 30, [0d, 0.5, 1d]);
        var identical = new TrajectorySamplePlan("v1", 30, [0d, 0.5, 1d]);
        var differentPolicy = new TrajectorySamplePlan("v2", 30, [0d, 0.5, 1d]);
        var differentRate = new TrajectorySamplePlan("v1", 29, [0d, 0.5, 1d]);
        var differentBits = new TrajectorySamplePlan("v1", 30, [0d, Math.BitIncrement(0.5), 1d]);

        Assert.Equal(original.Fingerprint, identical.Fingerprint);
        Assert.NotEqual(original.Fingerprint, differentPolicy.Fingerprint);
        Assert.NotEqual(original.Fingerprint, differentRate.Fingerprint);
        Assert.NotEqual(original.Fingerprint, differentBits.Fingerprint);
    }

    [Fact]
    public void Settings_and_sample_plan_reject_malformed_invariants()
    {
        Assert.Throws<ArgumentException>(() => new TrajectorySamplingSettings(" ", 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectorySamplingSettings("v1", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectorySamplePlan("v1", 30, [-0.1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectorySamplePlan("v1", 30, [double.NaN]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrajectorySamplePlan("v1", 30, [double.PositiveInfinity]));
        Assert.Throws<ArgumentException>(() => new TrajectorySamplePlan("v1", 30, [0.5, 0.25]));
        Assert.Throws<ArgumentException>(() => new TrajectorySamplePlan("v1", 30, [0.5, 0.5]));
        Assert.Throws<ArgumentException>(() => new TrajectorySamplePlan("v1", 30, [0d], "not-the-fingerprint"));

        var document = SceneDocument.Create(
            "range",
            "Range",
            null,
            1,
            30,
            [Track("host", [Frame("only", 0, 0, 0, 0)])]);
        var outside = new TrajectorySamplePlan("v1", 30, [0d, Math.BitIncrement(1d)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateMovementTrajectories(outside));
    }

    [Fact]
    public void Simultaneous_actor_lock_and_active_target_anchors_are_combined()
    {
        var document = CreatePairedDocument();
        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 2));

        var trajectories = document.CreateMovementTrajectories(plan);
        var sample = trajectories.Actors["host"].Samples.Single(item => item.TimeSeconds == 0.5);

        Assert.Equal(
            TrajectoryAnchorKind.ActorTransform |
            TrajectoryAnchorKind.ActorLockOn |
            TrajectoryAnchorKind.ActiveTargetTransform,
            sample.AnchorKind);
        Assert.Equal(new Position3(1, 0, 0), sample.Position);
        Assert.Equal(45, sample.FreeYawDegrees);
        Assert.Equal(90, sample.LockOnFacing.YawDegrees, 10);
        Assert.Equal(FacingResolutionKind.ContinuousTarget, sample.LockOnFacing.ResolutionKind);
    }

    [Fact]
    public void Bulk_sweep_matches_point_facing_at_every_sample_with_linear_segment_steps()
    {
        var document = CreateSweepDocument();
        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 8));
        var actorsById = document.Actors.ToDictionary(actor => actor.ActorId, StringComparer.Ordinal);

        var trajectories = document.CreateMovementTrajectories(plan);
        var host = trajectories.Actors["host"];

        foreach (var sample in host.Samples)
        {
            var expected = LockOnFacingEvaluator.Evaluate(actorsById["host"], actorsById, sample.TimeSeconds);
            Assert.Equal(expected.ResolutionKind, sample.LockOnFacing.ResolutionKind);
            Assert.Equal(expected.SourceLockOnKeyframeId, sample.LockOnFacing.SourceLockOnKeyframeId);
            Assert.Equal(expected.YawDegrees, sample.LockOnFacing.YawDegrees, 10);

            var authored = actorsById["host"].Evaluate(sample.TimeSeconds);
            Assert.Equal(authored.Position, sample.Position);
            Assert.Equal(authored.YawDegrees, sample.FreeYawDegrees, 10);
        }

        const int canonicalSegments = 20;
        Assert.InRange(host.SegmentSteps, 1, canonicalSegments + host.Samples.Count + 4);
    }

    [Fact]
    public void Sparse_target_graph_keeps_unrelated_same_time_keys_out_of_the_host_sweep()
    {
        var baseDocument = CreateSparseDiagnosticDocument([]);
        var expandedDocument = CreateSparseDiagnosticDocument(
            [
                Track("bystander-a", [
                    Frame("a-0", 0, 10, 0, 0),
                    Frame("a-quarter", 0.25, 11, 0, 0),
                    Frame("a-three-quarters", 0.75, 12, 0, 0),
                    Frame("a-1", 1, 13, 0, 0),
                ]),
                Track("bystander-b", [
                    Frame("b-0", 0, -10, 0, 0),
                    Frame("b-quarter", 0.25, -11, 0, 0),
                    Frame("b-three-quarters", 0.75, -12, 0, 0),
                    Frame("b-1", 1, -13, 0, 0),
                ]),
            ]);
        var plan = new TrajectorySamplePlan("v1", 2, [0d, 1d]);

        var baseline = baseDocument.CreateMovementTrajectories(plan);
        var expanded = expandedDocument.CreateMovementTrajectories(plan);

        Assert.Equal(9, baseline.Actors["host"].SegmentSteps);
        Assert.Equal(baseline.Actors["host"].SegmentSteps, expanded.Actors["host"].SegmentSteps);
        Assert.Equal(14, baseline.SegmentSteps);
        Assert.Equal(14, expanded.SegmentSteps - baseline.SegmentSteps);
    }

    [Fact]
    public void Empty_arbitrary_plan_returns_empty_actor_geometry_without_sweep_work()
    {
        var document = CreatePairedDocument();
        var plan = new TrajectorySamplePlan("v1", 30, []);

        var trajectories = document.CreateMovementTrajectories(plan);

        Assert.Equal(document.Actors.Count, trajectories.Actors.Count);
        Assert.All(trajectories.Actors.Values, actor => Assert.Empty(actor.Samples));
        Assert.All(trajectories.Actors.Values, actor => Assert.Equal(0, actor.SegmentSteps));
        Assert.Equal(0, trajectories.SegmentSteps);
    }

    [Fact]
    public void Trajectory_values_are_defensive_read_only_and_validate_nested_invariants()
    {
        var sourceTimes = new[] { 0d, 1d };
        var plan = new TrajectorySamplePlan("v1", 30, sourceTimes);
        sourceTimes[0] = 0.25;
        Assert.Equal(0d, plan.OrderedTimes[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)plan.OrderedTimes).Add(2));

        var sample = new MovementTrajectorySample(
            0,
            new Position3(0, 0, 0),
            0,
            new EvaluatedActorFacing(0, FacingResolutionKind.AuthoredDisabled, null),
            TrajectoryAnchorKind.ActorTransform);
        var sourceSamples = new[] { sample };
        var actor = new ActorMovementTrajectory("host", sourceSamples, 1);
        sourceSamples[0] = new MovementTrajectorySample(
            0,
            new Position3(99, 0, 0),
            0,
            new EvaluatedActorFacing(0, FacingResolutionKind.AuthoredDisabled, null),
            TrajectoryAnchorKind.None);
        Assert.Equal(new Position3(0, 0, 0), actor.Samples[0].Position);
        Assert.Throws<NotSupportedException>(() => ((IList<MovementTrajectorySample>)actor.Samples).Add(sample));

        var sourceActors = new Dictionary<string, ActorMovementTrajectory> { ["host"] = actor };
        var set = new MovementTrajectorySet("document", 2, 1, plan.Fingerprint, sourceActors, 1);
        sourceActors.Clear();
        Assert.Single(set.Actors);
        Assert.Null(set.UniformRate);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ActorMovementTrajectory>)set.Actors).Clear());

        Assert.Throws<ArgumentException>(() => new ActorMovementTrajectory("other", [sample, sample], 0));
        Assert.Throws<ArgumentException>(() => new MovementTrajectorySet(
            "document", 0, 0, plan.Fingerprint,
            new Dictionary<string, ActorMovementTrajectory> { ["wrong-key"] = actor },
            0));
        Assert.Throws<ArgumentException>(() => new MovementTrajectorySample(
            0,
            new Position3(0, 0, 0),
            360,
            new EvaluatedActorFacing(0, FacingResolutionKind.AuthoredDisabled, null),
            TrajectoryAnchorKind.None));
    }

    [Fact]
    public void Evaluation_is_pure_and_WithRevision_reuses_actor_geometry_identity()
    {
        var document = CreatePairedDocument();
        var revision = document.Revision;
        var motionRevision = document.MotionRevision;
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("v1", 30));
        var trajectories = document.CreateMovementTrajectories(plan);
        var sameRevision = trajectories.WithRevision(trajectories.Revision);
        var nextRevision = trajectories.WithRevision(trajectories.Revision + 1);

        Assert.Equal(revision, document.Revision);
        Assert.Equal(motionRevision, document.MotionRevision);
        Assert.Equal(0, notifications);
        Assert.Same(trajectories, sameRevision);
        Assert.NotSame(trajectories, nextRevision);
        Assert.Same(trajectories.Actors, nextRevision.Actors);
        Assert.Same(trajectories.Actors["host"], nextRevision.Actors["host"]);
        Assert.Equal(trajectories.MotionRevision, nextRevision.MotionRevision);
        Assert.Equal(trajectories.SamplingPolicyFingerprint, nextRevision.SamplingPolicyFingerprint);
        Assert.Equal(plan.UniformRate, trajectories.UniformRate);
        Assert.Equal(trajectories.UniformRate, nextRevision.UniformRate);
    }

    private static SceneDocument CreatePairedDocument() => SceneDocument.Create(
        "paired",
        "Paired",
        null,
        1,
        30,
        [
            new ActorTrack(
                "host", "Host", "Hero",
                [Frame("host-start", 0, 0, 0, 0), Frame("host-anchor", 0.5, 1, 0, 45), Frame("host-end", 1, 2, 0, 90)],
                [],
                [new LockOnKeyframe("host-lock", 0.5, true, "target", 0, LockOnTrackingMode.Continuous)]),
            Track("target", [Frame("target-start", 0, 0, 2, 0), Frame("target-anchor", 0.5, 1, 2, 0), Frame("target-end", 1, 2, 2, 0)]),
        ]);

    private static SceneDocument CreateSweepDocument() => SceneDocument.Create(
        "sweep",
        "Sweep",
        null,
        2,
        30,
        [
            new ActorTrack(
                "host", "Host", "Hero",
                [
                    Frame("host-0", 0, -1, 0, 37),
                    Frame("host-1", 1, 1, 0, 37),
                    Frame("host-2", 2, 2, 0, 180),
                ],
                [],
                [
                    new LockOnKeyframe("continuous", 0, true, "target", 0, LockOnTrackingMode.Continuous),
                    new LockOnKeyframe("snap", 1.25, true, "target", -30, LockOnTrackingMode.Snap),
                    new LockOnKeyframe("keyframe", 1.75, true, "target", 45, LockOnTrackingMode.KeyframeOnly),
                ]),
            Track("target", [
                Frame("target-0", 0, 0, 0, 0),
                Frame("target-1", 1, 0, 0, 0),
                Frame("target-2", 2, 0, 2, 0),
            ]),
        ]);

    private static SceneDocument CreateSparseDiagnosticDocument(IEnumerable<ActorTrack> bystanders) =>
        SceneDocument.Create(
            "sparse-diagnostic",
            "Sparse diagnostic",
            null,
            1,
            30,
            [
                new ActorTrack(
                    "host", "Host", "Hero",
                    [Frame("host-0", 0, 0, 0, 0), Frame("host-1", 1, 1, 0, 0)],
                    [],
                    [new LockOnKeyframe("host-lock", 0, true, "target", 0, LockOnTrackingMode.Continuous)]),
                Track("target", [
                    Frame("target-0", 0, 0, 2, 0),
                    Frame("target-half", 0.5, 0.5, 2, 0),
                    Frame("target-1", 1, 1, 2, 0),
                ]),
                .. bystanders,
            ]);

    private static ActorTrack Track(string actorId, IEnumerable<TransformKeyframe> frames) =>
        new(actorId, actorId, actorId, frames, [], []);

    private static TransformKeyframe Frame(
        string id,
        double time,
        double x,
        double z,
        double yaw) =>
        new(id, time, new Position3(x, 0, z), yaw);
}

internal static class ReadOnlyListTestExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(source[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
