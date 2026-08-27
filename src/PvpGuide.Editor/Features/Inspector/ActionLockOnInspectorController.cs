using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Playback;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;
using PvpGuide.Domain.Timeline;

namespace PvpGuide.Editor.Features.Inspector;

public sealed class ActionLockOnInspectorController : IDisposable
{
    private readonly DocumentSession _session;
    private readonly Control _transformSection;
    private readonly Control _actionSection;
    private readonly Label _actionSelectionLabel;
    private readonly SpinBox _actionTimeInput;
    private readonly LineEdit _actionKeyInput;
    private readonly Button _actionApplyButton;
    private readonly Label _actionErrorLabel;
    private readonly Control _lockOnSection;
    private readonly Label _lockOnSelectionLabel;
    private readonly SpinBox _lockTimeInput;
    private readonly CheckBox _lockEnabledInput;
    private readonly OptionButton _lockTargetInput;
    private readonly OptionButton _lockModeInput;
    private readonly SpinBox _lockYawOffsetInput;
    private readonly Button _lockApplyButton;
    private readonly Label _lockErrorLabel;
    private bool _disposed;

    public ActionLockOnInspectorController(
        DocumentSession session,
        Control transformSection,
        Control actionSection,
        Label actionSelectionLabel,
        SpinBox actionTimeInput,
        LineEdit actionKeyInput,
        Button actionApplyButton,
        Label actionErrorLabel,
        Control lockOnSection,
        Label lockOnSelectionLabel,
        SpinBox lockTimeInput,
        CheckBox lockEnabledInput,
        OptionButton lockTargetInput,
        OptionButton lockModeInput,
        SpinBox lockYawOffsetInput,
        Button lockApplyButton,
        Label lockErrorLabel)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _transformSection = transformSection ?? throw new ArgumentNullException(nameof(transformSection));
        _actionSection = actionSection ?? throw new ArgumentNullException(nameof(actionSection));
        _actionSelectionLabel = actionSelectionLabel ?? throw new ArgumentNullException(nameof(actionSelectionLabel));
        _actionTimeInput = actionTimeInput ?? throw new ArgumentNullException(nameof(actionTimeInput));
        _actionKeyInput = actionKeyInput ?? throw new ArgumentNullException(nameof(actionKeyInput));
        _actionApplyButton = actionApplyButton ?? throw new ArgumentNullException(nameof(actionApplyButton));
        _actionErrorLabel = actionErrorLabel ?? throw new ArgumentNullException(nameof(actionErrorLabel));
        _lockOnSection = lockOnSection ?? throw new ArgumentNullException(nameof(lockOnSection));
        _lockOnSelectionLabel = lockOnSelectionLabel ?? throw new ArgumentNullException(nameof(lockOnSelectionLabel));
        _lockTimeInput = lockTimeInput ?? throw new ArgumentNullException(nameof(lockTimeInput));
        _lockEnabledInput = lockEnabledInput ?? throw new ArgumentNullException(nameof(lockEnabledInput));
        _lockTargetInput = lockTargetInput ?? throw new ArgumentNullException(nameof(lockTargetInput));
        _lockModeInput = lockModeInput ?? throw new ArgumentNullException(nameof(lockModeInput));
        _lockYawOffsetInput = lockYawOffsetInput ?? throw new ArgumentNullException(nameof(lockYawOffsetInput));
        _lockApplyButton = lockApplyButton ?? throw new ArgumentNullException(nameof(lockApplyButton));
        _lockErrorLabel = lockErrorLabel ?? throw new ArgumentNullException(nameof(lockErrorLabel));

        ConfigureTimeInput(_actionTimeInput);
        ConfigureTimeInput(_lockTimeInput);
        _lockYawOffsetInput.MinValue = -360000;
        _lockYawOffsetInput.MaxValue = 360000;
        _lockYawOffsetInput.Step = 0.1;
        _lockYawOffsetInput.AllowGreater = true;
        _lockYawOffsetInput.AllowLesser = true;
        PopulateTrackingModes();

