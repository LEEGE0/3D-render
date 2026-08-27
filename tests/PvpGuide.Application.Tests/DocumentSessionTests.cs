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
    public void Selected_actor_is_editable_only_while_paused_at_its_first_keyframe_time()
    {
        var session = CreateQuarterSecondSession(out _);
        var availability = new List<(bool CanEdit, string? Reason)>();
        session.EditAvailabilityChanged += (_, args) => availability.Add((args.CanEditSelectedTransform, args.EditLockReason));

        session.SelectActor("host");
        Assert.False(session.CanEditSelectedTransform);
        Assert.Equal("최초 키프레임 시각에서만 편집할 수 있습니다", session.EditLockReason);

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
                (false, "최초 키프레임 시각에서만 편집할 수 있습니다"),
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
        session.Playback.Changed += (_, _) => events.Add("playback");

        Assert.True(session.Playback.Seek(0.5));

        Assert.Equal(["preview:null", "playback"], events);
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
    public void GetSelectedTransform_returns_the_selected_actors_first_keyframe()
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
