using PvpGuide.Application.Editing;
using PvpGuide.Application.Sessions;
using PvpGuide.Editor.Features.Inspector;
using Xunit;

namespace PvpGuide.Editor.Tests;

public sealed class SemanticEditMessageFormatterTests
{
    public static TheoryData<SemanticEditIssue, TimelineTrackKind, string, string> DistinctIssueMessages => new()
    {
        {
            SemanticEditIssue.NoChange,
            TimelineTrackKind.Action,
            "Action 적용",
            "Action 적용: 적용할 실제 Action 변경이 없습니다."
        },
        {
            SemanticEditIssue.DuplicateTime,
            TimelineTrackKind.Action,
            "Action 적용",
            "Action 적용 실패: 해당 시각에는 이미 Action 키프레임이 있습니다."
        },
        {
            SemanticEditIssue.StalePreimage,
            TimelineTrackKind.Action,
            "Action 적용",
            "Action 적용 실패: 선택 정보가 오래되어 최신 문서와 충돌했습니다."
        },
        {
            SemanticEditIssue.TimeOutOfRange,
            TimelineTrackKind.Action,
            "Action 적용",
            "Action 적용 실패: 시각은 0초 이상 10초 이하여야 합니다."
        },
        {
            SemanticEditIssue.InvalidLockOnTarget,
            TimelineTrackKind.LockOn,
            "Lock-on 적용",
            "Lock-on 적용 실패: Lock-on 대상은 같은 문서의 다른 배우여야 하며 활성 상태에는 대상이 필요합니다."
        },
        {
            SemanticEditIssue.InvalidTrackingMode,
            TimelineTrackKind.LockOn,
            "Lock-on 적용",
            "Lock-on 적용 실패: 지원하는 Lock-on 추적 모드를 선택해야 합니다."
        },
    };

    [Theory]
    [MemberData(nameof(DistinctIssueMessages))]
    public void Formatter_gives_semantic_issues_distinct_korean_messages(
        SemanticEditIssue issue,
        TimelineTrackKind track,
        string operation,
        string expected)
    {
        var outcome = issue == SemanticEditIssue.NoChange
            ? SemanticEditOutcome.NoChange
            : SemanticEditOutcome.Conflict(issue);

        Assert.Equal(expected, SemanticEditMessageFormatter.Format(outcome, track, operation, 10));
    }

    [Fact]
    public void Formatter_distinguishes_mutation_after_observer_failure()
    {
        Assert.Equal(
            "Lock-on 추가: 변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: observer failed",
            SemanticEditMessageFormatter.FormatObserverFailure("Lock-on 추가", "observer failed"));
    }

    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(5, 5, false)]
    [InlineData(5, 4, false)]
    public void Observer_failure_policy_handles_only_revision_increase(
        long revisionBefore,
        long revisionAfter,
        bool expected)
    {
        Assert.Equal(
            expected,
            SemanticEditMessageFormatter.ShouldHandleObserverFailure(revisionBefore, revisionAfter));
    }
}
