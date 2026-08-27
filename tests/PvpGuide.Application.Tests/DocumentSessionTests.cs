using PvpGuide.Application.Commands;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Actors;
using PvpGuide.Domain.Timeline;
using Xunit;

namespace PvpGuide.Application.Tests;

public sealed class DocumentSessionTests
{
    [Fact]
    public void Action_and_lock_commands_share_one_monotonic_history()
    {
        var session = CreateSemanticSession(out var document);
        session.SelectActor("host");

        Assert.Equal(SceneEditResult.Applied,
            session.AddActionKeyframeAtCurrentTime("attack"));
        Assert.Equal(SceneEditResult.Applied,
            session.AddLockOnKeyframeAtCurrentTime(
                true, "invader", 0, LockOnTrackingMode.Continuous));

        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.True(session.Redo());
        Assert.True(session.Redo());
        Assert.Equal(6, document.Revision);
    }

    [Fact]
    public void Selecting_lock_marker_pauses_seeks_and_activates_lock_track()
    {
        var session = CreateSemanticSession(out _);
        session.SelectActor("host");
        session.Playback.Play();

        Assert.Equal(SceneEditResult.Applied, session.SelectLockOnKeyframe("host-lock-1"));

        Assert.False(session.Playback.IsPlaying);
        Assert.Equal(1, session.Playback.CurrentTimeSeconds);
        Assert.Equal(TimelineTrackKind.LockOn, session.ActiveTimelineTrack);
        Assert.Equal("host-lock-1", session.SelectedLockOnKeyframeId);
    }

    [Fact]
    public void Selecting_action_and_transform_markers_activates_their_tracks()
    {
        var session = CreateSemanticSession(out _);
        session.SelectActor("host");
        session.Playback.Play();

        Assert.Equal(SceneEditResult.Applied, session.SelectActionKeyframe("host-action-1"));
        Assert.False(session.Playback.IsPlaying);
        Assert.Equal(1, session.Playback.CurrentTimeSeconds);
        Assert.Equal(TimelineTrackKind.Action, session.ActiveTimelineTrack);

        Assert.Equal(SceneEditResult.Applied, session.SelectTransformKeyframe("host-first"));
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.Equal(TimelineTrackKind.Transform, session.ActiveTimelineTrack);
    }

    [Fact]
    public void Semantic_CRUD_uses_deterministic_ids_and_reports_full_selection_payloads()
    {
        var session = CreateSemanticSession(out var document);
        session.SelectActor("host");
        ActionKeyframeSelectionChangedEventArgs? actionSelection = null;
        LockOnKeyframeSelectionChangedEventArgs? lockSelection = null;
        session.ActionKeyframeSelectionChanged += (_, args) => actionSelection = args;
        session.LockOnKeyframeSelectionChanged += (_, args) => lockSelection = args;

        Assert.Equal(SceneEditResult.Applied, session.AddActionKeyframeAtCurrentTime("attack"));
        Assert.Equal("host-action-0001", session.SelectedActionKeyframeId);
        Assert.Equal(TimelineTrackKind.Action, session.ActiveTimelineTrack);
        AssertActionFrame(actionSelection!.Keyframe, "host-action-0001", 0, "attack");
        Assert.Equal("host", actionSelection.ActorId);
        Assert.Equal("host-action-0001", actionSelection.KeyframeId);

        Assert.Equal(SceneEditResult.Applied, session.UpdateSelectedActionKeyframe(2, "roll"));
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        AssertActionFrame(session.GetSelectedActionKeyframe(), "host-action-0001", 2, "roll");
        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedActionKeyframe());
        Assert.DoesNotContain(session.GetSelectedActorActionKeyframes(), frame => frame.Id == "host-action-0001");

        Assert.True(session.Playback.Seek(0));
        Assert.Equal(SceneEditResult.Applied, session.AddLockOnKeyframeAtCurrentTime(
            true, "invader", 25, LockOnTrackingMode.KeyframeOnly));
        Assert.Equal("host-lock-on-0001", session.SelectedLockOnKeyframeId);
        Assert.Equal(TimelineTrackKind.LockOn, session.ActiveTimelineTrack);
        AssertLockFrame(
            lockSelection!.Keyframe,
            "host-lock-on-0001",
            0,
            true,
            "invader",
            25,
            LockOnTrackingMode.KeyframeOnly);

