using System.Collections.ObjectModel;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Domain;

public interface ISceneSnapshotSource
{
    event EventHandler<SceneDocumentChangedEventArgs> Changed;

    SceneSnapshot CreateSnapshot(double timeSeconds);
}

public sealed class SceneDocument : ISceneSnapshotSource
{
    private readonly Dictionary<string, ActorTrack> _actorsById = new(StringComparer.Ordinal);
    private readonly List<ActorTrack> _actors = [];
    private readonly ReadOnlyCollection<ActorTrack> _readOnlyActors;

    public SceneDocument(string documentId, double durationSeconds, int framesPerSecond)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Document duration must be finite and non-negative.");
        }

        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frames per second must be positive.");
        }

        DocumentId = documentId;
        DurationSeconds = durationSeconds;
        FramesPerSecond = framesPerSecond;
        _readOnlyActors = _actors.AsReadOnly();
    }

    public const string Schema = "pvp-guide-scene/1";

    public string DocumentId { get; }

    public double DurationSeconds { get; }

    public int FramesPerSecond { get; }

    public long Revision { get; private set; }

    public IReadOnlyList<ActorTrack> Actors => _readOnlyActors;

    public event EventHandler<SceneDocumentChangedEventArgs>? Changed;

    public void AddActor(ActorTrack actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Keyframes.Count == 0)
        {
            throw new ArgumentException("An actor must have at least one keyframe.", nameof(actor));
        }

        if (_actorsById.ContainsKey(actor.ActorId))
        {
            throw new ArgumentException($"An actor named '{actor.ActorId}' already exists.", nameof(actor));
        }

        foreach (var keyframe in actor.Keyframes)
        {
            EnsureTimeWithinDocument(keyframe.TimeSeconds, nameof(actor));
        }

        _actorsById.Add(actor.ActorId, actor);
        _actors.Add(actor);
        RaiseChanged();
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

        var updatedActor = new ActorTrack(actorId, actor.Keyframes.Append(keyframe));
        _actorsById[actorId] = updatedActor;
        _actors[_actors.IndexOf(actor)] = updatedActor;
        RaiseChanged();
    }

    public SceneSnapshot CreateSnapshot(double timeSeconds)
    {
        EnsureTimeWithinDocument(timeSeconds, nameof(timeSeconds));

        var evaluatedTransforms = new Dictionary<string, EvaluatedTransform>(_actors.Count, StringComparer.Ordinal);
        foreach (var actor in _actors)
        {
            evaluatedTransforms.Add(actor.ActorId, actor.Evaluate(timeSeconds));
        }

        return new SceneSnapshot(DocumentId, Revision, timeSeconds, evaluatedTransforms);
    }

    private void EnsureTimeWithinDocument(double timeSeconds, string parameterName)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > DurationSeconds)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Time must be finite and within the document duration.");
        }
    }

    private void RaiseChanged()
    {
        Revision++;
        Changed?.Invoke(this, new SceneDocumentChangedEventArgs(Revision));
    }
}

public sealed class SceneDocumentChangedEventArgs(long revision) : EventArgs
{
    public long Revision { get; } = revision;
}
