using PvpGuide.Application.Playback;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Projection;

public sealed class SceneProjectionController : IDisposable
{
    private const int MaximumConsistencyAttempts = 3;

    private readonly ISceneProjectionSource _source;
    private readonly IPlaybackTimeSource _playback;
    private readonly ISceneProjectionConsumer _topConsumer;
    private readonly ISceneProjectionConsumer _worldConsumer;
    private readonly TrajectorySamplingSettings _samplingSettings = TrajectorySamplingPolicy.CreateSettings();
    private CachedTrajectories? _cachedTrajectories;
    private (long Revision, double TimeSeconds)? _lastProjectedKey;
    private bool _isProjecting;
    private bool _hasPendingProjection;
    private bool _disposed;

    public SceneProjectionController(
        ISceneProjectionSource source,
        IPlaybackTimeSource playback,
        ISceneProjectionConsumer topConsumer,
        ISceneProjectionConsumer worldConsumer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(topConsumer);
        ArgumentNullException.ThrowIfNull(worldConsumer);
        if (ReferenceEquals(topConsumer, worldConsumer))
        {
            throw new ArgumentException("Top and world consumers must be distinct instances.", nameof(worldConsumer));
        }

        _source = source;
        _playback = playback;
        _topConsumer = topConsumer;
        _worldConsumer = worldConsumer;
        _source.Changed += OnDocumentChanged;
        _playback.Changed += OnPlaybackChanged;
    }

    internal int CachedTrajectoryEntryCount => _cachedTrajectories is null ? 0 : 1;

    public void ProjectCurrent()
    {
        if (_disposed)
        {
            return;
        }

        RequestProjection();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.Changed -= OnDocumentChanged;
        _playback.Changed -= OnPlaybackChanged;
        _hasPendingProjection = false;
        _cachedTrajectories = null;
        _disposed = true;
    }

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs)
    {
        if (!_disposed)
        {
            RequestProjection();
        }
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs)
    {
        if (!_disposed)
        {
            RequestProjection();
        }
    }

    private void RequestProjection()
    {
        _hasPendingProjection = true;
        if (_isProjecting)
        {
            return;
        }

        _isProjecting = true;
        try
        {
            while (_hasPendingProjection && !_disposed)
            {
                _hasPendingProjection = false;
                ProjectLatestRequest();
            }
        }
        catch
        {
            _hasPendingProjection = false;
            throw;
        }
        finally
        {
            _isProjecting = false;
        }
    }

    private void ProjectLatestRequest()
    {
        var timeSeconds = _playback.CurrentTimeSeconds;
        var initialMetadata = _source.GetProjectionMetadata();
        if (_lastProjectedKey == (initialMetadata.Revision, timeSeconds))
        {
            return;
        }

        var frame = CreateStableFrame(timeSeconds, initialMetadata);
        if (_lastProjectedKey == (frame.Snapshot.Revision, frame.Snapshot.TimeSeconds))
        {
            return;
        }

        _topConsumer.Apply(frame);
        _worldConsumer.Apply(frame);
        _lastProjectedKey = (frame.Snapshot.Revision, frame.Snapshot.TimeSeconds);
    }

    private SceneProjectionFrame CreateStableFrame(
        double timeSeconds,
        ProjectionSourceMetadata firstMetadata)
    {
        var metadata = firstMetadata;
        for (var attempt = 0; attempt < MaximumConsistencyAttempts; attempt++)
        {
            var evaluation = EvaluateProjection(timeSeconds, metadata);
            var finalMetadata = _source.GetProjectionMetadata();
            if (metadata == finalMetadata)
            {
                var frame = new SceneProjectionFrame(
                    evaluation.Snapshot,
                    evaluation.Trajectories,
                    evaluation.Fingerprint);
                ValidateMetadata(frame, metadata);
                return frame;
            }

            metadata = finalMetadata;
        }

        throw new InvalidOperationException(
            $"Could not obtain a stable projection after {MaximumConsistencyAttempts} attempts.");
    }

    private ProjectionEvaluation EvaluateProjection(
        double timeSeconds,
        ProjectionSourceMetadata metadata)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > metadata.DurationSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeSeconds),
                "Playback time must be finite and within the projection source duration.");
        }

        var plan = _source.CreateTrajectorySamplePlan(_samplingSettings);
        TrajectorySamplingPolicy.ValidatePlan(plan, metadata);
        var trajectories = GetTrajectories(metadata, plan);
        var snapshot = _source.CreateSnapshot(timeSeconds);
        return new ProjectionEvaluation(snapshot, trajectories, plan.Fingerprint);
    }

    private MovementTrajectorySet GetTrajectories(
        ProjectionSourceMetadata metadata,
        TrajectorySamplePlan plan)
    {
        if (_cachedTrajectories is { } cached &&
            cached.MotionRevision == metadata.MotionRevision &&
            string.Equals(cached.Fingerprint, plan.Fingerprint, StringComparison.Ordinal))
        {
            return cached.Trajectories.WithRevision(metadata.Revision);
        }

        var trajectories = _source.CreateMovementTrajectories(plan);
        _cachedTrajectories = new CachedTrajectories(
            metadata.MotionRevision,
            plan.Fingerprint,
            trajectories);
        return trajectories;
    }

    private static void ValidateMetadata(
        SceneProjectionFrame frame,
        ProjectionSourceMetadata metadata)
    {
        if (!string.Equals(frame.Snapshot.DocumentId, metadata.DocumentId, StringComparison.Ordinal) ||
            frame.Snapshot.Revision != metadata.Revision ||
            frame.Snapshot.MotionRevision != metadata.MotionRevision)
        {
            throw new InvalidOperationException(
                "Projection payload does not match the stable source metadata.");
        }
    }

    private sealed record CachedTrajectories(
        long MotionRevision,
        string Fingerprint,
        MovementTrajectorySet Trajectories);

    private sealed record ProjectionEvaluation(
        SceneSnapshot Snapshot,
        MovementTrajectorySet Trajectories,
        string Fingerprint);
}
