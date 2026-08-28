using Godot;
using PvpGuide.Domain;
using PvpGuide.Editor.Features.Timeline;
using PvpGuide.Editor.Features.TopView;
using PvpGuide.Editor.Features.ViewportSync;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class WorldTransformMapperTests
{
    [Theory]
    [InlineData("Enemy", true)]
    [InlineData("red invader", true)]
    [InlineData("target", true)]
    [InlineData("적대 NPC", true)]
    [InlineData("Hero", false)]
    [InlineData("phantom", false)]
    public void Hostile_roles_use_a_distinct_body_shape(string role, bool expected)
    {
        Assert.Equal(expected, TopViewSurface.UsesHostileBodyShape(role));
    }

    [Fact]
    public void Lock_target_markers_render_after_actor_bodies()
    {
        var layers = TopViewSurface.SemanticDrawLayerOrder;

        Assert.Equal(
            [
                TopViewSemanticDrawLayer.LockLines,
                TopViewSemanticDrawLayer.ActorBodies,
                TopViewSemanticDrawLayer.TargetMarkers,
            ],
            layers);
    }

    [Fact]
    public void World_overlay_label_style_keeps_text_camera_facing()
    {
        var style = WorldViewProjectionAdapter.OverlayLabelStyle;

        Assert.Equal(BaseMaterial3D.BillboardModeEnum.Enabled, style.Billboard);
    }

    [Fact]
    public void Colliding_sanitized_actor_ids_keep_first_exact_and_add_a_stable_suffix_to_the_next()
    {
        Assert.Equal(
            "Actor_a_b",
            WorldViewProjectionAdapter.CreateActorNodeName("a_b", []));
        Assert.Equal(
            "Actor_a_b__0061_002D_0062",
            WorldViewProjectionAdapter.CreateActorNodeName("a-b", ["Actor_a_b"]));
    }

    [Fact]
    public void Domain_position_maps_x_y_z_to_a_pure_double_world_position()
    {
        var position = WorldTransformMapper.ToWorldPosition(new Position3(1.25, -2.5, 3.75));

        Assert.Equal(new WorldPosition(1.25, -2.5, 3.75), position);
    }

    [Fact]
    public void Semantic_lock_line_maps_both_centers_to_world_coordinates()
    {
        var vertices = WorldViewProjectionAdapter.ToWorldLineVertices(
            new SemanticOverlayLine(
                new Position3(1.25, -2.5, 3.75),
                new Position3(-4.5, 6.25, 8.75)));

        Assert.Equal(new WorldPosition(1.25, -2.5, 3.75), vertices.Start);
        Assert.Equal(new WorldPosition(-4.5, 6.25, 8.75), vertices.End);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, -Math.PI / 2)]
    [InlineData(180, -Math.PI)]
    [InlineData(270, -Math.PI * 3 / 2)]
    public void Domain_yaw_maps_to_negative_y_rotation_radians(double yaw, double radians)
    {
        Assert.Equal(radians, WorldTransformMapper.ToRotationYRadians(yaw), precision: 10);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(90, 0, 1)]
    [InlineData(180, -1, 0)]
    [InlineData(270, 0, -1)]
    public void Domain_yaw_rotates_local_positive_x_to_the_same_world_cardinal_direction(
        double yawDegrees,
        double expectedX,
        double expectedZ)
    {
        var facing = WorldFacingTransform.Create(yawDegrees, modelForwardOffsetDegrees: 0);

        Assert.Equal(expectedX, facing.WorldFacingPositiveX.X, precision: 10);
        Assert.Equal(expectedZ, facing.WorldFacingPositiveX.Z, precision: 10);
    }

    [Fact]
    public void Model_forward_offset_changes_only_visual_local_rotation()
    {
        var withoutOffset = WorldFacingTransform.Create(90, modelForwardOffsetDegrees: 0);
        var withOffset = WorldFacingTransform.Create(90, modelForwardOffsetDegrees: 30);

        Assert.Equal(-Math.PI / 2, withoutOffset.ActorRootRotationYRadians, precision: 10);
        Assert.Equal(withoutOffset.ActorRootRotationYRadians, withOffset.ActorRootRotationYRadians);
        Assert.Equal(0, withoutOffset.VisualLocalRotationYRadians);
        Assert.Equal(-Math.PI / 6, withOffset.VisualLocalRotationYRadians, precision: 10);
    }

    [Fact]
    public void Domain_rejects_non_finite_position_before_world_mapping()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(0, double.PositiveInfinity, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(0, 0, double.NegativeInfinity));
    }
}
