# Transform Keyframe CRUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 선택 배우의 임의 시점 transform keyframe을 추가·선택·시간/pose 수정·삭제하고 모든 동작을 Undo/Redo하는 Godot 편집 흐름을 완성한다.

**Architecture:** Domain은 immutable `ActorTrack` 재구성으로 update/remove 불변조건을 소유하고 Application Command가 stale preimage와 history를 조정한다. `DocumentSession`은 저장되지 않는 keyframe selection과 CRUD 가능 상태를 소유하며, Editor는 순수 marker layout을 사용하는 Godot surface·timeline·Inspector adapter로만 동작한다.

**Tech Stack:** Windows 11, Godot 4.7.2 Stable .NET, C#/.NET 8, Forward+, xUnit, PowerShell, 오프라인 독립 실행

**Spec:** `docs/superpowers/specs/2026-08-27-transform-keyframe-crud-design.md`

## Global Constraints

- 모든 프로젝트·도구·캐시·출력은 `D:\3D-render` 아래에서 관리한다.
- 런타임 네트워크 의존성, 원격 분석, 자동 업로드와 게임패드 입력을 추가하지 않는다.
- 게임 설치 파일과 추출 자산을 읽거나 수정하지 않는다.
- transform keyframe ID는 update 중 불변이고, actor별 transform time과 ID는 각각 유일하다.
- actor에는 항상 transform keyframe이 최소 한 개 남아야 한다.
- 성공 mutation만 revision/event/history를 한 번씩 전이하며 실패·no-op은 상태를 바꾸지 않는다.
- 모든 영구 편집은 `DocumentSession` 공개 API와 `ISceneEditCommand`를 통한다.
- preview·playback·selection은 저장 문서가 아닌 세션 상태다.
- 하위 에이전트는 커밋·푸시하지 않으며 메인 에이전트가 정확한 경로만 스테이징한다.
- 기존 exact runtime marker 문자열은 변경하지 않는다.

---

### Task 1: Domain transform update/delete 계약

**Files:**
- Modify: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`

**Interfaces:**
- Consumes: 기존 `TransformKeyframe`, `ActorTrack` 정렬·유일성 생성자, `SceneDocument.RaiseChanged()`
- Produces: `ActorTrack.UpdateTransformKeyframe(expectedCurrent, replacement)`, `ActorTrack.RemoveTransformKeyframe(expectedCurrent)`, `SceneDocument.UpdateTransformKeyframe(actorId, expectedCurrent, replacement)`, `SceneDocument.RemoveTransformKeyframe(actorId, expectedCurrent)`

- [ ] **Step 1: update/time-move RED 테스트를 작성한다**

  `SceneDocumentTests`에 time 4 keyframe을 time 3과 새 pose로 바꾸는 성공 테스트를 추가한다. ID 유지, `[0,3]` 정렬, t=2 보간, revision/event 정확히 1회를 assertion한다.

```csharp
var before = document.GetTransformKeyframe("host", "host-second");
var after = new TransformKeyframe(before.Id, 3, new Position3(8, 4, 6), 120);
var changed = document.UpdateTransformKeyframe("host", before, after);
Assert.True(changed);
Assert.Equal([0d, 3d], document.Actors.Single(a => a.ActorId == "host")
    .TransformKeyframes.Select(frame => frame.TimeSeconds));
Assert.Equal(1, document.Revision);
Assert.Equal(1, notifications);
```

- [ ] **Step 2: update RED를 확인한다**

  Run: `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~UpdateTransformKeyframe"`

  Expected: FAIL — `SceneDocument.UpdateTransformKeyframe`가 정의되지 않음.

- [ ] **Step 3: 최소 update 구현을 추가한다**

```csharp
public ActorTrack UpdateTransformKeyframe(
    TransformKeyframe expectedCurrent,
    TransformKeyframe replacement)
{
    if (replacement.Id != expectedCurrent.Id)
        throw new ArgumentException("Replacement identity must remain unchanged.", nameof(replacement));
    var current = GetTransformKeyframe(expectedCurrent.Id);
    ValidateExpected(current, expectedCurrent);
    return new ActorTrack(ActorId, DisplayName, Role,
        _transformKeyframes.Select(frame => frame.Id == current.Id ? replacement : frame),
        _actionKeyframes, _lockOnKeyframes);
}
```

  `SceneDocument.UpdateTransformKeyframe`는 replacement time을 `EnsureTimeWithinDocument`로 검증하고 same transform이면 `false`, 아니면 actor를 교체한 뒤 `RaiseChanged()`와 `true`를 반환한다. 기존 `ReplaceTransformKeyframe`은 time 동일 검증 후 새 update API를 호출한다.