        _actionApplyButton.Pressed += OnActionApplyPressed;
        _actionTimeInput.GetLineEdit().TextSubmitted += OnActionTextSubmitted;
        _actionKeyInput.TextSubmitted += OnActionTextSubmitted;
        _lockApplyButton.Pressed += OnLockApplyPressed;
        _lockTimeInput.GetLineEdit().TextSubmitted += OnLockTextSubmitted;
        _lockYawOffsetInput.GetLineEdit().TextSubmitted += OnLockTextSubmitted;
        _session.SelectionChanged += OnSelectionChanged;
        _session.ActionKeyframeSelectionChanged += OnActionSelectionChanged;
        _session.LockOnKeyframeSelectionChanged += OnLockOnSelectionChanged;
        _session.TimelineEditAvailabilityChanged += OnTimelineAvailabilityChanged;
        _session.Playback.Changed += OnPlaybackChanged;
        _session.SnapshotSource.Changed += OnDocumentChanged;
        RefreshPresentation();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _actionApplyButton.Pressed -= OnActionApplyPressed;
        _actionTimeInput.GetLineEdit().TextSubmitted -= OnActionTextSubmitted;
        _actionKeyInput.TextSubmitted -= OnActionTextSubmitted;
        _lockApplyButton.Pressed -= OnLockApplyPressed;
        _lockTimeInput.GetLineEdit().TextSubmitted -= OnLockTextSubmitted;
        _lockYawOffsetInput.GetLineEdit().TextSubmitted -= OnLockTextSubmitted;
        _session.SelectionChanged -= OnSelectionChanged;
        _session.ActionKeyframeSelectionChanged -= OnActionSelectionChanged;
        _session.LockOnKeyframeSelectionChanged -= OnLockOnSelectionChanged;
        _session.TimelineEditAvailabilityChanged -= OnTimelineAvailabilityChanged;
        _session.Playback.Changed -= OnPlaybackChanged;
        _session.SnapshotSource.Changed -= OnDocumentChanged;
        _disposed = true;
    }

    private void ConfigureTimeInput(SpinBox input)
    {
        input.MinValue = 0;
        input.MaxValue = _session.Playback.DurationSeconds;
        input.Step = 1d / _session.Playback.FramesPerSecond;
        input.AllowGreater = true;
        input.AllowLesser = true;
    }

    private void PopulateTrackingModes()
    {
        _lockModeInput.Clear();
        _lockModeInput.AddItem("Snap", (int)LockOnTrackingMode.Snap);
        _lockModeInput.AddItem("Continuous", (int)LockOnTrackingMode.Continuous);
        _lockModeInput.AddItem("Keyframe only", (int)LockOnTrackingMode.KeyframeOnly);
    }

    private void OnActionApplyPressed() => ApplyAction();

    private void OnActionTextSubmitted(string text) => ApplyAction();

    private void ApplyAction()
    {
        if (!_session.ActionEditAvailability.CanUpdate || _session.GetSelectedActionKeyframe() is null)
        {
            ShowActionError(_session.ActionEditAvailability.UpdateLockReason ?? "Action 키프레임을 선택해야 합니다.");
            return;
        }

        var actionKey = _actionKeyInput.Text;
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            ShowActionError("ActionKey는 공백일 수 없습니다.");
            return;
        }

        HandleActionResult(_session.UpdateSelectedActionKeyframe(_actionTimeInput.Value, actionKey));
    }

    private void OnLockApplyPressed() => ApplyLockOn();

    private void OnLockTextSubmitted(string text) => ApplyLockOn();

    private void ApplyLockOn()
    {
        if (!_session.LockOnEditAvailability.CanUpdate || _session.GetSelectedLockOnKeyframe() is null)
        {
            ShowLockError(_session.LockOnEditAvailability.UpdateLockReason ?? "Lock-on 키프레임을 선택해야 합니다.");
            return;
        }

        var targetActorId = ReadSelectedTargetActorId();
        if (_lockEnabledInput.ButtonPressed && targetActorId is null)
        {
            ShowLockError("활성 Lock-on에는 대상 배우가 필요합니다.");
            return;
        }

        HandleLockResult(_session.UpdateSelectedLockOnKeyframe(
            _lockTimeInput.Value,
            _lockEnabledInput.ButtonPressed,
            targetActorId,
            _lockYawOffsetInput.Value,
            ReadTrackingMode()));
    }

    private void HandleActionResult(SceneEditResult result)
    {
        if (result == SceneEditResult.Applied)
        {
            ClearActionError();
            return;
        }

        ShowActionError(result == SceneEditResult.NoChange
            ? "적용할 실제 Action 변경이 없습니다."
            : "Action 변경이 오래되었거나 같은 시각의 키프레임과 충돌했습니다.");
    }

    private void HandleLockResult(SceneEditResult result)
    {
        if (result == SceneEditResult.Applied)
        {
            ClearLockError();
            return;
        }

        ShowLockError(result == SceneEditResult.NoChange
            ? "적용할 실제 Lock-on 변경이 없습니다."
            : "Lock-on 변경이 오래되었거나 시각·대상 제약과 충돌했습니다.");
    }

    private void RefreshPresentation()
    {
        RefreshSectionVisibility();
        RefreshActionInputs();
        RefreshLockInputs();
    }

    private void RefreshSectionVisibility()
    {
        _transformSection.Visible = _session.ActiveTimelineTrack == TimelineTrackKind.Transform;
        _actionSection.Visible = _session.ActiveTimelineTrack == TimelineTrackKind.Action;
        _lockOnSection.Visible = _session.ActiveTimelineTrack == TimelineTrackKind.LockOn;
    }

    private void RefreshActionInputs()
    {
        var frame = _session.GetSelectedActionKeyframe();
        var canUpdate = frame is not null && _session.ActionEditAvailability.CanUpdate;
        var inputsEnabled = canUpdate || _session.ActionEditAvailability.CanAdd;
        if (frame is null)
        {
            _actionSelectionLabel.Text = "선택된 Action 키프레임: 없음";
            _actionTimeInput.Value = _session.Playback.CurrentTimeSeconds;
            _actionKeyInput.Text = string.Empty;
        }
        else
        {
            _actionSelectionLabel.Text = $"선택된 Action: {frame.Id} · {frame.TimeSeconds:0.###}초";
            _actionTimeInput.Value = frame.TimeSeconds;
            _actionKeyInput.Text = frame.ActionKey;
        }

        _actionTimeInput.Editable = inputsEnabled;
        _actionKeyInput.Editable = inputsEnabled;
        _actionApplyButton.Disabled = !canUpdate;
    }

    private void RefreshLockInputs()
    {
        var frame = _session.GetSelectedLockOnKeyframe();
        PopulateTargets(frame?.TargetActorId);
        var canUpdate = frame is not null && _session.LockOnEditAvailability.CanUpdate;
        var inputsEnabled = canUpdate || _session.LockOnEditAvailability.CanAdd;
        if (frame is null)
        {
            _lockOnSelectionLabel.Text = "선택된 Lock-on 키프레임: 없음";
            _lockTimeInput.Value = _session.Playback.CurrentTimeSeconds;
            _lockEnabledInput.ButtonPressed = false;
            _lockModeInput.Select((int)LockOnTrackingMode.Continuous);
            _lockYawOffsetInput.Value = 0;
        }
        else
        {
            _lockOnSelectionLabel.Text = $"선택된 Lock-on: {frame.Id} · {frame.TimeSeconds:0.###}초";
            _lockTimeInput.Value = frame.TimeSeconds;
            _lockEnabledInput.ButtonPressed = frame.Enabled;
            _lockModeInput.Select((int)frame.TrackingMode);
            _lockYawOffsetInput.Value = frame.YawOffsetDegrees;
        }

        _lockTimeInput.Editable = inputsEnabled;
        _lockEnabledInput.Disabled = !inputsEnabled;
        _lockTargetInput.Disabled = !inputsEnabled;
        _lockModeInput.Disabled = !inputsEnabled;
        _lockYawOffsetInput.Editable = inputsEnabled;
        _lockApplyButton.Disabled = !canUpdate;
    }

    private void PopulateTargets(string? selectedTargetActorId)
    {
        _lockTargetInput.Clear();
        _lockTargetInput.AddItem("없음");
        var targetIds = _session.ActorDisplayInfos
            .Select(actor => actor.ActorId)
            .Where(actorId => actorId != _session.SelectedActorId)
            .OrderBy(actorId => actorId, StringComparer.Ordinal)
            .ToArray();
        foreach (var targetId in targetIds)
        {
            _lockTargetInput.AddItem(targetId);
        }

        var selectedIndex = selectedTargetActorId is null
            ? 0
            : Array.IndexOf(targetIds, selectedTargetActorId) + 1;
        _lockTargetInput.Select(Math.Max(0, selectedIndex));
    }

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

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) => RefreshPresentation();

    private void OnActionSelectionChanged(object? sender, ActionKeyframeSelectionChangedEventArgs eventArgs) =>
        RefreshPresentation();

    private void OnLockOnSelectionChanged(object? sender, LockOnKeyframeSelectionChangedEventArgs eventArgs) =>
        RefreshPresentation();

    private void OnTimelineAvailabilityChanged(object? sender, TimelineEditAvailabilityChangedEventArgs eventArgs) =>
        RefreshPresentation();

    private void OnPlaybackChanged(object? sender, PlaybackChangedEventArgs eventArgs) => RefreshPresentation();

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs) => RefreshPresentation();

    private void ClearActionError() => _actionErrorLabel.Text = string.Empty;

    private void ShowActionError(string message) => _actionErrorLabel.Text = message;

    private void ClearLockError() => _lockErrorLabel.Text = string.Empty;

    private void ShowLockError(string message) => _lockErrorLabel.Text = message;
}
