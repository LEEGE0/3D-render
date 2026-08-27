namespace PvpGuide.Application.Sessions;

public sealed class SelectionChangedEventArgs(string? selectedActorId) : EventArgs
{
    public string? SelectedActorId { get; } = selectedActorId;
}
