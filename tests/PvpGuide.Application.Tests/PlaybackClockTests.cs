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

    [Fact]
    public void Reentrant_pause_and_seek_publish_fifo_states_with_matching_public_state_for_every_observer()
    {
        var clock = new PlaybackClock(3, 30);
        var firstObserver = new List<(
            double EventTime,
            bool EventIsPlaying,
            double PublicTime,
            bool PublicIsPlaying)>();
        var secondObserver = new List<(
            double EventTime,
            bool EventIsPlaying,
            double PublicTime,
            bool PublicIsPlaying)>();
        var requestedFollowup = false;
        clock.Changed += (_, args) =>
        {
            firstObserver.Add((
                args.CurrentTimeSeconds,
                args.IsPlaying,
                clock.CurrentTimeSeconds,
                clock.IsPlaying));
            if (requestedFollowup)
            {
                return;
            }

            requestedFollowup = true;
            Assert.True(clock.Pause());
            Assert.True(clock.Seek(2));
        };
        clock.Changed += (_, args) => secondObserver.Add((
            args.CurrentTimeSeconds,
            args.IsPlaying,
            clock.CurrentTimeSeconds,
            clock.IsPlaying));

        Assert.True(clock.Play());

        var expected = new[]
        {
            (0d, true, 0d, true),
            (0d, false, 0d, false),
            (2d, false, 2d, false)
        };
        Assert.Equal(expected, firstObserver);
        Assert.Equal(expected, secondObserver);
        Assert.Equal(2, clock.CurrentTimeSeconds);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Throwing_later_observer_does_not_discard_accepted_reentrant_states()
    {
        var clock = new PlaybackClock(3, 30);
        var expectedFailure = new ApplicationException("later playback observer failed");
        var firstObserverStates = new List<(double Time, bool IsPlaying)>();
        var secondObserverStates = new List<(double Time, bool IsPlaying)>();
        var finalObserverStates = new List<(double Time, bool IsPlaying)>();
        var requestedFollowup = false;
        var callbackDepth = 0;
        var maximumCallbackDepth = 0;
        clock.Changed += (_, args) =>
        {
            callbackDepth++;
            maximumCallbackDepth = Math.Max(maximumCallbackDepth, callbackDepth);
            try
            {
                firstObserverStates.Add((args.CurrentTimeSeconds, args.IsPlaying));
                if (!requestedFollowup)
                {
                    requestedFollowup = true;
                    Assert.True(clock.Pause());
                    Assert.True(clock.Seek(2));
                }
            }
            finally
            {
                callbackDepth--;
            }
        };
        clock.Changed += (_, args) =>
        {
            secondObserverStates.Add((args.CurrentTimeSeconds, args.IsPlaying));
            if (args.CurrentTimeSeconds == 0 && args.IsPlaying)
            {
                throw expectedFailure;
            }
        };
        clock.Changed += (_, args) =>
            finalObserverStates.Add((args.CurrentTimeSeconds, args.IsPlaying));

        var exception = Record.Exception(() => clock.Play());

        Assert.Same(expectedFailure, exception);
        Assert.Equal([(0d, true), (0d, false), (2d, false)], firstObserverStates);
        Assert.Equal([(0d, true), (0d, false), (2d, false)], secondObserverStates);
        Assert.Equal([(0d, false), (2d, false)], finalObserverStates);
        Assert.Equal(1, maximumCallbackDepth);
        Assert.Equal(2, clock.CurrentTimeSeconds);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Alternating_reentrant_changes_keep_payload_and_public_state_equal_until_bound_failure()
    {
        var clock = new PlaybackClock(3, 30);
        var callbackDepth = 0;
        var maximumCallbackDepth = 0;
        var callbackCount = 0;
        clock.Changed += (_, args) =>
        {
            callbackDepth++;
            maximumCallbackDepth = Math.Max(maximumCallbackDepth, callbackDepth);
            try
            {
                callbackCount++;
                Assert.True(clock.Seek(args.CurrentTimeSeconds == 1 ? 2 : 1));
                Assert.Equal(args.CurrentTimeSeconds, clock.CurrentTimeSeconds);
                Assert.Equal(args.IsPlaying, clock.IsPlaying);
            }
            finally
            {
                callbackDepth--;
            }
        };

        var exception = Record.Exception(() => clock.Seek(1));

        Assert.Equal(1, maximumCallbackDepth);
        Assert.InRange(callbackCount, 1, 32);
        var boundedFailure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Playback state notification did not stabilize.", boundedFailure.Message);
    }

    [Fact]
    public void Bounded_non_stabilization_takes_precedence_over_an_earlier_observer_failure()
    {
        var clock = new PlaybackClock(3, 30);
        var observerFailure = new ApplicationException("first observer cycle failed");
        var callbackCount = 0;
        var observerFailed = false;
        clock.Changed += (_, args) =>
        {
            callbackCount++;
            Assert.True(clock.Seek(args.CurrentTimeSeconds == 1 ? 2 : 1));
        };
        clock.Changed += (_, _) =>
        {
            if (!observerFailed)
            {
                observerFailed = true;
                throw observerFailure;
            }
        };

        var exception = Record.Exception(() => clock.Seek(1));

        Assert.True(observerFailed);
        Assert.Equal(32, callbackCount);
        var boundedFailure = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Playback state notification did not stabilize.", boundedFailure.Message);
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
