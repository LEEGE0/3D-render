using System.Collections.ObjectModel;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain;

public interface ISceneSnapshotSource
{
    event EventHandler<SceneDocumentChangedEventArgs> Changed;

    SceneSnapshot CreateSnapshot(double timeSeconds);
}

public sealed class SceneDocument : ISceneProjectionSource
{
    private readonly Dictionary<string, ActorTrack> _actorsById = new(StringComparer.Ordinal);
    private readonly List<ActorTrack> _actors = [];
    private readonly ReadOnlyCollection<ActorTrack> _readOnlyActors;
    private long _motionRevision;

    public SceneDocument(string documentId, double durationSeconds, int framesPerSecond)
        : this(documentId, documentId, null, durationSeconds, framesPerSecond, null)
    {
    }

    private SceneDocument(
        string documentId,
        string name,
        string? note,
        double durationSeconds,
        int framesPerSecond,
        ImportMetadata? importMetadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Document duration must be finite and non-negative.");
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frames per second must be positive.");
        }

        DocumentId = documentId;
        Name = name;
        Note = note;
        DurationSeconds = durationSeconds;
        FramesPerSecond = framesPerSecond;
        ImportMetadata = importMetadata;
        _readOnlyActors = _actors.AsReadOnly();
    }

    public const string Schema = "pvp-guide-scene/2";

    public string DocumentId { get; }

    public string Name { get; }

    public string? Note { get; }

    public double DurationSeconds { get; }

    public int FramesPerSecond { get; }

    public ImportMetadata? ImportMetadata { get; }

    public long Revision { get; private set; }

    public long MotionRevision => _motionRevision;

    public IReadOnlyList<ActorTrack> Actors => _readOnlyActors;

    public event EventHandler<SceneDocumentChangedEventArgs>? Changed;

    public ProjectionSourceMetadata GetProjectionMetadata() =>
        new(DocumentId, DurationSeconds, FramesPerSecond, Revision, MotionRevision);

    public static SceneDocument Create(
        string documentId,
        string name,
        string? note,
        double durationSeconds,
        int framesPerSecond,
        IEnumerable<ActorTrack> actors,
        ImportMetadata? importMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var copiedActors = actors.ToArray();
        if (copiedActors.Any(actor => actor is null))
        {
            throw new ArgumentException("Actor collections cannot contain null values.", nameof(actors));
        }

        var document = new SceneDocument(documentId, name, note, durationSeconds, framesPerSecond, importMetadata);
        var actorsById = new Dictionary<string, ActorTrack>(StringComparer.Ordinal);
        foreach (var actor in copiedActors)
        {
            if (!actorsById.TryAdd(actor.ActorId, actor))
            {
                throw new ArgumentException($"An actor named '{actor.ActorId}' already exists.", nameof(actors));
            }
        }

        foreach (var actor in copiedActors)
        {
            document.ValidateActor(actor, actorsById.Keys, nameof(actors));
        }

        foreach (var actor in copiedActors)
        {
            document._actorsById.Add(actor.ActorId, actor);
            document._actors.Add(actor);
        }

        return document;
    }

    public void AddActor(ActorTrack actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_actorsById.ContainsKey(actor.ActorId))
        {
            throw new ArgumentException($"An actor named '{actor.ActorId}' already exists.", nameof(actor));
        }

        ValidateActor(actor, _actorsById.Keys.Append(actor.ActorId), nameof(actor));

        _actorsById.Add(actor.ActorId, actor);
        _actors.Add(actor);
        RaiseChanged(affectsMotion: true);
    }

    public void AddKeyframe(string actorId, TransformKeyframe keyframe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentNullException.ThrowIfNull(keyframe);
        EnsureTimeWithinDocument(keyframe.TimeSeconds, nameof(keyframe));
        if (!_actorsById.TryGetValue(actorId, out var actor))
        {
            throw new ArgumentException($"Actor '{actorId}' does not exist.", nameof(actorId));
        }

        var updatedActor = new ActorTrack(
            actorId,
            actor.DisplayName,
            actor.Role,
            actor.TransformKeyframes.Append(keyframe),
            actor.ActionKeyframes,
            actor.LockOnKeyframes);
        _actorsById[actorId] = updatedActor;
        _actors[_actors.IndexOf(actor)] = updatedActor;
        RaiseChanged(affectsMotion: true);
    }

    public TransformKeyframe GetTransformKeyframe(string actorId, string keyframeId)
    {
        var actor = GetRequiredActor(actorId);
        return actor.GetTransformKeyframe(keyframeId);
    }

    public void AddActionKeyframe(string actorId, ActionKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        EnsureTimeWithinDocument(keyframe.TimeSeconds, nameof(keyframe));

        var actor = GetRequiredActor(actorId);
        ReplaceActor(actor, actor.AddActionKeyframe(keyframe));
        RaiseChanged(affectsMotion: false);
    }

    public ActionKeyframe GetActionKeyframe(string actorId, string keyframeId)
    {
        var actor = GetRequiredActor(actorId);
        return actor.GetActionKeyframe(keyframeId);
    }

    public bool UpdateActionKeyframe(
        string actorId,
        ActionKeyframe expectedCurrent,
        ActionKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        EnsureTimeWithinDocument(replacement.TimeSeconds, nameof(replacement));

        var actor = GetRequiredActor(actorId);
        var current = actor.GetActionKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        if (SameAction(current, replacement))
        {
            return false;
        }

        ReplaceActor(actor, actor.UpdateActionKeyframe(expectedCurrent, replacement));
        RaiseChanged(affectsMotion: false);
        return true;
    }

    public void RemoveActionKeyframe(string actorId, ActionKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var actor = GetRequiredActor(actorId);
        ReplaceActor(actor, actor.RemoveActionKeyframe(expectedCurrent));
        RaiseChanged(affectsMotion: false);
    }

    public void AddLockOnKeyframe(string actorId, LockOnKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        EnsureTimeWithinDocument(keyframe.TimeSeconds, nameof(keyframe));

        var actor = GetRequiredActor(actorId);
        ValidateLockOnTarget(actor.ActorId, keyframe.TargetActorId, nameof(keyframe));
        ReplaceActor(actor, actor.AddLockOnKeyframe(keyframe));
        RaiseChanged(affectsMotion: true);
    }

    public LockOnKeyframe GetLockOnKeyframe(string actorId, string keyframeId)
    {
        var actor = GetRequiredActor(actorId);
        return actor.GetLockOnKeyframe(keyframeId);
    }

    public bool UpdateLockOnKeyframe(
        string actorId,
        LockOnKeyframe expectedCurrent,
        LockOnKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        EnsureTimeWithinDocument(replacement.TimeSeconds, nameof(replacement));

        var actor = GetRequiredActor(actorId);
        ValidateLockOnTarget(actor.ActorId, replacement.TargetActorId, nameof(replacement));
        var current = actor.GetLockOnKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        if (SameLockOn(current, replacement))
        {
            return false;
        }

        ReplaceActor(actor, actor.UpdateLockOnKeyframe(expectedCurrent, replacement));
        RaiseChanged(affectsMotion: true);
        return true;
    }

    public void RemoveLockOnKeyframe(string actorId, LockOnKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var actor = GetRequiredActor(actorId);
        ReplaceActor(actor, actor.RemoveLockOnKeyframe(expectedCurrent));
        RaiseChanged(affectsMotion: true);
    }

    public bool ReplaceTransformKeyframe(
        string actorId,
        TransformKeyframe expectedCurrent,
        TransformKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        if (replacement.TimeSeconds != expectedCurrent.TimeSeconds)
        {
            throw new ArgumentException("Replacement time must remain unchanged.", nameof(replacement));
        }

        return UpdateTransformKeyframe(actorId, expectedCurrent, replacement);
    }

    public bool UpdateTransformKeyframe(
        string actorId,
        TransformKeyframe expectedCurrent,
        TransformKeyframe replacement)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(replacement);
        EnsureTimeWithinDocument(replacement.TimeSeconds, nameof(replacement));

        var actor = GetRequiredActor(actorId);
        var current = actor.GetTransformKeyframe(expectedCurrent.Id);
        ValidateExpected(current, expectedCurrent);
        if (SameTransform(current, replacement))
        {
            return false;
        }

        var updated = actor.UpdateTransformKeyframe(expectedCurrent, replacement);
        _actorsById[actorId] = updated;
        _actors[_actors.IndexOf(actor)] = updated;
        RaiseChanged(affectsMotion: true);
        return true;
    }

    public void RemoveTransformKeyframe(string actorId, TransformKeyframe expectedCurrent)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);

        var actor = GetRequiredActor(actorId);
        var updated = actor.RemoveTransformKeyframe(expectedCurrent);
        _actorsById[actorId] = updated;
        _actors[_actors.IndexOf(actor)] = updated;
        RaiseChanged(affectsMotion: true);
    }

    public SceneSnapshot CreateSnapshot(double timeSeconds)
    {
        EnsureTimeWithinDocument(timeSeconds, nameof(timeSeconds));

        var evaluatedTransforms = new Dictionary<string, EvaluatedTransform>(_actors.Count, StringComparer.Ordinal);
        var evaluatedTimelineStates = new Dictionary<string, EvaluatedActorTimelineState>(_actors.Count, StringComparer.Ordinal);
        var evaluatedFacings = new Dictionary<string, EvaluatedActorFacing>(_actors.Count, StringComparer.Ordinal);
        foreach (var actor in _actors)
        {
            evaluatedTransforms.Add(actor.ActorId, actor.Evaluate(timeSeconds));
            evaluatedTimelineStates.Add(actor.ActorId, new EvaluatedActorTimelineState(
                actor.EvaluateAction(timeSeconds),
                actor.EvaluateLockOn(timeSeconds)));
        }

        foreach (var actor in _actors)
        {
            evaluatedFacings.Add(actor.ActorId, LockOnFacingEvaluator.Evaluate(actor, _actorsById, timeSeconds));
        }

        return new SceneSnapshot(
            DocumentId,
            Revision,
            timeSeconds,
            evaluatedTransforms,
            evaluatedTimelineStates,
            evaluatedFacings,
            MotionRevision);
    }

    public TrajectorySamplePlan CreateTrajectorySamplePlan(TrajectorySamplingSettings settings) =>
        MovementTrajectoryEvaluator.CreatePlan(
            DurationSeconds,
            FramesPerSecond,
            _readOnlyActors,
            settings);

    public MovementTrajectorySet CreateMovementTrajectories(TrajectorySamplePlan plan) =>
        MovementTrajectoryEvaluator.Evaluate(
            DocumentId,
            Revision,
            MotionRevision,
            DurationSeconds,
            _readOnlyActors,
            plan);

    private void EnsureTimeWithinDocument(double timeSeconds, string parameterName)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > DurationSeconds)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Time must be finite and within the document duration.");
        }
    }

    private ActorTrack GetRequiredActor(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        return _actorsById.TryGetValue(actorId, out var actor)
            ? actor
            : throw new ArgumentException($"Actor '{actorId}' does not exist.", nameof(actorId));
    }

    private static void ValidateExpected(TransformKeyframe current, TransformKeyframe expected)
    {
        if (!SameTransform(current, expected))
        {
            throw new InvalidOperationException("The transform keyframe changed after the edit began.");
        }
    }

    private static void ValidateExpected(ActionKeyframe current, ActionKeyframe expected)
    {
        if (!SameAction(current, expected))
        {
            throw new InvalidOperationException("The action keyframe changed after the edit began.");
        }
    }

    private static void ValidateExpected(LockOnKeyframe current, LockOnKeyframe expected)
    {
        if (!SameLockOn(current, expected))
        {
            throw new InvalidOperationException("The lock-on keyframe changed after the edit began.");
        }
    }

    private static bool SameTransform(TransformKeyframe left, TransformKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Position == right.Position &&
        TransformKeyframe.NormalizeYaw(left.YawDegrees) == TransformKeyframe.NormalizeYaw(right.YawDegrees);

    private static bool SameAction(ActionKeyframe left, ActionKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.ActionKey == right.ActionKey;

    private static bool SameLockOn(LockOnKeyframe left, LockOnKeyframe right) =>
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Enabled == right.Enabled &&
        left.TargetActorId == right.TargetActorId &&
        left.YawOffsetDegrees == right.YawOffsetDegrees &&
        left.TrackingMode == right.TrackingMode;

    private void ReplaceActor(ActorTrack current, ActorTrack replacement)
    {
        _actorsById[current.ActorId] = replacement;
        _actors[_actors.IndexOf(current)] = replacement;
    }

    private void ValidateActor(ActorTrack actor, IEnumerable<string> actorIds, string parameterName)
    {
        if (actor.TransformKeyframes.Count == 0)
        {
            throw new ArgumentException("An actor must have at least one transform keyframe.", parameterName);
        }

        foreach (var timeSeconds in actor.TransformKeyframes.Select(frame => frame.TimeSeconds)
                     .Concat(actor.ActionKeyframes.Select(frame => frame.TimeSeconds))
                     .Concat(actor.LockOnKeyframes.Select(frame => frame.TimeSeconds)))
        {
            EnsureTimeWithinDocument(timeSeconds, parameterName);
        }

        var knownActorIds = actorIds.ToHashSet(StringComparer.Ordinal);
        foreach (var lockOn in actor.LockOnKeyframes)
        {
            if (lockOn.TargetActorId is null)
            {
                continue;
            }

            if (lockOn.TargetActorId == actor.ActorId || !knownActorIds.Contains(lockOn.TargetActorId))
            {
                throw new ArgumentException("Lock-on targets must name a different actor in the same document.", parameterName);
            }
        }
    }

    private void ValidateLockOnTarget(string actorId, string? targetActorId, string parameterName)
    {
        if (targetActorId is not null &&
            (targetActorId == actorId || !_actorsById.ContainsKey(targetActorId)))
        {
            throw new ArgumentException("Lock-on targets must name a different actor in the same document.", parameterName);
        }
    }

    private void RaiseChanged(bool affectsMotion)
    {
        Revision++;
        if (affectsMotion)
        {
            _motionRevision++;
        }

        Changed?.Invoke(this, new SceneDocumentChangedEventArgs(Revision));
    }
}

public sealed class ImportMetadata
{
    public ImportMetadata(string sourceFormat, string rawSourcePayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFormat);
        ArgumentNullException.ThrowIfNull(rawSourcePayload);
        SourceFormat = sourceFormat;
        RawSourcePayload = rawSourcePayload;
    }

    public string SourceFormat { get; }

    public string RawSourcePayload { get; }
}

public sealed class SceneDocumentChangedEventArgs(long revision) : EventArgs
{
    public long Revision { get; } = revision;
}
