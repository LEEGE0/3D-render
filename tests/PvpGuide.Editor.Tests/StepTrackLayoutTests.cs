using PvpGuide.Editor.Features.Timeline;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class StepTrackLayoutTests
{
    [Fact]
    public void Layout_builds_left_hold_segments_to_next_marker_or_document_end()
    {
        var lane = StepTrackLayout.Create(
            durationSeconds: 4,
            width: 220,
            horizontalPadding: 10,
            [new("a0", 1, "idle", false), new("a1", 3, "attack", true)]);

        Assert.Equal((1d, 3d), (lane.Segments[0].StartTimeSeconds, lane.Segments[0].EndTimeSeconds));
        Assert.Equal((3d, 4d), (lane.Segments[1].StartTimeSeconds, lane.Segments[1].EndTimeSeconds));
        Assert.Equal((60d, 160d), (lane.Segments[0].StartX, lane.Segments[0].EndX));
        Assert.Equal((160d, 210d), (lane.Segments[1].StartX, lane.Segments[1].EndX));
        Assert.Equal("a1", lane.HitTest(lane.Markers[1].X, 6));
    }

    [Fact]
    public void Empty_track_has_no_markers_segments_or_hits()
    {
        var lane = StepTrackLayout.Create(4, 220, 10, []);

        Assert.Empty(lane.Markers);
        Assert.Empty(lane.Segments);
        Assert.Null(lane.HitTest(10, 6));
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(8, 10, 4)]
    public void Zero_or_narrow_width_collapses_geometry_without_negative_segments(
        double width,
        double horizontalPadding,
        double expectedX)
    {
        var lane = StepTrackLayout.Create(
            durationSeconds: 4,
            width,
            horizontalPadding,
            [new("a0", 1, "idle", false), new("a1", 3, "attack", true)]);

        Assert.All(lane.Markers, marker => Assert.Equal(expectedX, marker.X));
        Assert.All(lane.Segments, segment =>
        {
            Assert.Equal(expectedX, segment.StartX);
            Assert.Equal(expectedX, segment.EndX);
        });
    }

    [Fact]
    public void First_segment_starts_at_first_marker_and_last_segment_clips_to_document_end()
    {
        var lane = StepTrackLayout.Create(
            durationSeconds: 5,
            width: 110,
            horizontalPadding: 5,
            [new("a0", 2, "idle", false), new("a1", 4, "attack", true)]);

        Assert.Equal(2, lane.Segments.Count);
        Assert.Equal((2d, 4d, 45d, 85d), (
            lane.Segments[0].StartTimeSeconds,
            lane.Segments[0].EndTimeSeconds,
            lane.Segments[0].StartX,
            lane.Segments[0].EndX));
        Assert.Equal((4d, 5d, 85d, 105d), (
            lane.Segments[1].StartTimeSeconds,
            lane.Segments[1].EndTimeSeconds,
            lane.Segments[1].StartX,
            lane.Segments[1].EndX));
    }

    [Fact]
    public void Overlapping_hit_prefers_earlier_time_then_ordinal_id()
    {
        var lane = StepTrackLayout.Create(
            durationSeconds: 4,
            width: 0,
            horizontalPadding: 10,
            [
                new("b", 2, "second", false),
                new("z", 1, "later-id", false),
                new("a", 1, "earlier-id", true),
            ]);

        Assert.Equal("a", lane.HitTest(0, 0));
    }

    [Theory]
    [InlineData("invader", LockOnTrackingMode.KeyframeOnly, "OFF · invader · KEY")]
    [InlineData(null, LockOnTrackingMode.Continuous, "OFF · 없음 · CONT")]
    public void Disabled_lock_on_label_preserves_target_or_explicit_none(
        string? targetActorId,
        LockOnTrackingMode trackingMode,
        string expected)
    {
        var frame = new LockOnKeyframe(
            "lock-disabled",
            timeSeconds: 1,
            enabled: false,
            targetActorId,
            yawOffsetDegrees: 0,
            trackingMode);

        Assert.Equal(expected, LockOnTrackLabelFormatter.Format(frame));
    }
}
