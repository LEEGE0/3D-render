namespace PvpGuide.Application.Editing;

public enum SemanticEditIssue
{
    None,
    NoChange,
    ActorSelectionRequired,
    PlaybackActive,
    KeyframeSelectionRequired,
    SelectionTimeMismatch,
    DuplicateTime,
    StalePreimage,
    TimeOutOfRange,
    InvalidActionKey,
    InvalidLockOnTarget,
    InvalidYawOffset,
    InvalidTrackingMode,
    Conflict,
}

public readonly record struct SemanticEditOutcome(
    SceneEditResult Result,
    SemanticEditIssue Issue)
{
    public static SemanticEditOutcome Applied { get; } = new(SceneEditResult.Applied, SemanticEditIssue.None);

    public static SemanticEditOutcome NoChange { get; } = new(SceneEditResult.NoChange, SemanticEditIssue.NoChange);

    public static SemanticEditOutcome Conflict(SemanticEditIssue issue)
    {
        if (issue is SemanticEditIssue.None or SemanticEditIssue.NoChange)
        {
            throw new ArgumentOutOfRangeException(nameof(issue), "A conflict outcome requires a conflict issue.");
        }

        return new SemanticEditOutcome(SceneEditResult.Conflict, issue);
    }
}
