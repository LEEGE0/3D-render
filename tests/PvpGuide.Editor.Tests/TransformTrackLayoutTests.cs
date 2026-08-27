using PvpGuide.Editor.Features.Timeline;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class TransformTrackLayoutTests
{
    [Fact]
    public void CreateMarkersMapsTimeToPaddedTrackCoordinates()
    {
        var markers = TransformTrackLayout.CreateMarkers(
            durationSeconds: 10,
            width: 200,
            horizontalPadding: 10,
            [("start", 0d), ("middle", 5d), ("end", 10d)]);

        Assert.Equal([10d, 100d, 190d], markers.Select(marker => marker.X));
    }

    [Fact]
    public void HitTestReturnsMarkerWithinRadius()
    {
        var markers = TransformTrackLayout.CreateMarkers(
            10,
            200,
            10,
            [("start", 0d), ("middle", 5d), ("end", 10d)]);

        Assert.Equal("middle", TransformTrackLayout.HitTest(markers, pointerX: 104, hitRadius: 6));
        Assert.Null(TransformTrackLayout.HitTest(markers, pointerX: 120, hitRadius: 6));
    }

    [Fact]
    public void CreateMarkersUsesLeftPaddingWhenDurationIsZero()
    {
        var markers = TransformTrackLayout.CreateMarkers(0, 200, 10, [("a", 0d), ("b", 0d)]);

        Assert.Equal([10d, 10d], markers.Select(marker => marker.X));
    }

    [Fact]
    public void HitTestBreaksDistanceTiesByTimeThenOrdinalId()
    {
        var markers = new TransformTrackMarker[]
        {
            new("z", 2, 98),
            new("b", 1, 102),
            new("a", 1, 102)
        };

        Assert.Equal("a", TransformTrackLayout.HitTest(markers, pointerX: 100, hitRadius: 2));
    }

    [Fact]
    public void CreateMarkersRejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(-1, 200, 10, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(double.NaN, 200, 10, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(10, 0, 10, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(10, 200, -1, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(10, 200, 101, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(10, 200, 10, [("late", 11d)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.CreateMarkers(10, 200, 10, [("early", -1d)]));
    }

    [Fact]
    public void HitTestRejectsNegativeRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.HitTest([], 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformTrackLayout.HitTest([], 0, double.NaN));
    }

    [Fact]
    public void CreateMarkerStates_distinguishes_selected_fill_from_current_time_outline_with_tolerance()
    {
        var markers = new TransformTrackMarker[]
        {
            new("selected", 1, 20),
            new("current", 2, 40),
            new("outside-tolerance", 2.000000002, 60),
        };

        var states = TransformTrackLayout.CreateMarkerStates(
            markers,
            selectedKeyframeId: "selected",
            currentTimeSeconds: 2.0000000005,
            timeToleranceSeconds: 0.000000001);

        Assert.Collection(
            states,
            state =>
            {
                Assert.True(state.IsSelected);
                Assert.False(state.IsAtCurrentTime);
            },
            state =>
            {
                Assert.False(state.IsSelected);
                Assert.True(state.IsAtCurrentTime);
            },
            state =>
            {
                Assert.False(state.IsSelected);
                Assert.False(state.IsAtCurrentTime);
            });
    }
}
