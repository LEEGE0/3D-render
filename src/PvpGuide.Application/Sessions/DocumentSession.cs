using PvpGuide.Application.Commands;
using PvpGuide.Application.Editing;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Application.Sessions;

public sealed class DocumentSession
{
    private readonly SceneDocument _document;
    private readonly Stack<ISceneEditCommand> _undoStack = [];
    private readonly Stack<ISceneEditCommand> _redoStack = [];
    private TransformKeyframe? _previewStart;
    private TransformPreview? _preview;

    public DocumentSession(SceneDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public ISceneSnapshotSource SnapshotSource => _document;

    public string? SelectedActorId { get; private set; }

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    internal int UndoCount => _undoStack.Count;

    internal int RedoCount => _redoStack.Count;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<TransformPreviewChangedEventArgs>? PreviewChanged;

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
        var before = GetSelectedTransform();
        return before is null
            ? false
            : ExecuteSelectedTransform(before, destination, before.YawDegrees);
    }

    public bool RotateSelectedActor(double yawDegrees)
    {
        var before = GetSelectedTransform();
        return before is null
            ? false
            : ExecuteSelectedTransform(before, before.Position, yawDegrees);
    }

    public bool SetSelectedActorTransform(Position3 position, double yawDegrees)
    {
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
        if (!TryExecute(command, revisionBefore, () => MoveRedoToUndo(command)))
        {
            return false;
        }

        MoveRedoToUndo(command);
        return true;
    }

    public void BeginPreview()
    {
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

    public bool CommitPreview()
    {
        if (_previewStart is null || _preview is null)
        {
            return false;
        }

        var before = _previewStart;
        var preview = _preview;
        ClearPreview();
        return ExecuteCommand(new ReplaceTransformCommand(
            SelectedActorId!,
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, preview.Position, preview.YawDegrees)));
    }

    public void CancelPreview() => ClearPreview();

    internal bool ExecuteCommand(ISceneEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var revisionBefore = _document.Revision;
        if (!TryExecute(command, revisionBefore, () => CommitExecute(command)))
        {
            return false;
        }

        CommitExecute(command);
        return true;
    }

    private bool ExecuteSelectedTransform(TransformKeyframe before, Position3 position, double yawDegrees) =>
        ExecuteCommand(new ReplaceTransformCommand(
            SelectedActorId!,
            before,
            new TransformKeyframe(before.Id, before.TimeSeconds, position, yawDegrees)));

    private bool TryExecute(ISceneEditCommand command, long revisionBefore, Action onMutationException)
    {
        try
        {
            return command.Execute(_document);
        }
        catch (Exception) when (_document.Revision > revisionBefore)
        {
            onMutationException();
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

    private bool TryUndo(ISceneEditCommand command, long revisionBefore, Action onMutationException)
    {
        try
        {
            return command.Undo(_document);
        }
        catch (Exception) when (_document.Revision > revisionBefore)
        {
            onMutationException();
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
    }

    private void MoveUndoToRedo(ISceneEditCommand command)
    {
        _undoStack.Pop();
        _redoStack.Push(command);
    }

    private void MoveRedoToUndo(ISceneEditCommand command)
    {
        _redoStack.Pop();
        _undoStack.Push(command);
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