- [ ] **Step 4: update 충돌·원자성 테스트를 추가한다**

  다음 각각에서 actor 인스턴스, revision, notification이 변하지 않는지 검사한다.

```csharp
Assert.Throws<ArgumentException>(() => document.UpdateTransformKeyframe("host", before,
    new TransformKeyframe("changed-id", 3, before.Position, before.YawDegrees)));
Assert.Throws<ArgumentException>(() => document.UpdateTransformKeyframe("host", before,
    new TransformKeyframe(before.Id, duplicateTime, before.Position, before.YawDegrees)));
Assert.Throws<ArgumentOutOfRangeException>(() => document.UpdateTransformKeyframe("host", before,
    new TransformKeyframe(before.Id, document.DurationSeconds + 1, before.Position, before.YawDegrees)));
Assert.Throws<InvalidOperationException>(() => document.UpdateTransformKeyframe("host", stale, after));
```

  동일 ID/time/position/정규화 yaw는 `false`와 event 0회를 assertion한다.

- [ ] **Step 5: delete RED 테스트를 작성하고 확인한다**

```csharp
document.RemoveTransformKeyframe("host", second);
Assert.Single(document.Actors.Single(a => a.ActorId == "host").TransformKeyframes);
Assert.Equal(1, document.Revision);
Assert.Throws<InvalidOperationException>(() => document.RemoveTransformKeyframe("host", remaining));
```

  Run: `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~RemoveTransformKeyframe"`

  Expected: FAIL — remove API가 정의되지 않음.

- [ ] **Step 6: 최소 delete 구현과 stale 검증을 추가한다**

```csharp
public ActorTrack RemoveTransformKeyframe(TransformKeyframe expectedCurrent)
{
    var current = GetTransformKeyframe(expectedCurrent.Id);
    ValidateExpected(current, expectedCurrent);
    if (_transformKeyframes.Count == 1)
        throw new InvalidOperationException("An actor must keep at least one transform keyframe.");
    return new ActorTrack(ActorId, DisplayName, Role,
        _transformKeyframes.Where(frame => frame.Id != current.Id),
        _actionKeyframes, _lockOnKeyframes);
}
```

  `SceneDocument.RemoveTransformKeyframe`는 actor와 전체 expected를 검증하고 actor를 교체한 뒤 event를 한 번 발생시킨다. stale expected, 없는 actor/keyframe, 마지막 하나 삭제 실패의 mutation 0을 테스트한다.

- [ ] **Step 7: Domain 전체 회귀를 실행한다**

  Run: `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo`

  Expected: 모든 Domain 테스트 PASS.

- [ ] **Step 8: 메인 에이전트가 Task 1을 검토·커밋·푸시한다**

  Exact stage paths: 위 세 파일.

  Commit: `feat: 변환 키프레임 시간 수정과 삭제 계약 추가`

---

### Task 2: Godot 독립 transform marker layout

**Files:**
- Create: `src/PvpGuide.Editor/Features/Timeline/TransformTrackLayout.cs`
- Test: `tests/PvpGuide.Editor.Tests/TransformTrackLayoutTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`

**Interfaces:**
- Consumes: document duration, control width, `IEnumerable<(string Id, double TimeSeconds)>`
- Produces: `TransformTrackMarker`, `TransformTrackLayout.CreateMarkers(...)`, `TransformTrackLayout.HitTest(...)`

- [ ] **Step 1: marker 좌표·hit-test RED 테스트를 작성한다**

```csharp
var markers = TransformTrackLayout.CreateMarkers(
    durationSeconds: 10,
    width: 200,
    horizontalPadding: 10,
    [("start", 0d), ("middle", 5d), ("end", 10d)]);
Assert.Equal([10d, 100d, 190d], markers.Select(marker => marker.X));
Assert.Equal("middle", TransformTrackLayout.HitTest(markers, pointerX: 104, hitRadius: 6));
```

  경계 밖 time, 비유한 duration/width/padding, 음수 hit radius는 예외를 assertion한다. 두 marker가 같은 거리면 time, ID ordinal 순으로 선택되는 테스트도 추가한다.

