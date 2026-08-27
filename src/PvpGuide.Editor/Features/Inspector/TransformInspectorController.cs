using Godot;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Sessions;
using PvpGuide.Domain;

namespace PvpGuide.Editor.Features.Inspector;

public sealed class TransformInspectorController : IDisposable
{
    private readonly DocumentSession _session;
    private readonly Label _selectedActorLabel;
    private readonly Label _errorLabel;
    private readonly SpinBox _xInput;
    private readonly SpinBox _yInput;
    private readonly SpinBox _zInput;
    private readonly SpinBox _yawInput;
    private readonly Button _applyButton;
    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly SpinBox[] _inputs;
    private bool _updatingInputs;
    private bool _hasActivePreview;
    private bool _preserveInvalidInputsDuringPreviewClear;
    private bool _disposed;

    public TransformInspectorController(
        DocumentSession session,
        Label selectedActorLabel,
        Label errorLabel,
        SpinBox xInput,
        SpinBox yInput,
        SpinBox zInput,
        SpinBox yawInput,
        Button applyButton,
        Button undoButton,
        Button redoButton)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _selectedActorLabel = selectedActorLabel ?? throw new ArgumentNullException(nameof(selectedActorLabel));
        _errorLabel = errorLabel ?? throw new ArgumentNullException(nameof(errorLabel));
        _xInput = xInput ?? throw new ArgumentNullException(nameof(xInput));
        _yInput = yInput ?? throw new ArgumentNullException(nameof(yInput));
        _zInput = zInput ?? throw new ArgumentNullException(nameof(zInput));
        _yawInput = yawInput ?? throw new ArgumentNullException(nameof(yawInput));
        _applyButton = applyButton ?? throw new ArgumentNullException(nameof(applyButton));
        _undoButton = undoButton ?? throw new ArgumentNullException(nameof(undoButton));
        _redoButton = redoButton ?? throw new ArgumentNullException(nameof(redoButton));
        _inputs = [_xInput, _yInput, _zInput, _yawInput];

        ConfigureInput(_xInput, -1000, 1000);
        ConfigureInput(_zInput, -1000, 1000);
        ConfigureInput(_yInput, -100, 100);
        ConfigureInput(_yawInput, -360000, 360000);

        foreach (var input in _inputs)
        {
            input.ValueChanged += OnValueChanged;
            input.GetLineEdit().TextSubmitted += OnTextSubmitted;
        }