        Assert.Equal(SceneEditResult.Applied, session.UpdateSelectedLockOnKeyframe(
            2, false, null, -45, LockOnTrackingMode.Snap));
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        AssertLockFrame(
            session.GetSelectedLockOnKeyframe(),
            "host-lock-on-0001",
            2,
            false,
            null,
            -45,
            LockOnTrackingMode.Snap);
        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedLockOnKeyframe());
        Assert.DoesNotContain(session.GetSelectedActorLockOnKeyframes(), frame => frame.Id == "host-lock-on-0001");
        Assert.Equal(6, document.Revision);
    }

    [Fact]
    public void Semantic_tracks_allow_the_last_marker_to_be_deleted_and_restored()
    {
        var actionSession = CreateSemanticSession(out _);
        actionSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, actionSession.SelectActionKeyframe("host-action-1"));
        Assert.True(actionSession.ActionEditAvailability.CanDelete);
        Assert.Equal(SceneEditResult.Applied, actionSession.RemoveSelectedActionKeyframe());
        Assert.Empty(actionSession.GetSelectedActorActionKeyframes());
        Assert.Null(actionSession.SelectedActionKeyframeId);
        Assert.True(actionSession.Undo());
        AssertActionFrame(actionSession.GetSelectedActionKeyframe(), "host-action-1", 1, "idle");

        var lockSession = CreateSemanticSession(out _);
        lockSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, lockSession.SelectLockOnKeyframe("host-lock-1"));
        Assert.True(lockSession.LockOnEditAvailability.CanDelete);
        Assert.Equal(SceneEditResult.Applied, lockSession.RemoveSelectedLockOnKeyframe());
        Assert.Empty(lockSession.GetSelectedActorLockOnKeyframes());
        Assert.Null(lockSession.SelectedLockOnKeyframeId);
        Assert.True(lockSession.Undo());
        AssertLockFrame(
            lockSession.GetSelectedLockOnKeyframe(),
            "host-lock-1",
            1,
            true,
            "invader",
            15,
            LockOnTrackingMode.Snap);
    }

    [Fact]
    public void Semantic_stale_updates_and_deletes_preserve_history_and_expose_latest_frames()
    {
        var actionSession = CreateSemanticSession(out var actionDocument);
        actionSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, actionSession.SelectActionKeyframe("host-action-1"));
        var staleAction = Assert.IsType<ActionKeyframe>(actionSession.GetSelectedActionKeyframe());
        var latestAction = new ActionKeyframe(staleAction.Id, 2, "external");
        Assert.True(actionDocument.UpdateActionKeyframe("host", staleAction, latestAction));

        Assert.Equal(SceneEditResult.Conflict, actionSession.UpdateSelectedActionKeyframe(3, "roll"));
        Assert.Equal(SceneEditResult.Conflict, actionSession.RemoveSelectedActionKeyframe());
        AssertActionFrame(actionSession.GetSelectedActionKeyframe(), "host-action-1", 2, "external");
        Assert.False(actionSession.CanUndo);

        var lockSession = CreateSemanticSession(out var lockDocument);
        lockSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, lockSession.SelectLockOnKeyframe("host-lock-1"));
        var staleLock = Assert.IsType<LockOnKeyframe>(lockSession.GetSelectedLockOnKeyframe());
        var latestLock = new LockOnKeyframe(
            staleLock.Id, 2, false, null, 60, LockOnTrackingMode.KeyframeOnly);
        Assert.True(lockDocument.UpdateLockOnKeyframe("host", staleLock, latestLock));

        Assert.Equal(SceneEditResult.Conflict, lockSession.UpdateSelectedLockOnKeyframe(
            3, true, "invader", 90, LockOnTrackingMode.Continuous));
        Assert.Equal(SceneEditResult.Conflict, lockSession.RemoveSelectedLockOnKeyframe());
        AssertLockFrame(
            lockSession.GetSelectedLockOnKeyframe(),
            "host-lock-1",
            2,
            false,
            null,
            60,
            LockOnTrackingMode.KeyframeOnly);
        Assert.False(lockSession.CanUndo);
    }

    [Fact]
    public void Semantic_availability_and_shared_history_require_a_paused_selected_actor()
    {
        var session = CreateSemanticSession(out var document);
        session.SelectActor("host");
        Assert.True(session.ActionEditAvailability.CanAdd);
        Assert.False(session.ActionEditAvailability.CanUpdate);
        Assert.True(session.LockOnEditAvailability.CanAdd);
        Assert.True(session.CanEditHistory);
        Assert.Equal(SceneEditResult.Applied, session.AddActionKeyframeAtCurrentTime("attack"));

        Assert.True(session.Playback.Play());
        Assert.False(session.ActionEditAvailability.CanAdd);
        Assert.False(session.ActionEditAvailability.CanUpdate);
        Assert.False(session.ActionEditAvailability.CanDelete);
        Assert.False(session.LockOnEditAvailability.CanAdd);
        Assert.False(session.CanEditHistory);
        var revision = document.Revision;
        Assert.False(session.Undo());
        Assert.Equal(SceneEditResult.Conflict, session.AddLockOnKeyframeAtCurrentTime(
            true, "invader", 0, LockOnTrackingMode.Continuous));
        Assert.Equal(revision, document.Revision);

        Assert.True(session.Playback.Pause());
        session.SelectActor(null);
        Assert.False(session.CanEditHistory);
        Assert.False(session.Undo());
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void Semantic_changed_observer_exceptions_commit_history_and_reconcile_latest_selections()
    {
        var actionSession = CreateSemanticSession(out var actionDocument);
        actionSession.SelectActor("host");
        var actionObserver = new EventHandler<SceneDocumentChangedEventArgs>((_, _) => throw new ChangedObserverException());
        actionDocument.Changed += actionObserver;

        Assert.Throws<ChangedObserverException>(() => actionSession.AddActionKeyframeAtCurrentTime("attack"));

        actionDocument.Changed -= actionObserver;
        Assert.True(actionSession.CanUndo);
        Assert.Equal(
            actionDocument.GetActionKeyframe("host", actionSession.SelectedActionKeyframeId!),
            actionSession.GetSelectedActionKeyframe());

        var updateSession = CreateSemanticSession(out var updateDocument);
        updateSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, updateSession.SelectLockOnKeyframe("host-lock-1"));
        var updateObserver = new EventHandler<SceneDocumentChangedEventArgs>((_, _) => throw new ChangedObserverException());
        updateDocument.Changed += updateObserver;
        Assert.Throws<ChangedObserverException>(() => updateSession.UpdateSelectedLockOnKeyframe(
            2, false, null, 75, LockOnTrackingMode.KeyframeOnly));
        updateDocument.Changed -= updateObserver;
        Assert.True(updateSession.CanUndo);
        Assert.Equal(
            updateDocument.GetLockOnKeyframe("host", updateSession.SelectedLockOnKeyframeId!),
            updateSession.GetSelectedLockOnKeyframe());

        var deleteSession = CreateSemanticSession(out var deleteDocument);
        deleteSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, deleteSession.SelectLockOnKeyframe("host-lock-1"));
        var deleteObserver = new EventHandler<SceneDocumentChangedEventArgs>((_, _) => throw new ChangedObserverException());
        deleteDocument.Changed += deleteObserver;
        Assert.Throws<ChangedObserverException>(() => deleteSession.RemoveSelectedLockOnKeyframe());
        deleteDocument.Changed -= deleteObserver;
        Assert.True(deleteSession.CanUndo);
        Assert.Empty(deleteSession.GetSelectedActorLockOnKeyframes());
        Assert.Null(deleteSession.GetSelectedLockOnKeyframe());
    }

    [Fact]
    public void Semantic_history_changed_can_reentrantly_undo_and_redo_without_stale_state()
    {
        var actionSession = CreateSemanticSession(out var actionDocument);
        actionSession.SelectActor("host");
        var undoRan = false;
        actionSession.HistoryChanged += (_, _) =>
        {
            if (!undoRan)
            {
                undoRan = true;
                Assert.True(actionSession.Undo());
            }
        };

        Assert.Equal(SceneEditResult.Applied, actionSession.AddActionKeyframeAtCurrentTime("attack"));

        Assert.DoesNotContain(actionSession.GetSelectedActorActionKeyframes(),
            frame => frame.Id == "host-action-0001");
        Assert.False(actionSession.CanUndo);
        Assert.True(actionSession.CanRedo);
        Assert.Equal(2, actionDocument.Revision);

        var lockSession = CreateSemanticSession(out var lockDocument);
        lockSession.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, lockSession.AddLockOnKeyframeAtCurrentTime(
            true, "invader", 0, LockOnTrackingMode.Continuous));
        var redoRan = false;
        lockSession.HistoryChanged += (_, _) =>
        {
            if (!redoRan)
            {
                redoRan = true;
                Assert.True(lockSession.Redo());
            }
        };

        Assert.True(lockSession.Undo());

        Assert.Contains(lockSession.GetSelectedActorLockOnKeyframes(),
            frame => frame.Id == "host-lock-on-0001");
        Assert.True(lockSession.CanUndo);
        Assert.False(lockSession.CanRedo);
        Assert.Equal(3, lockDocument.Revision);
    }

    [Fact]
    public void Lock_selection_playback_reentrancy_publishes_only_latest_full_frame_and_availability()
    {
        var session = CreateSemanticSession(out var document);
        session.SelectActor("host");
        var observedSelection = new List<LockOnKeyframeSelectionChangedEventArgs>();
        var observedAvailability = new List<TimelineEditAvailabilityChangedEventArgs>();
        session.LockOnKeyframeSelectionChanged += (_, args) =>
        {
            observedSelection.Add(args);
            Assert.Equal(session.SelectedActorId, args.ActorId);
            Assert.Equal(session.SelectedLockOnKeyframeId, args.KeyframeId);
            Assert.Equal(document.GetLockOnKeyframe(args.ActorId!, args.KeyframeId!), args.Keyframe);
        };
        session.TimelineEditAvailabilityChanged += (_, args) =>
        {
            observedAvailability.Add(args);
            Assert.Equal(session.ActionEditAvailability, args.ActionEditAvailability);
            Assert.Equal(session.LockOnEditAvailability, args.LockOnEditAvailability);
        };
        var callbackRan = false;
        session.Playback.Changed += (_, _) =>
        {
            if (callbackRan || session.Playback.IsPlaying)
            {
                return;
            }

            callbackRan = true;
            var before = document.GetLockOnKeyframe("host", "host-lock-1");
            Assert.True(document.UpdateLockOnKeyframe(
                "host",
                before,
                new LockOnKeyframe(
                    before.Id,
                    2,
                    false,
                    null,
                    70,
                    LockOnTrackingMode.KeyframeOnly)));
            Assert.True(session.Playback.Seek(2));
        };
        Assert.True(session.Playback.Play());

        Assert.Equal(SceneEditResult.Applied, session.SelectLockOnKeyframe("host-lock-1"));

        Assert.True(callbackRan);
        Assert.False(session.Playback.IsPlaying);
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        AssertLockFrame(
            session.GetSelectedLockOnKeyframe(),
            "host-lock-1",
            2,
            false,
            null,
            70,
            LockOnTrackingMode.KeyframeOnly);
        Assert.NotEmpty(observedSelection);
        Assert.NotEmpty(observedAvailability);
    }

    [Fact]
    public void Action_fallback_reconciliation_reseeks_when_playback_observer_moves_the_fallback_frame()
    {
        var session = CreateSemanticSession(out var document);
        session.SelectActor("host");
        Assert.Equal(SceneEditResult.Applied, session.AddActionKeyframeAtCurrentTime("attack"));
        var observedSelections = new List<ActionKeyframeSelectionChangedEventArgs>();
        session.ActionKeyframeSelectionChanged += (_, args) => observedSelections.Add(args);
        var observerRan = false;
        ActionKeyframe? latestFallback = null;
        session.Playback.Changed += (_, _) =>
        {
            if (observerRan || session.Playback.CurrentTimeSeconds != 1)
            {
                return;
            }

            observerRan = true;
            var fallback = document.GetActionKeyframe("host", "host-action-1");
            latestFallback = new ActionKeyframe(fallback.Id, 2, "observer-updated-idle");
            Assert.True(document.UpdateActionKeyframe("host", fallback, latestFallback));
        };

        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedActionKeyframe());

        Assert.True(observerRan);
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        Assert.Equal("host-action-1", session.SelectedActionKeyframeId);
        Assert.Same(latestFallback, session.GetSelectedActionKeyframe());
        Assert.Same(latestFallback, observedSelections[^1].Keyframe);
        Assert.False(session.ActionEditAvailability.CanAdd);
        Assert.True(session.ActionEditAvailability.CanUpdate);
        Assert.True(session.ActionEditAvailability.CanDelete);
        Assert.Equal(3, document.Revision);
    }

    [Fact]
    public void Selected_actor_is_editable_only_while_paused_at_its_selected_keyframe_time()
    {
        var session = CreateQuarterSecondSession(out _);
        var availability = new List<(bool CanEdit, string? Reason)>();
        session.EditAvailabilityChanged += (_, args) => availability.Add((args.CanEditSelectedTransform, args.EditLockReason));

        session.SelectActor("host");
        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("선택한 키프레임 시각에서만 편집할 수 있습니다", session.EditLockReason);

        Assert.True(session.Playback.Seek(0.25));
        Assert.True(session.CanEditSelectedTransform);
        Assert.Null(session.EditLockReason);

        Assert.True(session.Playback.Play());
        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("재생 중에는 편집할 수 없습니다", session.EditLockReason);

        Assert.True(session.Playback.Pause());
        Assert.True(session.CanEditSelectedTransform);
        Assert.Null(session.EditLockReason);
        Assert.Equal(
            [
                (false, "선택한 키프레임 시각에서만 편집할 수 있습니다"),
                (true, null),
                (false, "재생 중에는 편집할 수 없습니다"),
                (true, null),
            ],
            availability);
    }

    [Fact]
    public void Locked_transform_edits_and_preview_preserve_document_and_history()
    {
        var session = CreateQuarterSecondSession(out var document);
        session.SelectActor("host");

        Assert.False(session.CanEditSelectedTransform);
        Assert.False(session.MoveSelectedActor(new Position3(8, 2, 9)));
        Assert.False(session.RotateSelectedActor(90));
        Assert.False(session.SetSelectedActorTransform(new Position3(8, 2, 9), 90));
        var exception = Assert.Throws<InvalidOperationException>(() => session.BeginPreview());

        Assert.Contains(session.EditLockReason!, exception.Message);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
    }

    [Fact]
    public void Playback_change_clears_active_preview_before_external_playback_observers()
    {
        var session = CreateQuarterSecondSession(out _);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        var events = new List<string>();
        session.PreviewChanged += (_, args) => events.Add(args.Preview is null ? "preview:null" : "preview:value");
        session.EditAvailabilityChanged += (_, _) => events.Add("availability");
        session.Playback.Changed += (_, _) => events.Add("playback");

        Assert.True(session.Playback.Seek(0.5));

        Assert.Equal(["preview:null", "availability", "playback"], events);
    }

    [Fact]
    public void Preview_clear_observer_cannot_reentrantly_persist_a_transform_after_playback_starts()
    {
        var session = CreateQuarterSecondSession(out var document);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        var committedBefore = document.GetTransformKeyframe("host", "host-first");
        bool? observedCanEdit = null;
        string? observedLockReason = null;
        bool? reentrantEditApplied = null;
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is not null)
            {
                return;
            }

            observedCanEdit = session.CanEditSelectedTransform;
            observedLockReason = session.EditLockReason;
            reentrantEditApplied = session.SetSelectedActorTransform(new Position3(99, 98, 97), 180);
        };

        Assert.True(session.Playback.Play());

        Assert.False(observedCanEdit);
        Assert.Equal("재생 중에는 편집할 수 없습니다", observedLockReason);
        Assert.False(reentrantEditApplied);
        Assert.Same(committedBefore, document.GetTransformKeyframe("host", "host-first"));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.False(session.CommitPreview());
    }

    [Fact]
    public void Preview_clear_observer_cannot_reentrantly_begin_a_new_preview_after_playback_starts()
    {
        var session = CreateQuarterSecondSession(out var document);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        var committedBefore = document.GetTransformKeyframe("host", "host-first");
        var beginWasAllowed = false;
        InvalidOperationException? rejection = null;
        EventHandler<TransformPreviewChangedEventArgs> adversarialObserver = (_, args) =>
        {
            if (args.Preview is not null)
            {
                return;
            }

            try
            {
                session.BeginPreview();
                beginWasAllowed = true;
            }
            catch (InvalidOperationException exception)
            {
                rejection = exception;
            }
        };
        session.PreviewChanged += adversarialObserver;

        Assert.True(session.Playback.Play());
        session.PreviewChanged -= adversarialObserver;

        var residualPreviewClears = 0;
        session.PreviewChanged += (_, args) => residualPreviewClears += args.Preview is null ? 1 : 0;
        session.CancelPreview();

        Assert.False(beginWasAllowed);
        Assert.NotNull(rejection);
        Assert.Contains("재생 중에는 편집할 수 없습니다", rejection.Message);
        Assert.Equal(0, residualPreviewClears);
        Assert.Same(committedBefore, document.GetTransformKeyframe("host", "host-first"));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
    }

    [Fact]
    public void Preview_clear_observer_that_pauses_playback_does_not_receive_stale_outer_availability()
    {
        var session = CreateQuarterSecondSession(out _);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        var availabilityChanges = new List<(bool CanEdit, string? Reason)>();
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is null)
            {
                session.Playback.Pause();
            }
        };
        session.EditAvailabilityChanged += (_, args) =>
            availabilityChanges.Add((args.CanEditSelectedTransform, args.EditLockReason));

        Assert.True(session.Playback.Play());

        Assert.False(session.Playback.IsPlaying);
        Assert.True(session.CanEditSelectedTransform);
        Assert.Null(session.EditLockReason);
        Assert.Equal([(true, null)], availabilityChanges);
    }

    [Fact]
    public void Throwing_preview_observer_does_not_leave_transform_editing_enabled_after_playback_starts()
    {
        var session = CreateQuarterSecondSession(out var document);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is null)
            {
                throw new PreviewObserverException();
            }
        };

        Assert.Throws<PreviewObserverException>(() => session.Playback.Play());

        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("재생 중에는 편집할 수 없습니다", session.EditLockReason);
        Assert.False(session.CommitPreview());
        Assert.False(session.MoveSelectedActor(new Position3(8, 2, 9)));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
    }

    [Fact]
    public void Playback_change_preserves_preview_and_availability_observer_failures_after_restoring_state()
    {
        var session = CreateQuarterSecondSession(out _);
        session.SelectActor("host");
        session.Playback.Seek(0.25);
        session.BeginPreview();
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is null)
            {
                throw new PreviewObserverException();
            }
        };
        session.EditAvailabilityChanged += (_, _) => throw new EditAvailabilityObserverException();

        var exception = Assert.Throws<AggregateException>(() => session.Playback.Play());

        Assert.Contains(exception.InnerExceptions, item => item is PreviewObserverException);
        Assert.Contains(exception.InnerExceptions, item => item is EditAvailabilityObserverException);
        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("재생 중에는 편집할 수 없습니다", session.EditLockReason);
    }

    [Fact]
    public void Playback_changes_preserve_revision_and_history()
    {
        var session = CreateQuarterSecondSession(out var document);
        session.SelectActor("host");
        var revision = document.Revision;

        session.Playback.Seek(0.25);
        session.Playback.Play();
        session.Playback.Pause();

        Assert.Equal(revision, document.Revision);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
    }

    [Fact]
    public void Selection_change_recalculates_edit_availability()
    {
        var session = CreateQuarterSecondSession(out _);
        session.Playback.Seek(0.25);
        session.SelectActor("host");
        Assert.True(session.CanEditSelectedTransform);

        session.SelectActor(null);

        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("배우를 선택해야 편집할 수 있습니다", session.EditLockReason);
    }

    [Fact]
    public void Actor_display_info_exposes_immutable_name_and_role_without_exposing_actor_tracks()
    {
        var session = CreateSession(out _);

        var actors = session.ActorDisplayInfos;

        Assert.Equal(2, actors.Count);
        Assert.Equal(new ActorDisplayInfo("host", "Host", "Hero"), actors[0]);
        Assert.Equal(new ActorDisplayInfo("target", "Target", "Enemy"), session.GetActorDisplayInfo("target"));
        Assert.Throws<ArgumentException>(() => session.GetActorDisplayInfo("missing"));
        Assert.Throws<NotSupportedException>(() => ((IList<ActorDisplayInfo>)actors)[0] = actors[1]);
    }

    [Fact]
    public void History_changed_observes_final_stack_state_after_move_undo_and_redo()
    {
        var session = CreateSession(out _);
        var states = new List<(bool CanUndo, bool CanRedo)>();
        session.HistoryChanged += (_, _) => states.Add((session.CanUndo, session.CanRedo));
        session.SelectActor("host");

        Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.True(session.Undo());
        Assert.True(session.Redo());

        Assert.Equal([(true, false), (false, true), (true, false)], states);
    }

    [Fact]
    public void History_changed_is_silent_for_no_op_and_pre_mutation_failure()
    {
        var session = CreateSession(out _);
        var eventCount = 0;
        session.HistoryChanged += (_, _) => eventCount++;
        session.SelectActor("host");
        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());

        Assert.False(session.SetSelectedActorTransform(current.Position, current.YawDegrees + 360));
        Assert.False(session.ExecuteCommand(new ReplaceTransformCommand(
            "host",
            new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(99, 2, 3), current.YawDegrees),
            new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(5, 2, 7), 90))));

        Assert.Equal(0, eventCount);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void History_changed_observes_transition_when_document_observer_throws_after_mutation()
    {
        var session = CreateSession(out var document);
        var states = new List<(bool CanUndo, bool CanRedo)>();
        session.HistoryChanged += (_, _) => states.Add((session.CanUndo, session.CanRedo));
        session.SelectActor("host");
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.MoveSelectedActor(new Position3(5, 2, 7)));

        Assert.Equal([(true, false)], states);
    }

    [Fact]
    public void Move_undo_redo_changes_only_the_first_transform_and_keeps_revision_monotonic()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");

        Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.True(session.Undo());
        Assert.True(session.Redo());

        var host = document.Actors.Single(actor => actor.ActorId == "host");
        Assert.Equal(new Position3(5, 2, 7), host.TransformKeyframes[0].Position);
        Assert.Equal(new Position3(2, 3, 4), host.TransformKeyframes[1].Position);
        Assert.Equal(3, document.Revision);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void SelectActor_changes_selection_without_changing_document_and_raises_once_per_change()
    {
        var session = CreateSession(out var document);
        var selections = new List<string?>();
        session.SelectionChanged += (_, args) => selections.Add(args.SelectedActorId);

        session.SelectActor("host");
        session.SelectActor("host");
        session.SelectActor(null);

        Assert.Equal(["host", null], selections);
        Assert.Null(session.SelectedActorId);
        Assert.Equal(0, document.Revision);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SelectActor_rejects_unknown_actor_and_keeps_the_current_selection()
    {
        var session = CreateSession(out _);
        session.SelectActor("host");

        Assert.Throws<ArgumentException>(() => session.SelectActor("missing"));

        Assert.Equal("host", session.SelectedActorId);
    }

    [Fact]
    public void GetSelectedTransform_returns_the_exact_keyframe_selected_at_the_current_time()
    {
        var session = CreateSession(out _);

        Assert.Null(session.GetSelectedTransform());
        session.SelectActor("host");

        var selected = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal("host-first", selected.Id);
        Assert.Equal(0, selected.TimeSeconds);
    }

    [Fact]
    public void Editing_without_a_selection_returns_false_without_changing_the_document()
    {
        var session = CreateSession(out var document);

        Assert.False(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.False(session.RotateSelectedActor(90));
        Assert.False(session.SetSelectedActorTransform(new Position3(5, 2, 7), 90));

        Assert.Equal(0, document.Revision);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Move_preserves_yaw_and_rotate_preserves_position_while_normalizing_yaw()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");

        Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.True(session.RotateSelectedActor(-90));

        var first = document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes[0];
        Assert.Equal(new Position3(5, 2, 7), first.Position);
        Assert.Equal(270, first.YawDegrees);
    }

    [Fact]
    public void No_op_edit_preserves_existing_undo_and_redo_history()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        var expectedRevision = document.Revision;
        var expectedTransform = session.GetSelectedTransform();

        Assert.False(session.SetSelectedActorTransform(expectedTransform!.Position, expectedTransform.YawDegrees + 360));

        Assert.Equal(expectedRevision, document.Revision);
        Assert.Equal(expectedTransform, session.GetSelectedTransform());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void New_edit_after_undo_clears_redo_history()
    {
        var session = CreateSession(out _);
        session.SelectActor("host");
        Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.True(session.Undo());

        Assert.True(session.RotateSelectedActor(90));

        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Failed_execute_preserves_history_stacks()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        var original = new TransformKeyframe("host-first", 0, new Position3(1, 2, 3), 10);
        var expectedTransform = session.GetSelectedTransform();
        var expectedRevision = document.Revision;

        Assert.False(session.ExecuteCommand(new ReplaceTransformCommand(
            "host",
            original,
            new TransformKeyframe(original.Id, original.TimeSeconds, new Position3(9, 2, 11), 90))));

        Assert.Equal(expectedRevision, document.Revision);
        Assert.Equal(expectedTransform, session.GetSelectedTransform());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Failed_undo_preserves_history_stacks()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        document.ReplaceTransformKeyframe(
            "host",
            current,
            new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(6, 2, 8), current.YawDegrees));
        var expectedTransform = session.GetSelectedTransform();
        var expectedRevision = document.Revision;

        Assert.False(session.Undo());

        Assert.Equal(expectedRevision, document.Revision);
        Assert.Equal(expectedTransform, session.GetSelectedTransform());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Failed_redo_preserves_history_stacks()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        document.ReplaceTransformKeyframe(
            "host",
            current,
            new TransformKeyframe(current.Id, current.TimeSeconds, new Position3(6, 2, 8), current.YawDegrees));
        var expectedTransform = session.GetSelectedTransform();
        var expectedRevision = document.Revision;

        Assert.False(session.Redo());

        Assert.Equal(expectedRevision, document.Revision);
        Assert.Equal(expectedTransform, session.GetSelectedTransform());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Execute_commits_history_transition_when_a_changed_observer_throws_after_mutation()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.RotateSelectedActor(180));

        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(4, document.Revision);
        Assert.Equal(new Position3(5, 2, 7), current.Position);
        Assert.Equal(180, current.YawDegrees);
        Assert.Equal(2, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Undo_commits_history_transition_when_a_changed_observer_throws_after_mutation()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.Undo());

        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(4, document.Revision);
        Assert.Equal(new Position3(1, 2, 3), current.Position);
        Assert.Equal(10, current.YawDegrees);
        Assert.Equal(0, session.UndoCount);
        Assert.Equal(2, session.RedoCount);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Redo_commits_history_transition_when_a_changed_observer_throws_after_mutation()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.Redo());

        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(4, document.Revision);
        Assert.Equal(new Position3(5, 2, 7), current.Position);
        Assert.Equal(90, current.YawDegrees);
        Assert.Equal(2, session.UndoCount);
        Assert.Equal(0, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Preview_updates_without_changing_document_or_history_and_commit_changes_once()
    {
        var session = CreateSession(out var document);
        TransformPreview? firstSubscriberPreview = null;
        TransformPreview? secondSubscriberPreview = null;
        TransformPreview? lastPreview = new TransformPreview("placeholder", "placeholder", new Position3(0, 0, 0), 0);
        var previewEvents = new List<string>();
        session.PreviewChanged += (_, args) =>
        {
            firstSubscriberPreview = args.Preview;
            lastPreview = args.Preview;
            previewEvents.Add(args.Preview is null ? "null" : "preview");
        };
        session.PreviewChanged += (_, args) => secondSubscriberPreview = args.Preview;
        session.SelectActor("host");

        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 4), 450);

        Assert.Equal(0, document.Revision);
        Assert.False(session.CanUndo);
        Assert.NotNull(firstSubscriberPreview);
        Assert.Equal("host", firstSubscriberPreview.ActorId);
        Assert.Equal("host-first", firstSubscriberPreview.KeyframeId);
        Assert.Equal(90, firstSubscriberPreview.YawDegrees);
        Assert.Same(firstSubscriberPreview, secondSubscriberPreview);
        Assert.Equal(["preview"], previewEvents);

        Assert.True(session.CommitPreview());
        Assert.Equal(1, document.Revision);
        Assert.True(session.CanUndo);
        Assert.Null(lastPreview);
        Assert.Equal(["preview", "null"], previewEvents);
    }

    [Fact]
    public void Preview_requires_an_active_selected_actor()
    {
        var session = CreateSession(out _);
        Assert.Throws<InvalidOperationException>(() => session.BeginPreview());
        Assert.Throws<InvalidOperationException>(() => session.UpdatePreview(new Position3(1, 2, 3), 0));
        Assert.False(session.CommitPreview());

        session.SelectActor("host");
        session.BeginPreview();
        Assert.Throws<InvalidOperationException>(() => session.BeginPreview());
    }

    [Fact]
    public void CancelPreview_clears_preview_without_changing_document_or_history()
    {
        var session = CreateSession(out var document);
        TransformPreview? lastPreview = new TransformPreview("placeholder", "placeholder", new Position3(0, 0, 0), 0);
        var previewEvents = new List<string>();
        session.PreviewChanged += (_, args) =>
        {
            lastPreview = args.Preview;
            previewEvents.Add(args.Preview is null ? "null" : "preview");
        };
        session.SelectActor("host");
        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 4), 90);

        session.CancelPreview();

        Assert.Null(lastPreview);
        Assert.Equal(0, document.Revision);
        Assert.False(session.CanUndo);
        Assert.Equal(["preview", "null"], previewEvents);

        session.CancelPreview();

        Assert.Equal(["preview", "null"], previewEvents);
    }

    [Fact]
    public void No_op_preview_commit_clears_preview_and_preserves_existing_undo_and_redo_history()
    {
        var session = CreateSessionWithUndoAndRedo(out var document);
        TransformPreview? lastPreview = new TransformPreview("placeholder", "placeholder", new Position3(0, 0, 0), 0);
        session.PreviewChanged += (_, args) => lastPreview = args.Preview;
        var expectedTransform = session.GetSelectedTransform();
        var expectedRevision = document.Revision;
        session.BeginPreview();
        session.UpdatePreview(expectedTransform!.Position, expectedTransform.YawDegrees + 360);

        Assert.False(session.CommitPreview());

        Assert.Null(lastPreview);
        Assert.Equal(expectedRevision, document.Revision);
        Assert.Equal(expectedTransform, session.GetSelectedTransform());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        Assert.True(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Detailed_preview_commit_classifies_normalized_same_transform_as_no_change()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        var current = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        session.BeginPreview();
        session.UpdatePreview(current.Position, current.YawDegrees + 360);

        var result = session.CommitPreviewDetailed();

        Assert.Equal(SceneEditResult.NoChange, result);
        Assert.Equal(0, session.CurrentRevision);
        Assert.Equal(0, document.Revision);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Detailed_preview_commit_classifies_stale_preimage_as_conflict_without_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        var original = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        var external = new TransformKeyframe(
            original.Id,
            original.TimeSeconds,
            new Position3(4, 2, 6),
            original.YawDegrees);
        Assert.True(document.ReplaceTransformKeyframe("host", original, external));

        var result = session.CommitPreviewDetailed();

        Assert.Equal(SceneEditResult.Conflict, result);
        Assert.Equal(1, session.CurrentRevision);
        Assert.Equal(external, session.GetSelectedTransform());
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Detailed_preview_commit_rethrows_original_observer_exception_after_history_transition()
    {
        var session = CreateSession(out var document);
        var historyStates = new List<(bool CanUndo, bool CanRedo)>();
        session.HistoryChanged += (_, _) => historyStates.Add((session.CanUndo, session.CanRedo));
        session.SelectActor("host");
        session.BeginPreview();
        session.UpdatePreview(new Position3(8, 2, 9), 90);
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.CommitPreviewDetailed());

        Assert.Equal(1, session.CurrentRevision);
        var committed = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(new Position3(8, 2, 9), committed.Position);
        Assert.Equal(90, committed.YawDegrees);
        Assert.Equal([(true, false)], historyStates);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Selection_change_clears_preview_before_raising_selection_and_same_selection_is_silent()
    {
        var session = CreateSession(out _);
        var events = new List<string>();
        session.PreviewChanged += (_, args) => events.Add(args.Preview is null ? "preview:null" : "preview:value");
        session.SelectionChanged += (_, args) => events.Add($"selection:{args.SelectedActorId}");
        session.SelectActor("host");
        events.Clear();
        session.BeginPreview();
        session.UpdatePreview(new Position3(3, 2, 4), 90);

        session.SelectActor("target");
        session.SelectActor("target");
        session.CancelPreview();

        Assert.Equal(["preview:value", "preview:null", "selection:target"], events);
    }

    [Fact]
    public void Transform_keyframe_add_uses_the_current_snapshot_and_moves_through_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        var evaluatedPosition = document.CreateSnapshot(2).ActorTransforms["host"].Position;

        Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());
        var added = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(2, added.TimeSeconds);
        Assert.Equal(evaluatedPosition, added.Position);
        Assert.Equal("host-transform-0001", added.Id);
        Assert.Equal(1, document.Revision);
        Assert.True(session.Undo());
        Assert.DoesNotContain(document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes,
            frame => frame.Id == added.Id);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.Contains(document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes,
            frame => frame.Id == added.Id);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
        Assert.Equal(3, document.Revision);
    }

    [Fact]
    public void Transform_keyframe_update_changes_the_selected_keyframe_time_and_pose_through_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");

        Assert.Equal(SceneEditResult.Applied,
            session.UpdateSelectedTransformKeyframe(2, new Position3(8, 2, 9), 90));
        var updated = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal("host-first", updated.Id);
        Assert.Equal(2, updated.TimeSeconds);
        Assert.Equal(new Position3(8, 2, 9), updated.Position);
        Assert.Equal(90, updated.YawDegrees);
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        Assert.True(session.Undo());
        Assert.Equal(0, document.GetTransformKeyframe("host", "host-first").TimeSeconds);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.Equal(2, document.GetTransformKeyframe("host", "host-first").TimeSeconds);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
        Assert.Equal(3, document.Revision);
    }

    [Fact]
    public void Transform_keyframe_delete_selects_the_earlier_nearest_remaining_keyframe_and_moves_through_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());
        var added = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());

        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedTransformKeyframe());
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.True(session.Undo());
        Assert.Contains(document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes,
            frame => frame.Id == "host-first");
        Assert.Equal(added, document.GetTransformKeyframe("host", added.Id));
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.DoesNotContain(document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes,
            frame => frame.Id == added.Id);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.Equal(4, document.Revision);
    }

    [Fact]
    public void Transform_keyframe_add_reconciles_actual_state_after_history_observer_undoes_the_add()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        AssertReconciliationPayloadsStayCurrent(session, document);
        var observerRan = false;
        session.HistoryChanged += (_, _) =>
        {
            if (!observerRan)
            {
                observerRan = true;
                Assert.True(session.Undo());
            }
        };

        Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());

        Assert.DoesNotContain(document.Actors.Single(actor => actor.ActorId == "host").TransformKeyframes,
            frame => frame.Id == "host-transform-0001");
        AssertSessionSelectionAndAvailabilityMatchDocument(session, document, "host-first", 0);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Transform_keyframe_update_reconciles_actual_state_after_history_observer_undoes_the_update()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        var original = document.GetTransformKeyframe("host", "host-first");
        AssertReconciliationPayloadsStayCurrent(session, document);
        var observerRan = false;
        session.HistoryChanged += (_, _) =>
        {
            if (!observerRan)
            {
                observerRan = true;
                Assert.True(session.Undo());
            }
        };

        Assert.Equal(
            SceneEditResult.Applied,
            session.UpdateSelectedTransformKeyframe(2, new Position3(8, 2, 9), 90));

        Assert.Equal(original, document.GetTransformKeyframe("host", "host-first"));
        AssertSessionSelectionAndAvailabilityMatchDocument(session, document, "host-first", 0);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Transform_keyframe_delete_reconciles_actual_state_after_history_observer_undoes_the_delete()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        var original = document.GetTransformKeyframe("host", "host-first");
        AssertReconciliationPayloadsStayCurrent(session, document);
        var observerRan = false;
        session.HistoryChanged += (_, _) =>
        {
            if (!observerRan)
            {
                observerRan = true;
                Assert.True(session.Undo());
            }
        };

        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedTransformKeyframe());

        Assert.Equal(original, document.GetTransformKeyframe("host", "host-first"));
        AssertSessionSelectionAndAvailabilityMatchDocument(session, document, "host-first", 0);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void Transform_keyframe_selection_pauses_seeks_and_reports_the_full_selected_keyframe()
    {
        var session = CreateSession(out _);
        TransformKeyframeSelectionChangedEventArgs? observed = null;
        session.TransformKeyframeSelectionChanged += (_, args) => observed = args;
        session.SelectActor("host");
        Assert.True(session.Playback.Play());

        Assert.Equal(SceneEditResult.Applied, session.SelectTransformKeyframe("host-second"));

        Assert.False(session.Playback.IsPlaying);
        Assert.Equal(4, session.Playback.CurrentTimeSeconds);
        Assert.Equal("host-second", session.SelectedTransformKeyframeId);
        Assert.Equal("host", observed!.ActorId);
        Assert.Equal("host-second", observed.KeyframeId);
        Assert.Equal(4, observed.Keyframe!.TimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
    }

    [Fact]
    public void Transform_keyframe_availability_and_conflicts_preserve_document_and_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        Assert.True(session.CanAddTransformKeyframe);
        Assert.False(session.CanEditSelectedTransform);
        Assert.False(session.CanDeleteSelectedTransformKeyframe);

        Assert.True(session.Playback.Play());
        var revision = document.Revision;
        Assert.Equal(SceneEditResult.Conflict, session.AddTransformKeyframeAtCurrentTime());
        Assert.Equal(SceneEditResult.Conflict,
            session.UpdateSelectedTransformKeyframe(3, new Position3(8, 2, 9), 90));
        Assert.Equal(SceneEditResult.Conflict, session.RemoveSelectedTransformKeyframe());
        Assert.Equal(revision, document.Revision);
        Assert.False(session.CanUndo);

        Assert.True(session.Playback.Pause());
        Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());
        Assert.Equal(SceneEditResult.Conflict, session.AddTransformKeyframeAtCurrentTime());
        Assert.Equal(SceneEditResult.Conflict,
            session.UpdateSelectedTransformKeyframe(11, new Position3(8, 2, 9), 90));
        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedTransformKeyframe());
        Assert.Equal(SceneEditResult.Applied, session.RemoveSelectedTransformKeyframe());
        Assert.Equal(SceneEditResult.Conflict, session.RemoveSelectedTransformKeyframe());
    }

    [Fact]
    public void Transform_keyframe_stale_update_and_delete_do_not_create_history()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        var before = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        var external = new TransformKeyframe(before.Id, before.TimeSeconds, new Position3(8, 2, 9), 90);
        Assert.True(document.UpdateTransformKeyframe("host", before, external));

        Assert.Equal(SceneEditResult.Conflict,
            session.UpdateSelectedTransformKeyframe(2, new Position3(6, 2, 8), 120));
        Assert.Equal(SceneEditResult.Conflict, session.RemoveSelectedTransformKeyframe());
        Assert.Equal(1, document.Revision);
        Assert.False(session.CanUndo);
        Assert.Equal(external, session.GetSelectedTransform());
    }

    [Fact]
    public void Transform_keyframe_add_commits_history_when_a_document_observer_throws_after_mutation()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        document.Changed += (_, _) => throw new ChangedObserverException();

        Assert.Throws<ChangedObserverException>(() => session.AddTransformKeyframeAtCurrentTime());

        Assert.Equal(1, document.Revision);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Transform_keyframe_playback_reentrancy_does_not_publish_a_stale_full_selection_payload()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        session.BeginPreview();
        var observedStaleSelection = false;
        session.TransformKeyframeSelectionChanged += (_, args) =>
        {
            if (args.Keyframe is not null)
            {
                observedStaleSelection |= args.Keyframe != document.GetTransformKeyframe(args.ActorId!, args.KeyframeId!);
            }
        };
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is not null)
            {
                return;
            }

            var second = document.GetTransformKeyframe("host", "host-second");
            Assert.True(document.UpdateTransformKeyframe(
                "host",
                second,
                new TransformKeyframe(second.Id, second.TimeSeconds, new Position3(8, 2, 9), 90)));
            Assert.True(session.Playback.Seek(0));
            Assert.True(session.Playback.Seek(4));
        };

        Assert.True(session.Playback.Seek(4));

        Assert.False(observedStaleSelection);
        var selected = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(document.GetTransformKeyframe("host", "host-second"), selected);
    }

    [Fact]
    public void Transform_keyframe_mutation_observer_exceptions_reconcile_selection_for_add_update_and_remove()
    {
        var addSession = CreateSession(out var addDocument);
        addSession.SelectActor("host");
        Assert.True(addSession.Playback.Seek(2));
        AssertSelectionReconcilesAfterMutationObserverException(
            addSession,
            addDocument,
            () => addSession.AddTransformKeyframeAtCurrentTime());

        var updateSession = CreateSession(out var updateDocument);
        updateSession.SelectActor("host");
        AssertSelectionReconcilesAfterMutationObserverException(
            updateSession,
            updateDocument,
            () => updateSession.UpdateSelectedTransformKeyframe(2, new Position3(8, 2, 9), 90));
        Assert.Equal(2, updateSession.Playback.CurrentTimeSeconds);
        Assert.True(updateSession.CanEditSelectedTransform);

        var removeSession = CreateSession(out var removeDocument);
        removeSession.SelectActor("host");
        AssertSelectionReconcilesAfterMutationObserverException(
            removeSession,
            removeDocument,
            () => removeSession.RemoveSelectedTransformKeyframe());
    }

    [Fact]
    public void Transform_keyframe_undo_and_redo_observer_exceptions_reconcile_selection()
    {
        var session = CreateSession(out var document);
        session.SelectActor("host");
        Assert.True(session.Playback.Seek(2));
        Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());
        var throwingObserver = new EventHandler<SceneDocumentChangedEventArgs>((_, _) => throw new ChangedObserverException());
        document.Changed += throwingObserver;

        Assert.Throws<ChangedObserverException>(() => session.Undo());
        AssertSelectionExistsInDocument(session, document);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanRedo);
        Assert.Throws<ChangedObserverException>(() => session.Redo());
        AssertSelectionExistsInDocument(session, document);
        Assert.Equal("host-first", session.SelectedTransformKeyframeId);
        Assert.Equal(0, session.Playback.CurrentTimeSeconds);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Transform_keyframe_playback_reentrancy_publishes_only_current_availability_payloads()
    {
        var session = CreateSession(out _);
        session.SelectActor("host");
        session.BeginPreview();
        var availability = new List<(bool CanEdit, string? Reason)>();
        var previewObserverSawPlayingLock = false;
        session.EditAvailabilityChanged += (_, args) =>
            availability.Add((args.CanEditSelectedTransform, args.EditLockReason));
        session.PreviewChanged += (_, args) =>
        {
            if (args.Preview is not null)
            {
                return;
            }

            previewObserverSawPlayingLock = !session.CanEditSelectedTransform &&
                session.EditLockReason == "재생 중에는 편집할 수 없습니다";
            Assert.Throws<InvalidOperationException>(() => session.BeginPreview());
            Assert.True(session.Playback.Pause());
            Assert.True(session.Playback.Seek(2));
        };

        Assert.True(session.Playback.Play());

        Assert.True(previewObserverSawPlayingLock);
        Assert.False(session.Playback.IsPlaying);
        Assert.Equal(2, session.Playback.CurrentTimeSeconds);
        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("선택한 키프레임 시각에서만 편집할 수 있습니다", session.EditLockReason);
        Assert.Equal(
            [
                (true, null),
                (false, "선택한 키프레임 시각에서만 편집할 수 있습니다"),
            ],
            availability);
    }

    private static void AssertSelectionReconcilesAfterMutationObserverException(
        DocumentSession session,
        SceneDocument document,
        Action mutation)
    {
        var throwingObserver = new EventHandler<SceneDocumentChangedEventArgs>((_, _) => throw new ChangedObserverException());
        document.Changed += throwingObserver;

        Assert.Throws<ChangedObserverException>(mutation);

        document.Changed -= throwingObserver;
        AssertSelectionExistsInDocument(session, document);
    }

    private static void AssertSelectionExistsInDocument(DocumentSession session, SceneDocument document)
    {
        var keyframeId = Assert.IsType<string>(session.SelectedTransformKeyframeId);
        var selected = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(document.GetTransformKeyframe(session.SelectedActorId!, keyframeId), selected);
    }

    private static void AssertSessionSelectionAndAvailabilityMatchDocument(
        DocumentSession session,
        SceneDocument document,
        string expectedKeyframeId,
        double expectedTimeSeconds)
    {
        Assert.Equal(expectedKeyframeId, session.SelectedTransformKeyframeId);
        var selected = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
        Assert.Equal(document.GetTransformKeyframe("host", expectedKeyframeId), selected);
        Assert.Equal(expectedTimeSeconds, session.Playback.CurrentTimeSeconds);
        Assert.True(session.CanEditSelectedTransform);
        Assert.False(session.CanAddTransformKeyframe);
        Assert.True(session.CanDeleteSelectedTransformKeyframe);
    }

    private static void AssertReconciliationPayloadsStayCurrent(
        DocumentSession session,
        SceneDocument document)
    {
        session.TransformKeyframeSelectionChanged += (_, args) =>
        {
            Assert.Equal(session.SelectedActorId, args.ActorId);
            Assert.Equal(session.SelectedTransformKeyframeId, args.KeyframeId);
            if (args.KeyframeId is null)
            {
                Assert.Null(args.Keyframe);
            }
            else
            {
                Assert.Equal(document.GetTransformKeyframe(args.ActorId!, args.KeyframeId), args.Keyframe);
            }
        };
        session.EditAvailabilityChanged += (_, args) =>
        {
            Assert.Equal(session.CanEditSelectedTransform, args.CanEditSelectedTransform);
            Assert.Equal(session.EditLockReason, args.EditLockReason);
        };
    }

    private static void AssertActionFrame(
        ActionKeyframe? frame,
        string id,
        double timeSeconds,
        string actionKey)
    {
        var actual = Assert.IsType<ActionKeyframe>(frame);
        Assert.Equal(id, actual.Id);
        Assert.Equal(timeSeconds, actual.TimeSeconds);
        Assert.Equal(actionKey, actual.ActionKey);
    }

    private static void AssertLockFrame(
        LockOnKeyframe? frame,
        string id,
        double timeSeconds,
        bool enabled,
        string? targetActorId,
        double yawOffsetDegrees,
        LockOnTrackingMode trackingMode)
    {
        var actual = Assert.IsType<LockOnKeyframe>(frame);
        Assert.Equal(id, actual.Id);
        Assert.Equal(timeSeconds, actual.TimeSeconds);
        Assert.Equal(enabled, actual.Enabled);
        Assert.Equal(targetActorId, actual.TargetActorId);
        Assert.Equal(yawOffsetDegrees, actual.YawOffsetDegrees);
        Assert.Equal(trackingMode, actual.TrackingMode);
    }

    private static DocumentSession CreateSession(out SceneDocument document)
    {
        document = SceneDocument.Create(
            "document-1",
            "Editable",
            null,
            10,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [
                        new TransformKeyframe("host-first", 0, new Position3(1, 2, 3), 10),
                        new TransformKeyframe("host-second", 4, new Position3(2, 3, 4), 20),
                    ],
                    [],
                    []),
                new ActorTrack(
                    "target",
                    "Target",
                    "Enemy",
                    [new TransformKeyframe("target-first", 1, new Position3(7, 0, 8), 180)],
                    [],
                    [])
            ]);
        return new DocumentSession(document);
    }

    private static DocumentSession CreateSemanticSession(out SceneDocument document)
    {
        document = SceneDocument.Create(
            "semantic-document",
            "Semantic timeline",
            null,
            10,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [new TransformKeyframe("host-first", 0, new Position3(1, 2, 3), 10)],
                    [new ActionKeyframe("host-action-1", 1, "idle")],
                    [new LockOnKeyframe(
                        "host-lock-1",
                        1,
                        true,
                        "invader",
                        15,
                        LockOnTrackingMode.Snap)]),
                new ActorTrack(
                    "invader",
                    "Invader",
                    "Enemy",
                    [new TransformKeyframe("invader-first", 0, new Position3(7, 0, 8), 180)],
                    [],
                    [])
            ]);
        return new DocumentSession(document);
    }

    private static DocumentSession CreateQuarterSecondSession(out SceneDocument document)
    {
        document = SceneDocument.Create(
            "quarter-second-document",
            "Timeline editable",
            null,
            1,
            30,
            [
                new ActorTrack(
                    "host",
                    "Host",
                    "Hero",
                    [new TransformKeyframe("host-first", 0.25, new Position3(1, 2, 3), 10)],
                    [],
                    [])
            ]);
        return new DocumentSession(document);
    }

    private static DocumentSession CreateSessionWithUndoAndRedo(out SceneDocument document)
    {
        var session = CreateSession(out document);
        session.SelectActor("host");
        Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
        Assert.True(session.RotateSelectedActor(90));
        Assert.True(session.Undo());
        Assert.Equal(1, session.UndoCount);
        Assert.Equal(1, session.RedoCount);
        return session;
    }

    private sealed class ChangedObserverException : Exception;

    private sealed class PreviewObserverException : Exception;

    private sealed class EditAvailabilityObserverException : Exception;
}
