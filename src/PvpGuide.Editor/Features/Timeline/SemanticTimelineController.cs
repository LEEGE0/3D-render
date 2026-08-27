using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;
using PvpGuide.Editor.Features.Inspector;

namespace PvpGuide.Editor.Features.Timeline;

public sealed class SemanticTimelineController : IDisposable
{
    private readonly DocumentSession _session;
    private readonly Button _actionAddButton;
    private readonly Button _actionDeleteButton;
    private readonly Button _lockOnAddButton;
    private readonly Button _lockOnDeleteButton;
    private readonly LineEdit _actionKeyInput;
    private readonly CheckBox _lockEnabledInput;
    private readonly OptionButton _lockTargetInput;
    private readonly OptionButton _lockModeInput;
    private readonly SpinBox _lockYawOffsetInput;
    private readonly Label _statusLabel;
    private bool _disposed;

    public SemanticTimelineController(
        DocumentSession session,
        Button actionAddButton,
        Button actionDeleteButton,
        Button lockOnAddButton,
        Button lockOnDeleteButton,
        LineEdit actionKeyInput,
        CheckBox lockEnabledInput,
        OptionButton lockTargetInput,
        OptionButton lockModeInput,
        SpinBox lockYawOffsetInput,
        Label statusLabel)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _actionAddButton = actionAddButton ?? throw new ArgumentNullException(nameof(actionAddButton));
        _actionDeleteButton = actionDeleteButton ?? throw new ArgumentNullException(nameof(actionDeleteButton));
        _lockOnAddButton = lockOnAddButton ?? throw new ArgumentNullException(nameof(lockOnAddButton));
        _lockOnDeleteButton = lockOnDeleteButton ?? throw new ArgumentNullException(nameof(lockOnDeleteButton));
        _actionKeyInput = actionKeyInput ?? throw new ArgumentNullException(nameof(actionKeyInput));
        _lockEnabledInput = lockEnabledInput ?? throw new ArgumentNullException(nameof(lockEnabledInput));
        _lockTargetInput = lockTargetInput ?? throw new ArgumentNullException(nameof(lockTargetInput));
        _lockModeInput = lockModeInput ?? throw new ArgumentNullException(nameof(lockModeInput));
        _lockYawOffsetInput = lockYawOffsetInput ?? throw new ArgumentNullException(nameof(lockYawOffsetInput));
        _statusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));

        _actionAddButton.Pressed += OnActionAddPressed;
        _actionDeleteButton.Pressed += OnActionDeletePressed;
        _lockOnAddButton.Pressed += OnLockOnAddPressed;
        _lockOnDeleteButton.Pressed += OnLockOnDeletePressed;
        _session.SelectionChanged += OnSessionChanged;
        _session.ActionKeyframeSelectionChanged += OnActionSelectionChanged;
        _session.LockOnKeyframeSelectionChanged += OnLockOnSelectionChanged;
        _session.TimelineEditAvailabilityChanged += OnTimelineAvailabilityChanged;
        _session.Playback.Changed += OnPlaybackChanged;
        _session.SnapshotSource.Changed += OnDocumentChanged;
        RefreshAvailability();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _actionAddButton.Pressed -= OnActionAddPressed;
        _actionDeleteButton.Pressed -= OnActionDeletePressed;
        _lockOnAddButton.Pressed -= OnLockOnAddPressed;
        _lockOnDeleteButton.Pressed -= OnLockOnDeletePressed;
        _session.SelectionChanged -= OnSessionChanged;
        _session.ActionKeyframeSelectionChanged -= OnActionSelectionChanged;
        _session.LockOnKeyframeSelectionChanged -= OnLockOnSelectionChanged;
        _session.TimelineEditAvailabilityChanged -= OnTimelineAvailabilityChanged;
        _session.Playback.Changed -= OnPlaybackChanged;
        _session.SnapshotSource.Changed -= OnDocumentChanged;
        _disposed = true;
    }

    private void OnActionAddPressed()
    {
        HandleOperation(
            () => _session.AddActionKeyframeAtCurrentTimeDetailed(_actionKeyInput.Text),
            TimelineTrackKind.Action,
            "Action 추가");
    }

    private void OnActionDeletePressed() => HandleOperation(
        _session.RemoveSelectedActionKeyframeDetailed,
        TimelineTrackKind.Action,
        "Action 삭제");

    private void OnLockOnAddPressed()
    {
        HandleOperation(
            () => _session.AddLockOnKeyframeAtCurrentTimeDetailed(
                _lockEnabledInput.ButtonPressed,
                ReadSelectedTargetActorId(),
                _lockYawOffsetInput.Value,
                ReadTrackingMode()),
            TimelineTrackKind.LockOn,
            "Lock-on 추가");
    }

    private void OnLockOnDeletePressed() => HandleOperation(
        _session.RemoveSelectedLockOnKeyframeDetailed,
        TimelineTrackKind.LockOn,
        "Lock-on 삭제");

    private string? ReadSelectedTargetActorId() => _lockTargetInput.Selected <= 0
        ? null
        : _lockTargetInput.GetItemText(_lockTargetInput.Selected);

    private LockOnTrackingMode ReadTrackingMode()
    {
        var selected = _lockModeInput.Selected;
        return Enum.IsDefined(typeof(LockOnTrackingMode), selected)
            ? (LockOnTrackingMode)selected
            : LockOnTrackingMode.Continuous;
    }

    private void HandleOperation(
        Func<SemanticEditOutcome> operation,
        TimelineTrackKind track,
        string actionName)
    {
        var revisionBefore = _session.CurrentRevision;
        try
        {
            var outcome = operation();
            RefreshAvailability();
            ShowStatus(SemanticEditMessageFormatter.Format(
                outcome,
                track,
                actionName,
                _session.Playback.DurationSeconds));
        }
        catch (Exception exception) when (SemanticEditMessageFormatter.ShouldHandleObserverFailure(
            revisionBefore,
            _session.CurrentRevision))
        {
            RefreshAvailability();
            ShowStatus(SemanticEditMessageFormatter.FormatObserverFailure(actionName, exception.Message));
        }
    }

    private void RefreshAvailability()
    {
        _actionAddButton.Disabled = !_session.ActionEditAvailability.CanAdd;
        _actionDeleteButton.Disabled = !_session.ActionEditAvailability.CanDelete;
        _lockOnAddButton.Disabled = !_session.LockOnEditAvailability.CanAdd;
        _lockOnDeleteButton.Disabled = !_session.LockOnEditAvailability.CanDelete;
    }

    private void ShowStatus(string message) => _statusLabel.Text = message;

    private void OnSessionChanged(object? sender, SelectionChangedEventArgs eventArgs) => RefreshAvailability();

    private void OnActionSelectionChanged(object? sender, ActionKeyframeSelectionChangedEventArgs eventArgs) =>
        RefreshAvailability();

    private void OnLockOnSelectionChanged(object? sender, LockOnKeyframeSelectionChangedEventArgs eventArgs) =>
        RefreshAvailability();

    private void OnTimelineAvailabilityChanged(object? sender, TimelineEditAvailabilityChangedEventArgs eventArgs) =>
        RefreshAvailability();

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs) => RefreshAvailability();

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs) => RefreshAvailability();
}
