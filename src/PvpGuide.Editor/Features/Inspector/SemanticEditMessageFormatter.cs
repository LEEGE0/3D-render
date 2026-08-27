using System.Globalization;
using PvpGuide.Application.Editing;
using PvpGuide.Application.Sessions;

namespace PvpGuide.Editor.Features.Inspector;

public static class SemanticEditMessageFormatter
{
    public static string Format(
        SemanticEditOutcome outcome,
        TimelineTrackKind track,
        string operation,
        double durationSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

        var trackName = track switch
        {
            TimelineTrackKind.Action => "Action",
            TimelineTrackKind.LockOn => "Lock-on",
            _ => throw new ArgumentOutOfRangeException(nameof(track), "A semantic track is required."),
        };
        if (outcome.Result == SceneEditResult.Applied)
        {
            return $"{operation} 완료";
        }

        if (outcome.Result == SceneEditResult.NoChange || outcome.Issue == SemanticEditIssue.NoChange)
        {
            return $"{operation}: 적용할 실제 {trackName} 변경이 없습니다.";
        }

        var reason = outcome.Issue switch
        {
            SemanticEditIssue.ActorSelectionRequired => "배우를 선택해야 편집할 수 있습니다.",
            SemanticEditIssue.PlaybackActive => "재생 중에는 편집할 수 없습니다.",
            SemanticEditIssue.KeyframeSelectionRequired => $"{trackName} 키프레임을 선택해야 합니다.",
            SemanticEditIssue.SelectionTimeMismatch => "선택한 키프레임 시각에서만 편집할 수 있습니다.",
            SemanticEditIssue.DuplicateTime => $"해당 시각에는 이미 {trackName} 키프레임이 있습니다.",
            SemanticEditIssue.StalePreimage => "선택 정보가 오래되어 최신 문서와 충돌했습니다.",
            SemanticEditIssue.TimeOutOfRange =>
                $"시각은 0초 이상 {durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}초 이하여야 합니다.",
            SemanticEditIssue.InvalidActionKey => "ActionKey는 공백일 수 없습니다.",
            SemanticEditIssue.InvalidLockOnTarget =>
                "Lock-on 대상은 같은 문서의 다른 배우여야 하며 활성 상태에는 대상이 필요합니다.",
            SemanticEditIssue.InvalidYawOffset => "Lock-on 방향 오프셋은 유한한 숫자여야 합니다.",
            SemanticEditIssue.InvalidTrackingMode => "지원하는 Lock-on 추적 모드를 선택해야 합니다.",
            SemanticEditIssue.Conflict => "최신 문서 상태와 충돌했습니다.",
            _ => "편집 결과를 적용할 수 없습니다.",
        };
        return $"{operation} 실패: {reason}";
    }

    public static string FormatObserverFailure(string operation, string observerMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(observerMessage);
        return $"{operation}: 변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: {observerMessage}";
    }

    public static bool ShouldHandleObserverFailure(long revisionBefore, long revisionAfter) =>
        revisionAfter > revisionBefore;
}