- [ ] **Step 2: layout RED를 확인한다**

  Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~TransformTrackLayout"`

  Expected: FAIL — layout 타입이 정의되지 않음.

- [ ] **Step 3: Godot 독립 layout을 최소 구현한다**

```csharp
public sealed record TransformTrackMarker(string Id, double TimeSeconds, double X);

public static IReadOnlyList<TransformTrackMarker> CreateMarkers(
    double durationSeconds,
    double width,
    double horizontalPadding,
    IEnumerable<(string Id, double TimeSeconds)> keyframes);

public static string? HitTest(
    IReadOnlyList<TransformTrackMarker> markers,
    double pointerX,
    double hitRadius);
```

  파일에는 `Godot`, `Node`, `Control`, `Vector2` 참조를 넣지 않는다. duration이 0이면 모든 marker를 가운데가 아닌 left padding에 둔다.

- [ ] **Step 4: skeleton 구조 검사를 갱신한다**

  `Test-ProjectSkeleton.ps1`의 required file 목록에 layout과 test를 추가하고 `Assert-NotContains`로 Godot 타입 독립성을 검사한다. 사람용 한글 문장 grep은 추가하지 않는다.

- [ ] **Step 5: Editor 테스트와 skeleton을 실행한다**

  Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo`

  Run: `& .\scripts\Test-ProjectSkeleton.ps1`

  Expected: Editor PASS, `PROJECT_SKELETON_VERIFICATION=PASS`.

- [ ] **Step 6: 메인 에이전트가 Task 2를 검토·커밋·푸시한다**

  Exact stage paths: 위 세 파일.

  Commit: `feat: 타임라인 키프레임 마커 좌표 계산 추가`

---

### Task 3: Application Command와 keyframe selection 세션

**Files:**
- Create: `src/PvpGuide.Application/Commands/AddTransformKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/UpdateTransformKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/RemoveTransformKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Sessions/TransformKeyframeSelectionChangedEventArgs.cs`
- Modify: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Modify: `tests/PvpGuide.Application.Tests/DocumentSessionTests.cs`

**Interfaces:**
- Consumes: Task 1의 Domain CRUD, 기존 `ISceneEditCommand`, `SceneEditResult`, `PlaybackClock`, preview/history 예외 전이
- Produces: `SelectedTransformKeyframeId`, `TransformKeyframeSelectionChanged`, `GetSelectedActorTransformKeyframes()`, `SelectTransformKeyframe(...)`, `CanAddTransformKeyframe`, `AddTransformKeyframeLockReason`, `CanDeleteSelectedTransformKeyframe`, `DeleteTransformKeyframeLockReason`, `AddTransformKeyframeAtCurrentTime()`, `UpdateSelectedTransformKeyframe(...)`, `RemoveSelectedTransformKeyframe()`

- [ ] **Step 1: Command execute/undo/redo RED 테스트를 세션 공개 API로 작성한다**

  paused t=2에서 평가 pose로 Add하고 다음을 assertion한다.

```csharp
Assert.Equal(SceneEditResult.Applied, session.AddTransformKeyframeAtCurrentTime());
var added = Assert.IsType<TransformKeyframe>(session.GetSelectedTransform());
Assert.Equal(2, added.TimeSeconds);
Assert.Equal(document.CreateSnapshot(2).ActorTransforms["host"].Position, added.Position);
Assert.True(session.Undo());
Assert.DoesNotContain(document.Actors.Single(a => a.ActorId == "host").TransformKeyframes,
    frame => frame.Id == added.Id);
Assert.True(session.Redo());
```

  update time/pose와 delete도 각각 Execute→Undo→Redo, revision 증가, history 이동을 검증한다.

- [ ] **Step 2: Application RED를 확인한다**

  Run: `dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Transform_keyframe"`

  Expected: FAIL — 새 세션 API가 정의되지 않음.

- [ ] **Step 3: 세 Command를 최소 구현한다**

```csharp
internal sealed class AddTransformKeyframeCommand(string actorId, TransformKeyframe keyframe)
    : ISceneEditCommand
{
    public bool Execute(SceneDocument document) { document.AddKeyframe(actorId, keyframe); return true; }
    public bool Undo(SceneDocument document) { document.RemoveTransformKeyframe(actorId, keyframe); return true; }
}
```

  Update는 `before→after`와 `after→before`, Remove는 remove와 add를 대칭으로 구현한다. 모든 생성자 인수를 null/공백 검증한다.