        _applyButton.Pressed += OnApplyPressed;
        _undoButton.Pressed += OnUndoPressed;
        _redoButton.Pressed += OnRedoPressed;
        _session.SelectionChanged += OnSelectionChanged;
        _session.PreviewChanged += OnPreviewChanged;
        _session.HistoryChanged += OnHistoryChanged;
        _session.SnapshotSource.Changed += OnDocumentChanged;
        RefreshCommittedValues();
        UpdateButtonStates();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var input in _inputs)
        {
            input.ValueChanged -= OnValueChanged;
            input.GetLineEdit().TextSubmitted -= OnTextSubmitted;
        }

        _applyButton.Pressed -= OnApplyPressed;
        _undoButton.Pressed -= OnUndoPressed;
        _redoButton.Pressed -= OnRedoPressed;
        _session.SelectionChanged -= OnSelectionChanged;
        _session.PreviewChanged -= OnPreviewChanged;
        _session.HistoryChanged -= OnHistoryChanged;
        _session.SnapshotSource.Changed -= OnDocumentChanged;
        _disposed = true;
    }

    private static void ConfigureInput(SpinBox input, double minimum, double maximum)
    {
        input.MinValue = minimum;
        input.MaxValue = maximum;
        input.Step = 0.1;
        input.AllowGreater = true;
        input.AllowLesser = true;
    }

    private void OnValueChanged(double value)
    {
        if (_updatingInputs)
        {
            return;
        }

        if (_session.SelectedActorId is null)
        {
            ShowError("변환을 편집하려면 먼저 배우를 선택하세요.");
            RefreshCommittedValues();
            return;
        }

        if (!TryReadInputs(out var position, out var yawDegrees))
        {
            return;
        }

        try
        {
            if (!_hasActivePreview)
            {
                _session.BeginPreview();
                _hasActivePreview = true;
            }

            _session.UpdatePreview(position, yawDegrees);
            ClearError();
        }
        catch (ArgumentException exception)
        {
            CancelPreviewAfterError($"변환 값이 올바르지 않습니다: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            CancelPreviewAfterError($"선택한 배우가 더 이상 유효하지 않습니다: {exception.Message}");
        }
    }

    private void OnTextSubmitted(string text) => CommitPreview();

    private void OnApplyPressed() => CommitPreview();

    private void OnUndoPressed()
    {
        CancelActivePreview();
        if (!_session.Undo())
        {
            ShowError("실행 취소할 변경이 없거나 최신 문서 상태와 충돌했습니다.");
        }
        else
        {
            ClearError();
        }

    }

    private void OnRedoPressed()
    {
        CancelActivePreview();
        if (!_session.Redo())
        {
            ShowError("다시 실행할 변경이 없거나 최신 문서 상태와 충돌했습니다.");
        }
        else
        {
            ClearError();
        }

    }

    private void CommitPreview()
    {
        if (_session.SelectedActorId is null)
        {
            ShowError("변환을 적용하려면 먼저 배우를 선택하세요.");
            return;
        }

        if (!TryReadInputs(out var position, out var yawDegrees))
        {
            return;
        }

        try
        {
            if (!_hasActivePreview)
            {
                _session.BeginPreview();
                _hasActivePreview = true;
            }

            _session.UpdatePreview(position, NormalizeYaw(yawDegrees));
            var changed = _session.CommitPreview();
            _hasActivePreview = false;
            if (!changed)
            {
                ShowError("적용할 실제 변환 변경이 없습니다.");
            }
            else
            {
                ClearError();
            }
        }
        catch (ArgumentException exception)
        {
            CancelPreviewAfterError($"변환 값을 적용할 수 없습니다: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            CancelPreviewAfterError($"선택한 배우의 변경이 오래되어 적용할 수 없습니다: {exception.Message}");
        }

    }

    private bool TryReadInputs(out Position3 position, out double yawDegrees)
    {
        position = default;
        yawDegrees = _yawInput.Value;
        var x = _xInput.Value;
        var y = _yInput.Value;
        var z = _zInput.Value;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || !double.IsFinite(yawDegrees))
        {
            RejectInvalidInput("좌표와 방향각은 유한한 숫자여야 합니다.");
            return false;
        }

        if (x is < -1000 or > 1000 || z is < -1000 or > 1000 || y is < -100 or > 100)
        {
            RejectInvalidInput("X/Z는 ±1000, Y는 ±100 범위 안이어야 합니다.");
            return false;
        }

        position = new Position3(x, y, z);
        return true;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        _hasActivePreview = false;
        ClearError();
        RefreshCommittedValues();
    }

    private void OnPreviewChanged(object? sender, TransformPreviewChangedEventArgs eventArgs)
    {
        if (eventArgs.Preview is null)
        {
            _hasActivePreview = false;
            if (!_preserveInvalidInputsDuringPreviewClear)
            {
                RefreshCommittedValues();
            }

            return;
        }

        _hasActivePreview = true;
        if (eventArgs.Preview.ActorId == _session.SelectedActorId)
        {
            SetInputs(eventArgs.Preview.Position, eventArgs.Preview.YawDegrees);
            ClearError();
        }
    }

    private void OnDocumentChanged(object? sender, SceneDocumentChangedEventArgs eventArgs)
    {
        if (!_hasActivePreview)
        {
            RefreshCommittedValues();
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs eventArgs) => UpdateButtonStates();

    private void RefreshCommittedValues()
    {
        var actorId = _session.SelectedActorId;
        if (actorId is null)
        {
            _selectedActorLabel.Text = "선택된 배우: 없음";
            SetInputsEnabled(false);
            return;
        }

        try
        {
            var transform = _session.GetSelectedTransform()
                ?? throw new InvalidOperationException("선택한 배우의 최초 변환 키프레임이 없습니다.");
            _selectedActorLabel.Text = $"선택된 배우: {actorId} (최초 키프레임)";
            SetInputs(transform.Position, transform.YawDegrees);
            SetInputsEnabled(true);
        }
        catch (InvalidOperationException exception)
        {
            _selectedActorLabel.Text = "선택된 배우: 유효하지 않음";
            SetInputsEnabled(false);
            ShowError($"선택한 배우가 더 이상 유효하지 않습니다: {exception.Message}");
        }
    }

    private void SetInputs(Position3 position, double yawDegrees)
    {
        _updatingInputs = true;
        try
        {
            _xInput.Value = position.X;
            _yInput.Value = position.Y;
            _zInput.Value = position.Z;
            _yawInput.Value = NormalizeYaw(yawDegrees);
        }
        finally
        {
            _updatingInputs = false;
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        foreach (var input in _inputs)
        {
            input.Editable = enabled;
        }

        _applyButton.Disabled = !enabled;
    }

    private void UpdateButtonStates()
    {
        _undoButton.Disabled = !_session.CanUndo;
        _redoButton.Disabled = !_session.CanRedo;
    }

    private void CancelActivePreview()
    {
        if (_hasActivePreview)
        {
            _session.CancelPreview();
            _hasActivePreview = false;
        }
    }

    private void CancelPreviewAfterError(string message)
    {
        CancelActivePreview();
        ShowError(message);
    }

    private void RejectInvalidInput(string message)
    {
        _preserveInvalidInputsDuringPreviewClear = true;
        try
        {
            CancelActivePreview();
        }
        finally
        {
            _preserveInvalidInputsDuringPreviewClear = false;
        }

        ShowError(message);
    }

    private void ClearError() => _errorLabel.Text = string.Empty;

    private void ShowError(string message) => _errorLabel.Text = message;

    private static double NormalizeYaw(double yawDegrees)
    {
        var normalized = yawDegrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
