namespace PvpGuide.Application.Sessions;

public sealed class EditAvailabilityChangedEventArgs(bool canEditSelectedTransform, string? editLockReason) : EventArgs
{
    public bool CanEditSelectedTransform { get; } = canEditSelectedTransform;

    public string? EditLockReason { get; } = editLockReason;
}
