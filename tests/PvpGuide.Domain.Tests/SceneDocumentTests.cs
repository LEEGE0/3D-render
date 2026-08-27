using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Domain.Tests;

public sealed class SceneDocumentTests
{
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
        Assert.Throws<InvalidOperationException>(() => new ActorTrack("actor-1", []).Evaluate(0));
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
}
