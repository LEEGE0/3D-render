namespace PvpGuide.Application.Sessions;

public sealed record ActorDisplayInfo
{
    public ActorDisplayInfo(string actorId, string displayName, string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ActorId = actorId;
        DisplayName = displayName;
        Role = role;
    }

    public string ActorId { get; }

    public string DisplayName { get; }

    public string Role { get; }
}