- [ ] **Step 4: selection 상태와 가능 상태를 구현한다**

```csharp
public string? SelectedTransformKeyframeId { get; private set; }
public bool CanAddTransformKeyframe { get; private set; }
public string? AddTransformKeyframeLockReason { get; private set; }
public bool CanDeleteSelectedTransformKeyframe { get; private set; }
public string? DeleteTransformKeyframeLockReason { get; private set; }
public event EventHandler<TransformKeyframeSelectionChangedEventArgs>? TransformKeyframeSelectionChanged;
```

```csharp
public sealed class TransformKeyframeSelectionChangedEventArgs(
    string? actorId,
    string? keyframeId,
    TransformKeyframe? keyframe) : EventArgs
{
    public string? ActorId { get; } = actorId;
    public string? KeyframeId { get; } = keyframeId;
    public TransformKeyframe? Keyframe { get; } = keyframe;
}
```

  actor 선택 시 현재 playback time의 exact keyframe만 선택한다. keyframe 선택 시 actor 내부 ID를 검증하고 기본적으로 pause→seek한다. `GetSelectedTransform()`은 첫 keyframe이 아니라 선택 ID를 조회한다. availability tolerance는 기존 `1e-9`를 재사용한다.

- [ ] **Step 5: Add/Update/Delete 공개 API를 구현한다**

  Add는 current snapshot pose와 충돌 없는 deterministic ordinal ID를 사용한다. Update는 selected full preimage와 새 time/pose로 Command를 실행하고 성공 후 새 time으로 seek한다. Delete는 최소 개수 guard 후 Command를 실행하고 nearest remaining을 `(abs(time-deletedTime), time, id)` 순으로 선택·seek한다.

- [ ] **Step 6: selection·guard·conflict 테스트를 추가한다**

  다음을 각각 문서/history/selection assertion과 함께 검사한다.

- marker에 해당하는 keyframe 선택이 pause와 seek를 수행한다.
- selection event args는 actor/keyframe ID와 full keyframe을 가진다.
- 재생 중 Add/Update/Delete는 `Conflict`이고 mutation 0이다.
- 같은 current time Add, 범위 밖 update time, 마지막 keyframe Delete는 `Conflict`다.
- 외부 Domain update 뒤 stale preview/update/delete는 history를 만들지 않는다.
- delete 후 nearest tie는 더 이른 time을 선택한다.
- Undo/Redo 후 `SelectedTransformKeyframeId`가 실제 문서에 존재한다.
- playback seek/play와 actor 전환 전에 active preview가 clear된다.
- observer 예외가 mutation 후 발생하면 document revision과 history transition이 완료된다.

- [ ] **Step 7: 기존 최초 keyframe 편집 회귀를 조정한다**

  t=0 actor 선택 시 exact 첫 keyframe이 자동 선택되어 기존 Move/Rotate/Inspector 테스트가 같은 동작을 유지해야 한다. 중간 보간 시각은 keyframe 미선택 또는 selected time 불일치로 pose 편집이 잠기고 Add만 가능해야 한다.

- [ ] **Step 8: Application 전체 회귀를 실행한다**

  Run: `dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo`

  Expected: 모든 Application 테스트 PASS.

- [ ] **Step 9: 메인 에이전트가 Task 3을 검토·커밋·푸시한다**

  Exact stage paths: 위 여섯 파일.

  Commit: `feat: 키프레임 CRUD 명령과 세션 선택 상태 추가`

---

### Task 4: Godot marker·toolbar·Inspector 조립

**Files:**
- Create: `src/PvpGuide.Editor/Features/Timeline/TransformTrackSurface.cs`
- Modify: `src/PvpGuide.Editor/Features/Timeline/TimelineController.cs`
- Modify: `src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs`
- Verify only: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.tscn`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`

**Interfaces:**
- Consumes: Task 2 layout, Task 3 session selection/CRUD/availability API
- Produces: 실제 marker draw/hit, Add/Delete toolbar, TimeInput을 포함한 선택 keyframe Inspector, UI signal 기반 CRUD 흐름

- [ ] **Step 1: Main scene 구조 RED를 만든다**

  `Test-ProjectSkeleton.ps1`에서 다음 node와 파일 존재를 검사한다.

