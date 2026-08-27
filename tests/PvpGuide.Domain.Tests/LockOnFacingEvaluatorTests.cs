using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class LockOnFacingEvaluatorTests
{
    [Fact]
    public void Continuous_resolves_hand_derived_horizontal_direction_and_ignores_height()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 12)],
            [Lock("lock", 0, "target")]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, 4, 7, 3)]);

        var facing = Evaluate(actor, target, 0);

        AssertFacing(36.86989764584402, FacingResolutionKind.ContinuousTarget, "lock", facing);
    }

    [Fact]
    public void Continuous_applies_negative_offset_to_hand_derived_direction()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 12)],
            [Lock("lock", 0, "target", -30)]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, 4, 7, 3)]);

        var facing = Evaluate(actor, target, 0);

        AssertFacing(6.86989764584402, FacingResolutionKind.ContinuousTarget, "lock", facing);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 90)]
    [InlineData(-1, 0, 180)]
    [InlineData(0, -1, 270)]
    public void Continuous_uses_domain_cardinal_yaw_convention(double targetX, double targetZ, double expectedYaw)
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0)],
            [Lock("lock", 0, "target")]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, targetX, 0, targetZ)]);

        var facing = Evaluate(actor, target, 0);

        AssertFacing(expectedYaw, FacingResolutionKind.ContinuousTarget, "lock", facing);
    }

    [Fact]
    public void Snap_holds_source_lock_time_direction_after_both_actors_move()
    {
        var actor = Track(
            "actor",
            [
                Transform("actor-first", 0, 0, 0, 0),
                Transform("actor-last", 2, 2, 0, 0),
            ],
            [Lock("snap", 0.5, "target", mode: LockOnTrackingMode.Snap)]);
        var target = Track(
            "target",
            [
                Transform("target-first", 0, 4, 0, 0),
                Transform("target-last", 2, 2, 0, 4),
            ]);

        var atSource = Evaluate(actor, target, 0.5);
        var afterMovement = Evaluate(actor, target, 2);

        AssertFacing(18.43494882292201, FacingResolutionKind.SnapTarget, "snap", atSource);
        AssertFacing(18.43494882292201, FacingResolutionKind.SnapTarget, "snap", afterMovement);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 45)]
    [InlineData(1, 90)]
    public void Continuous_recomputes_moving_target_direction_at_requested_time(double timeSeconds, double expectedYaw)
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-first", 0, 4, 0, 0),
                Transform("target-last", 1, 0, 0, 4),
            ]);

        var facing = Evaluate(actor, target, timeSeconds);

        AssertFacing(expectedYaw, FacingResolutionKind.ContinuousTarget, "continuous", facing);
    }

    [Fact]
    public void KeyframeOnly_keeps_authored_yaw_and_ignores_nonzero_offset()
    {
        var actor = Track(
            "actor",
            [
                Transform("actor-first", 0, 0, 0, 0, 20),
                Transform("actor-last", 1, 0, 0, 0, 60),
            ],
            [Lock("keyframe-only", 0, "target", 75, LockOnTrackingMode.KeyframeOnly)]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, 0, 0, 10)]);

        var facing = Evaluate(actor, target, 0.5);

        AssertFacing(40, FacingResolutionKind.AuthoredKeyframeOnly, "keyframe-only", facing);
    }

    [Fact]
    public void Before_first_lock_and_disabled_lock_return_authored_yaw()
    {
        var actor = Track(
            "actor",
            [
                Transform("actor-first", 0, 0, 0, 0, 10),
                Transform("actor-last", 3, 0, 0, 0, 70),
            ],
            [
                Lock("enabled", 1, "target"),
                new LockOnKeyframe("disabled", 2, false, "target", 25, LockOnTrackingMode.Snap),
            ]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, 5, 0, 0)]);

        var beforeFirst = Evaluate(actor, target, 0.5);
        var afterDisabled = Evaluate(actor, target, 2.5);

        AssertFacing(20, FacingResolutionKind.AuthoredDisabled, null, beforeFirst);
        AssertFacing(60, FacingResolutionKind.AuthoredDisabled, "disabled", afterDisabled);
    }

    [Theory]
    [InlineData(0.0000005, 37, FacingResolutionKind.CoincidentAuthoredFallback)]
    [InlineData(0.000001, 37, FacingResolutionKind.CoincidentAuthoredFallback)]
    [InlineData(0.0000010000001, 90, FacingResolutionKind.ContinuousTarget)]
    public void Coincidence_epsilon_includes_inside_and_boundary_but_excludes_outside(
        double targetZ,
        double expectedYaw,
        FacingResolutionKind expectedKind)
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [Transform("target-transform", 0, 0, 0, targetZ)]);

        var facing = Evaluate(actor, target, 0);

        AssertFacing(expectedYaw, expectedKind, "continuous", facing);
    }

    [Fact]
    public void Snap_that_starts_coincident_holds_authored_yaw()
    {
        var actor = Track(
            "actor",
            [
                Transform("actor-first", 0, 0, 0, 0, 37),
                Transform("actor-last", 2, 2, 0, 0, 57),
            ],
            [Lock("snap", 0, "target", 45, LockOnTrackingMode.Snap)]);
        var target = Track(
            "target",
            [
                Transform("target-first", 0, 0, 5, 0),
                Transform("target-last", 2, 2, 5, 2),
            ]);

        var facing = Evaluate(actor, target, 2);

        AssertFacing(37, FacingResolutionKind.CoincidentAuthoredFallback, "snap", facing);
    }

    [Fact]
    public void Continuous_coincidence_keeps_latest_valid_direction_in_same_lock_regime()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-first", 0, 1, 0, 0),
                Transform("target-last", 1, 0, 0, 0),
            ]);

        var facing = Evaluate(actor, target, 1);

        AssertFacing(0, FacingResolutionKind.CoincidentPrevious, "continuous", facing);
    }

    [Fact]
    public void Continuous_steps_past_stationary_coincident_segment_to_previous_direction()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-valid", 0, 0, 0, 1),
                Transform("target-inside", 1, 0, 0, 0.0000005),
                Transform("target-stationary", 2, 0, 0, 0.0000005),
            ]);

        var facing = Evaluate(actor, target, 2);

        AssertFacing(90, FacingResolutionKind.CoincidentPrevious, "continuous", facing);
    }

    [Fact]
    public void Continuous_tangent_at_non_key_time_uses_left_limit_when_discriminant_is_zero()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-left", 0, -1, 0, 0.000001),
                Transform("target-right", 1, 1, 0, 0.000001),
            ]);

        var facing = Evaluate(actor, target, 0.5);

        AssertFacing(90, FacingResolutionKind.CoincidentPrevious, "continuous", facing);
    }

    [Fact]
    public void Continuous_full_source_segment_inside_epsilon_uses_current_authored_yaw()
    {
        var actor = Track(
            "actor",
            [
                Transform("actor-first", 0, 0, 0, 0, 17),
                Transform("actor-last", 1, 0, 0, 0, 37),
            ],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-first", 0, 0.0000005, 0, 0),
                Transform("target-last", 1, -0.0000005, 0, 0),
            ]);

        var facing = Evaluate(actor, target, 1);

        AssertFacing(37, FacingResolutionKind.CoincidentAuthoredFallback, "continuous", facing);
    }

    [Fact]
    public void Continuous_non_key_crossing_uses_piecewise_linear_left_limit()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [Lock("continuous", 0, "target")]);
        var target = Track(
            "target",
            [
                Transform("target-left", 0, -3, 0, 0),
                Transform("target-right", 2, 1, 0, 0),
            ]);

        var facing = Evaluate(actor, target, 1.5);

        AssertFacing(180, FacingResolutionKind.CoincidentPrevious, "continuous", facing);
    }

    [Fact]
    public void Missing_target_returns_finite_normalized_authored_fallback()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 725)],
            [Lock("continuous", 0, "missing")]);
        IReadOnlyDictionary<string, ActorTrack> actorsById = new Dictionary<string, ActorTrack>
        {
            [actor.ActorId] = actor,
        };

        var facing = LockOnFacingEvaluator.Evaluate(actor, actorsById, 0);

        AssertFacing(5, FacingResolutionKind.TargetUnavailableFallback, "continuous", facing);
        Assert.True(double.IsFinite(facing.YawDegrees));
        Assert.InRange(facing.YawDegrees, 0, 359.99999999999994);
    }

    [Fact]
    public void New_lock_keyframe_resets_previous_direction_regime()
    {
        var actor = Track(
            "actor",
            [Transform("actor-transform", 0, 0, 0, 0, 37)],
            [
                Lock("first-regime", 0, "target"),
                Lock("second-regime", 1, "target"),
            ]);
        var target = Track(
            "target",
            [
                Transform("target-valid", 0, 0, 0, 1),
                Transform("target-coincident", 1, 0, 0, 0),
                Transform("target-still-coincident", 2, 0, 0, 0),
            ]);

        var beforeReset = Evaluate(actor, target, 0.5);
        var afterReset = Evaluate(actor, target, 2);

        AssertFacing(90, FacingResolutionKind.ContinuousTarget, "first-regime", beforeReset);
        AssertFacing(37, FacingResolutionKind.CoincidentAuthoredFallback, "second-regime", afterReset);
    }

    private static ActorTrack Track(
        string actorId,
        IEnumerable<TransformKeyframe> transforms,
        IEnumerable<LockOnKeyframe>? lockOns = null) =>
        new(actorId, actorId, actorId, transforms, [], lockOns ?? []);

    private static TransformKeyframe Transform(
        string id,
        double timeSeconds,
        double x,
        double y,
        double z,
        double yawDegrees = 0) =>
        new(id, timeSeconds, new Position3(x, y, z), yawDegrees);

    private static LockOnKeyframe Lock(
        string id,
        double timeSeconds,
        string targetActorId,
        double offsetDegrees = 0,
        LockOnTrackingMode mode = LockOnTrackingMode.Continuous) =>
        new(id, timeSeconds, true, targetActorId, offsetDegrees, mode);

    private static EvaluatedActorFacing Evaluate(ActorTrack actor, ActorTrack target, double timeSeconds)
    {
        IReadOnlyDictionary<string, ActorTrack> actorsById = new Dictionary<string, ActorTrack>
        {
            [actor.ActorId] = actor,
            [target.ActorId] = target,
        };

        return LockOnFacingEvaluator.Evaluate(actor, actorsById, timeSeconds);
    }

    private static void AssertFacing(
        double expectedYaw,
        FacingResolutionKind expectedKind,
        string? expectedSourceId,
        EvaluatedActorFacing actual)
    {
        Assert.Equal(expectedYaw, actual.YawDegrees, 12);
        Assert.Equal(expectedKind, actual.ResolutionKind);
        Assert.Equal(expectedSourceId, actual.SourceLockOnKeyframeId);
        Assert.True(double.IsFinite(actual.YawDegrees));
        Assert.True(actual.YawDegrees >= 0 && actual.YawDegrees < 360);
    }
}
