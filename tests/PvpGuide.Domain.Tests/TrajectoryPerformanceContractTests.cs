using System.Diagnostics;
using System.Globalization;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class TrajectoryPerformanceContractTests(ITestOutputHelper output)
{
    [Fact]
    public void Diagnostics_expose_public_immutable_counts_from_the_production_result()
    {
        var document = CreateRingDocument(actorCount: 4, keysPerTrack: 10, durationSeconds: 2);
        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("perf/v1", 30));
        var trajectories = document.CreateMovementTrajectories(plan);

        var diagnostics = TrajectoryEvaluationDiagnostics.Create(document, plan, trajectories);

        Assert.Equal(4, diagnostics.ActorCount);
        Assert.Equal(40, diagnostics.TransformKeyCount);
        Assert.Equal(40, diagnostics.LockOnKeyCount);
        Assert.Equal(80, diagnostics.CanonicalKeyCount);
        Assert.Equal(10, diagnostics.CanonicalAnchorTimeCount);
        Assert.Equal(plan.OrderedTimes.Count, diagnostics.PlanSampleCount);
        Assert.Equal(
            trajectories.Actors.Values.Sum(actor => actor.Samples.Count),
            diagnostics.TotalSampleCount);
        Assert.Equal(trajectories.SegmentSteps, diagnostics.EvaluatorSegmentSteps);
    }

    [Fact]
    public void Four_actor_operation_count_grows_linearly_with_samples_and_keys()
    {
        var smaller = EvaluateDiagnostics(actorCount: 4, keysPerTrack: 20, durationSeconds: 2);
        var larger = EvaluateDiagnostics(actorCount: 4, keysPerTrack: 40, durationSeconds: 4);

        Assert.True(larger.TotalSampleCount > smaller.TotalSampleCount);
        Assert.Equal(smaller.CanonicalKeyCount * 2, larger.CanonicalKeyCount);
        Assert.InRange(smaller.EvaluatorSegmentSteps, 1, LinearUpperBound(smaller));
        Assert.InRange(larger.EvaluatorSegmentSteps, 1, LinearUpperBound(larger));
        Assert.InRange(
            larger.EvaluatorSegmentSteps - smaller.EvaluatorSegmentSteps,
            1,
            (2 * (larger.TotalSampleCount - smaller.TotalSampleCount)) +
            (3 * (larger.CanonicalKeyCount - smaller.CanonicalKeyCount)));
    }

    [Fact]
    public void Performance_probe_emits_machine_readable_production_api_measurements_without_a_wall_clock_assertion()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PVP_GUIDE_TRAJECTORY_PERF_PROBE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        MeasureAndWrite("4x100", actorCount: 4, keysPerTrack: 100, durationSeconds: 10, iterations: 40);
        MeasureAndWrite("16x1000", actorCount: 16, keysPerTrack: 1000, durationSeconds: 10, iterations: 12);
    }

    private static long LinearUpperBound(TrajectoryEvaluationDiagnostics diagnostics) =>
        (2 * diagnostics.TotalSampleCount) +
        (2L * diagnostics.TransformKeyCount) +
        diagnostics.LockOnKeyCount +
        (4L * diagnostics.ActorCount);

    private static TrajectoryEvaluationDiagnostics EvaluateDiagnostics(
        int actorCount,
        int keysPerTrack,
        double durationSeconds)
    {
        var document = CreateRingDocument(actorCount, keysPerTrack, durationSeconds);
        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("perf/v1", 30));
        var trajectories = document.CreateMovementTrajectories(plan);
        return TrajectoryEvaluationDiagnostics.Create(document, plan, trajectories);
    }

    private void MeasureAndWrite(
        string fixture,
        int actorCount,
        int keysPerTrack,
        double durationSeconds,
        int iterations)
    {
        var document = CreateRingDocument(actorCount, keysPerTrack, durationSeconds);
        var plan = document.CreateTrajectorySamplePlan(new TrajectorySamplingSettings("perf/v1", 30));

        for (var iteration = 0; iteration < 5; iteration++)
        {
            _ = document.CreateMovementTrajectories(plan);
            _ = document.CreateSnapshot(durationSeconds / 2);
        }

        var buildDurations = Measure(iterations, () => document.CreateMovementTrajectories(plan));
        var snapshotDurations = Measure(
            Math.Max(iterations, 40),
            () => document.CreateSnapshot(durationSeconds / 2));
        var trajectories = document.CreateMovementTrajectories(plan);
        var diagnostics = TrajectoryEvaluationDiagnostics.Create(document, plan, trajectories);

        output.WriteLine(string.Join(
            ' ',
            "TRAJECTORY_PERFORMANCE_RESULT",
            $"fixture={fixture}",
            $"build_p95_ms={Percentile95(buildDurations).ToString("F6", CultureInfo.InvariantCulture)}",
            $"snapshot_p95_ms={Percentile95(snapshotDurations).ToString("F6", CultureInfo.InvariantCulture)}",
            $"actors={diagnostics.ActorCount}",
            $"samples={diagnostics.TotalSampleCount}",
            $"keys={diagnostics.CanonicalKeyCount}",
            $"segment_steps={diagnostics.EvaluatorSegmentSteps}",
            $"plan_samples={diagnostics.PlanSampleCount}",
            $"transform_keys={diagnostics.TransformKeyCount}",
            $"lock_keys={diagnostics.LockOnKeyCount}",
            $"anchor_times={diagnostics.CanonicalAnchorTimeCount}"));
    }

    private static double[] Measure<T>(int iterations, Func<T> operation)
    {
        var durations = new double[iterations];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var started = Stopwatch.GetTimestamp();
            _ = operation();
            durations[iteration] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        return durations;
    }

    private static double Percentile95(double[] durations)
    {
        var ordered = durations.Order().ToArray();
        var index = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95) - 1);
        return ordered[index];
    }

    private static SceneDocument CreateRingDocument(
        int actorCount,
        int keysPerTrack,
        double durationSeconds)
    {
        var actors = new ActorTrack[actorCount];
        for (var actorIndex = 0; actorIndex < actorCount; actorIndex++)
        {
            var actorId = $"actor-{actorIndex}";
            var targetActorId = $"actor-{(actorIndex + 1) % actorCount}";
            var transforms = new TransformKeyframe[keysPerTrack];
            var locks = new LockOnKeyframe[keysPerTrack];
            for (var keyIndex = 0; keyIndex < keysPerTrack; keyIndex++)
            {
                var timeSeconds = keysPerTrack == 1
                    ? 0
                    : durationSeconds * keyIndex / (keysPerTrack - 1d);
                transforms[keyIndex] = new TransformKeyframe(
                    $"{actorId}-transform-{keyIndex}",
                    timeSeconds,
                    new Position3(timeSeconds, 0, actorIndex * 3),
                    (actorIndex * 15 + keyIndex) % 360);
                locks[keyIndex] = new LockOnKeyframe(
                    $"{actorId}-lock-{keyIndex}",
                    timeSeconds,
                    true,
                    targetActorId,
                    0,
                    LockOnTrackingMode.Continuous);
            }

            actors[actorIndex] = new ActorTrack(
                actorId,
                actorId,
                actorIndex == 0 ? "Hero" : "Enemy",
                transforms,
                [],
                locks);
        }

        return SceneDocument.Create(
            $"perf-{actorCount}x{keysPerTrack}",
            "Trajectory performance fixture",
            null,
            durationSeconds,
            60,
            actors);
    }
}
