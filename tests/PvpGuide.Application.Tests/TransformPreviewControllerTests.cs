using PvpGuide.Application.Editing;
using PvpGuide.Application.Projection;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Application.Tests;

public sealed class TransformPreviewControllerTests
{
    [Fact]
    public void Preview_changed_delivers_the_exact_same_preview_instance_to_distinct_consumers()
    {
        var session = CreateSession();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new TransformPreviewController(session, top, world);

        session.SelectActor("actor-1");
        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 1), 90);

        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.NotNull(top.Received[0]);
        Assert.Same(top.Received[0], world.Received[0]);
    }

    [Fact]
    public void Cancel_preview_delivers_same_null_clear_to_each_consumer()
    {
        var session = CreateSession();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new TransformPreviewController(session, top, world);

        session.SelectActor("actor-1");
        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 1), 90);
        session.CancelPreview();

        Assert.Equal(2, top.Received.Count);
        Assert.Equal(2, world.Received.Count);
        Assert.Null(top.Received[1]);
        Assert.Same(top.Received[1], world.Received[1]);
    }

    [Fact]
    public void Dispose_stops_future_preview_delivery()
    {
        var session = CreateSession();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var controller = new TransformPreviewController(session, top, world);

        controller.Dispose();
        session.SelectActor("actor-1");
        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 1), 90);
        session.CancelPreview();

        Assert.Empty(top.Received);
        Assert.Empty(world.Received);
    }

    [Fact]
    public void Constructor_rejects_the_same_consumer_for_top_and_world_views()
    {
        var consumer = new RecordingConsumer();

        Assert.Throws<ArgumentException>(() => new TransformPreviewController(CreateSession(), consumer, consumer));
    }

    private static DocumentSession CreateSession()
    {
        var document = new SceneDocument("document-1", 10, 30);
        document.AddActor(new ActorTrack("actor-1", [
            new TransformKeyframe("origin", 0, new Position3(0, 0, 0), 0),
        ]));
        return new DocumentSession(document);
    }

    private sealed class RecordingConsumer : ITransformPreviewConsumer
    {
        public List<TransformPreview?> Received { get; } = [];

        public void ApplyPreview(TransformPreview? preview) => Received.Add(preview);
    }
}
