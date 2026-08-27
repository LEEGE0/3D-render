using PvpGuide.Domain;
using PvpGuide.Editor.Features.TopView;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class TopViewCoordinateMapperTests
{
    [Fact]
    public void World_and_screen_mapping_preserve_y_and_map_positive_z_downward()
    {
        var mapper = CreateMapper();
        var screen = mapper.WorldToScreen(new Position3(1, 7, 1));

        Assert.Equal(new ScreenPoint(360, 220), screen);
        Assert.Equal(new Position3(1, 7, 1), mapper.ScreenToWorld(screen, preservedY: 7));
    }

    [Theory]
    [InlineData(360, 180, 320, 180, 0)]
    [InlineData(320, 220, 320, 180, 90)]
    [InlineData(280, 180, 320, 180, 180)]
    [InlineData(320, 140, 320, 180, 270)]
    public void Pointer_yaw_uses_screen_clockwise_angles(double pointerX, double pointerY, double actorX, double actorY, double expected)
    {
        var mapper = CreateMapper();

        Assert.Equal(expected, mapper.PointerYawDegrees(new ScreenPoint(pointerX, pointerY), new ScreenPoint(actorX, actorY)));
    }

    [Fact]
    public void Hit_test_prioritizes_rotation_handle_then_actor_body_and_rejects_outside_panel()
    {
        var mapper = CreateMapper();
        var actor = new ScreenPoint(320, 180);
        var handle = new ScreenPoint(330, 180);

        Assert.Equal(TopViewHitKind.RotationHandle, mapper.HitTest(handle, actor, handle));
        Assert.Equal(TopViewHitKind.ActorBody, mapper.HitTest(new ScreenPoint(304, 180), actor, new ScreenPoint(350, 180)));
        Assert.Equal(TopViewHitKind.None, mapper.HitTest(new ScreenPoint(-0.1, 180), actor, handle));
    }

    [Fact]
    public void Hit_test_accepts_exact_pixel_radius_boundaries()
    {
        var mapper = CreateMapper();
        var actor = new ScreenPoint(320, 180);
        var handle = new ScreenPoint(400, 180);

        Assert.True(mapper.IsActorBodyHit(new ScreenPoint(336, 180), actor));
        Assert.False(mapper.IsActorBodyHit(new ScreenPoint(336.01, 180), actor));
        Assert.True(mapper.IsRotationHandleHit(new ScreenPoint(410, 180), handle));
        Assert.False(mapper.IsRotationHandleHit(new ScreenPoint(410.01, 180), handle));
    }

    [Theory]
    [InlineData(0, 348, 200)]
    [InlineData(90, 320, 228)]
    [InlineData(180, 292, 200)]
    [InlineData(270, 320, 172)]
    [InlineData(-90, 320, 172)]
    [InlineData(450, 320, 228)]
    public void Rotation_handle_position_uses_normalized_screen_clockwise_yaw(double yawDegrees, double expectedX, double expectedY)
    {
        var mapper = CreateMapper();

        var position = mapper.RotationHandlePosition(new ScreenPoint(320, 200), yawDegrees);

        Assert.Equal(expectedX, position.X, precision: 10);
        Assert.Equal(expectedY, position.Y, precision: 10);
    }

    [Fact]
    public void Rotation_handle_position_accepts_an_explicit_positive_distance()
    {
        var mapper = CreateMapper();

        var position = mapper.RotationHandlePosition(new ScreenPoint(320, 200), 0, distancePixels: 10);

        Assert.Equal(new ScreenPoint(330, 200), position);
    }

    [Theory]
    [InlineData(double.NaN, 28)]
    [InlineData(double.PositiveInfinity, 28)]
    [InlineData(0, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void Rotation_handle_position_rejects_non_finite_yaw_or_non_positive_distance(double yawDegrees, double distancePixels)
    {
        var mapper = CreateMapper();

        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.RotationHandlePosition(
            new ScreenPoint(320, 200),
            yawDegrees,
            distancePixels));
    }

    [Fact]
    public void Screen_point_rejects_non_finite_components()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenPoint(0, double.PositiveInfinity));
    }

    private static TopViewCoordinateMapper CreateMapper() => new(
        panelWidth: 640,
        panelHeight: 360,
        centerX: 0,
        centerZ: 0,
        pixelsPerUnit: 40);
}
