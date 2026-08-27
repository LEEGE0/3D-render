namespace PvpGuide.Application.Sessions;

public sealed record TimelineTrackEditAvailability(
    bool CanAdd,
    string? AddLockReason,
    bool CanUpdate,
    string? UpdateLockReason,
    bool CanDelete,
    string? DeleteLockReason);
