using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Trajectory;
using PvpGuide.Editor.Features.TopView;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class TrajectoryOverlayLayoutTests
{
    [Fact]
    public void Draw_layers_keep_trajectory_semantics_below_bodies_and_text()
    {
        Assert.Equal(
            [
                TopViewDrawLayer.SharedPaths,
                TopViewDrawLayer.FreeFacingTicks,
                TopViewDrawLayer.LockOnFacingTicks,
                TopViewDrawLayer.LockLines,
                TopViewDrawLayer.ActorBodies,
                TopViewDrawLayer.TargetMarkers,
                TopViewDrawLayer.Text,
            ],
            TrajectoryOverlayLayout.DrawLayerOrder);
    }

    [Fact]
    public void Four_hertz_uniform_samples_and_many_anchors_are_all_preserved()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            uniformRate: 4,
            Sample(0.00),
            Sample(0.20, anchor: TrajectoryAnchorKind.ActorLockOn),
            Sample(0.25),
            Sample(0.40, anchor: TrajectoryAnchorKind.ActorTransform),
            Sample(0.50),
            Sample(0.60, anchor: TrajectoryAnchorKind.ActiveTargetTransform),
            Sample(0.75),
            Sample(0.80, anchor: TrajectoryAnchorKind.ActorLockOn),
            Sample(1.00)));

        var actor = geometry.Actors["host"];

        Assert.Equal(
            [0.00, 0.20, 0.25, 0.40, 0.50, 0.60, 0.75, 0.80, 1.00],
            actor.FreeFacingTicks.Select(tick => tick.TimeSeconds));
    }

    [Fact]
    public void Below_five_hertz_keeps_a_non_grid_duration_boundary_sample()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            uniformRate: 4,
            Sample(0),
            Sample(0.25),
            Sample(0.50),
            Sample(0.75),
            Sample(0.90)));

        Assert.Equal(
            [0, 0.25, 0.50, 0.75, 0.90],
            geometry.Actors["host"].FreeFacingTicks.Select(tick => tick.TimeSeconds));
    }

    [Fact]
    public void Six_hertz_nearest_uses_uniform_candidate_while_nearby_anchor_is_union_only()
    {
        var oneSixth = 1d / 6;
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            uniformRate: 6,
            Sample(0),
            Sample(oneSixth),
            Sample(0.19, anchor: TrajectoryAnchorKind.ActorLockOn),
            Sample(2d / 6)));

        Assert.Equal(
            [0, oneSixth, 0.19],
            geometry.Actors["host"].FreeFacingTicks.Select(tick => tick.TimeSeconds));
    }

    [Fact]
    public void Nearest_uniform_candidate_uses_earlier_only_for_an_exact_tie()
    {
        var samples = new[] { Sample(0.125), Sample(0.375) };

        var selected = TrajectoryTickSelectionPolicy.SelectNearestSampleIndex(
            samples,
            candidateIndices: [0, 1],
            requestedTimeSeconds: 0.25);

        Assert.Equal(0, selected);
    }

    [Fact]
    public void Nearest_uniform_candidate_accepts_a_later_sample_that_is_five_e_minus_thirteen_closer()
    {
        var samples = new[] { Sample(0.125), Sample(0.375 - 5e-13) };

        var selected = TrajectoryTickSelectionPolicy.SelectNearestSampleIndex(
            samples,
            candidateIndices: [0, 1],
            requestedTimeSeconds: 0.25);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void Anchor_flags_map_transform_to_circle_lock_to_diamond_and_combine_without_loss()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            Sample(0.0, anchor: TrajectoryAnchorKind.ActorTransform),
            Sample(0.1, anchor: TrajectoryAnchorKind.ActorLockOn),
            Sample(
                0.2,
                anchor: TrajectoryAnchorKind.ActorTransform |
                    TrajectoryAnchorKind.ActorLockOn |
                    TrajectoryAnchorKind.ActiveTargetTransform)));

        Assert.Equal(
            [
                TopViewAnchorMarker.TransformCircle,
                TopViewAnchorMarker.LockOnDiamond,
                TopViewAnchorMarker.TransformCircle | TopViewAnchorMarker.LockOnDiamond,
            ],
            geometry.Actors["host"].FreeFacingTicks.Select(tick => tick.AnchorMarker));
    }

    [Fact]
    public void Presentation_marks_past_and_current_full_brightness_and_future_at_forty_five_percent()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            Sample(0.0),
            Sample(0.5),
            Sample(1.0)));

        var presentation = TrajectoryOverlayLayout.CreatePresentation(
            geometry,
            currentTimeSeconds: 0.5,
            selectedActorId: null);

        Assert.Equal([1.0, 1.0, 0.45], presentation.Actors["host"].SharedPath.Select(point => point.Brightness));
        Assert.Equal([1.0, 1.0, 0.45], presentation.Actors["host"].FreeFacingTicks.Select(tick => tick.Brightness));
    }

    [Fact]
    public void Same_yaw_ticks_share_position_but_keep_distinct_endpoint_shapes()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(
            Sample(0, x: 3, z: -2, freeYaw: 45, lockYaw: 45)));

        var actor = geometry.Actors["host"];
        var free = Assert.Single(actor.FreeFacingTicks);
        var locked = Assert.Single(actor.LockOnFacingTicks);

        Assert.Equal(new Position3(3, 0, -2), free.Position);
        Assert.Equal(free.Position, locked.Position);
        Assert.Equal(45, free.YawDegrees);
        Assert.Equal(free.YawDegrees, locked.YawDegrees);
        Assert.Equal(TopViewTickEndpointShape.FreeArrow, free.EndpointShape);
        Assert.Equal(TopViewTickEndpointShape.LockOnBar, locked.EndpointShape);
    }

    [Fact]
    public void Selection_changes_presentation_emphasis_without_replacing_geometry()
    {
        var geometry = TrajectoryOverlayLayout.CreateGeometry(CreateSet(Sample(0)));

        var selected = TrajectoryOverlayLayout.CreatePresentation(geometry, 0, "host");
        var unselected = TrajectoryOverlayLayout.CreatePresentation(geometry, 0, "other");

        Assert.Same(geometry, selected.Geometry);
        Assert.Same(geometry, unselected.Geometry);
        Assert.Same(geometry.Actors["host"], selected.Actors["host"].Geometry);
        Assert.Same(geometry.Actors["host"], unselected.Actors["host"].Geometry);
        Assert.Equal(1.0, selected.Actors["host"].SelectionBrightness);
        Assert.Equal(0.35, unselected.Actors["host"].SelectionBrightness);
    }

    [Fact]
    public void Preview_uses_authored_body_transform_without_replacing_committed_trajectory_identity()
    {
        var frame = CreateFrame(
            CreateSet(Sample(0, freeYaw: 25, lockYaw: 90)),
            authoredYaw: 25,
            resolvedYaw: 90);
        var committed = TrajectoryOverlayLayout.CreateDisplay(frame, previous: null, "host", preview: null);

        var preview = TrajectoryOverlayLayout.WithPreview(
            committed,
            new TransformPreview("host", "transform", new Position3(7, 0, 8), 135));

        Assert.Same(frame.Trajectories, preview.DisplayedTrajectories);
        Assert.Same(committed.Geometry, preview.Geometry);
        Assert.Equal(new Position3(7, 0, 8), preview.ActorBodies["host"].Position);
        Assert.Equal(135, preview.ActorBodies["host"].YawDegrees);
        Assert.Equal(135, preview.ActorBodies["host"].AuthoredYawDegrees);
        Assert.Equal(90, committed.ActorBodies["host"].YawDegrees);
        Assert.Equal(25, committed.ActorBodies["host"].AuthoredYawDegrees);
        Assert.Equal(25, frame.Snapshot.ActorTransforms["host"].YawDegrees);
    }

    [Fact]
    public void Display_reuses_geometry_for_action_only_wrappers_and_replaces_it_for_new_actor_geometry()
    {
        var firstSet = CreateSet(Sample(0));
        var first = TrajectoryOverlayLayout.CreateDisplay(
            CreateFrame(firstSet, authoredYaw: 0, resolvedYaw: 0),
            previous: null,
            selectedActorId: null,
            preview: null);
        var actionOnlySet = firstSet.WithRevision(3);
        var actionOnly = TrajectoryOverlayLayout.CreateDisplay(
            CreateFrame(actionOnlySet, authoredYaw: 0, resolvedYaw: 0, revision: 3),
            first,
            selectedActorId: null,
            preview: null);
        var rebuiltSet = CreateSet(Sample(0, x: 5));
        var rebuilt = TrajectoryOverlayLayout.CreateDisplay(
            CreateFrame(rebuiltSet, authoredYaw: 0, resolvedYaw: 0),
            actionOnly,
            selectedActorId: null,
            preview: null);

        Assert.Same(first.Geometry, actionOnly.Geometry);
        Assert.NotSame(actionOnly.Geometry, rebuilt.Geometry);
    }

    [Fact]
    public void Every_public_output_collection_rejects_external_mutation()
    {
        var display = TrajectoryOverlayLayout.CreateDisplay(
            CreateFrame(CreateSet(Sample(0), Sample(0.5))),
            previous: null,
            selectedActorId: "host",
            preview: null);
        var geometry = display.Geometry;
        var presentation = display.Presentation;

        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ActorTrajectoryOverlayGeometry>)geometry.Actors)
            .Add("other", geometry.Actors["host"]));
        Assert.Throws<NotSupportedException>(() => ((IList<TrajectoryPathPointGeometry>)geometry.Actors["host"].SharedPath)
            .Add(geometry.Actors["host"].SharedPath[0]));
        Assert.Throws<NotSupportedException>(() => ((IList<TrajectoryFacingTickGeometry>)geometry.Actors["host"].FreeFacingTicks)
            .Add(geometry.Actors["host"].FreeFacingTicks[0]));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ActorTrajectoryOverlayPresentation>)presentation.Actors)
            .Add("other", presentation.Actors["host"]));
        Assert.Throws<NotSupportedException>(() => ((IList<TrajectoryPathPointPresentation>)presentation.Actors["host"].SharedPath)
            .Add(presentation.Actors["host"].SharedPath[0]));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, TopViewActorBodyLayout>)display.ActorBodies)
            .Add("other", display.ActorBodies["host"]));
    }

    private static SceneProjectionFrame CreateFrame(
        MovementTrajectorySet set,
        double authoredYaw = 0,
        double resolvedYaw = 0,
        long? revision = null)
    {
        var snapshot = new SceneSnapshot(
            "top-view",
            revision ?? set.Revision,
            timeSeconds: 0,
            new Dictionary<string, EvaluatedTransform>(StringComparer.Ordinal)
            {
                ["host"] = new(new Position3(0, 0, 0), authoredYaw),
            },
            new Dictionary<string, EvaluatedActorTimelineState>(StringComparer.Ordinal),
            new Dictionary<string, EvaluatedActorFacing>(StringComparer.Ordinal)
            {
                ["host"] = new(resolvedYaw, FacingResolutionKind.ContinuousTarget, "lock"),
            },
            set.MotionRevision);

        return new SceneProjectionFrame(snapshot, set, set.SamplingPolicyFingerprint);
    }

    private static MovementTrajectorySet CreateSet(params MovementTrajectorySample[] samples) =>
        CreateSet(uniformRate: 30, samples);

    private static MovementTrajectorySet CreateSet(
        int uniformRate,
        params MovementTrajectorySample[] samples)
    {
        var actor = new ActorMovementTrajectory("host", samples, segmentSteps: samples.Length);
        return new MovementTrajectorySet(
            "top-view",
            revision: 2,
            motionRevision: 2,
            samplingPolicyFingerprint: "top-view-policy",
            uniformRate,
            new Dictionary<string, ActorMovementTrajectory>(StringComparer.Ordinal)
            {
                ["host"] = actor,
            },
            segmentSteps: samples.Length);
    }

    private static MovementTrajectorySample Sample(
        double time,
        double x = 0,
        double z = 0,
        double freeYaw = 0,
        double lockYaw = 90,
        TrajectoryAnchorKind anchor = TrajectoryAnchorKind.None) =>
        new(
            time,
            new Position3(x, 0, z),
            freeYaw,
            new EvaluatedActorFacing(lockYaw, FacingResolutionKind.ContinuousTarget, "lock"),
            anchor);
}
