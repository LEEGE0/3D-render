using PvpGuide.Application.Commands;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class DocumentSession
{
    private const double EditTimeToleranceSeconds = 0.000000001;
    private const string NoSelectionLockReason = "배우를 선택해야 편집할 수 있습니다";
    private const string PlayingLockReason = "재생 중에는 편집할 수 없습니다";
    private const string SelectedKeyframeTimeLockReason = "선택한 키프레임 시각에서만 편집할 수 있습니다";
    private const string ExistingKeyframeTimeLockReason = "현재 시각에는 이미 변환 키프레임이 있습니다";
    private const string NoTransformKeyframeLockReason = "변환 키프레임을 선택해야 편집할 수 있습니다";
    private const string LastTransformKeyframeLockReason = "마지막 변환 키프레임은 삭제할 수 없습니다";
    private readonly SceneDocument _document;
    private readonly Stack<ISceneEditCommand> _undoStack = [];
    private readonly Stack<ISceneEditCommand> _redoStack = [];
    private TransformKeyframe? _previewStart;
    private TransformPreview? _preview;
    private TransformKeyframe? _selectedTransformKeyframe;

    public DocumentSession(SceneDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Playback = new PlaybackClock(_document.DurationSeconds, _document.FramesPerSecond);
        Playback.Changed += OnPlaybackChanged;
        UpdateEditAvailability(false);
    }

    public ISceneSnapshotSource SnapshotSource => _document;

    public string? SelectedActorId { get; private set; }

    public string? SelectedTransformKeyframeId { get; private set; }

    public PlaybackClock Playback { get; }

    public bool CanEditSelectedTransform { get; private set; }

    public string? EditLockReason { get; private set; }

    public bool CanAddTransformKeyframe { get; private set; }

    public string? AddTransformKeyframeLockReason { get; private set; }

    public bool CanDeleteSelectedTransformKeyframe { get; private set; }

    public string? DeleteTransformKeyframeLockReason { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public long CurrentRevision => _document.Revision;

    public IReadOnlyList<ActorDisplayInfo> ActorDisplayInfos => Array.AsReadOnly(
        _document.Actors
            .Select(actor => new ActorDisplayInfo(actor.ActorId, actor.DisplayName, actor.Role))
            .ToArray());

    internal int UndoCount => _undoStack.Count;

    internal int RedoCount => _redoStack.Count;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TransformKeyframeSelectionChangedEventArgs>? TransformKeyframeSelectionChanged;

    public event EventHandler<TransformPreviewChangedEventArgs>? PreviewChanged;

    public event EventHandler<EditAvailabilityChangedEventArgs>? EditAvailabilityChanged;

    public event EventHandler? HistoryChanged;

    public ActorDisplayInfo GetActorDisplayInfo(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var actor = _document.Actors.SingleOrDefault(candidate => candidate.ActorId == actorId)
            ?? throw new ArgumentException($"Actor '{actorId}' does not exist.", nameof(actorId));
        return new ActorDisplayInfo(actor.ActorId, actor.DisplayName, actor.Role);
    }

    public void SelectActor(string? actorId)
    {
        if (actorId is not null && !_document.Actors.Any(actor => actor.ActorId == actorId))
        {
            throw new ArgumentException($"Actor '{actorId}' does not exist.", nameof(actorId));
        }

        if (SelectedActorId == actorId)
        {
            return;
        }

        ClearPreview();
        SelectedActorId = actorId;
        var keyframeSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime(forceNotification: true);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedActorId));
    }

    public TransformKeyframe? GetSelectedTransform()
    {
        if (SelectedActorId is null || SelectedTransformKeyframeId is null)
        {
            return null;
        }

        return GetSelectedActor()?.TransformKeyframes
            .SingleOrDefault(frame => frame.Id == SelectedTransformKeyframeId);
    }

    public IReadOnlyList<TransformKeyframe> GetSelectedActorTransformKeyframes() =>
        GetSelectedActor()?.TransformKeyframes.ToArray() ?? [];

    public SceneEditResult SelectTransformKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        var actor = GetSelectedActor();
        if (actor is null)
        {
            return SceneEditResult.Conflict;
        }

        var keyframe = actor.GetTransformKeyframe(keyframeId);
        ClearPreview();
        Playback.Pause();
        Playback.Seek(keyframe.TimeSeconds);
        var keyframeSelectionChange = SetSelectedTransformKeyframe(keyframe);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        return SceneEditResult.Applied;
    }

    public SceneEditResult AddTransformKeyframeAtCurrentTime()
    {
        if (!CanAddTransformKeyframe || SelectedActorId is null)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        var actor = GetSelectedActor();
        if (actor is null)
        {
            return SceneEditResult.Conflict;
        }

        var timeSeconds = Playback.CurrentTimeSeconds;
        var pose = _document.CreateSnapshot(timeSeconds).ActorTransforms[SelectedActorId];
        var keyframe = new TransformKeyframe(
            GetNextTransformKeyframeId(actor),
            timeSeconds,
            pose.Position,
            pose.YawDegrees);
        var result = ExecuteCommandDetailed(new AddTransformKeyframeCommand(SelectedActorId, keyframe));
        if (result != SceneEditResult.Applied)
        {
            return result;
        }

        var keyframeSelectionChange = SetSelectedTransformKeyframe(keyframe);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        return SceneEditResult.Applied;
    }

    public SceneEditResult UpdateSelectedTransformKeyframe(
        double timeSeconds,
        Position3 position,
        double yawDegrees)
    {
        if (!CanEditSelectedTransform || SelectedActorId is null || _selectedTransformKeyframe is null ||
            !double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > _document.DurationSeconds ||
            !double.IsFinite(yawDegrees))
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedTransformKeyframe;
        TransformKeyframe after;
        try
        {
            after = new TransformKeyframe(before.Id, timeSeconds, position, yawDegrees);
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        var result = ExecuteCommandDetailed(new UpdateTransformKeyframeCommand(SelectedActorId, before, after));
        if (result != SceneEditResult.Applied)
        {
            return result;
        }

        var keyframeSelectionChange = SetSelectedTransformKeyframe(after);
        Playback.Seek(after.TimeSeconds);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        return SceneEditResult.Applied;
    }

    public SceneEditResult RemoveSelectedTransformKeyframe()
    {
        if (!CanDeleteSelectedTransformKeyframe || SelectedActorId is null || _selectedTransformKeyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedTransformKeyframe;
        ClearPreview();
        var result = ExecuteCommandDetailed(new RemoveTransformKeyframeCommand(SelectedActorId, before));
        if (result != SceneEditResult.Applied)
        {
            return result;
        }

        var next = GetSelectedActor()!.TransformKeyframes
            .OrderBy(frame => Math.Abs(frame.TimeSeconds - before.TimeSeconds))
            .ThenBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Id, StringComparer.Ordinal)
            .First();
        var keyframeSelectionChange = SetSelectedTransformKeyframe(next);
        Playback.Seek(next.TimeSeconds);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        return SceneEditResult.Applied;
    }

    public bool MoveSelectedActor(Position3 destination)
    {
        if (!CanEditSelectedTransform)
        {
            return false;
        }

        var before = GetSelectedTransform();
        return before is null
            ? false
            : ExecuteSelectedTransform(before, destination, before.YawDegrees);
    }

    public bool RotateSelectedActor(double yawDegrees)
    {
        if (!CanEditSelectedTransform)
        {
            return false;
        }

        var before = GetSelectedTransform();
        return before is null
            ? false
            : ExecuteSelectedTransform(before, before.Position, yawDegrees);
    }

    public bool SetSelectedActorTransform(Position3 position, double yawDegrees)
    {
        if (!CanEditSelectedTransform)
        {
            return false;
        }

        var before = GetSelectedTransform();
        return before is null
            ? false
            : ExecuteSelectedTransform(before, position, yawDegrees);
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        var command = _undoStack.Peek();
        var revisionBefore = _document.Revision;
        if (!TryUndo(command, revisionBefore, () => MoveUndoToRedoAndReconcile(command)))
        {
            return false;
        }

        MoveUndoToRedo(command);
        RefreshSelectionAfterHistoryTransition();
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        var command = _redoStack.Peek();
        var revisionBefore = _document.Revision;
        if (TryExecuteDetailed(command, revisionBefore, () => MoveRedoToUndoAndReconcile(command)) != SceneEditResult.Applied)
        {
            return false;
        }

        MoveRedoToUndo(command);
        RefreshSelectionAfterHistoryTransition();
        return true;
    }

    public void BeginPreview()
    {
        if (!CanEditSelectedTransform)
        {
            throw new InvalidOperationException(EditLockReason ?? "변환 편집을 시작할 수 없습니다.");
        }

        if (_previewStart is not null)
        {
            throw new InvalidOperationException("A transform preview is already active.");
        }

        var selected = GetSelectedTransform()
            ?? throw new InvalidOperationException("A selected actor is required to begin a preview.");
        _previewStart = selected;
        _preview = new TransformPreview(SelectedActorId!, selected.Id, selected.Position, selected.YawDegrees);
    }

    public void UpdatePreview(Position3 position, double yawDegrees)
    {
        if (_previewStart is null)
        {
            throw new InvalidOperationException("An active transform preview is required.");
        }

        _preview = new TransformPreview(SelectedActorId!, _previewStart.Id, position, yawDegrees);
        PreviewChanged?.Invoke(this, new TransformPreviewChangedEventArgs(_preview));
    }

    public bool CommitPreview() => CommitPreviewDetailed() == SceneEditResult.Applied;

    public SceneEditResult CommitPreviewDetailed()
    {
        if (_previewStart is null || _preview is null)
        {
            return SceneEditResult.NoChange;
        }

        var before = _previewStart;
        var preview = _preview;
        ClearPreview();
        return ExecuteCommandDetailed(new ReplaceTransformCommand(
            SelectedActorId!,
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, preview.Position, preview.YawDegrees)));
    }

    public void CancelPreview() => ClearPreview();

    internal bool ExecuteCommand(ISceneEditCommand command) =>
        ExecuteCommandDetailed(command) == SceneEditResult.Applied;

    internal SceneEditResult ExecuteCommandDetailed(ISceneEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var revisionBefore = _document.Revision;
        var result = TryExecuteDetailed(command, revisionBefore, () => CommitExecuteAndReconcile(command));
        if (result != SceneEditResult.Applied)
        {
            return result;
        }

        CommitExecute(command);
        return SceneEditResult.Applied;
    }

    private bool ExecuteSelectedTransform(TransformKeyframe before, Position3 position, double yawDegrees) =>
        ExecuteCommand(new ReplaceTransformCommand(
            SelectedActorId!,
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, position, yawDegrees)));

    private SceneEditResult TryExecuteDetailed(
        ISceneEditCommand command,
        long revisionBefore,
        Action onMutationException)
    {
        try
        {
            return command.Execute(_document)
                ? SceneEditResult.Applied
                : SceneEditResult.NoChange;
        }
        catch (Exception exception) when (_document.Revision > revisionBefore)
        {
            CompleteMutationExceptionTransition(exception, onMutationException);
            throw;
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }
        catch (InvalidOperationException)
        {
            return SceneEditResult.Conflict;
        }
    }

    private bool TryUndo(ISceneEditCommand command, long revisionBefore, Action onMutationException)
    {
        try
        {
            return command.Undo(_document);
        }
        catch (Exception exception) when (_document.Revision > revisionBefore)
        {
            CompleteMutationExceptionTransition(exception, onMutationException);
            throw;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void CommitExecute(ISceneEditCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        RaiseHistoryChanged();
    }

    private void MoveUndoToRedo(ISceneEditCommand command)
    {
        _undoStack.Pop();
        _redoStack.Push(command);
        RaiseHistoryChanged();
    }

    private void MoveRedoToUndo(ISceneEditCommand command)
    {
        _redoStack.Pop();
        _undoStack.Push(command);
        RaiseHistoryChanged();
    }

    private static void CompleteMutationExceptionTransition(Exception originalException, Action transition)
    {
        try
        {
            transition();
        }
        catch (Exception transitionException)
        {
            throw new AggregateException(
                "The document mutation and history transition observers both failed.",
                originalException,
                transitionException);
        }
    }

    private void RaiseHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    private void CommitExecuteAndReconcile(ISceneEditCommand command) =>
        CompleteHistoryTransitionAndReconcile(() => CommitExecute(command));

    private void MoveUndoToRedoAndReconcile(ISceneEditCommand command) =>
        CompleteHistoryTransitionAndReconcile(() => MoveUndoToRedo(command));

    private void MoveRedoToUndoAndReconcile(ISceneEditCommand command) =>
        CompleteHistoryTransitionAndReconcile(() => MoveRedoToUndo(command));

    private void CompleteHistoryTransitionAndReconcile(Action historyTransition)
    {
        try
        {
            historyTransition();
        }
        catch (Exception historyException)
        {
            CompleteMutationExceptionTransition(historyException, ReconcileSelectedTransformKeyframeAfterMutation);
            throw;
        }

        ReconcileSelectedTransformKeyframeAfterMutation();
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs args)
    {
        var keyframeSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime();
        var availabilityChange = RefreshEditAvailabilityState();
        try
        {
            ClearPreview();
        }
        catch (Exception exception)
        {
            CompleteMutationExceptionTransition(
                exception,
                () => RaiseSelectionAndEditAvailabilityChanged(keyframeSelectionChange, availabilityChange));
            throw;
        }

        RaiseSelectionAndEditAvailabilityChanged(keyframeSelectionChange, availabilityChange);
    }

    private void UpdateEditAvailability(bool raiseEvent = true)
    {
        var availabilityChange = RefreshEditAvailabilityState();
        if (raiseEvent)
        {
            RaiseEditAvailabilityChanged(availabilityChange);
        }
    }

    private EditAvailabilityChangedEventArgs? RefreshEditAvailabilityState()
    {
        var (canEdit, reason) = GetEditAvailability();
        var (canAdd, addReason) = GetAddTransformKeyframeAvailability();
        var (canDelete, deleteReason) = GetDeleteTransformKeyframeAvailability();
        CanAddTransformKeyframe = canAdd;
        AddTransformKeyframeLockReason = addReason;
        CanDeleteSelectedTransformKeyframe = canDelete;
        DeleteTransformKeyframeLockReason = deleteReason;
        if (CanEditSelectedTransform == canEdit && EditLockReason == reason)
        {
            return null;
        }

        CanEditSelectedTransform = canEdit;
        EditLockReason = reason;
        return new EditAvailabilityChangedEventArgs(canEdit, reason);
    }

    private void RaiseEditAvailabilityChanged(EditAvailabilityChangedEventArgs? availabilityChange)
    {
        if (availabilityChange is not null &&
            availabilityChange.CanEditSelectedTransform == CanEditSelectedTransform &&
            availabilityChange.EditLockReason == EditLockReason)
        {
            EditAvailabilityChanged?.Invoke(this, availabilityChange);
        }
    }

    private (bool CanEdit, string? Reason) GetEditAvailability()
    {
        if (SelectedActorId is null)
        {
            return (false, NoSelectionLockReason);
        }

        if (Playback.IsPlaying)
        {
            return (false, PlayingLockReason);
        }

        var selected = GetSelectedTransform();
        return selected is not null && Math.Abs(Playback.CurrentTimeSeconds - selected.TimeSeconds) <= EditTimeToleranceSeconds
            ? (true, null)
            : (false, SelectedKeyframeTimeLockReason);
    }

    private ActorTrack? GetSelectedActor() => SelectedActorId is null
        ? null
        : _document.Actors.SingleOrDefault(actor => actor.ActorId == SelectedActorId);

    private TransformKeyframeSelectionChangedEventArgs? RefreshSelectedTransformKeyframeAtCurrentTime(
        bool forceNotification = false)
    {
        var actor = GetSelectedActor();
        var keyframe = actor?.TransformKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds);
        return SetSelectedTransformKeyframe(keyframe, forceNotification);
    }

    private TransformKeyframeSelectionChangedEventArgs? SetSelectedTransformKeyframe(
        TransformKeyframe? keyframe,
        bool forceNotification = false)
    {
        var keyframeId = keyframe?.Id;
        if (!forceNotification && SelectedTransformKeyframeId == keyframeId)
        {
            _selectedTransformKeyframe = keyframe;
            return null;
        }

        SelectedTransformKeyframeId = keyframeId;
        _selectedTransformKeyframe = keyframe;
        return new TransformKeyframeSelectionChangedEventArgs(SelectedActorId, keyframeId, keyframe);
    }

    private void RaiseTransformKeyframeSelectionChanged(TransformKeyframeSelectionChangedEventArgs? keyframeSelectionChange)
    {
        if (keyframeSelectionChange is not null &&
            keyframeSelectionChange.ActorId == SelectedActorId &&
            keyframeSelectionChange.KeyframeId == SelectedTransformKeyframeId &&
            ((keyframeSelectionChange.Keyframe is null && GetSelectedTransform() is null) ||
             SameTransform(keyframeSelectionChange.Keyframe, GetSelectedTransform())))
        {
            TransformKeyframeSelectionChanged?.Invoke(this, keyframeSelectionChange);
        }
    }

    private void RaiseSelectionAndEditAvailabilityChanged(
        TransformKeyframeSelectionChangedEventArgs? keyframeSelectionChange,
        EditAvailabilityChangedEventArgs? availabilityChange)
    {
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        RaiseEditAvailabilityChanged(availabilityChange);
    }

    private (bool CanAdd, string? Reason) GetAddTransformKeyframeAvailability()
    {
        if (SelectedActorId is null)
        {
            return (false, NoSelectionLockReason);
        }

        if (Playback.IsPlaying)
        {
            return (false, PlayingLockReason);
        }

        return GetSelectedActor()!.TransformKeyframes.Any(frame =>
                Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds)
            ? (false, ExistingKeyframeTimeLockReason)
            : (true, null);
    }

    private (bool CanDelete, string? Reason) GetDeleteTransformKeyframeAvailability()
    {
        if (SelectedActorId is null)
        {
            return (false, NoSelectionLockReason);
        }

        if (Playback.IsPlaying)
        {
            return (false, PlayingLockReason);
        }

        if (SelectedTransformKeyframeId is null || GetSelectedTransform() is null)
        {
            return (false, NoTransformKeyframeLockReason);
        }

        return GetSelectedActor()!.TransformKeyframes.Count > 1
            ? (true, null)
            : (false, LastTransformKeyframeLockReason);
    }

    private string GetNextTransformKeyframeId(ActorTrack actor)
    {
        var existingIds = actor.TransformKeyframes.Select(frame => frame.Id).ToHashSet(StringComparer.Ordinal);
        for (var ordinal = 1; ; ordinal++)
        {
            var candidate = $"{actor.ActorId}-transform-{ordinal:D4}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void RefreshSelectionAfterHistoryTransition()
    {
        var keyframeSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime();
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
    }

    private void ReconcileSelectedTransformKeyframeAfterMutation()
    {
        var actor = GetSelectedActor();
        if (actor is null)
        {
            var noSelectionChange = SetSelectedTransformKeyframe(null);
            UpdateEditAvailability();
            RaiseTransformKeyframeSelectionChanged(noSelectionChange);
            return;
        }

        var selected = SelectedTransformKeyframeId is null
            ? null
            : actor.TransformKeyframes.SingleOrDefault(frame => frame.Id == SelectedTransformKeyframeId);
        selected ??= actor.TransformKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds);
        selected ??= actor.TransformKeyframes
            .OrderBy(frame => Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds))
            .ThenBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var keyframeSelectionChange = SetSelectedTransformKeyframe(selected);
        if (selected is not null && Math.Abs(Playback.CurrentTimeSeconds - selected.TimeSeconds) > EditTimeToleranceSeconds)
        {
            Playback.Seek(selected.TimeSeconds);
        }

        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
    }

    private static bool SameTransform(TransformKeyframe? left, TransformKeyframe? right) =>
        left is not null && right is not null &&
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Position == right.Position &&
        left.YawDegrees == right.YawDegrees;

    private void ClearPreview()
    {
        if (_previewStart is null)
        {
            return;
        }

        _previewStart = null;
        _preview = null;
        PreviewChanged?.Invoke(this, new TransformPreviewChangedEventArgs(null));
    }
}
