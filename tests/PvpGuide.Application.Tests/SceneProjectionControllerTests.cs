using PvpGuide.Application.Projection;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Application.Tests;

public sealed class SceneProjectionControllerTests
{
    [Fact]
    public void Projection_delivers_same_semantic_state_to_both_consumers()
    {
        var document = SceneDocument.Create(
            "semantic-projection",
            "semantic-projection",
            null,
            durationSeconds: 2,
            framesPerSecond: 30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "player",
                    [new TransformKeyframe("host-transform", 0, new Position3(0, 0, 0), 0)],
                    [new ActionKeyframe("host-action", 1, "attack")],
                    [new LockOnKeyframe("host-lock", 1, true, "invader")]),
                new ActorTrack(
                    "invader",
                    "Invader",
                    "enemy",
                    [new TransformKeyframe("invader-transform", 0, new Position3(4, 0, -1), 180)],
                    [],
                    []),
            ]);
        var playback = new PlaybackClock(2, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(document, playback, top, world);

        Assert.True(playback.Seek(1.5));

        Assert.Same(top.Received.Single(), world.Received.Single());
        Assert.Equal("attack", top.Received[0].ActorTimelineStates["host"].Action.ActionKey);
        Assert.Equal("invader", top.Received[0].ActorTimelineStates["host"].LockOn.TargetActorId);
    }

    [Fact]
    public void Time_change_projects_same_revision_at_new_time_to_both_consumers()
    {
        var document = SceneDocument.Create(
            "document-1",
            "document-1",
            null,
            durationSeconds: 1,
            framesPerSecond: 30,
            [
                new ActorTrack("actor-1", [
                    new TransformKeyframe("start", 0, new Position3(0, 0, 0), 350),
                    new TransformKeyframe("end", 1, new Position3(10, 20, 30), 10),
                ]),
            ]);
        var session = new DocumentSession(document);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(document, session.Playback, top, world);

        controller.ProjectCurrent();
        Assert.True(session.Playback.Seek(0.5));

        Assert.Equal(2, top.Received.Count);
        Assert.Equal(2, world.Received.Count);
        Assert.Same(top.Received[1], world.Received[1]);
        Assert.Equal(0, top.Received[0].Revision);
        Assert.Equal(0, top.Received[1].Revision);
        Assert.Equal(0.5, top.Received[1].TimeSeconds);
        Assert.Equal(new Position3(5, 10, 15), top.Received[1].ActorTransforms["actor-1"].Position);
        Assert.Equal(0, top.Received[1].ActorTransforms["actor-1"].YawDegrees);
    }

    [Fact]
    public void ProjectCurrent_delivers_one_shared_snapshot_before_any_change_event()
    {
        var source = new RecordingSnapshotSource(revision: 5);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(10, 30), top, world);

        controller.ProjectCurrent();
        controller.ProjectCurrent();

        Assert.Equal(2, source.CreateSnapshotCalls);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Same(top.Received[0], world.Received[0]);
    }

    [Fact]
    public void Change_delivers_one_shared_snapshot_to_each_consumer_once_per_revision()
    {
        var source = new RecordingSnapshotSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(10, 30), top, world);

        source.Publish(7);
        source.Publish(7);
        source.Publish(8);

        Assert.Equal(2, source.CreateSnapshotCalls);
        Assert.Equal([7L, 8L], top.Received.Select(snapshot => snapshot.Revision));
        Assert.Equal([7L, 8L], world.Received.Select(snapshot => snapshot.Revision));
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Same(top.Received[1], world.Received[1]);
    }

    [Fact]
    public void ProjectCurrent_then_same_revision_change_is_deduplicated()
    {
        var source = new RecordingSnapshotSource(revision: 7);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(10, 30), top, world);

        controller.ProjectCurrent();
        source.Publish(7);
        source.Publish(8);

        Assert.Equal([7L, 8L], top.Received.Select(snapshot => snapshot.Revision));
        Assert.Equal(2, source.CreateSnapshotCalls);
    }

    [Fact]
    public void ProjectCurrent_reads_a_new_current_revision_without_a_change_event_and_deduplicates_delivery()
    {
        var source = new RecordingSnapshotSource(revision: 5);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(10, 30), top, world);

        controller.ProjectCurrent();
        source.SetRevisionSilently(6);
        controller.ProjectCurrent();
        controller.ProjectCurrent();

        Assert.Equal(3, source.CreateSnapshotCalls);
        Assert.Equal([5L, 6L], top.Received.Select(snapshot => snapshot.Revision));
        Assert.Equal([5L, 6L], world.Received.Select(snapshot => snapshot.Revision));
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Same(top.Received[1], world.Received[1]);
    }

    [Fact]
    public void ProjectCurrent_deduplicates_an_exact_revision_time_key()
    {
        var source = new RecordingSnapshotSource(revision: 5);
        var playback = new PlaybackClock(10, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, playback, top, world);

        controller.ProjectCurrent();
        controller.ProjectCurrent();

        Assert.Equal(2, source.CreateSnapshotCalls);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Equal(5, top.Received[0].Revision);
        Assert.Equal(0, top.Received[0].TimeSeconds);
    }

    [Fact]
    public void Time_or_revision_change_each_delivers_one_shared_snapshot()
    {
        var source = new RecordingSnapshotSource(revision: 5);
        var playback = new PlaybackClock(10, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, playback, top, world);

        controller.ProjectCurrent();
        Assert.True(playback.Seek(0.5));
        source.Publish(6);

        Assert.Equal(3, source.CreateSnapshotCalls);
        Assert.Equal([(5L, 0d), (5L, 0.5d), (6L, 0.5d)], top.Received.Select(snapshot => (snapshot.Revision, snapshot.TimeSeconds)));
        Assert.Equal([(5L, 0d), (5L, 0.5d), (6L, 0.5d)], world.Received.Select(snapshot => (snapshot.Revision, snapshot.TimeSeconds)));
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Same(top.Received[1], world.Received[1]);
        Assert.Same(top.Received[2], world.Received[2]);
    }

    [Fact]
    public void Dispose_stops_document_and_playback_event_projection()
    {
        var source = new RecordingSnapshotSource();
        var playback = new PlaybackClock(10, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var controller = new SceneProjectionController(source, playback, top, world);

        controller.Dispose();
        controller.ProjectCurrent();
        source.Publish(1);
        Assert.True(playback.Seek(0.5));

        Assert.Equal(0, source.CreateSnapshotCalls);
        Assert.Empty(top.Received);
        Assert.Empty(world.Received);
    }

    [Fact]
    public void Constructor_rejects_the_same_consumer_for_top_and_world_views()
    {
        var consumer = new RecordingConsumer();

        Assert.Throws<ArgumentException>(() => new SceneProjectionController(
            new RecordingSnapshotSource(),
            new PlaybackClock(10, 30),
            consumer,
            consumer));
    }

    [Fact]
    public void Real_document_rejects_empty_actor_without_projection_and_delivers_valid_actor_once_to_each_view()
    {
        var document = new SceneDocument("document-1", 10, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        using var controller = new SceneProjectionController(document, new PlaybackClock(10, 30), top, world);

        Assert.Throws<ArgumentException>(() => document.AddActor(new ActorTrack("empty", [])));

        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.Empty(document.Actors);
        Assert.Empty(top.Received);
        Assert.Empty(world.Received);

        document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("origin", 0, new Position3(1, 2, 3), 0),
        ]));

        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Equal(new Position3(1, 2, 3), top.Received[0].ActorTransforms["actor-1"].Position);
    }

    private sealed class RecordingSnapshotSource(long revision = 0) : ISceneSnapshotSource
    {
        private long _currentRevision = revision;

        public event EventHandler<SceneDocumentChangedEventArgs>? Changed;

        public int CreateSnapshotCalls { get; private set; }

        public SceneSnapshot CreateSnapshot(double timeSeconds)
        {
            CreateSnapshotCalls++;
            return new SceneSnapshot("source", _currentRevision, timeSeconds, new Dictionary<string, EvaluatedTransform>());
        }

        public void Publish(long revision)
        {
            _currentRevision = revision;
            Changed?.Invoke(this, new SceneDocumentChangedEventArgs(revision));
        }

        public void SetRevisionSilently(long revision) => _currentRevision = revision;
    }

    private sealed class RecordingConsumer : ISceneProjectionConsumer
    {
        public List<SceneSnapshot> Received { get; } = [];

        public void Apply(SceneSnapshot snapshot) => Received.Add(snapshot);
    }
}