```text
TransformTrackSurface
KeyframeToolbar
AddKeyframeButton
DeleteKeyframeButton
SelectedKeyframeLabel
TimeInput
```

  Run: `& .\scripts\Test-ProjectSkeleton.ps1`

  Expected: FAIL — 새 파일/node가 없음.

- [ ] **Step 2: scene node와 surface 골격을 추가한다**

  `Main.tscn`에 marker surface 최소 높이 44, toolbar 버튼 두 개, Inspector label/time SpinBox를 추가한다. `TransformTrackSurface`는 session을 Attach/Detach하고 `_Draw()`에서 선·다이아몬드·선택 강조를 그리며 `_GuiInput()`에서 실제 left-click을 layout `HitTest`로 전달한다.

```csharp
public void Attach(DocumentSession session);
public void Detach();
public override void _Draw();
public override void _GuiInput(InputEvent @event);
```

  document Changed, actor/keyframe selection, playback Changed에서 `QueueRedraw()`하고 `_ExitTree()` 이전에 Detach한다.

- [ ] **Step 3: TimelineController에 Add/Delete와 selection presentation을 연결한다**

  생성자에 surface와 두 버튼을 추가하고 signal을 구독한다. button state는 `CanAddTransformKeyframe`과 `CanDeleteSelectedTransformKeyframe`만 사용한다. 비활성화·Conflict 문구는 `AddTransformKeyframeLockReason`과 `DeleteTransformKeyframeLockReason`을 표시한다. playback/selection/document 변경 뒤 marker와 state를 갱신한다.

- [ ] **Step 4: Inspector를 선택 keyframe과 TimeInput 기준으로 바꾼다**

  생성자에 `SelectedKeyframeLabel`, `TimeInput`을 추가한다. `RefreshCommittedValues()`는 selected keyframe을 표시하고 없으면 입력을 비활성화한다. pose ValueChanged만 preview를 갱신하고 TimeInput ValueChanged는 preview를 시작하지 않는다. Apply/Enter는 time/pose를 `UpdateSelectedTransformKeyframe` 한 번으로 확정한다.

```csharp
var result = _session.UpdateSelectedTransformKeyframe(
    _timeInput.Value,
    new Position3(_xInput.Value, _yInput.Value, _zInput.Value),
    NormalizeYaw(_yawInput.Value));
```

  range 오류, same-value, stale conflict, mutation-after-observer 메시지를 기존 패턴으로 구분한다.

- [ ] **Step 5: TopView가 session selection만 소비하는지 기계적으로 검증한다**

  Run: `rg -n "TransformKeyframes|GetTransformKeyframe|최초" src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`

  Expected: 출력 없음. 현재 `TopViewSurface`는 `CanEditSelectedTransform`과 `GetSelectedTransform()`만 사용하므로 파일을 수정하지 않는다. 출력이 있으면 Task 4를 시작하지 말고 설계와 현재 소스의 불일치를 메인 에이전트에게 보고한다.

- [ ] **Step 6: Main 조립과 수명주기를 갱신한다**

  `_Ready()`의 exact node lookup, controller 생성, surface Attach 순서를 추가한다. 실패 시 생성된 controller/surface를 역순 정리하고 `_ExitTree()`에서 event와 Godot signal을 모두 해제한다. 기존 Space `_Input`, playback `_Process`, projection 조립은 바꾸지 않는다.

- [ ] **Step 7: skeleton과 Editor 회귀를 실행한다**

  Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo`

  Run: `& .\scripts\Test-ProjectSkeleton.ps1`

  Expected: Editor PASS, skeleton PASS.

- [ ] **Step 8: 메인 에이전트가 Task 4를 검토·커밋·푸시한다**

  Exact stage paths: 생성·수정된 여섯 파일.

  Commit: `feat: 타임라인 마커와 키프레임 편집 UI 연결`

---

### Task 5: 결정적 CRUD 런타임 검증과 한글 문서

**Files:**
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-GodotRuntime.ps1`
- Modify: `README.md`
- Modify: `docs/03-system-architecture.md`
- Modify: `docs/04-data-architecture.md`
- Modify: `docs/05-editor-architecture.md`
- Modify: `docs/13-roadmap.md`
- Modify: `docs/superpowers/plans/2026-08-27-transform-keyframe-crud.md`

