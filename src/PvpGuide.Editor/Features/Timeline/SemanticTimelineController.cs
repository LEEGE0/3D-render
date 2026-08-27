using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

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
        var actionKey = _actionKeyInput.Text;
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            ShowStatus("Action 추가 실패: ActionKey는 공백일 수 없습니다.");
            return;
        }

        HandleResult(
            _session.AddActionKeyframeAtCurrentTime(actionKey),
            "Action 추가",
            _session.ActionEditAvailability.AddLockReason);
    }

    private void OnActionDeletePressed() => HandleResult(
        _session.RemoveSelectedActionKeyframe(),
        "Action 삭제",
        _session.ActionEditAvailability.DeleteLockReason);

    private void OnLockOnAddPressed()
    {
        var targetActorId = ReadSelectedTargetActorId();
        if (_lockEnabledInput.ButtonPressed && targetActorId is null)
        {
            ShowStatus("Lock-on 추가 실패: 활성 Lock-on에는 대상 배우가 필요합니다.");
            return;
        }

        HandleResult(
            _session.AddLockOnKeyframeAtCurrentTime(
                _lockEnabledInput.ButtonPressed,
                targetActorId,
                _lockYawOffsetInput.Value,
                ReadTrackingMode()),
            "Lock-on 추가",
            _session.LockOnEditAvailability.AddLockReason);
    }

    private void OnLockOnDeletePressed() => HandleResult(
        _session.RemoveSelectedLockOnKeyframe(),
        "Lock-on 삭제",
        _session.LockOnEditAvailability.DeleteLockReason);

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

    private void HandleResult(SceneEditResult result, string actionName, string? lockReason)
    {
        RefreshAvailability();
        ShowStatus(result switch
        {
            SceneEditResult.Applied => $"{actionName} 완료",
            SceneEditResult.NoChange => $"{actionName}: 적용할 변경이 없습니다.",
            SceneEditResult.Conflict => $"{actionName} 실패: {lockReason ?? "최신 문서 상태와 충돌했습니다."}",
            _ => throw new InvalidOperationException($"알 수 없는 편집 결과입니다: {result}"),
        });
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
