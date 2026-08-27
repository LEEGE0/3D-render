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
    private const int MaxReconciliationAttempts = 32;
    private const string NoSelectionLockReason = "배우를 선택해야 편집할 수 있습니다";
    private const string PlayingLockReason = "재생 중에는 편집할 수 없습니다";
    private const string SelectedKeyframeTimeLockReason = "선택한 키프레임 시각에서만 편집할 수 있습니다";
    private const string ExistingKeyframeTimeLockReason = "현재 시각에는 이미 변환 키프레임이 있습니다";
    private const string NoTransformKeyframeLockReason = "변환 키프레임을 선택해야 편집할 수 있습니다";
    private const string LastTransformKeyframeLockReason = "마지막 변환 키프레임은 삭제할 수 없습니다";
    private const string ExistingActionKeyframeTimeLockReason = "현재 시각에는 이미 액션 키프레임이 있습니다";
    private const string ExistingLockOnKeyframeTimeLockReason = "현재 시각에는 이미 Lock-on 키프레임이 있습니다";
    private const string NoActionKeyframeLockReason = "액션 키프레임을 선택해야 편집할 수 있습니다";
    private const string NoLockOnKeyframeLockReason = "Lock-on 키프레임을 선택해야 편집할 수 있습니다";
    private readonly SceneDocument _document;
    private readonly Stack<ISceneEditCommand> _undoStack = [];
    private readonly Stack<ISceneEditCommand> _redoStack = [];
    private TransformKeyframe? _previewStart;
    private TransformPreview? _preview;
    private TransformKeyframe? _selectedTransformKeyframe;
    private ActionKeyframe? _selectedActionKeyframe;
    private LockOnKeyframe? _selectedLockOnKeyframe;

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

    public string? SelectedActionKeyframeId { get; private set; }

    public string? SelectedLockOnKeyframeId { get; private set; }

    public TimelineTrackKind ActiveTimelineTrack { get; private set; }

    public PlaybackClock Playback { get; }

    public bool CanEditSelectedTransform { get; private set; }

    public string? EditLockReason { get; private set; }

    public bool CanAddTransformKeyframe { get; private set; }

    public string? AddTransformKeyframeLockReason { get; private set; }

    public bool CanDeleteSelectedTransformKeyframe { get; private set; }

    public string? DeleteTransformKeyframeLockReason { get; private set; }

    public TimelineTrackEditAvailability ActionEditAvailability { get; private set; } =
        new(false, NoSelectionLockReason, false, NoSelectionLockReason, false, NoSelectionLockReason);

    public TimelineTrackEditAvailability LockOnEditAvailability { get; private set; } =
        new(false, NoSelectionLockReason, false, NoSelectionLockReason, false, NoSelectionLockReason);

    public bool CanEditHistory => SelectedActorId is not null && !Playback.IsPlaying;

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

    public event EventHandler<ActionKeyframeSelectionChangedEventArgs>? ActionKeyframeSelectionChanged;

    public event EventHandler<LockOnKeyframeSelectionChangedEventArgs>? LockOnKeyframeSelectionChanged;

    public event EventHandler<TransformPreviewChangedEventArgs>? PreviewChanged;

    public event EventHandler<EditAvailabilityChangedEventArgs>? EditAvailabilityChanged;

    public event EventHandler<TimelineEditAvailabilityChangedEventArgs>? TimelineEditAvailabilityChanged;

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
        ActiveTimelineTrack = TimelineTrackKind.Transform;
        var transformSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime(forceNotification: true);
        var actionSelectionChange = RefreshSelectedActionKeyframeAtCurrentTime(forceNotification: true);
        var lockOnSelectionChange = RefreshSelectedLockOnKeyframeAtCurrentTime(forceNotification: true);
        var availabilityChanges = RefreshAllEditAvailabilityState();
        RaiseAllSelectionAndAvailabilityChanged(
            transformSelectionChange,
            actionSelectionChange,
            lockOnSelectionChange,
            availabilityChanges.Transform,
            availabilityChanges.Timeline);
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

    public ActionKeyframe? GetSelectedActionKeyframe()
    {
        if (SelectedActorId is null || SelectedActionKeyframeId is null)
        {
            return null;
        }

        return GetSelectedActor()?.ActionKeyframes
            .SingleOrDefault(frame => frame.Id == SelectedActionKeyframeId);
    }

    public LockOnKeyframe? GetSelectedLockOnKeyframe()
    {
        if (SelectedActorId is null || SelectedLockOnKeyframeId is null)
        {
            return null;
        }

        return GetSelectedActor()?.LockOnKeyframes
            .SingleOrDefault(frame => frame.Id == SelectedLockOnKeyframeId);
    }

    public IReadOnlyList<ActionKeyframe> GetSelectedActorActionKeyframes() =>
        GetSelectedActor()?.ActionKeyframes.ToArray() ?? [];

    public IReadOnlyList<LockOnKeyframe> GetSelectedActorLockOnKeyframes() =>
        GetSelectedActor()?.LockOnKeyframes.ToArray() ?? [];

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
        ActiveTimelineTrack = TimelineTrackKind.Transform;
        var keyframeSelectionChange = SetSelectedTransformKeyframe(keyframe);
        UpdateEditAvailability();
        RaiseTransformKeyframeSelectionChanged(keyframeSelectionChange);
        return SceneEditResult.Applied;
    }

    public SceneEditResult SelectActionKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        var actorId = SelectedActorId;
        var keyframe = GetSelectedActor()?.ActionKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);
        if (actorId is null || keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        Playback.Pause();
        keyframe = GetCurrentActionKeyframe(actorId, keyframeId);
        if (keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        Playback.Seek(keyframe.TimeSeconds);
        keyframe = GetCurrentActionKeyframe(actorId, keyframeId);
        if (keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        if (Math.Abs(Playback.CurrentTimeSeconds - keyframe.TimeSeconds) > EditTimeToleranceSeconds)
        {
            Playback.Seek(keyframe.TimeSeconds);
            keyframe = GetCurrentActionKeyframe(actorId, keyframeId);
            if (keyframe is null)
            {
                return SceneEditResult.Conflict;
            }
        }

        ActiveTimelineTrack = TimelineTrackKind.Action;
        var selectionChange = SetSelectedActionKeyframe(keyframe);
        var availabilityChanges = RefreshAllEditAvailabilityState();
        RaiseActionKeyframeSelectionChanged(selectionChange);
        RaiseEditAvailabilityChanged(availabilityChanges.Transform);
        RaiseTimelineEditAvailabilityChanged(availabilityChanges.Timeline);
        return SceneEditResult.Applied;
    }

    public SceneEditResult SelectLockOnKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        var actorId = SelectedActorId;
        var keyframe = GetSelectedActor()?.LockOnKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);
        if (actorId is null || keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        Playback.Pause();
        keyframe = GetCurrentLockOnKeyframe(actorId, keyframeId);
        if (keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        Playback.Seek(keyframe.TimeSeconds);
        keyframe = GetCurrentLockOnKeyframe(actorId, keyframeId);
        if (keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        if (Math.Abs(Playback.CurrentTimeSeconds - keyframe.TimeSeconds) > EditTimeToleranceSeconds)
        {
            Playback.Seek(keyframe.TimeSeconds);
            keyframe = GetCurrentLockOnKeyframe(actorId, keyframeId);
            if (keyframe is null)
            {
                return SceneEditResult.Conflict;
            }
        }

        ActiveTimelineTrack = TimelineTrackKind.LockOn;
        var selectionChange = SetSelectedLockOnKeyframe(keyframe);
        var availabilityChanges = RefreshAllEditAvailabilityState();
        RaiseLockOnKeyframeSelectionChanged(selectionChange);
        RaiseEditAvailabilityChanged(availabilityChanges.Transform);
        RaiseTimelineEditAvailabilityChanged(availabilityChanges.Timeline);
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

        return SceneEditResult.Applied;
    }

    public SceneEditResult AddActionKeyframeAtCurrentTime(string actionKey)
    {
        if (!ActionEditAvailability.CanAdd || SelectedActorId is null)
        {
            return SceneEditResult.Conflict;
        }

        var actor = GetSelectedActor();
        if (actor is null)
        {
            return SceneEditResult.Conflict;
        }

        ActionKeyframe keyframe;
        try
        {
            keyframe = new ActionKeyframe(
                GetNextActionKeyframeId(actor),
                Playback.CurrentTimeSeconds,
                actionKey);
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.Action,
            new AddActionKeyframeCommand(SelectedActorId, keyframe));
    }

    public SceneEditResult UpdateSelectedActionKeyframe(double timeSeconds, string actionKey)
    {
        if (!ActionEditAvailability.CanUpdate || SelectedActorId is null || _selectedActionKeyframe is null ||
            !double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > _document.DurationSeconds)
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedActionKeyframe;
        ActionKeyframe after;
        try
        {
            after = new ActionKeyframe(before.Id, timeSeconds, actionKey);
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.Action,
            new UpdateActionKeyframeCommand(SelectedActorId, before, after));
    }

    public SceneEditResult RemoveSelectedActionKeyframe()
    {
        if (!ActionEditAvailability.CanDelete || SelectedActorId is null || _selectedActionKeyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedActionKeyframe;
        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.Action,
            new RemoveActionKeyframeCommand(SelectedActorId, before));
    }

    public SceneEditResult AddLockOnKeyframeAtCurrentTime(
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        if (!LockOnEditAvailability.CanAdd || SelectedActorId is null)
        {
            return SceneEditResult.Conflict;
        }

        var actor = GetSelectedActor();
        if (actor is null)
        {
            return SceneEditResult.Conflict;
        }

        LockOnKeyframe keyframe;
        try
        {
            keyframe = new LockOnKeyframe(
                GetNextLockOnKeyframeId(actor),
                Playback.CurrentTimeSeconds,
                enabled,
                targetActorId,
                yawOffsetDegrees,
                trackingMode);
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.LockOn,
            new AddLockOnKeyframeCommand(SelectedActorId, keyframe));
    }

    public SceneEditResult UpdateSelectedLockOnKeyframe(
        double timeSeconds,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        if (!LockOnEditAvailability.CanUpdate || SelectedActorId is null || _selectedLockOnKeyframe is null ||
            !double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > _document.DurationSeconds ||
            !double.IsFinite(yawOffsetDegrees))
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedLockOnKeyframe;
        LockOnKeyframe after;
        try
        {
            after = new LockOnKeyframe(
                before.Id,
                timeSeconds,
                enabled,
                targetActorId,
                yawOffsetDegrees,
                trackingMode);
        }
        catch (ArgumentException)
        {
            return SceneEditResult.Conflict;
        }

        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.LockOn,
            new UpdateLockOnKeyframeCommand(SelectedActorId, before, after));
    }

    public SceneEditResult RemoveSelectedLockOnKeyframe()
    {
        if (!LockOnEditAvailability.CanDelete || SelectedActorId is null || _selectedLockOnKeyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        var before = _selectedLockOnKeyframe;
        ClearPreview();
        return ExecuteCommandOnTrack(
            TimelineTrackKind.LockOn,
            new RemoveLockOnKeyframeCommand(SelectedActorId, before));
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
        if (!CanEditHistory || _undoStack.Count == 0)
        {
            return false;
        }

        var command = _undoStack.Peek();
        var revisionBefore = _document.Revision;
        if (!TryUndo(command, revisionBefore, () => MoveUndoToRedoAndReconcile(command)))
        {
            return false;
        }

        MoveUndoToRedoAndReconcile(command);
        return true;
    }

    public bool Redo()
    {
        if (!CanEditHistory || _redoStack.Count == 0)
        {
            return false;
        }

        var command = _redoStack.Peek();
        var revisionBefore = _document.Revision;
        if (TryExecuteDetailed(command, revisionBefore, () => MoveRedoToUndoAndReconcile(command)) != SceneEditResult.Applied)
        {
            return false;
        }

        MoveRedoToUndoAndReconcile(command);
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

        CommitExecuteAndReconcile(command);
        return SceneEditResult.Applied;
    }

    private bool ExecuteSelectedTransform(TransformKeyframe before, Position3 position, double yawDegrees) =>
        ExecuteCommand(new ReplaceTransformCommand(
            SelectedActorId!,
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, position, yawDegrees)));

    private SceneEditResult ExecuteCommandOnTrack(TimelineTrackKind track, ISceneEditCommand command)
    {
        var previousTrack = ActiveTimelineTrack;
        ActiveTimelineTrack = track;
        var result = ExecuteCommandDetailed(command);
        if (result != SceneEditResult.Applied)
        {
            ActiveTimelineTrack = previousTrack;
        }

        return result;
    }

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
            CompleteMutationExceptionTransition(historyException, ReconcileAllSelectedKeyframesAfterMutation);
            throw;
        }

        ReconcileAllSelectedKeyframesAfterMutation();
    }

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs args)
    {
        var transformSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime();
        var actionSelectionChange = RefreshSelectedActionKeyframeAtCurrentTime();
        var lockOnSelectionChange = RefreshSelectedLockOnKeyframeAtCurrentTime();
        var availabilityChanges = RefreshAllEditAvailabilityState();
        try
        {
            ClearPreview();
        }
        catch (Exception exception)
        {
            CompleteMutationExceptionTransition(
                exception,
                () => RaiseAllSelectionAndAvailabilityChanged(
                    transformSelectionChange,
                    actionSelectionChange,
                    lockOnSelectionChange,
                    availabilityChanges.Transform,
                    availabilityChanges.Timeline));
            throw;
        }

        RaiseAllSelectionAndAvailabilityChanged(
            transformSelectionChange,
            actionSelectionChange,
            lockOnSelectionChange,
            availabilityChanges.Transform,
            availabilityChanges.Timeline);
    }

    private void UpdateEditAvailability(bool raiseEvent = true)
    {
        var availabilityChanges = RefreshAllEditAvailabilityState();
        if (raiseEvent)
        {
            RaiseEditAvailabilityChanged(availabilityChanges.Transform);
            RaiseTimelineEditAvailabilityChanged(availabilityChanges.Timeline);
        }
    }

    private (
        EditAvailabilityChangedEventArgs? Transform,
        TimelineEditAvailabilityChangedEventArgs? Timeline) RefreshAllEditAvailabilityState() =>
        (RefreshEditAvailabilityState(), RefreshTimelineEditAvailabilityState());

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

    private TimelineEditAvailabilityChangedEventArgs? RefreshTimelineEditAvailabilityState()
    {
        var action = GetActionEditAvailability();
        var lockOn = GetLockOnEditAvailability();
        if (ActionEditAvailability == action && LockOnEditAvailability == lockOn)
        {
            return null;
        }

        ActionEditAvailability = action;
        LockOnEditAvailability = lockOn;
        return new TimelineEditAvailabilityChangedEventArgs(action, lockOn);
    }

    private void RaiseTimelineEditAvailabilityChanged(
        TimelineEditAvailabilityChangedEventArgs? availabilityChange)
    {
        if (availabilityChange is not null &&
            availabilityChange.ActionEditAvailability == ActionEditAvailability &&
            availabilityChange.LockOnEditAvailability == LockOnEditAvailability)
        {
            TimelineEditAvailabilityChanged?.Invoke(this, availabilityChange);
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

    private TimelineTrackEditAvailability GetActionEditAvailability() =>
        GetTimelineTrackEditAvailability(
            GetSelectedActor()?.ActionKeyframes.Select(frame => frame.TimeSeconds),
            GetSelectedActionKeyframe()?.TimeSeconds,
            ExistingActionKeyframeTimeLockReason,
            NoActionKeyframeLockReason);

    private TimelineTrackEditAvailability GetLockOnEditAvailability() =>
        GetTimelineTrackEditAvailability(
            GetSelectedActor()?.LockOnKeyframes.Select(frame => frame.TimeSeconds),
            GetSelectedLockOnKeyframe()?.TimeSeconds,
            ExistingLockOnKeyframeTimeLockReason,
            NoLockOnKeyframeLockReason);

    private TimelineTrackEditAvailability GetTimelineTrackEditAvailability(
        IEnumerable<double>? frameTimes,
        double? selectedTimeSeconds,
        string existingTimeReason,
        string noKeyframeReason)
    {
        if (SelectedActorId is null || frameTimes is null)
        {
            return new TimelineTrackEditAvailability(
                false,
                NoSelectionLockReason,
                false,
                NoSelectionLockReason,
                false,
                NoSelectionLockReason);
        }

        if (Playback.IsPlaying)
        {
            return new TimelineTrackEditAvailability(
                false,
                PlayingLockReason,
                false,
                PlayingLockReason,
                false,
                PlayingLockReason);
        }

        var hasFrameAtCurrentTime = frameTimes.Any(timeSeconds =>
            Math.Abs(timeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds);
        var selectionIsAtCurrentTime = selectedTimeSeconds is not null &&
            Math.Abs(selectedTimeSeconds.Value - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds;
        var selectionReason = selectedTimeSeconds is null
            ? noKeyframeReason
            : SelectedKeyframeTimeLockReason;
        return new TimelineTrackEditAvailability(
            !hasFrameAtCurrentTime,
            hasFrameAtCurrentTime ? existingTimeReason : null,
            selectionIsAtCurrentTime,
            selectionIsAtCurrentTime ? null : selectionReason,
            selectionIsAtCurrentTime,
            selectionIsAtCurrentTime ? null : selectionReason);
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
        if (!forceNotification &&
            SelectedTransformKeyframeId == keyframeId &&
            ((_selectedTransformKeyframe is null && keyframe is null) ||
             SameTransform(_selectedTransformKeyframe, keyframe)))
        {
            _selectedTransformKeyframe = keyframe;
            return null;
        }

        SelectedTransformKeyframeId = keyframeId;
        _selectedTransformKeyframe = keyframe;
        return new TransformKeyframeSelectionChangedEventArgs(SelectedActorId, keyframeId, keyframe);
    }

    private ActionKeyframeSelectionChangedEventArgs? RefreshSelectedActionKeyframeAtCurrentTime(
        bool forceNotification = false)
    {
        var actor = GetSelectedActor();
        var keyframe = actor?.ActionKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds);
        return SetSelectedActionKeyframe(keyframe, forceNotification);
    }

    private LockOnKeyframeSelectionChangedEventArgs? RefreshSelectedLockOnKeyframeAtCurrentTime(
        bool forceNotification = false)
    {
        var actor = GetSelectedActor();
        var keyframe = actor?.LockOnKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds);
        return SetSelectedLockOnKeyframe(keyframe, forceNotification);
    }

    private ActionKeyframeSelectionChangedEventArgs? SetSelectedActionKeyframe(
        ActionKeyframe? keyframe,
        bool forceNotification = false)
    {
        var keyframeId = keyframe?.Id;
        if (!forceNotification &&
            SelectedActionKeyframeId == keyframeId &&
            ((_selectedActionKeyframe is null && keyframe is null) ||
             SameAction(_selectedActionKeyframe, keyframe)))
        {
            _selectedActionKeyframe = keyframe;
            return null;
        }

        SelectedActionKeyframeId = keyframeId;
        _selectedActionKeyframe = keyframe;
        return new ActionKeyframeSelectionChangedEventArgs(SelectedActorId, keyframeId, keyframe);
    }

    private LockOnKeyframeSelectionChangedEventArgs? SetSelectedLockOnKeyframe(
        LockOnKeyframe? keyframe,
        bool forceNotification = false)
    {
        var keyframeId = keyframe?.Id;
        if (!forceNotification &&
            SelectedLockOnKeyframeId == keyframeId &&
            ((_selectedLockOnKeyframe is null && keyframe is null) ||
             SameLockOn(_selectedLockOnKeyframe, keyframe)))
        {
            _selectedLockOnKeyframe = keyframe;
            return null;
        }

        SelectedLockOnKeyframeId = keyframeId;
        _selectedLockOnKeyframe = keyframe;
        return new LockOnKeyframeSelectionChangedEventArgs(SelectedActorId, keyframeId, keyframe);
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

    private void RaiseActionKeyframeSelectionChanged(ActionKeyframeSelectionChangedEventArgs? selectionChange)
    {
        if (selectionChange is not null &&
            selectionChange.ActorId == SelectedActorId &&
            selectionChange.KeyframeId == SelectedActionKeyframeId &&
            ((selectionChange.Keyframe is null && GetSelectedActionKeyframe() is null) ||
             SameAction(selectionChange.Keyframe, GetSelectedActionKeyframe())))
        {
            ActionKeyframeSelectionChanged?.Invoke(this, selectionChange);
        }
    }

    private void RaiseLockOnKeyframeSelectionChanged(LockOnKeyframeSelectionChangedEventArgs? selectionChange)
    {
        if (selectionChange is not null &&
            selectionChange.ActorId == SelectedActorId &&
            selectionChange.KeyframeId == SelectedLockOnKeyframeId &&
            ((selectionChange.Keyframe is null && GetSelectedLockOnKeyframe() is null) ||
             SameLockOn(selectionChange.Keyframe, GetSelectedLockOnKeyframe())))
        {
            LockOnKeyframeSelectionChanged?.Invoke(this, selectionChange);
        }
    }

    private void RaiseAllSelectionAndAvailabilityChanged(
        TransformKeyframeSelectionChangedEventArgs? transformSelectionChange,
        ActionKeyframeSelectionChangedEventArgs? actionSelectionChange,
        LockOnKeyframeSelectionChangedEventArgs? lockOnSelectionChange,
        EditAvailabilityChangedEventArgs? transformAvailabilityChange,
        TimelineEditAvailabilityChangedEventArgs? timelineAvailabilityChange)
    {
        RaiseTransformKeyframeSelectionChanged(transformSelectionChange);
        RaiseActionKeyframeSelectionChanged(actionSelectionChange);
        RaiseLockOnKeyframeSelectionChanged(lockOnSelectionChange);
        RaiseEditAvailabilityChanged(transformAvailabilityChange);
        RaiseTimelineEditAvailabilityChanged(timelineAvailabilityChange);
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

    private static string GetNextActionKeyframeId(ActorTrack actor)
    {
        var existingIds = actor.ActionKeyframes.Select(frame => frame.Id).ToHashSet(StringComparer.Ordinal);
        for (var ordinal = 1; ; ordinal++)
        {
            var candidate = $"{actor.ActorId}-action-{ordinal:D4}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string GetNextLockOnKeyframeId(ActorTrack actor)
    {
        var existingIds = actor.LockOnKeyframes.Select(frame => frame.Id).ToHashSet(StringComparer.Ordinal);
        for (var ordinal = 1; ; ordinal++)
        {
            var candidate = $"{actor.ActorId}-lock-on-{ordinal:D4}";
            if (!existingIds.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void ReconcileAllSelectedKeyframesAfterMutation()
    {
        var preferredTransformId = SelectedTransformKeyframeId;
        var preferredActionId = SelectedActionKeyframeId;
        var preferredLockOnId = SelectedLockOnKeyframeId;
        for (var attempt = 0; attempt < MaxReconciliationAttempts; attempt++)
        {
            var actor = GetSelectedActor();
            if (actor is null)
            {
                var revisionBeforePublish = _document.Revision;
                var noTransformChange = SetSelectedTransformKeyframe(null);
                var noActionChange = SetSelectedActionKeyframe(null);
                var noLockOnChange = SetSelectedLockOnKeyframe(null);
                var noActorAvailabilityChanges = RefreshAllEditAvailabilityState();
                RaiseAllSelectionAndAvailabilityChanged(
                    noTransformChange,
                    noActionChange,
                    noLockOnChange,
                    noActorAvailabilityChanges.Transform,
                    noActorAvailabilityChanges.Timeline);
                if (_document.Revision == revisionBeforePublish && GetSelectedActor() is null)
                {
                    return;
                }

                continue;
            }

            var actorId = actor.ActorId;
            var transform = SelectTransformForReconciliation(actor, preferredTransformId);
            var action = SelectActionForReconciliation(actor, preferredActionId);
            var lockOn = SelectLockOnForReconciliation(actor, preferredLockOnId);
            var activeTime = GetActiveTimelineSelectionTime(transform, action, lockOn);
            if (activeTime is not null &&
                Math.Abs(Playback.CurrentTimeSeconds - activeTime.Value) > EditTimeToleranceSeconds)
            {
                Playback.Seek(activeTime.Value);
                continue;
            }

            var revisionBeforeSelectionPublish = _document.Revision;
            var transformChange = SetSelectedTransformKeyframe(transform);
            var actionChange = SetSelectedActionKeyframe(action);
            var lockOnChange = SetSelectedLockOnKeyframe(lockOn);
            var availabilityChanges = RefreshAllEditAvailabilityState();
            RaiseAllSelectionAndAvailabilityChanged(
                transformChange,
                actionChange,
                lockOnChange,
                availabilityChanges.Transform,
                availabilityChanges.Timeline);

            actor = GetSelectedActor();
            if (_document.Revision != revisionBeforeSelectionPublish || actor?.ActorId != actorId)
            {
                continue;
            }

            transform = SelectTransformForReconciliation(actor, preferredTransformId);
            action = SelectActionForReconciliation(actor, preferredActionId);
            lockOn = SelectLockOnForReconciliation(actor, preferredLockOnId);
            activeTime = GetActiveTimelineSelectionTime(transform, action, lockOn);
            if (activeTime is null ||
                Math.Abs(Playback.CurrentTimeSeconds - activeTime.Value) <= EditTimeToleranceSeconds)
            {
                return;
            }
        }

        throw new InvalidOperationException("Timeline selection reconciliation did not stabilize.");
    }

    private double? GetActiveTimelineSelectionTime(
        TransformKeyframe? transform,
        ActionKeyframe? action,
        LockOnKeyframe? lockOn) => ActiveTimelineTrack switch
        {
            TimelineTrackKind.Transform => transform?.TimeSeconds,
            TimelineTrackKind.Action => action?.TimeSeconds,
            TimelineTrackKind.LockOn => lockOn?.TimeSeconds,
            _ => null
        };

    private TransformKeyframe? SelectTransformForReconciliation(ActorTrack actor, string? preferredId) =>
        (preferredId is null
            ? null
            : actor.TransformKeyframes.SingleOrDefault(frame => frame.Id == preferredId)) ??
        actor.TransformKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds) ??
        actor.TransformKeyframes
            .OrderBy(frame => Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds))
            .ThenBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private ActionKeyframe? SelectActionForReconciliation(ActorTrack actor, string? preferredId) =>
        (preferredId is null
            ? null
            : actor.ActionKeyframes.SingleOrDefault(frame => frame.Id == preferredId)) ??
        actor.ActionKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds) ??
        actor.ActionKeyframes
            .OrderBy(frame => Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds))
            .ThenBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private LockOnKeyframe? SelectLockOnForReconciliation(ActorTrack actor, string? preferredId) =>
        (preferredId is null
            ? null
            : actor.LockOnKeyframes.SingleOrDefault(frame => frame.Id == preferredId)) ??
        actor.LockOnKeyframes.SingleOrDefault(frame =>
            Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds) <= EditTimeToleranceSeconds) ??
        actor.LockOnKeyframes
            .OrderBy(frame => Math.Abs(frame.TimeSeconds - Playback.CurrentTimeSeconds))
            .ThenBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    private ActionKeyframe? GetCurrentActionKeyframe(string actorId, string keyframeId) =>
        SelectedActorId == actorId
            ? GetSelectedActor()?.ActionKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
            : null;

    private LockOnKeyframe? GetCurrentLockOnKeyframe(string actorId, string keyframeId) =>
        SelectedActorId == actorId
            ? GetSelectedActor()?.LockOnKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
            : null;

    private static bool SameTransform(TransformKeyframe? left, TransformKeyframe? right) =>
        left is not null && right is not null &&
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Position == right.Position &&
        left.YawDegrees == right.YawDegrees;

    private static bool SameAction(ActionKeyframe? left, ActionKeyframe? right) =>
        left is not null && right is not null &&
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.ActionKey == right.ActionKey;

    private static bool SameLockOn(LockOnKeyframe? left, LockOnKeyframe? right) =>
        left is not null && right is not null &&
        left.Id == right.Id &&
        left.TimeSeconds == right.TimeSeconds &&
        left.Enabled == right.Enabled &&
        left.TargetActorId == right.TargetActorId &&
        left.YawOffsetDegrees == right.YawOffsetDegrees &&
        left.TrackingMode == right.TrackingMode;

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
