using PvpGuide.Application.Playback;
using Xunit;

namespace PvpGuide.Application.Tests;

public sealed class PlaybackClockTests
{
    [Fact]
    public void Constructor_requires_positive_finite_duration_and_positive_frame_rate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(0, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(-1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(double.PositiveInfinity, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(1, 0));

        var clock = new PlaybackClock(2, 24);

        Assert.Equal(2, clock.DurationSeconds);
        Assert.Equal(24, clock.FramesPerSecond);
        Assert.Equal(0, clock.CurrentTimeSeconds);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Seek_clamps_to_document_bounds_and_omits_events_for_same_effective_time()
    {
        var clock = new PlaybackClock(2, 30);
        var changes = new List<(double Time, bool IsPlaying)>();
        clock.Changed += (_, args) => changes.Add((args.CurrentTimeSeconds, args.IsPlaying));

        Assert.False(clock.Seek(-4));
        Assert.True(clock.Seek(3));
        Assert.False(clock.Seek(20));
        Assert.True(clock.Seek(0.5));

        Assert.Equal(0.5, clock.CurrentTimeSeconds);
        Assert.Equal([(2d, false), (0.5d, false)], changes);
    }

    [Fact]
    public void Advance_changes_time_only_while_playing_and_auto_pauses_at_end()
    {
        var clock = new PlaybackClock(durationSeconds: 1, framesPerSecond: 30);

        Assert.False(clock.Advance(0.25));
        Assert.True(clock.Play());
        Assert.True(clock.Advance(0.75));
        Assert.True(clock.Advance(0.5));

        Assert.Equal(1, clock.CurrentTimeSeconds);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Advance_crossing_duration_notifies_exactly_one_final_end_state()
    {
        var clock = new PlaybackClock(durationSeconds: 1, framesPerSecond: 30);
        Assert.True(clock.Seek(0.75));
        Assert.True(clock.Play());
        var changes = new List<(double Time, bool IsPlaying)>();
        clock.Changed += (_, args) => changes.Add((args.CurrentTimeSeconds, args.IsPlaying));

        Assert.True(clock.Advance(0.5));

        Assert.Equal([(1d, false)], changes);
    }

    [Fact]
    public void Play_at_end_rewinds_and_notifies_only_its_final_playing_state()
    {
        var clock = new PlaybackClock(1, 30);
        var changes = new List<(double Time, bool IsPlaying)>();
        clock.Changed += (_, args) => changes.Add((args.CurrentTimeSeconds, args.IsPlaying));
        Assert.True(clock.Seek(1));
        changes.Clear();

        Assert.True(clock.Play());

        Assert.Equal(0, clock.CurrentTimeSeconds);
        Assert.True(clock.IsPlaying);
        Assert.Equal([(0d, true)], changes);
    }

    [Fact]
    public void Pause_and_stop_apply_final_state_once_and_ignore_no_op_calls()
    {
        var clock = new PlaybackClock(2, 30);
        var changes = new List<(double Time, bool IsPlaying)>();
        clock.Changed += (_, args) => changes.Add((args.CurrentTimeSeconds, args.IsPlaying));
        clock.Seek(0.5);
        clock.Play();
        changes.Clear();

        Assert.True(clock.Pause());
        Assert.False(clock.Pause());
        Assert.True(clock.Play());
        Assert.True(clock.Stop());
        Assert.False(clock.Stop());

        Assert.Equal(0, clock.CurrentTimeSeconds);
        Assert.False(clock.IsPlaying);
        Assert.Equal([(0.5d, false), (0.5d, true), (0d, false)], changes);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Seek_rejects_non_finite_input(double input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(1, 30).Seek(input));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Advance_rejects_negative_or_non_finite_delta(double input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(1, 30).Advance(input));
}
