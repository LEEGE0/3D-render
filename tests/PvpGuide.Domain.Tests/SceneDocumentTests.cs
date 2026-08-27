using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class SceneDocumentTests
{
    [Fact]
    public void Lock_on_keyframe_rejects_undefined_tracking_mode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LockOnKeyframe(
            "lock",
            1,
            false,
            null,
            0,
            (LockOnTrackingMode)99));
    }

    private static readonly double[] NonFiniteValues = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    [Fact]
    public void Position3_rejects_non_finite_x_components_with_x_parameter_name()
    {
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(nonFiniteValue, 0, 0));
            Assert.Equal("x", exception.ParamName);
        }
    }

    [Fact]
    public void Position3_rejects_non_finite_y_components_with_y_parameter_name()
    {
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(0, nonFiniteValue, 0));
            Assert.Equal("y", exception.ParamName);
        }
    }

    [Fact]
    public void Position3_rejects_non_finite_z_components_with_z_parameter_name()
    {
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Position3(0, 0, nonFiniteValue));
            Assert.Equal("z", exception.ParamName);
        }
    }

    [Fact]
    public void TransformKeyframe_normalizes_yaw_and_rejects_invalid_required_values()
    {
        var keyframe = new TransformKeyframe("key-1", 2, new Position3(1, 2, 3), -10);

        Assert.Equal(350, keyframe.YawDegrees);
        Assert.Throws<ArgumentException>(() => new TransformKeyframe("", 2, new Position3(0, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransformKeyframe("key-2", -1, new Position3(0, 0, 0), 0));
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TransformKeyframe("key-3", nonFiniteValue, new Position3(0, 0, 0), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TransformKeyframe("key-4", 0, new Position3(0, 0, 0), nonFiniteValue));
        }
    }

    [Fact]
    public void ActorTrack_evaluates_hand_calculated_position_and_shortest_yaw_path()
    {
        var track = new ActorTrack("actor-1", [
            new TransformKeyframe("left", 2, new Position3(-2, 4, 10), 350),
            new TransformKeyframe("right", 6, new Position3(6, -4, 2), 10),
        ]);

        Assert.Equal(new Position3(0, 2, 8), track.Evaluate(3).Position);
        Assert.Equal(355, track.Evaluate(3).YawDegrees);
        Assert.Equal(new Position3(2, 0, 6), track.Evaluate(4).Position);
        Assert.Equal(0, track.Evaluate(4).YawDegrees);
    }

    [Fact]
    public void ActorTrack_uses_positive_direction_for_exact_half_turn_ties()
    {
        var zeroTo180 = new ActorTrack("zero-to-180", [
            new TransformKeyframe("first", 0, new Position3(0, 0, 0), 0),
            new TransformKeyframe("last", 2, new Position3(0, 0, 0), 180),
        ]);
        var oneEightyToZero = new ActorTrack("180-to-zero", [
            new TransformKeyframe("first", 0, new Position3(0, 0, 0), 180),
            new TransformKeyframe("last", 2, new Position3(0, 0, 0), 0),
        ]);

        Assert.Equal(90, zeroTo180.Evaluate(1).YawDegrees);
        Assert.Equal(270, oneEightyToZero.Evaluate(1).YawDegrees);
    }

    [Fact]
    public void ActorTrack_sorts_keyframes_by_time_and_interpolates_reverse_yaw_across_zero()
    {
        var track = new ActorTrack("actor-1", [
            new TransformKeyframe("later", 2, new Position3(2, 0, 0), 350),
            new TransformKeyframe("earlier", 1, new Position3(0, 0, 0), 10),
        ]);

        Assert.Equal([1d, 2d], track.Keyframes.Select(keyframe => keyframe.TimeSeconds));
        Assert.Equal(new Position3(1, 0, 0), track.Evaluate(1.5).Position);
        Assert.Equal(0, track.Evaluate(1.5).YawDegrees);
    }

    [Fact]
    public void ActorTrack_clamps_to_first_and_last_keyframes_and_rejects_duplicate_times()
    {
        var track = new ActorTrack("actor-1", [
            new TransformKeyframe("first", 2, new Position3(1, 2, 3), 10),
            new TransformKeyframe("last", 6, new Position3(4, 5, 6), 20),
        ]);

        Assert.Equal(new EvaluatedTransform(new Position3(1, 2, 3), 10), track.Evaluate(1));
        Assert.Equal(new EvaluatedTransform(new Position3(4, 5, 6), 20), track.Evaluate(7));
        Assert.Throws<ArgumentException>(() => new ActorTrack("actor-1", [
            new TransformKeyframe("first", 2, new Position3(0, 0, 0), 0),
            new TransformKeyframe("duplicate", 2, new Position3(0, 0, 0), 0),
        ]));
    }

    [Fact]
    public void ActorTrack_rejects_empty_transform_keyframes()
    {
        Assert.Throws<ArgumentException>(() => new ActorTrack("actor-1", []));
    }

    [Fact]
    public void SceneDocument_only_changes_revision_and_raises_event_for_successful_mutations()
    {
        var document = new SceneDocument("document-1", 10, 30);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("first", 2, new Position3(0, 0, 0), 0),
        ]));
        document.AddKeyframe("actor-1", new TransformKeyframe("second", 3, new Position3(1, 0, 0), 0));

        Assert.Equal(2, document.Revision);
        Assert.Equal(2, notifications);
        Assert.Throws<ArgumentException>(() => document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("third", 4, new Position3(2, 0, 0), 0),
        ])));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddKeyframe("actor-1", new TransformKeyframe("outside", 11, new Position3(0, 0, 0), 0)));

        Assert.Equal(2, document.Revision);
        Assert.Equal(2, notifications);
        Assert.Single(document.Actors);
        Assert.Equal(2, document.Actors[0].Keyframes.Count);
    }

    [Fact]
    public void SceneDocument_returns_snapshot_with_evaluated_transforms_and_defensive_actor_collection()
    {
        var document = new SceneDocument("document-1", 10, 30);
        document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("first", 0, new Position3(0, 0, 0), 350),
        ]));
        document.AddKeyframe("actor-1", new TransformKeyframe("last", 10, new Position3(10, 20, 30), 10));

        var snapshot = document.CreateSnapshot(5);

        Assert.Equal("document-1", snapshot.DocumentId);
        Assert.Equal(2, snapshot.Revision);
        Assert.Equal(5, snapshot.TimeSeconds);
        Assert.Equal(new Position3(5, 10, 15), snapshot.ActorTransforms["actor-1"].Position);
        Assert.Equal(0, snapshot.ActorTransforms["actor-1"].YawDegrees);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, EvaluatedTransform>)snapshot.ActorTransforms).Add("intruder", new EvaluatedTransform(new Position3(0, 0, 0), 0)));
        Assert.Single(snapshot.ActorTransforms);
    }

    [Fact]
    public void SceneDocument_rejects_invalid_duration_and_non_positive_integer_fps()
    {
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SceneDocument("document-1", nonFiniteValue, 30));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneDocument("document-1", -1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneDocument("document-1", 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneDocument("document-1", 1, -1));
        Assert.IsType<int>(new SceneDocument("document-1", 1, 30).FramesPerSecond);
    }

    [Fact]
    public void SceneDocument_rejects_invalid_snapshot_times()
    {
        var document = new SceneDocument("document-1", 10, 30);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateSnapshot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateSnapshot(11));
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => document.CreateSnapshot(nonFiniteValue));
        }
    }

    [Fact]
    public void SceneDocument_sorts_late_added_keyframes_and_preserves_state_when_time_is_duplicated()
    {
        var document = new SceneDocument("document-1", 10, 30);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("last", 6, new Position3(6, 0, 0), 10),
        ]));

        document.AddKeyframe("actor-1", new TransformKeyframe("first", 2, new Position3(2, 0, 0), 350));

        Assert.Equal([2d, 6d], document.Actors[0].Keyframes.Select(keyframe => keyframe.TimeSeconds));
        Assert.Equal(new Position3(3, 0, 0), document.CreateSnapshot(3).ActorTransforms["actor-1"].Position);
        Assert.Equal(355, document.CreateSnapshot(3).ActorTransforms["actor-1"].YawDegrees);
        Assert.Throws<ArgumentException>(() => document.AddKeyframe("actor-1", new TransformKeyframe("duplicate", 2, new Position3(0, 0, 0), 0)));

        Assert.Equal(2, document.Revision);
        Assert.Equal(2, notifications);
        Assert.Equal([2d, 6d], document.Actors[0].Keyframes.Select(keyframe => keyframe.TimeSeconds));
    }

    [Fact]
    public void ReplaceTransformKeyframe_changes_pose_once_and_preserves_track_meaning()
    {
        var document = CreateEditableDocument();
        var before = document.GetTransformKeyframe("host", "host-first");
        var after = new TransformKeyframe(before.Id, before.TimeSeconds, new Position3(4, 2, 6), 90);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        var changed = document.ReplaceTransformKeyframe("host", before, after);

        Assert.True(changed);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Equal(after.Position, document.CreateSnapshot(before.TimeSeconds).ActorTransforms["host"].Position);
        Assert.Equal(90, document.CreateSnapshot(before.TimeSeconds).ActorTransforms["host"].YawDegrees);
        var host = document.Actors.Single(a => a.ActorId == "host");
        Assert.Equal("Host", host.DisplayName);
        Assert.Equal("Hero", host.Role);
        Assert.Equal(["idle"], host.ActionKeyframes.Select(k => k.ActionKey));
        Assert.Equal(["host-lock"], host.LockOnKeyframes.Select(k => k.Id));
        Assert.Equal(["host-second"], host.TransformKeyframes.Where(k => k.Id != after.Id).Select(k => k.Id));
        Assert.Equal("target", document.Actors.Single(a => a.ActorId == "target").ActorId);
    }

    [Fact]
    public void ReplaceTransformKeyframe_same_value_is_a_no_op()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var before = document.GetTransformKeyframe("host", "host-first");
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        var changed = document.ReplaceTransformKeyframe(
            "host",
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, before.Position, before.YawDegrees + 360));

        Assert.False(changed);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Equal(before, document.GetTransformKeyframe("host", "host-first"));
    }

    [Fact]
    public void ReplaceTransformKeyframe_rejects_missing_actor_without_mutation()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var before = document.GetTransformKeyframe("host", "host-first");
        var expected = new TransformKeyframe("missing", 0, new Position3(0, 0, 0), 0);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => document.ReplaceTransformKeyframe(
            "missing-actor", expected, expected));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Equal(before, document.GetTransformKeyframe("host", "host-first"));
    }

    [Fact]
    public void ReplaceTransformKeyframe_rejects_missing_keyframe_without_mutation()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var expected = new TransformKeyframe("missing", 0, new Position3(0, 0, 0), 0);
        var before = document.GetTransformKeyframe("host", "host-first");
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => document.ReplaceTransformKeyframe("host", expected, expected));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Equal(before, document.GetTransformKeyframe("host", "host-first"));
    }

    [Fact]
    public void ReplaceTransformKeyframe_rejects_stale_expected_without_mutation()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var current = document.GetTransformKeyframe("host", "host-first");
        var stalePosition = new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(9, 2, 6), current.YawDegrees);
        var staleYaw = new TransformKeyframe(current.Id, current.TimeSeconds, current.Position, current.YawDegrees + 45);
        var replacement = new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(4, 2, 6), 90);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<InvalidOperationException>(() => document.ReplaceTransformKeyframe("host", stalePosition, replacement));
        Assert.Throws<InvalidOperationException>(() => document.ReplaceTransformKeyframe("host", staleYaw, replacement));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Equal(current, document.GetTransformKeyframe("host", "host-first"));
    }

    [Fact]
    public void ReplaceTransformKeyframe_rejects_changed_identity_or_time_without_mutation()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var current = document.GetTransformKeyframe("host", "host-first");
        var changedId = new TransformKeyframe("new-id", current.TimeSeconds, new Position3(4, 2, 6), 90);
        var changedTime = new TransformKeyframe(current.Id, current.TimeSeconds + 1, new Position3(4, 2, 6), 90);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => document.ReplaceTransformKeyframe("host", current, changedId));
        Assert.Throws<ArgumentException>(() => document.ReplaceTransformKeyframe("host", current, changedTime));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Equal(current, document.GetTransformKeyframe("host", "host-first"));
    }

    [Fact]
    public void UpdateTransformKeyframe_moves_time_and_pose_once()
    {
        var document = SceneDocument.Create("document-1", "Document", null, 10, 30, [
            new ActorTrack("host", [
                new TransformKeyframe("host-first", 0, new Position3(0, 0, 0), 0),
                new TransformKeyframe("host-second", 4, new Position3(2, 3, 4), 20),
            ])
        ]);
        var before = document.GetTransformKeyframe("host", "host-second");
        var after = new TransformKeyframe(before.Id, 3, new Position3(8, 4, 6), 120);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        var changed = document.UpdateTransformKeyframe("host", before, after);

        Assert.True(changed);
        Assert.Equal([0d, 3d], document.Actors.Single(a => a.ActorId == "host")
            .TransformKeyframes.Select(frame => frame.TimeSeconds));
        Assert.Equal(new Position3(5.333333333333333, 2.6666666666666665, 4), document.CreateSnapshot(2).ActorTransforms["host"].Position);
        Assert.Equal(80, document.CreateSnapshot(2).ActorTransforms["host"].YawDegrees);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void UpdateTransformKeyframe_rejects_conflicts_and_preserves_document_state()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var before = document.GetTransformKeyframe("host", "host-second");
        var stale = new TransformKeyframe(before.Id, before.TimeSeconds, new Position3(9, 2, 6), before.YawDegrees);
        var after = new TransformKeyframe(before.Id, 3, new Position3(8, 4, 6), 120);
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => document.UpdateTransformKeyframe("host", before,
            new TransformKeyframe("changed-id", 3, before.Position, before.YawDegrees)));
        Assert.Throws<ArgumentException>(() => document.UpdateTransformKeyframe("host", before,
            new TransformKeyframe(before.Id, 1, before.Position, before.YawDegrees)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.UpdateTransformKeyframe("host", before,
            new TransformKeyframe(before.Id, document.DurationSeconds + 1, before.Position, before.YawDegrees)));
        Assert.Throws<InvalidOperationException>(() => document.UpdateTransformKeyframe("host", stale, after));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Same(beforeActors.Single(actor => actor.ActorId == "host"), document.Actors.Single(actor => actor.ActorId == "host"));
    }

    [Fact]
    public void UpdateTransformKeyframe_same_normalized_transform_is_a_no_op()
    {
        var document = CreateEditableDocument();
        var beforeActors = document.Actors.ToArray();
        var before = document.GetTransformKeyframe("host", "host-second");
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        var changed = document.UpdateTransformKeyframe("host", before,
            new TransformKeyframe(before.Id, before.TimeSeconds, before.Position, before.YawDegrees + 360));

        Assert.False(changed);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Same(beforeActors.Single(actor => actor.ActorId == "host"), document.Actors.Single(actor => actor.ActorId == "host"));
    }

    [Fact]
    public void RemoveTransformKeyframe_removes_once_and_rejects_stale_or_invalid_requests_without_mutation()
    {
        var document = CreateEditableDocument();
        var second = document.GetTransformKeyframe("host", "host-second");
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        document.RemoveTransformKeyframe("host", second);

        Assert.Single(document.Actors.Single(a => a.ActorId == "host").TransformKeyframes);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);

        var beforeActors = document.Actors.ToArray();
        var remaining = document.GetTransformKeyframe("host", "host-first");
        var stale = new TransformKeyframe(remaining.Id, remaining.TimeSeconds, new Position3(9, 2, 6), remaining.YawDegrees);
        var missing = new TransformKeyframe("missing", 0, new Position3(0, 0, 0), 0);

        Assert.Throws<InvalidOperationException>(() => document.RemoveTransformKeyframe("host", stale));
        Assert.Throws<ArgumentException>(() => document.RemoveTransformKeyframe("missing-actor", remaining));
        Assert.Throws<ArgumentException>(() => document.RemoveTransformKeyframe("host", missing));
        Assert.Throws<InvalidOperationException>(() => document.RemoveTransformKeyframe("host", remaining));

        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Equal(beforeActors, document.Actors);
        Assert.Same(beforeActors.Single(actor => actor.ActorId == "host"), document.Actors.Single(actor => actor.ActorId == "host"));
    }

    [Fact]
    public void ActorTrack_rejects_duplicate_ids_within_each_track_but_allows_ids_across_track_types()
    {
        Assert.Throws<ArgumentException>(() => new ActorTrack(
            "actor", "Actor", "Role",
            [
                new TransformKeyframe("same", 0, new Position3(0, 0, 0), 0),
                new TransformKeyframe("same", 1, new Position3(1, 0, 0), 0),
            ], [], []));
        Assert.Throws<ArgumentException>(() => new ActorTrack(
            "actor", "Actor", "Role", [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)],
            [new ActionKeyframe("same", 0, "idle"), new ActionKeyframe("same", 1, "walk")], []));
        Assert.Throws<ArgumentException>(() => new ActorTrack(
            "actor", "Actor", "Role", [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)], [],
            [new LockOnKeyframe("same", 0, false, null), new LockOnKeyframe("same", 1, false, null)]));

        var allowed = new ActorTrack(
            "actor", "Actor", "Role",
            [new TransformKeyframe("same", 0, new Position3(0, 0, 0), 0)],
            [new ActionKeyframe("same", 1, "idle")],
            [new LockOnKeyframe("same", 2, false, null)]);
        Assert.Equal("same", allowed.TransformKeyframes[0].Id);
        Assert.Equal("same", allowed.ActionKeyframes[0].Id);
        Assert.Equal("same", allowed.LockOnKeyframes[0].Id);
    }

    [Fact]
    public void Snapshot_evaluates_action_and_lock_as_left_hold_states()
    {
        var document = CreateSemanticDocument();

        var before = document.CreateSnapshot(0.25).ActorTimelineStates["host"];
        Assert.Null(before.Action.ActionKey);
        Assert.False(before.LockOn.Enabled);

        var between = document.CreateSnapshot(1.5).ActorTimelineStates["host"];
        Assert.Equal("attack", between.Action.ActionKey);
        Assert.Equal("host-action-1", between.Action.SourceKeyframeId);
        Assert.True(between.LockOn.Enabled);
        Assert.Equal("invader", between.LockOn.TargetActorId);
        Assert.Equal(LockOnTrackingMode.Continuous, between.LockOn.TrackingMode);
    }

    [Fact]
    public void Action_update_and_remove_require_full_current_preimage()
    {
        var document = CreateSemanticDocument();
        var before = document.GetActionKeyframe("host", "host-action-1");
        var after = new ActionKeyframe(before.Id, 1.25, "roll");

        Assert.True(document.UpdateActionKeyframe("host", before, after));
        Assert.False(document.UpdateActionKeyframe("host", after, after));
        Assert.Throws<InvalidOperationException>(() =>
            document.RemoveActionKeyframe("host", before));
        Assert.Equal(1, document.Revision);
    }

    [Fact]
    public void Action_add_replaces_the_held_state_and_preserves_actor_metadata()
    {
        var document = CreateSemanticDocument();

        document.AddActionKeyframe("host", new ActionKeyframe("host-action-2", 2, "roll"));

        var host = document.Actors.Single(actor => actor.ActorId == "host");
        Assert.Equal(["host-action-1", "host-action-2"], host.ActionKeyframes.Select(frame => frame.Id));
        Assert.Equal("roll", document.CreateSnapshot(2).ActorTimelineStates["host"].Action.ActionKey);
        Assert.Equal("Host", host.DisplayName);
        Assert.Equal("Hero", host.Role);
    }

    [Fact]
    public void Lock_on_mutation_validates_target_and_normalizes_offset()
    {
        var document = CreateSemanticDocument();
        var frame = new LockOnKeyframe(
            "host-lock-2", 2, true, "invader", 190, LockOnTrackingMode.Snap);

        document.AddLockOnKeyframe("host", frame);

        Assert.Equal(-170, document.GetLockOnKeyframe("host", frame.Id).YawOffsetDegrees);
        Assert.Throws<ArgumentException>(() => document.AddLockOnKeyframe(
            "host",
            new LockOnKeyframe("self", 2.5, true, "host", 0, LockOnTrackingMode.Continuous)));
    }

    [Fact]
    public void Lock_on_update_and_remove_require_full_current_preimage()
    {
        var document = CreateSemanticDocument();
        var before = document.GetLockOnKeyframe("host", "host-lock-1");
        var after = new LockOnKeyframe(before.Id, 1.25, false, "invader", -190, LockOnTrackingMode.Snap);

        Assert.True(document.UpdateLockOnKeyframe("host", before, after));
        Assert.False(document.UpdateLockOnKeyframe("host", after, after));
        var current = document.GetLockOnKeyframe("host", after.Id);
        Assert.Equal(170, current.YawOffsetDegrees);
        Assert.Throws<InvalidOperationException>(() => document.RemoveLockOnKeyframe(
            "host", new LockOnKeyframe(current.Id, current.TimeSeconds, current.Enabled, current.TargetActorId, current.YawOffsetDegrees, LockOnTrackingMode.Continuous)));
        Assert.Equal(1, document.Revision);
    }

    [Fact]
    public void Snapshot_holds_semantic_markers_at_boundaries_and_defensively_copies_states()
    {
        var document = SceneDocument.Create(
            "semantic-boundaries",
            "Semantic boundaries",
            null,
            10,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [new TransformKeyframe("host-transform", 0, new Position3(0, 0, 0), 0)],
                    [
                        new ActionKeyframe("action-first", 1, "idle"),
                        new ActionKeyframe("action-last", 3, "attack"),
                    ],
                    [
                        new LockOnKeyframe("lock-first", 1, true, "invader", -190, LockOnTrackingMode.Snap),
                        new LockOnKeyframe("lock-last", 3, false, "invader", 20, LockOnTrackingMode.KeyframeOnly),
                    ]),
                new ActorTrack("invader", [new TransformKeyframe("invader-transform", 0, new Position3(1, 0, 0), 0)])
            ]);

        var before = document.CreateSnapshot(0.5).ActorTimelineStates["host"];
        var exact = document.CreateSnapshot(1).ActorTimelineStates["host"];
        var between = document.CreateSnapshot(2).ActorTimelineStates["host"];
        var after = document.CreateSnapshot(10).ActorTimelineStates["host"];

        Assert.Equal(new EvaluatedActionState(null, null), before.Action);
        Assert.Equal(new EvaluatedLockOnState(null, false, null, 0, LockOnTrackingMode.Continuous), before.LockOn);
        Assert.Equal("action-first", exact.Action.SourceKeyframeId);
        Assert.Equal(170, exact.LockOn.YawOffsetDegrees);
        Assert.Equal("idle", between.Action.ActionKey);
        Assert.True(between.LockOn.Enabled);
        Assert.Equal("action-last", after.Action.SourceKeyframeId);
        Assert.False(after.LockOn.Enabled);
        Assert.Equal("invader", after.LockOn.TargetActorId);
        Assert.Equal(LockOnTrackingMode.KeyframeOnly, after.LockOn.TrackingMode);

        var source = new Dictionary<string, EvaluatedActorTimelineState>
        {
            ["host"] = exact
        };
        var snapshot = new SceneSnapshot("copy", 0, 0, new Dictionary<string, EvaluatedTransform>(), source);
        source.Clear();
        Assert.Single(snapshot.ActorTimelineStates);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, EvaluatedActorTimelineState>)snapshot.ActorTimelineStates).Add("intruder", exact));
    }

    [Fact]
    public void Empty_semantic_tracks_evaluate_to_default_states()
    {
        var document = SceneDocument.Create(
            "empty-semantic",
            "Empty semantic",
            null,
            1,
            30,
            [new ActorTrack("host", [new TransformKeyframe("transform", 0, new Position3(0, 0, 0), 0)])]);

        var state = document.CreateSnapshot(1).ActorTimelineStates["host"];

        Assert.Equal(new EvaluatedActionState(null, null), state.Action);
        Assert.Equal(new EvaluatedLockOnState(null, false, null, 0, LockOnTrackingMode.Continuous), state.LockOn);
    }

    [Fact]
    public void Semantic_mutations_reject_invalid_requests_without_changing_document_and_allow_last_deletes()
    {
        var document = CreateSemanticDocument();
        var originalActor = document.Actors.Single(actor => actor.ActorId == "host");
        var originalTransform = originalActor.TransformKeyframes.Single();
        var notifications = 0;
        document.Changed += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => document.AddActionKeyframe(
            "host", new ActionKeyframe("duplicate-time", 1, "roll")));
        Assert.Throws<ArgumentException>(() => document.AddActionKeyframe(
            "host", new ActionKeyframe("host-action-1", 2, "roll")));
        Assert.Throws<ArgumentException>(() => document.AddLockOnKeyframe(
            "host", new LockOnKeyframe("duplicate-lock-time", 1, false, null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddActionKeyframe(
            "host", new ActionKeyframe("outside-action", 11, "roll")));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddLockOnKeyframe(
            "host", new LockOnKeyframe("outside-lock", 11, false, null)));
        Assert.Throws<ArgumentException>(() => document.AddLockOnKeyframe(
            "host", new LockOnKeyframe("missing-target", 2, true, "missing", 0, LockOnTrackingMode.Snap)));
        Assert.Throws<ArgumentException>(() => document.AddLockOnKeyframe(
            "host", new LockOnKeyframe("self-target", 2, true, "host", 0, LockOnTrackingMode.Snap)));
        foreach (var nonFiniteValue in NonFiniteValues)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LockOnKeyframe(
                "nonfinite", 2, false, null, nonFiniteValue, LockOnTrackingMode.Continuous));
        }

        document.AddLockOnKeyframe(
            "host", new LockOnKeyframe("disabled-candidate", 2, false, "invader", 0, LockOnTrackingMode.Continuous));
        document.RemoveLockOnKeyframe("host", document.GetLockOnKeyframe("host", "disabled-candidate"));
        document.RemoveActionKeyframe("host", document.GetActionKeyframe("host", "host-action-1"));
        document.RemoveLockOnKeyframe("host", document.GetLockOnKeyframe("host", "host-lock-1"));

        var host = document.Actors.Single(actor => actor.ActorId == "host");
        Assert.Empty(host.ActionKeyframes);
        Assert.Empty(host.LockOnKeyframes);
        Assert.Equal("Host", host.DisplayName);
        Assert.Equal("Hero", host.Role);
        Assert.Equal(originalTransform, host.TransformKeyframes.Single());
        Assert.Equal(4, document.Revision);
        Assert.Equal(4, notifications);
    }

    private static SceneDocument CreateEditableDocument()
    {
        return SceneDocument.Create(
            "document-1",
            "Editable",
            null,
            10,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [
                        new TransformKeyframe("host-first", 1, new Position3(1, 2, 3), 10),
                        new TransformKeyframe("host-second", 4, new Position3(2, 3, 4), 20),
                    ],
                    [new ActionKeyframe("host-action", 1, "idle")],
                    [new LockOnKeyframe("host-lock", 2, false, null)]),
                new ActorTrack(
                    "target",
                    "Target",
                    "Enemy",
                    [new TransformKeyframe("target-first", 1, new Position3(7, 0, 8), 180)], [], [])
            ]);
    }

    private static SceneDocument CreateSemanticDocument()
    {
        return SceneDocument.Create(
            "semantic-document",
            "Semantic",
            null,
            10,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [new TransformKeyframe("host-transform", 0, new Position3(0, 0, 0), 0)],
                    [new ActionKeyframe("host-action-1", 1, "attack")],
                    [new LockOnKeyframe("host-lock-1", 1, true, "invader", 15, LockOnTrackingMode.Continuous)]),
                new ActorTrack(
                    "invader",
                    "Invader",
                    "Enemy",
                    [new TransformKeyframe("invader-transform", 0, new Position3(1, 0, 0), 180)],
                    [],
                    [])
            ]);
    }
}
