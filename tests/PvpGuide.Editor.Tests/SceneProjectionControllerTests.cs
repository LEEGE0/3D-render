using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.ViewportSync;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class SceneProjectionControllerTests
{
    [Fact]
    public void Change_delivers_one_shared_snapshot_to_each_consumer_once_per_revision()
    {
        var source = new RecordingSnapshotSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, top, world);

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
    public void Dispose_stops_future_projection_delivery()
    {
        var source = new RecordingSnapshotSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var controller = new SceneProjectionController(source, top, world);

        controller.Dispose();
        source.Publish(1);

        Assert.Equal(0, source.CreateSnapshotCalls);
        Assert.Empty(top.Received);
        Assert.Empty(world.Received);
    }

    [Fact]
    public void Constructor_rejects_the_same_consumer_for_top_and_world_views()
    {
        var consumer = new RecordingConsumer();

        Assert.Throws<ArgumentException>(() => new SceneProjectionController(new RecordingSnapshotSource(), consumer, consumer));
    }

    [Fact]
    public void Real_document_rejects_empty_actor_without_projection_and_delivers_valid_actor_once_to_each_view()
    {
        var document = new SceneDocument("document-1", 10, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var notifications = 0;
        document.Changed += (_, _) => notifications++;
        using var controller = new SceneProjectionController(document, top, world);

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

    private sealed class RecordingSnapshotSource : ISceneSnapshotSource
    {
        private long _currentRevision;

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
    }

    private sealed class RecordingConsumer : ISceneProjectionConsumer
    {
        public List<SceneSnapshot> Received { get; } = [];

        public void Apply(SceneSnapshot snapshot)
        {
            Received.Add(snapshot);
        }
    }
}
