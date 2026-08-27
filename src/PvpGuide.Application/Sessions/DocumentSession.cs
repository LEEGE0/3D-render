using PvpGuide.Application.Commands;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class DocumentSession
{
    private const double EditTimeToleranceSeconds = 0.000000001;
    private const string NoSelectionLockReason = "배우를 선택해야 편집할 수 있습니다";
    private const string PlayingLockReason = "재생 중에는 편집할 수 없습니다";
    private const string FirstKeyframeTimeLockReason = "최초 키프레임 시각에서만 편집할 수 있습니다";
    private readonly SceneDocument _document;
    private readonly Stack<ISceneEditCommand> _undoStack = [];
    private readonly Stack<ISceneEditCommand> _redoStack = [];
    private TransformKeyframe? _previewStart;
    private TransformPreview? _preview;

    public DocumentSession(SceneDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Playback = new PlaybackClock(_document.DurationSeconds, _document.FramesPerSecond);
        Playback.Changed += OnPlaybackChanged;
        UpdateEditAvailability(false);
    }

    public ISceneSnapshotSource SnapshotSource => _document;

    public string? SelectedActorId { get; private set; }

    public PlaybackClock Playback { get; }

    public bool CanEditSelectedTransform { get; private set; }

    public string? EditLockReason { get; private set; }

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
        UpdateEditAvailability();
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedActorId));
    }

    public TransformKeyframe? GetSelectedTransform()
    {
        if (SelectedActorId is null)
        {
            return null;
        }

        return _document.Actors.Single(actor => actor.ActorId == SelectedActorId).TransformKeyframes[0];
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
        if (!TryUndo(command, revisionBefore, () => MoveUndoToRedo(command)))
        {
            return false;
        }

        MoveUndoToRedo(command);
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
        if (TryExecuteDetailed(command, revisionBefore, () => MoveRedoToUndo(command)) != SceneEditResult.Applied)
        {
            return false;
        }

        MoveRedoToUndo(command);
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
        var result = TryExecuteDetailed(command, revisionBefore, () => CommitExecute(command));
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

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs args)
    {
        try
        {
            ClearPreview();
        }
        catch (Exception exception)
        {
            CompleteMutationExceptionTransition(exception, () => UpdateEditAvailability());
            throw;
        }

        UpdateEditAvailability();
    }

    private void UpdateEditAvailability(bool raiseEvent = true)
    {
        var (canEdit, reason) = GetEditAvailability();
        if (CanEditSelectedTransform == canEdit && EditLockReason == reason)
        {
            return;
        }

        CanEditSelectedTransform = canEdit;
        EditLockReason = reason;
        if (raiseEvent)
        {
            EditAvailabilityChanged?.Invoke(this, new EditAvailabilityChangedEventArgs(canEdit, reason));
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
            : (false, FirstKeyframeTimeLockReason);
    }

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
