using PvpGuide.Application.Playback;
using PvpGuide.Application.Projection;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Application.Tests;

public sealed class SceneProjectionControllerTests
{
    [Fact]
    public void Projection_delivers_the_exact_same_frame_snapshot_and_trajectories_to_both_consumers()
    {
        var document = CreateSemanticDocument();
        var playback = new PlaybackClock(2, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(document, playback, top, world);

        Assert.True(playback.Seek(1.5));

        var topFrame = Assert.Single(top.Received);
        var worldFrame = Assert.Single(world.Received);
        Assert.Same(topFrame, worldFrame);
        Assert.Same(topFrame.Snapshot, worldFrame.Snapshot);
        Assert.Same(topFrame.Trajectories, worldFrame.Trajectories);
        Assert.Equal("attack", topFrame.Snapshot.ActorTimelineStates["host"].Action.ActionKey);
        Assert.Equal("invader", topFrame.Snapshot.ActorTimelineStates["host"].LockOn.TargetActorId);
    }

    [Fact]
    public void Frame_rejects_document_revision_motion_revision_and_policy_mismatches()
    {
        var snapshot = CreateSnapshot("document", revision: 5, motionRevision: 3);
        var valid = CreateTrajectories("document", revision: 5, motionRevision: 3, "policy-a");

        Assert.Throws<ArgumentException>(() => new SceneProjectionFrame(
            snapshot,
            CreateTrajectories("other", 5, 3, "policy-a"),
            "policy-a"));
        Assert.Throws<ArgumentException>(() => new SceneProjectionFrame(
            snapshot,
            CreateTrajectories("document", 4, 3, "policy-a"),
            "policy-a"));
        Assert.Throws<ArgumentException>(() => new SceneProjectionFrame(
            snapshot,
            CreateTrajectories("document", 5, 2, "policy-a"),
            "policy-a"));
        Assert.Throws<ArgumentException>(() => new SceneProjectionFrame(snapshot, valid, "policy-b"));
    }

    [Fact]
    public void First_projection_builds_once_and_seek_reuses_the_same_trajectory_instance()
    {
        var source = new ControllableProjectionSource(durationSeconds: 2, framesPerSecond: 30);
        var playback = new PlaybackClock(2, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, playback, top, world);

        controller.ProjectCurrent();
        Assert.True(playback.Seek(0.5));

        Assert.Equal(1, source.CreateTrajectoriesCalls);
        Assert.Equal(2, source.CreateSnapshotCalls);
        Assert.Same(top.Received[0].Trajectories, top.Received[1].Trajectories);
        Assert.Equal(1, controller.CachedTrajectoryEntryCount);
    }

    [Fact]
    public void Controller_requests_lock_on_motion_v1_with_a_30hz_uniform_limit()
    {
        var source = new ControllableProjectionSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();

        Assert.Equal("lock-on-motion/v1", source.LastSettings?.PolicyVersion);
        Assert.Equal(30, source.LastSettings?.MaximumUniformRate);
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(3, 30)]
    public void Source_plan_with_a_noncanonical_uniform_rate_is_rejected_without_publishing(
        int framesPerSecond,
        int returnedUniformRate)
    {
        var source = new ControllableProjectionSource(framesPerSecond: framesPerSecond)
        {
            ReturnedUniformRate = returnedUniformRate,
        };
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        var error = Assert.Throws<InvalidOperationException>(controller.ProjectCurrent);

        Assert.Contains("uniform rate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(top.Received);
        Assert.Empty(world.Received);
        Assert.Equal(0, source.CreateTrajectoriesCalls);
    }

    [Theory]
    [InlineData(60, 30)]
    [InlineData(3, 3)]
    public void Source_plan_with_the_canonical_uniform_rate_is_published(
        int framesPerSecond,
        int returnedUniformRate)
    {
        var source = new ControllableProjectionSource(framesPerSecond: framesPerSecond)
        {
            ReturnedUniformRate = returnedUniformRate,
        };
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();

        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Equal(1, source.CreateTrajectoriesCalls);
    }

    [Fact]
    public void Action_only_revision_wraps_the_cached_set_and_reuses_actor_geometry()
    {
        var source = new ControllableProjectionSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();
        source.Publish(revision: 1, motionRevision: 0);

        Assert.Equal(1, source.CreateTrajectoriesCalls);
        Assert.Equal([0L, 1L], top.Received.Select(frame => frame.Snapshot.Revision));
        Assert.NotSame(top.Received[0].Trajectories, top.Received[1].Trajectories);
        Assert.Same(top.Received[0].Trajectories.Actors, top.Received[1].Trajectories.Actors);
        Assert.Equal(1, top.Received[1].Trajectories.Revision);
    }

    [Fact]
    public void Motion_revision_change_rebuilds_once_and_replaces_the_single_cache_entry()
    {
        var source = new ControllableProjectionSource();
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();
        source.Publish(revision: 1, motionRevision: 1);

        Assert.Equal(2, source.CreateTrajectoriesCalls);
        Assert.NotSame(top.Received[0].Trajectories.Actors, top.Received[1].Trajectories.Actors);
        Assert.Equal(1, controller.CachedTrajectoryEntryCount);
    }

    [Fact]
    public void Metadata_change_during_evaluation_retries_without_publishing_a_stale_frame()
    {
        var source = new ControllableProjectionSource();
        source.BeforeNextSnapshot = () => source.SetMetadataSilently(revision: 1, motionRevision: 1);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();

        var frame = Assert.Single(top.Received);
        Assert.Same(frame, Assert.Single(world.Received));
        Assert.Equal(1, frame.Snapshot.Revision);
        Assert.Equal(1, frame.Snapshot.MotionRevision);
        Assert.Equal(2, source.CreateSnapshotCalls);
    }

    [Fact]
    public void Continuously_changing_metadata_exhausts_the_bounded_retry_without_publishing()
    {
        var source = new ControllableProjectionSource { ChangeMetadataBeforeEverySnapshot = true };
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        var error = Assert.Throws<InvalidOperationException>(controller.ProjectCurrent);

        Assert.Contains("stable projection", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, source.CreateSnapshotCalls);
        Assert.Empty(top.Received);
        Assert.Empty(world.Received);
    }

    [Fact]
    public void Reentrant_source_change_is_serialized_after_both_consumers_receive_the_old_frame()
    {
        var source = new ControllableProjectionSource();
        var order = new List<string>();
        var reentered = false;
        var top = new RecordingConsumer(frame =>
        {
            order.Add($"top {frame.Snapshot.Revision}");
            if (!reentered)
            {
                reentered = true;
                source.Publish(revision: 1, motionRevision: 0);
            }
        });
        var world = new RecordingConsumer(frame => order.Add($"world {frame.Snapshot.Revision}"));
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();

        Assert.Equal(["top 0", "world 0", "top 1", "world 1"], order);
        Assert.Same(top.Received[0], world.Received[0]);
        Assert.Same(top.Received[1], world.Received[1]);
    }

    [Fact]
    public void Exact_revision_and_time_duplicates_are_suppressed_before_snapshot_evaluation()
    {
        var source = new ControllableProjectionSource(revision: 5, motionRevision: 3);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        controller.ProjectCurrent();
        controller.ProjectCurrent();
        source.Publish(revision: 5, motionRevision: 3);

        Assert.Equal(1, source.CreateSnapshotCalls);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
    }

    [Fact]
    public void Consumer_exception_is_preserved_and_projection_state_is_reusable()
    {
        var source = new ControllableProjectionSource();
        var expected = new ProjectionConsumerException("top failed");
        var top = new ThrowOnceConsumer(expected);
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(source, new PlaybackClock(2, 30), top, world);

        var actual = Assert.Throws<ProjectionConsumerException>(controller.ProjectCurrent);
        controller.ProjectCurrent();

        Assert.Same(expected, actual);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Same(top.Received[0], world.Received[0]);
    }

    [Fact]
    public void Dispose_unsubscribes_events_ignores_projection_and_clears_the_cache()
    {
        var source = new ControllableProjectionSource();
        var playback = new PlaybackClock(2, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        var controller = new SceneProjectionController(source, playback, top, world);
        controller.ProjectCurrent();

        controller.Dispose();
        controller.ProjectCurrent();
        source.Publish(revision: 1, motionRevision: 1);
        Assert.True(playback.Seek(0.5));

        Assert.Equal(1, source.CreateSnapshotCalls);
        Assert.Single(top.Received);
        Assert.Single(world.Received);
        Assert.Equal(0, controller.CachedTrajectoryEntryCount);
    }

    [Fact]
    public void Session_preserves_snapshot_source_and_exposes_the_same_projection_source()
    {
        var document = new SceneDocument("document", 2, 30);
        var session = new DocumentSession(document);

        Assert.Same(document, session.SnapshotSource);
        Assert.Same(document, session.ProjectionSource);
    }

    [Fact]
    public void Constructor_rejects_the_same_consumer_for_top_and_world_views()
    {
        var consumer = new RecordingConsumer();

        Assert.Throws<ArgumentException>(() => new SceneProjectionController(
            new ControllableProjectionSource(),
            new PlaybackClock(2, 30),
            consumer,
            consumer));
    }

    [Fact]
    public void Real_document_change_projects_valid_actor_once_to_each_view()
    {
        var document = new SceneDocument("document", 2, 30);
        var top = new RecordingConsumer();
        var world = new RecordingConsumer();
        using var controller = new SceneProjectionController(document, new PlaybackClock(2, 30), top, world);

        document.AddActor(new ActorTrack("actor", [
            new TransformKeyframe("origin", 0, new Position3(1, 2, 3), 0),
        ]));

        var frame = Assert.Single(top.Received);
        Assert.Same(frame, Assert.Single(world.Received));
        Assert.Equal(new Position3(1, 2, 3), frame.Snapshot.ActorTransforms["actor"].Position);
    }

    private static SceneDocument CreateSemanticDocument() => SceneDocument.Create(
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

    private static SceneSnapshot CreateSnapshot(string documentId, long revision, long motionRevision) =>
        new(
            documentId,
            revision,
            timeSeconds: 0,
            new Dictionary<string, EvaluatedTransform>(),
            new Dictionary<string, EvaluatedActorTimelineState>(),
            new Dictionary<string, EvaluatedActorFacing>(),
            motionRevision);

    private static MovementTrajectorySet CreateTrajectories(
        string documentId,
        long revision,
        long motionRevision,
        string fingerprint) =>
        new(
            documentId,
            revision,
            motionRevision,
            fingerprint,
            new Dictionary<string, ActorMovementTrajectory>(),
            segmentSteps: 0);

    private sealed class ControllableProjectionSource : ISceneProjectionSource
    {
        private long _revision;
        private long _motionRevision;

        public ControllableProjectionSource(
            long revision = 0,
            long motionRevision = 0,
            double durationSeconds = 2,
            int framesPerSecond = 30)
        {
            _revision = revision;
            _motionRevision = motionRevision;
            DurationSeconds = durationSeconds;
            FramesPerSecond = framesPerSecond;
        }

        public event EventHandler<SceneDocumentChangedEventArgs>? Changed;

        public double DurationSeconds { get; }

        public int FramesPerSecond { get; }

        public int CreateSnapshotCalls { get; private set; }

        public int CreateTrajectoriesCalls { get; private set; }

        public TrajectorySamplingSettings? LastSettings { get; private set; }

        public int? ReturnedUniformRate { get; init; }

        public Action? BeforeNextSnapshot { get; set; }

        public bool ChangeMetadataBeforeEverySnapshot { get; init; }

        public ProjectionSourceMetadata GetProjectionMetadata() =>
            new("source", DurationSeconds, FramesPerSecond, _revision, _motionRevision);

        public SceneSnapshot CreateSnapshot(double timeSeconds)
        {
            CreateSnapshotCalls++;
            if (ChangeMetadataBeforeEverySnapshot)
            {
                _revision++;
                _motionRevision++;
            }

            var callback = BeforeNextSnapshot;
            BeforeNextSnapshot = null;
            callback?.Invoke();
            return new SceneSnapshot(
                "source",
                _revision,
                timeSeconds,
                new Dictionary<string, EvaluatedTransform>(),
                new Dictionary<string, EvaluatedActorTimelineState>(),
                new Dictionary<string, EvaluatedActorFacing>(),
                _motionRevision);
        }

        public TrajectorySamplePlan CreateTrajectorySamplePlan(TrajectorySamplingSettings settings)
        {
            LastSettings = settings;
            var rate = ReturnedUniformRate ?? Math.Min(FramesPerSecond, settings.MaximumUniformRate);
            return new TrajectorySamplePlan(settings.PolicyVersion, rate, [0, DurationSeconds]);
        }

        public MovementTrajectorySet CreateMovementTrajectories(TrajectorySamplePlan plan)
        {
            CreateTrajectoriesCalls++;
            return CreateTrajectories("source", _revision, _motionRevision, plan.Fingerprint);
        }

        public void Publish(long revision, long motionRevision)
        {
            SetMetadataSilently(revision, motionRevision);
            Changed?.Invoke(this, new SceneDocumentChangedEventArgs(revision));
        }

        public void SetMetadataSilently(long revision, long motionRevision)
        {
            _revision = revision;
            _motionRevision = motionRevision;
        }
    }

    private class RecordingConsumer(Action<SceneProjectionFrame>? onApply = null) : ISceneProjectionConsumer
    {
        public List<SceneProjectionFrame> Received { get; } = [];

        public virtual void Apply(SceneProjectionFrame frame)
        {
            Received.Add(frame);
            onApply?.Invoke(frame);
        }
    }

    private sealed class ThrowOnceConsumer(Exception exception) : RecordingConsumer
    {
        private bool _hasThrown;

        public override void Apply(SceneProjectionFrame frame)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw exception;
            }

            base.Apply(frame);
        }
    }

    private sealed class ProjectionConsumerException(string message) : Exception(message);
}