**Interfaces:**
- Consumes: Task 1~4의 실제 UI signal과 세션/Domain 계약
- Produces: exact `TIMELINE_KEYFRAME_CRUD_READY ...` marker, 사용자 가이드, 완료 roadmap

- [ ] **Step 1: 실제 UI signal 기반 runtime RED를 추가한다**

  기존 runtime document와 실제 marker surface/controller를 사용해 다음 순서를 hand-derived 값으로 검증한다.

1. t=0.5 scrub 후 Add button signal로 평가 pose `(3,1,-2)`/45° keyframe 생성
2. 생성 marker 실제 click으로 selection/Inspector ID/time/pose 동기화
3. TimeInput 0.6과 pose `(3.5,1.5,-2.5)`/60° Apply로 원자적 update
4. 실제 Undo/Redo 버튼으로 time/pose 왕복
5. Delete button으로 삭제, Undo로 복원, Redo로 재삭제
6. duplicate Add, range 밖 time, 마지막 keyframe delete, stale preimage 불변
7. active preview 뒤 scrub cancel, 재생 중 CRUD/TopView/Inspector lock
8. 각 단계의 revision/history, TopView/WorldView apply count와 동일 snapshot 의미

  exact marker는 모든 assertion 뒤에만 출력한다.

```text
TIMELINE_KEYFRAME_CRUD_READY add=1 update=1 time_move=1 delete=1 undo=1 redo=1 duplicate_reject=1 range_reject=1 min_keyframe_guard=1 stale_conflict=1 selection_sync=1 preview_cancel=1 playback_lock=1
```

- [ ] **Step 2: runtime RED를 확인한다**

  Run: `& .\scripts\Test-GodotRuntime.ps1`

  Expected: FAIL — 새 marker 또는 assertion이 아직 충족되지 않음.

- [ ] **Step 3: runtime probe를 최종 UI 계약에 맞게 완성한다**

  wait, sleep, `_Process` 횟수, process count, test-only production API, source-string assertion을 사용하지 않는다. slider/button/SpinBox/marker의 실제 signal과 `Viewport.PushInput`을 사용한다. 수치는 구현 결과에서 읽어 기대값으로 쓰지 않고 문서 fixture로부터 독립 계산한다.

- [ ] **Step 4: runtime 스크립트 exact marker 검사를 갱신한다**

  기존 exact marker를 그대로 검사한 뒤 새 CRUD marker 한 줄을 exact match한다. build 경고/오류 0과 최종 `GODOT_RUNTIME_VERIFICATION=PASS`를 유지한다.

- [ ] **Step 5: 한글 사용자·아키텍처 문서를 갱신한다**

  README에 marker click→Add→Time/pose Apply→Delete→Undo/Redo 흐름과 오류 문구를 기록한다. system/data/editor architecture에 Domain CRUD, preimage Command, 비영구 selection, marker layout 경계를 기록한다. roadmap의 “다음 구현 단위”를 완료 목록으로 옮기고 다음 단위를 Action/Lock-on track foundation으로 지정한다.

- [ ] **Step 6: 계획 체크박스를 실제 완료 상태로 갱신한다**

  검증되지 않은 항목은 체크하지 않는다. 모든 Task review와 fresh verification이 끝난 뒤에만 이 계획의 남은 checkbox를 `[x]`로 바꾼다.

- [ ] **Step 7: 전체 직렬 검증을 실행한다**

  Run in order:

```powershell
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
```

  Expected: 모든 테스트 PASS, skeleton PASS, runtime exact marker PASS, Godot build 경고 0·오류 0.

- [ ] **Step 8: 최종 diff와 Git 추적 범위를 검토한다**

  `git diff --check`, `git status --short`, `git diff --stat`를 확인한다. `local-assets/`, `tools/`, `cache/`, `exports/`, `.godot/`, 로그와 임시 검토 파일은 추적하지 않는다.

- [ ] **Step 9: 메인 에이전트가 Task 5를 커밋·푸시하고 SHA를 검증한다**

  Exact stage paths: 위 여덟 파일과 리뷰로 승인된 잔여 수정 파일만 개별 지정한다.

  Commit: `docs: 키프레임 CRUD 검증과 사용법 정리`

  Push 후 local HEAD, upstream, `git ls-remote` SHA가 모두 같은지 확인한다.
