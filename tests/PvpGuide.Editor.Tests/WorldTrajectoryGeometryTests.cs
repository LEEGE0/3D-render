using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.ViewportSync;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class WorldTrajectoryGeometryTests
{
    [Fact]
    public void Path_vertices_preserve_world_x_z_and_add_the_same_positive_y_lift()
    {
        var trajectory = CreateTrajectory(
            Sample(0, new Position3(1.25, -2.5, 3.75), 0, 90),
            Sample(2, new Position3(-4.5, 6.25, 8.75), 180, 270));

        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 4, uniformRate: 30);

        Assert.True(WorldTrajectoryGeometry.TrajectoryLiftY > 0);
        Assert.Equal(
            [
                new WorldPosition(1.25, -2.5 + WorldTrajectoryGeometry.TrajectoryLiftY, 3.75),
                new WorldPosition(-4.5, 6.25 + WorldTrajectoryGeometry.TrajectoryLiftY, 8.75),
            ],
            geometry.SharedPath.Vertices);
        Assert.Equal([0, 0.5], geometry.SharedPath.NormalizedTimes);
    }

    [Fact]
    public void Selected_free_and_lock_ticks_share_the_source_position_without_a_fake_path_offset()
    {
        var source = new Position3(2, 3, 4);
        var trajectory = CreateTrajectory(Sample(1, source, freeYawDegrees: 0, lockYawDegrees: 90));

        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 2, uniformRate: 30);
        var liftedSource = new WorldPosition(2, 3 + WorldTrajectoryGeometry.TrajectoryLiftY, 4);

        Assert.Equal(liftedSource, geometry.FreeFacingTicks.Vertices[0]);
        Assert.Equal(liftedSource, geometry.LockOnFacingTicks.Vertices[0]);
        Assert.Equal(
            new WorldPosition(2 + WorldTrajectoryGeometry.FacingTickLength, liftedSource.Y, 4),
            geometry.FreeFacingTicks.Vertices[1]);
        Assert.Equal(
            new WorldPosition(2, liftedSource.Y, 4 + WorldTrajectoryGeometry.FacingTickLength),
            geometry.LockOnFacingTicks.Vertices[1]);
        Assert.Equal([0.5, 0.5], geometry.FreeFacingTicks.NormalizedTimes);
        Assert.Equal([0.5, 0.5], geometry.LockOnFacingTicks.NormalizedTimes);
    }

    [Fact]
    public void Tick_geometry_uses_the_exact_indices_selected_by_the_shared_policy()
    {
        var trajectory = CreateTrajectory(
            Sample(0, new Position3(0, 0, 0), 0, 0),
            Sample(0.1, new Position3(1, 0, 0), 0, 0),
            Sample(0.2, new Position3(2, 0, 0), 0, 0));

        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 0.2, uniformRate: 10);

        Assert.Equal(3, geometry.SharedPath.Vertices.Count);
        Assert.Equal(4, geometry.FreeFacingTicks.Vertices.Count);
        Assert.Equal(0, geometry.FreeFacingTicks.Vertices[0].X);
        Assert.Equal(2, geometry.FreeFacingTicks.Vertices[2].X);
    }

    [Fact]
    public void Zero_duration_assigns_zero_normalized_time_to_every_vertex()
    {
        var trajectory = CreateTrajectory(Sample(0, new Position3(0, 0, 0), 45, 135));

        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 0, uniformRate: 30);

        Assert.Equal([0], geometry.SharedPath.NormalizedTimes);
        Assert.Equal([0, 0], geometry.FreeFacingTicks.NormalizedTimes);
        Assert.Equal([0, 0], geometry.LockOnFacingTicks.NormalizedTimes);
    }

    [Fact]
    public void Geometry_collections_are_read_only()
    {
        var trajectory = CreateTrajectory(
            Sample(0, new Position3(0, 0, 0), 0, 0),
            Sample(1, new Position3(1, 0, 0), 90, 90));
        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 1, uniformRate: 30);

        Assert.Equal(0, geometry.FreeFacingTicks.Vertices[0].X);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorldPosition>)geometry.SharedPath.Vertices).Add(new WorldPosition(0, 0, 0)));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<double>)geometry.SharedPath.NormalizedTimes).Add(0));
    }

    [Fact]
    public void Zero_duration_rejects_a_sample_later_than_zero_instead_of_collapsing_its_uv()
    {
        var trajectory = CreateTrajectory(Sample(0.01, new Position3(0, 0, 0), 0, 0));

        Assert.Throws<ArgumentException>(() =>
            WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 0, uniformRate: 30));
    }

    [Fact]
    public void Positive_duration_rejects_a_sample_later_than_the_duration()
    {
        var trajectory = CreateTrajectory(Sample(1.01, new Position3(0, 0, 0), 0, 0));

        Assert.Throws<ArgumentException>(() =>
            WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 1, uniformRate: 30));
    }

    [Fact]
    public void Geometry_is_reusable_when_only_current_time_changes()
    {
        var trajectory = CreateTrajectory(
            Sample(0, new Position3(0, 0, 0), 0, 90),
            Sample(1, new Position3(1, 0, 0), 90, 180));
        var geometry = WorldTrajectoryGeometry.Create(trajectory, durationSeconds: 1, uniformRate: 30);

        var beforeSeek = new WorldTrajectoryPresentation(geometry, currentTimeNormalized: 0.25);
        var afterSeek = new WorldTrajectoryPresentation(geometry, currentTimeNormalized: 0.75);

        Assert.Same(beforeSeek.Geometry, afterSeek.Geometry);
        Assert.Equal(0.25, beforeSeek.CurrentTimeNormalized);
        Assert.Equal(0.75, afterSeek.CurrentTimeNormalized);
    }

    private static ActorMovementTrajectory CreateTrajectory(params MovementTrajectorySample[] samples) =>
        new("host", samples, segmentSteps: 0);

    private static MovementTrajectorySample Sample(
        double timeSeconds,
        Position3 position,
        double freeYawDegrees,
        double lockYawDegrees) =>
        new(
            timeSeconds,
            position,
            freeYawDegrees,
            new EvaluatedActorFacing(lockYawDegrees, FacingResolutionKind.AuthoredDisabled, null),
            TrajectoryAnchorKind.None);
}
