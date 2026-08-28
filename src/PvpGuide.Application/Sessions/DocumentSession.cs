using PvpGuide.Application.Commands;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using System.Runtime.ExceptionServices;

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
    private readonly Queue<SemanticRollbackWorkItem> _semanticRollbackWorkItems = [];
    private bool _isRestoringSemanticSelection;
    private bool _isDispatchingSemanticRollbackWork;
    private bool _isSelectingSemanticKeyframe;
    private long _actionSelectionPublicationSequence;
    private long _lockOnSelectionPublicationSequence;
    private ActionSelectionPublication? _lastActionSelectionPublication;
    private LockOnSelectionPublication? _lastLockOnSelectionPublication;

    public DocumentSession(SceneDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Playback = new PlaybackClock(_document.DurationSeconds, _document.FramesPerSecond);
        Playback.Changed += OnPlaybackChanged;
        UpdateEditAvailability(false);
    }

    public ISceneSnapshotSource SnapshotSource => _document;

    public ISceneProjectionSource ProjectionSource => _document;

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

        if (_isRestoringSemanticSelection && _isDispatchingSemanticRollbackWork)
        {
            EnqueueSemanticRollbackWork(new DeferredActorSelection(actorId));
            return;
        }

        var notification = ApplyActorSelection(actorId);
        if (notification is not null)
        {
            PublishSessionNotification(notification);
        }
    }

    private SessionNotificationBatch? ApplyActorSelection(string? actorId)
    {
        if (SelectedActorId == actorId)
        {
            return null;
        }

        ClearPreview();
        SelectedActorId = actorId;
        ActiveTimelineTrack = TimelineTrackKind.Transform;
        var transformSelectionChange = RefreshSelectedTransformKeyframeAtCurrentTime(forceNotification: true);
        var actionSelectionChange = RefreshSelectedActionKeyframeAtCurrentTime(forceNotification: true);
        var lockOnSelectionChange = RefreshSelectedLockOnKeyframeAtCurrentTime(forceNotification: true);
        var availabilityChanges = RefreshAllEditAvailabilityState();
        return new SessionNotificationBatch(
            transformSelectionChange,
            actionSelectionChange,
            lockOnSelectionChange,
            availabilityChanges.Transform,
            availabilityChanges.Timeline,
            new SelectionChangedEventArgs(SelectedActorId));
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

    public SceneEditResult ActivateSemanticTrack(TimelineTrackKind track)
    {
        if (track is not TimelineTrackKind.Action and not TimelineTrackKind.LockOn)
        {
            throw new ArgumentOutOfRangeException(nameof(track), "Only Action and Lock-on tracks can be activated here.");
        }

        if (_isRestoringSemanticSelection || SelectedActorId is null)
        {
            return SceneEditResult.Conflict;
        }

        if (ActiveTimelineTrack == track)
        {
            return SceneEditResult.NoChange;
        }

        ActiveTimelineTrack = track;
        if (track == TimelineTrackKind.Action)
        {
            RaiseActionKeyframeSelectionChanged(SetSelectedActionKeyframe(
                GetSelectedActionKeyframe(),
                forceNotification: true));
        }
        else
        {
            RaiseLockOnKeyframeSelectionChanged(SetSelectedLockOnKeyframe(
                GetSelectedLockOnKeyframe(),
                forceNotification: true));
        }

        return SceneEditResult.Applied;
    }

    public SceneEditResult SelectTransformKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        if (_isRestoringSemanticSelection)
        {
            return SceneEditResult.Conflict;
        }

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
        if (_isRestoringSemanticSelection || _isSelectingSemanticKeyframe)
        {
            return SceneEditResult.Conflict;
        }

        var actorId = SelectedActorId;
        var keyframe = GetSelectedActor()?.ActionKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);
        if (actorId is null || keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        var rollbackState = CaptureSemanticSelectionRollbackState();
        var activeTrackBeforeAttempt = ActiveTimelineTrack;
        var publicationSequenceBeforeAttempt = _actionSelectionPublicationSequence;
        _isSelectingSemanticKeyframe = true;
        try
        {
            ActiveTimelineTrack = TimelineTrackKind.Action;
            ClearPreview();
            for (var attempt = 0; attempt < MaxReconciliationAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    activeTrackBeforeAttempt = ActiveTimelineTrack;
                    publicationSequenceBeforeAttempt = _actionSelectionPublicationSequence;
                    ActiveTimelineTrack = TimelineTrackKind.Action;
                }

                Playback.Pause();
                keyframe = GetCurrentActionKeyframe(actorId, keyframeId);
                if (keyframe is null)
                {
                    return RestoreSemanticSelectionAfterConflict(rollbackState);
                }

                Playback.Seek(keyframe.TimeSeconds);
                keyframe = GetCurrentActionKeyframe(actorId, keyframeId);
                if (keyframe is null)
                {
                    return RestoreSemanticSelectionAfterConflict(rollbackState);
                }

                if (Playback.IsPlaying || !IsAtTime(Playback.CurrentTimeSeconds, keyframe.TimeSeconds))
                {
                    continue;
                }

                var targetPayloadPublishedThisAttempt = WasActionTargetPublishedSince(
                    publicationSequenceBeforeAttempt,
                    actorId,
                    keyframe);
                var forceTrackRefresh = ActiveTimelineTrack != TimelineTrackKind.Action ||
                    (!targetPayloadPublishedThisAttempt &&
                     activeTrackBeforeAttempt != TimelineTrackKind.Action &&
                     SelectedActionKeyframeId == keyframeId &&
                     SameAction(_selectedActionKeyframe, keyframe));
                ActiveTimelineTrack = TimelineTrackKind.Action;
                var selectionChange = SetSelectedActionKeyframe(keyframe, forceTrackRefresh);
                var availabilityChanges = RefreshAllEditAvailabilityState();
                RaiseActionKeyframeSelectionChanged(selectionChange);
                RaiseEditAvailabilityChanged(availabilityChanges.Transform);
                RaiseTimelineEditAvailabilityChanged(availabilityChanges.Timeline);

                var current = GetCurrentActionKeyframe(actorId, keyframeId);
                if (current is not null &&
                    !Playback.IsPlaying &&
                    IsAtTime(Playback.CurrentTimeSeconds, current.TimeSeconds) &&
                    ActiveTimelineTrack == TimelineTrackKind.Action &&
                    SelectedActionKeyframeId == keyframeId &&
                    SameAction(_selectedActionKeyframe, current))
                {
                    return SceneEditResult.Applied;
                }

            }

            return RestoreSemanticSelectionAfterConflict(rollbackState);
        }
        finally
        {
            _isSelectingSemanticKeyframe = false;
        }
    }

    public SceneEditResult SelectLockOnKeyframe(string keyframeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
        if (_isRestoringSemanticSelection || _isSelectingSemanticKeyframe)
        {
            return SceneEditResult.Conflict;
        }

        var actorId = SelectedActorId;
        var keyframe = GetSelectedActor()?.LockOnKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);
        if (actorId is null || keyframe is null)
        {
            return SceneEditResult.Conflict;
        }

        var rollbackState = CaptureSemanticSelectionRollbackState();
        var activeTrackBeforeAttempt = ActiveTimelineTrack;
        var publicationSequenceBeforeAttempt = _lockOnSelectionPublicationSequence;
        _isSelectingSemanticKeyframe = true;
        try
        {
            ActiveTimelineTrack = TimelineTrackKind.LockOn;
            ClearPreview();
            for (var attempt = 0; attempt < MaxReconciliationAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    activeTrackBeforeAttempt = ActiveTimelineTrack;
                    publicationSequenceBeforeAttempt = _lockOnSelectionPublicationSequence;
                    ActiveTimelineTrack = TimelineTrackKind.LockOn;
                }

                Playback.Pause();
                keyframe = GetCurrentLockOnKeyframe(actorId, keyframeId);
                if (keyframe is null)
                {
                    return RestoreSemanticSelectionAfterConflict(rollbackState);
                }

                Playback.Seek(keyframe.TimeSeconds);
                keyframe = GetCurrentLockOnKeyframe(actorId, keyframeId);
                if (keyframe is null)
                {
                    return RestoreSemanticSelectionAfterConflict(rollbackState);
                }

                if (Playback.IsPlaying || !IsAtTime(Playback.CurrentTimeSeconds, keyframe.TimeSeconds))
                {
                    continue;
                }

                var targetPayloadPublishedThisAttempt = WasLockOnTargetPublishedSince(
                    publicationSequenceBeforeAttempt,
                    actorId,
                    keyframe);
                var forceTrackRefresh = ActiveTimelineTrack != TimelineTrackKind.LockOn ||
                    (!targetPayloadPublishedThisAttempt &&
                     activeTrackBeforeAttempt != TimelineTrackKind.LockOn &&
                     SelectedLockOnKeyframeId == keyframeId &&
                     SameLockOn(_selectedLockOnKeyframe, keyframe));
                ActiveTimelineTrack = TimelineTrackKind.LockOn;
                var selectionChange = SetSelectedLockOnKeyframe(keyframe, forceTrackRefresh);
                var availabilityChanges = RefreshAllEditAvailabilityState();
                RaiseLockOnKeyframeSelectionChanged(selectionChange);
                RaiseEditAvailabilityChanged(availabilityChanges.Transform);
                RaiseTimelineEditAvailabilityChanged(availabilityChanges.Timeline);

                var current = GetCurrentLockOnKeyframe(actorId, keyframeId);
                if (current is not null &&
                    !Playback.IsPlaying &&
                    IsAtTime(Playback.CurrentTimeSeconds, current.TimeSeconds) &&
                    ActiveTimelineTrack == TimelineTrackKind.LockOn &&
                    SelectedLockOnKeyframeId == keyframeId &&
                    SameLockOn(_selectedLockOnKeyframe, current))
                {
                    return SceneEditResult.Applied;
                }

            }

            return RestoreSemanticSelectionAfterConflict(rollbackState);
        }
        finally
        {
            _isSelectingSemanticKeyframe = false;
        }
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

    public SceneEditResult AddActionKeyframeAtCurrentTime(string actionKey) =>
        AddActionKeyframeAtCurrentTimeDetailed(actionKey).Result;

    public SemanticEditOutcome AddActionKeyframeAtCurrentTimeDetailed(string actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.InvalidActionKey);
        }

        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        if (actor.ActionKeyframes.Any(frame => IsAtTime(frame.TimeSeconds, Playback.CurrentTimeSeconds)))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.DuplicateTime);
        }

        var keyframe = new ActionKeyframe(
            GetNextActionKeyframeId(actor),
            Playback.CurrentTimeSeconds,
            actionKey);
        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
            TimelineTrackKind.Action,
            new AddActionKeyframeCommand(SelectedActorId, keyframe));
    }

    public SceneEditResult UpdateSelectedActionKeyframe(double timeSeconds, string actionKey) =>
        UpdateSelectedActionKeyframeDetailed(timeSeconds, actionKey).Result;

    public SemanticEditOutcome UpdateSelectedActionKeyframeDetailed(double timeSeconds, string actionKey)
    {
        if (!IsTimeWithinDocument(timeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.TimeOutOfRange);
        }

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.InvalidActionKey);
        }

        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        var before = _selectedActionKeyframe;
        if (before is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.KeyframeSelectionRequired);
        }

        var current = actor.ActionKeyframes.SingleOrDefault(frame => frame.Id == before.Id);
        if (!SameAction(current, before))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.StalePreimage);
        }

        if (!IsAtTime(Playback.CurrentTimeSeconds, before.TimeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.SelectionTimeMismatch);
        }

        var after = new ActionKeyframe(before.Id, timeSeconds, actionKey);
        if (SameAction(before, after))
        {
            return SemanticEditOutcome.NoChange;
        }

        if (actor.ActionKeyframes.Any(frame =>
                frame.Id != before.Id && IsAtTime(frame.TimeSeconds, timeSeconds)))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.DuplicateTime);
        }

        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
            TimelineTrackKind.Action,
            new UpdateActionKeyframeCommand(SelectedActorId, before, after));
    }

    public SceneEditResult RemoveSelectedActionKeyframe() =>
        RemoveSelectedActionKeyframeDetailed().Result;

    public SemanticEditOutcome RemoveSelectedActionKeyframeDetailed()
    {
        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        var before = _selectedActionKeyframe;
        if (before is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.KeyframeSelectionRequired);
        }

        var current = actor.ActionKeyframes.SingleOrDefault(frame => frame.Id == before.Id);
        if (!SameAction(current, before))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.StalePreimage);
        }

        if (!IsAtTime(Playback.CurrentTimeSeconds, before.TimeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.SelectionTimeMismatch);
        }

        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
            TimelineTrackKind.Action,
            new RemoveActionKeyframeCommand(SelectedActorId, before));
    }

    public SceneEditResult AddLockOnKeyframeAtCurrentTime(
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode) =>
        AddLockOnKeyframeAtCurrentTimeDetailed(
            enabled,
            targetActorId,
            yawOffsetDegrees,
            trackingMode).Result;

    public SemanticEditOutcome AddLockOnKeyframeAtCurrentTimeDetailed(
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        var inputIssue = ValidateLockOnInput(actor.ActorId, enabled, targetActorId, yawOffsetDegrees, trackingMode);
        if (inputIssue is not null)
        {
            return SemanticEditOutcome.Conflict(inputIssue.Value);
        }

        if (actor.LockOnKeyframes.Any(frame => IsAtTime(frame.TimeSeconds, Playback.CurrentTimeSeconds)))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.DuplicateTime);
        }

        var keyframe = new LockOnKeyframe(
            GetNextLockOnKeyframeId(actor),
            Playback.CurrentTimeSeconds,
            enabled,
            targetActorId,
            yawOffsetDegrees,
            trackingMode);
        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
            TimelineTrackKind.LockOn,
            new AddLockOnKeyframeCommand(SelectedActorId, keyframe));
    }

    public SceneEditResult UpdateSelectedLockOnKeyframe(
        double timeSeconds,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode) =>
        UpdateSelectedLockOnKeyframeDetailed(
            timeSeconds,
            enabled,
            targetActorId,
            yawOffsetDegrees,
            trackingMode).Result;

    public SemanticEditOutcome UpdateSelectedLockOnKeyframeDetailed(
        double timeSeconds,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        if (!IsTimeWithinDocument(timeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.TimeOutOfRange);
        }

        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        var before = _selectedLockOnKeyframe;
        if (before is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.KeyframeSelectionRequired);
        }

        var current = actor.LockOnKeyframes.SingleOrDefault(frame => frame.Id == before.Id);
        if (!SameLockOn(current, before))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.StalePreimage);
        }

        if (!IsAtTime(Playback.CurrentTimeSeconds, before.TimeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.SelectionTimeMismatch);
        }

        var inputIssue = ValidateLockOnInput(actor.ActorId, enabled, targetActorId, yawOffsetDegrees, trackingMode);
        if (inputIssue is not null)
        {
            return SemanticEditOutcome.Conflict(inputIssue.Value);
        }

        var after = new LockOnKeyframe(
            before.Id,
            timeSeconds,
            enabled,
            targetActorId,
            yawOffsetDegrees,
            trackingMode);
        if (SameLockOn(before, after))
        {
            return SemanticEditOutcome.NoChange;
        }

        if (actor.LockOnKeyframes.Any(frame =>
                frame.Id != before.Id && IsAtTime(frame.TimeSeconds, timeSeconds)))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.DuplicateTime);
        }

        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
            TimelineTrackKind.LockOn,
            new UpdateLockOnKeyframeCommand(SelectedActorId, before, after));
    }

    public SceneEditResult RemoveSelectedLockOnKeyframe() =>
        RemoveSelectedLockOnKeyframeDetailed().Result;

    public SemanticEditOutcome RemoveSelectedLockOnKeyframeDetailed()
    {
        var actor = GetSelectedActor();
        if (actor is null || SelectedActorId is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.ActorSelectionRequired);
        }

        if (Playback.IsPlaying)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.PlaybackActive);
        }

        var before = _selectedLockOnKeyframe;
        if (before is null)
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.KeyframeSelectionRequired);
        }

        var current = actor.LockOnKeyframes.SingleOrDefault(frame => frame.Id == before.Id);
        if (!SameLockOn(current, before))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.StalePreimage);
        }

        if (!IsAtTime(Playback.CurrentTimeSeconds, before.TimeSeconds))
        {
            return SemanticEditOutcome.Conflict(SemanticEditIssue.SelectionTimeMismatch);
        }

        ClearPreview();
        return ExecuteSemanticCommandOnTrack(
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

    private SemanticEditOutcome ExecuteSemanticCommandOnTrack(
        TimelineTrackKind track,
        ISceneEditCommand command) => ExecuteCommandOnTrack(track, command) switch
        {
            SceneEditResult.Applied => SemanticEditOutcome.Applied,
            SceneEditResult.NoChange => SemanticEditOutcome.NoChange,
            SceneEditResult.Conflict => SemanticEditOutcome.Conflict(SemanticEditIssue.Conflict),
            var result => throw new InvalidOperationException($"Unknown scene edit result: {result}"),
        };

    private bool IsTimeWithinDocument(double timeSeconds) =>
        double.IsFinite(timeSeconds) && timeSeconds >= 0 && timeSeconds <= _document.DurationSeconds;

    private SemanticEditIssue? ValidateLockOnInput(
        string actorId,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        if (targetActorId is not null &&
            (string.IsNullOrWhiteSpace(targetActorId) ||
             targetActorId == actorId ||
             !_document.Actors.Any(actor => actor.ActorId == targetActorId)))
        {
            return SemanticEditIssue.InvalidLockOnTarget;
        }

        if (enabled && targetActorId is null)
        {
            return SemanticEditIssue.InvalidLockOnTarget;
        }

        if (!double.IsFinite(yawOffsetDegrees))
        {
            return SemanticEditIssue.InvalidYawOffset;
        }

        if (!Enum.IsDefined(trackingMode))
        {
            return SemanticEditIssue.InvalidTrackingMode;
        }

        return null;
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
        var playbackRequestVersion = Playback.StateRequestVersion;
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
            if (_isRestoringSemanticSelection)
            {
                throw;
            }

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

        if (_isRestoringSemanticSelection)
        {
            return;
        }

        if (playbackRequestVersion != Playback.StateRequestVersion ||
            args.CurrentTimeSeconds != Playback.CurrentTimeSeconds ||
            args.IsPlaying != Playback.IsPlaying)
        {
            return;
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
            _lastActionSelectionPublication = new ActionSelectionPublication(
                ++_actionSelectionPublicationSequence,
                SelectedActorId,
                ActiveTimelineTrack,
                SelectedActionKeyframeId,
                GetSelectedActionKeyframe());
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
            _lastLockOnSelectionPublication = new LockOnSelectionPublication(
                ++_lockOnSelectionPublicationSequence,
                SelectedActorId,
                ActiveTimelineTrack,
                SelectedLockOnKeyframeId,
                GetSelectedLockOnKeyframe());
            LockOnKeyframeSelectionChanged?.Invoke(this, selectionChange);
        }
    }

    private bool WasActionTargetPublishedSince(
        long sequence,
        string actorId,
        ActionKeyframe keyframe) =>
        _lastActionSelectionPublication is { } publication &&
        publication.Sequence > sequence &&
        publication.ActorId == actorId &&
        publication.ActiveTrack == TimelineTrackKind.Action &&
        publication.KeyframeId == keyframe.Id &&
        SameAction(publication.Keyframe, keyframe);

    private bool WasLockOnTargetPublishedSince(
        long sequence,
        string actorId,
        LockOnKeyframe keyframe) =>
        _lastLockOnSelectionPublication is { } publication &&
        publication.Sequence > sequence &&
        publication.ActorId == actorId &&
        publication.ActiveTrack == TimelineTrackKind.LockOn &&
        publication.KeyframeId == keyframe.Id &&
        SameLockOn(publication.Keyframe, keyframe);

    private void RaiseAllSelectionAndAvailabilityChanged(
        TransformKeyframeSelectionChangedEventArgs? transformSelectionChange,
        ActionKeyframeSelectionChangedEventArgs? actionSelectionChange,
        LockOnKeyframeSelectionChangedEventArgs? lockOnSelectionChange,
        EditAvailabilityChangedEventArgs? transformAvailabilityChange,
        TimelineEditAvailabilityChangedEventArgs? timelineAvailabilityChange)
    {
        PublishSessionNotification(new SessionNotificationBatch(
            transformSelectionChange,
            actionSelectionChange,
            lockOnSelectionChange,
            transformAvailabilityChange,
            timelineAvailabilityChange,
            null));
    }

    private void PublishSessionNotification(SessionNotificationBatch notification)
    {
        if (!_isRestoringSemanticSelection)
        {
            PublishSessionNotificationNow(notification);
            return;
        }

        EnqueueSemanticRollbackWork(new DeferredSessionNotification(notification));
        DrainSemanticRollbackWork();
    }

    private void EnqueueSemanticRollbackWork(SemanticRollbackWorkItem workItem)
    {
        if (_semanticRollbackWorkItems.Count >= MaxReconciliationAttempts)
        {
            throw new InvalidOperationException(
                "Semantic selection rollback notifications did not stabilize.");
        }

        _semanticRollbackWorkItems.Enqueue(workItem);
    }

    private void DrainSemanticRollbackWork()
    {
        if (_isDispatchingSemanticRollbackWork)
        {
            return;
        }

        _isDispatchingSemanticRollbackWork = true;
        try
        {
            ExceptionDispatchInfo? observerFailure = null;
            for (var completedWork = 0; completedWork < MaxReconciliationAttempts; completedWork++)
            {
                if (_semanticRollbackWorkItems.Count == 0)
                {
                    observerFailure?.Throw();
                    return;
                }

                try
                {
                    switch (_semanticRollbackWorkItems.Dequeue())
                    {
                        case DeferredSessionNotification deferredNotification:
                            PublishSessionNotificationNow(deferredNotification.Notification);
                            break;
                        case DeferredActorSelection deferredActorSelection:
                            var notification = ApplyActorSelection(deferredActorSelection.ActorId);
                            if (notification is not null)
                            {
                                PublishSessionNotificationNow(notification);
                            }

                            break;
                    }
                }
                catch (Exception exception)
                {
                    observerFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (_semanticRollbackWorkItems.Count == 0)
            {
                observerFailure?.Throw();
                return;
            }

            throw new InvalidOperationException(
                "Semantic selection rollback notifications did not stabilize.");
        }
        finally
        {
            _semanticRollbackWorkItems.Clear();
            _isDispatchingSemanticRollbackWork = false;
        }
    }

    private void PublishSessionNotificationNow(SessionNotificationBatch notification)
    {
        RaiseTransformKeyframeSelectionChanged(notification.TransformSelectionChange);
        RaiseActionKeyframeSelectionChanged(notification.ActionSelectionChange);
        RaiseLockOnKeyframeSelectionChanged(notification.LockOnSelectionChange);
        RaiseEditAvailabilityChanged(notification.TransformAvailabilityChange);
        RaiseTimelineEditAvailabilityChanged(notification.TimelineAvailabilityChange);
        if (notification.ActorSelectionChange is not null)
        {
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedActorId));
        }
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

    private SemanticSelectionRollbackState CaptureSemanticSelectionRollbackState() => new(
        SelectedActorId,
        Playback.CurrentTimeSeconds,
        Playback.IsPlaying,
        ActiveTimelineTrack,
        SelectedTransformKeyframeId,
        SelectedActionKeyframeId,
        SelectedLockOnKeyframeId);

    private SceneEditResult RestoreSemanticSelectionAfterConflict(SemanticSelectionRollbackState rollbackState)
    {
        _isRestoringSemanticSelection = true;
        try
        {
            for (var attempt = 0; attempt < MaxReconciliationAttempts; attempt++)
            {
                RestorePlaybackState(
                    rollbackState.PlaybackTimeSeconds,
                    rollbackState.PlaybackWasPlaying);
                if (Math.Abs(Playback.CurrentTimeSeconds - rollbackState.PlaybackTimeSeconds) >
                    EditTimeToleranceSeconds ||
                    Playback.IsPlaying != rollbackState.PlaybackWasPlaying)
                {
                    continue;
                }

                var actor = GetSelectedActor();
                var restoresOriginalActor = actor is not null && actor.ActorId == rollbackState.ActorId;
                var desiredActiveTrack = restoresOriginalActor
                    ? rollbackState.ActiveTrack
                    : actor is null
                        ? TimelineTrackKind.Transform
                        : ActiveTimelineTrack;
                ActiveTimelineTrack = desiredActiveTrack;
                var transform = actor is null
                    ? null
                    : restoresOriginalActor
                        ? FindTransformByRollbackId(actor, rollbackState.TransformKeyframeId)
                        : SelectTransformAtTime(actor, rollbackState.PlaybackTimeSeconds);
                var action = actor is null
                    ? null
                    : restoresOriginalActor
                        ? FindActionByRollbackId(actor, rollbackState.ActionKeyframeId)
                        : SelectActionAtTime(actor, rollbackState.PlaybackTimeSeconds);
                var lockOn = actor is null
                    ? null
                    : restoresOriginalActor
                        ? FindLockOnByRollbackId(actor, rollbackState.LockOnKeyframeId)
                        : SelectLockOnAtTime(actor, rollbackState.PlaybackTimeSeconds);
                var transformSelectionChange = SetSelectedTransformKeyframe(transform);
                var actionSelectionChange = SetSelectedActionKeyframe(action);
                var lockOnSelectionChange = SetSelectedLockOnKeyframe(lockOn);
                var availabilityChanges = RefreshAllEditAvailabilityState();
                var transformAvailabilityChange = availabilityChanges.Transform;
                var timelineAvailabilityChange = availabilityChanges.Timeline;

                if (actor is not null)
                {
                    transformSelectionChange ??= SetSelectedTransformKeyframe(
                        transform,
                        forceNotification: true);
                    actionSelectionChange ??= SetSelectedActionKeyframe(
                        action,
                        forceNotification: true);
                    lockOnSelectionChange ??= SetSelectedLockOnKeyframe(
                        lockOn,
                        forceNotification: true);
                    transformAvailabilityChange ??= new EditAvailabilityChangedEventArgs(
                        CanEditSelectedTransform,
                        EditLockReason);
                    timelineAvailabilityChange ??= new TimelineEditAvailabilityChangedEventArgs(
                        ActionEditAvailability,
                        LockOnEditAvailability);
                }

                var revisionBeforePublish = _document.Revision;
                var actorIdBeforePublish = SelectedActorId;
                RaiseAllSelectionAndAvailabilityChanged(
                    transformSelectionChange,
                    actionSelectionChange,
                    lockOnSelectionChange,
                    transformAvailabilityChange,
                    timelineAvailabilityChange);
                if (_document.Revision == revisionBeforePublish &&
                    SelectedActorId == actorIdBeforePublish &&
                    ActiveTimelineTrack == desiredActiveTrack &&
                    Math.Abs(Playback.CurrentTimeSeconds - rollbackState.PlaybackTimeSeconds) <=
                    EditTimeToleranceSeconds &&
                    Playback.IsPlaying == rollbackState.PlaybackWasPlaying &&
                    SelectedTransformKeyframeId == transform?.Id &&
                    SelectedActionKeyframeId == action?.Id &&
                    SelectedLockOnKeyframeId == lockOn?.Id &&
                    SameOrBothNull(_selectedTransformKeyframe, transform) &&
                    SameOrBothNull(_selectedActionKeyframe, action) &&
                    SameOrBothNull(_selectedLockOnKeyframe, lockOn))
                {
                    return SceneEditResult.Conflict;
                }
            }
        }
        finally
        {
            _isRestoringSemanticSelection = false;
        }

        throw new InvalidOperationException("Semantic selection rollback did not stabilize.");
    }

    private void RestorePlaybackState(double timeSeconds, bool wasPlaying)
    {
        if (Playback.IsPlaying && !wasPlaying)
        {
            Playback.Pause();
        }

        Playback.Seek(timeSeconds);
        if (Playback.IsPlaying != wasPlaying)
        {
            if (wasPlaying)
            {
                Playback.Play();
            }
            else
            {
                Playback.Pause();
            }
        }
    }

    private static TransformKeyframe? FindTransformByRollbackId(
        ActorTrack actor,
        string? keyframeId) => keyframeId is null
            ? null
            : actor.TransformKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);

    private static ActionKeyframe? FindActionByRollbackId(
        ActorTrack actor,
        string? keyframeId) => keyframeId is null
            ? null
            : actor.ActionKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);

    private static LockOnKeyframe? FindLockOnByRollbackId(
        ActorTrack actor,
        string? keyframeId) => keyframeId is null
            ? null
            : actor.LockOnKeyframes.SingleOrDefault(frame => frame.Id == keyframeId);

    private static TransformKeyframe? SelectTransformAtTime(ActorTrack actor, double timeSeconds) =>
        actor.TransformKeyframes.SingleOrDefault(frame => IsAtTime(frame.TimeSeconds, timeSeconds));

    private static ActionKeyframe? SelectActionAtTime(ActorTrack actor, double timeSeconds) =>
        actor.ActionKeyframes.SingleOrDefault(frame => IsAtTime(frame.TimeSeconds, timeSeconds));

    private static LockOnKeyframe? SelectLockOnAtTime(ActorTrack actor, double timeSeconds) =>
        actor.LockOnKeyframes.SingleOrDefault(frame => IsAtTime(frame.TimeSeconds, timeSeconds));

    private static bool IsAtTime(double left, double right) =>
        Math.Abs(left - right) <= EditTimeToleranceSeconds;

    private static bool SameOrBothNull(TransformKeyframe? left, TransformKeyframe? right) =>
        (left is null && right is null) || SameTransform(left, right);

    private static bool SameOrBothNull(ActionKeyframe? left, ActionKeyframe? right) =>
        (left is null && right is null) || SameAction(left, right);

    private static bool SameOrBothNull(LockOnKeyframe? left, LockOnKeyframe? right) =>
        (left is null && right is null) || SameLockOn(left, right);

    private sealed record SemanticSelectionRollbackState(
        string? ActorId,
        double PlaybackTimeSeconds,
        bool PlaybackWasPlaying,
        TimelineTrackKind ActiveTrack,
        string? TransformKeyframeId,
        string? ActionKeyframeId,
        string? LockOnKeyframeId);

    private sealed record SessionNotificationBatch(
        TransformKeyframeSelectionChangedEventArgs? TransformSelectionChange,
        ActionKeyframeSelectionChangedEventArgs? ActionSelectionChange,
        LockOnKeyframeSelectionChangedEventArgs? LockOnSelectionChange,
        EditAvailabilityChangedEventArgs? TransformAvailabilityChange,
        TimelineEditAvailabilityChangedEventArgs? TimelineAvailabilityChange,
        SelectionChangedEventArgs? ActorSelectionChange);

    private sealed record ActionSelectionPublication(
        long Sequence,
        string? ActorId,
        TimelineTrackKind ActiveTrack,
        string? KeyframeId,
        ActionKeyframe? Keyframe);

    private sealed record LockOnSelectionPublication(
        long Sequence,
        string? ActorId,
        TimelineTrackKind ActiveTrack,
        string? KeyframeId,
        LockOnKeyframe? Keyframe);

    private abstract record SemanticRollbackWorkItem;

    private sealed record DeferredSessionNotification(SessionNotificationBatch Notification)
        : SemanticRollbackWorkItem;

    private sealed record DeferredActorSelection(string? ActorId) : SemanticRollbackWorkItem;

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
