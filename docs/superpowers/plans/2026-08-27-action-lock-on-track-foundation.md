# Action/Lock-on Track Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Action과 Lock-on stepped state를 편집·Undo/Redo하고 같은 시각의 의미 상태를 TopView와 3D에 함께 표시하는 타임라인 기반을 완성한다.

**Architecture:** 기존 transform CRUD는 유지하고 Action·Lock-on에 명시적 Domain/Command/session API를 추가한다. left-hold 평가와 step lane 계산만 순수 공통 helper로 공유하고, `SceneSnapshot`을 semantic state까지 확장해 두 projection consumer가 같은 값을 받게 한다. Lock-on의 offset/tracking mode는 `pvp-guide-scene/2`로 저장하며 `/1`을 명시적으로 migration한다.

**Tech Stack:** Windows 11, Godot 4.7.2 Stable .NET, .NET 8, C#, xUnit, PowerShell

**Spec:** `docs/superpowers/specs/2026-08-27-action-lock-on-track-foundation-design.md`

## Global Constraints

- Windows 11 전용 오프라인 실행을 유지한다.
- Godot 4.7.2 Stable .NET, .NET 8, C#, Forward Plus를 유지한다.
- 프로젝트·도구·캐시·로컬 게임 자산은 D 드라이브 정책을 따른다.
- 실제 DSR 자산, AnimationPlayer, root motion, 전투 판정, 영상 렌더와 게임패드는 구현하지 않는다.
- `SceneDocument`만 영구 상태이며 selection, playback, preview와 history stack은 저장하지 않는다.
- 성공 mutation만 revision/event/history를 바꾼다.
- 기존 transform UI, 189개 baseline 테스트, startup failure probe와 모든 exact runtime marker를 보존한다.
- subagent는 stage/commit/push/tag하지 않는다. 메인 에이전트만 정확한 파일을 stage하고 검증 후 커밋·푸시한다.

Baseline: Domain 26, Application 77, Infrastructure 33, Editor 53 — 총 189 tests PASS at `2fdf5a9`.

---

### Task 1: Domain stepped state와 Action/Lock-on CRUD

**Files:**
- Create: `src/PvpGuide.Domain/Timeline/LockOnTrackingMode.cs`
- Create: `src/PvpGuide.Domain/Timeline/EvaluatedActionState.cs`
- Create: `src/PvpGuide.Domain/Timeline/EvaluatedLockOnState.cs`
- Create: `src/PvpGuide.Domain/Timeline/EvaluatedActorTimelineState.cs`
- Modify: `src/PvpGuide.Domain/Timeline/LockOnKeyframe.cs`
- Modify: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `src/PvpGuide.Domain/SceneSnapshot.cs`
- Modify: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`

**Interfaces:**
- Consumes: 기존 track 정렬·ID/time 유일성, transform immutable replacement, `SceneDocument.RaiseChanged()` 계약.
- Produces: action/lock Add·Update·Remove, left-hold evaluation, `SceneSnapshot.ActorTimelineStates`, v2 생성자가 사용할 offset/mode 포함 `LockOnKeyframe`.

- [ ] **Step 1: stepped evaluation RED 테스트를 작성한다**

```csharp
[Fact]
public void Snapshot_evaluates_action_and_lock_as_left_hold_states()
{
    var document = CreateSemanticDocument();

    var before = document.CreateSnapshot(0.25).ActorTimelineStates["host"];
    Assert.Null(before.Action.ActionKey);
    Assert.False(before.LockOn.Enabled);

    var between = document.CreateSnapshot(1.5).ActorTimelineStates["host"];
    Assert.Equal("attack", between.Action.ActionKey);
    Assert.Equal("host-action-1", between.Action.SourceKeyframeId);
    Assert.True(between.LockOn.Enabled);
    Assert.Equal("invader", between.LockOn.TargetActorId);
    Assert.Equal(LockOnTrackingMode.Continuous, between.LockOn.TrackingMode);
}
```

- [ ] **Step 2: RED를 확인한다**

Run: `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Snapshot_evaluates_action_and_lock"`

Expected: FAIL — evaluated semantic state 타입과 snapshot property가 없음.

- [ ] **Step 3: left-hold 타입과 평가를 최소 구현한다**

```csharp
public enum LockOnTrackingMode { Snap, Continuous, KeyframeOnly }

public sealed record EvaluatedActionState(string? SourceKeyframeId, string? ActionKey);

public sealed record EvaluatedLockOnState(
    string? SourceKeyframeId,
    bool Enabled,
    string? TargetActorId,
    double YawOffsetDegrees,
    LockOnTrackingMode TrackingMode);

public EvaluatedActionState EvaluateAction(double timeSeconds) =>
    EvaluateHeld(_actionKeyframes, timeSeconds) is { } frame
        ? new(frame.Id, frame.ActionKey)
        : new(null, null);
```

`EvaluateLockOn`은 marker가 없으면 `(null,false,null,0,Continuous)`를 반환한다. 공통 helper는 마지막 `TimeSeconds <= t` 항목만 선택하고 빈 트랙을 허용한다.

- [ ] **Step 4: Action CRUD와 stale/no-op RED 테스트를 작성한다**

```csharp
[Fact]
public void Action_update_and_remove_require_full_current_preimage()
{
    var document = CreateSemanticDocument();
    var before = document.GetActionKeyframe("host", "host-action-1");
    var after = new ActionKeyframe(before.Id, 1.25, "roll");

    Assert.True(document.UpdateActionKeyframe("host", before, after));
    Assert.False(document.UpdateActionKeyframe("host", after, after));
    Assert.Throws<InvalidOperationException>(() =>
        document.RemoveActionKeyframe("host", before));
    Assert.Equal(1, document.Revision);
}
```

- [ ] **Step 5: Lock-on CRUD와 target/offset RED 테스트를 작성한다**

```csharp
[Fact]
public void Lock_on_mutation_validates_target_and_normalizes_offset()
{
    var document = CreateSemanticDocument();
    var frame = new LockOnKeyframe(
        "host-lock-2", 2, true, "invader", 190, LockOnTrackingMode.Snap);

    document.AddLockOnKeyframe("host", frame);

    Assert.Equal(-170, document.GetLockOnKeyframe("host", frame.Id).YawOffsetDegrees);
    Assert.Throws<ArgumentException>(() => document.AddLockOnKeyframe(
        "host",
        new LockOnKeyframe("self", 2.5, true, "host", 0, LockOnTrackingMode.Continuous)));
}
```

- [ ] **Step 6: immutable CRUD API를 구현한다**

```csharp
public ActorTrack AddActionKeyframe(ActionKeyframe keyframe);
public ActorTrack UpdateActionKeyframe(
    ActionKeyframe expectedCurrent,
    ActionKeyframe replacement);
public ActorTrack RemoveActionKeyframe(ActionKeyframe expectedCurrent);

public ActorTrack AddLockOnKeyframe(LockOnKeyframe keyframe);
public ActorTrack UpdateLockOnKeyframe(
    LockOnKeyframe expectedCurrent,
    LockOnKeyframe replacement);
public ActorTrack RemoveLockOnKeyframe(LockOnKeyframe expectedCurrent);
```

`SceneDocument`는 actor replacement 전에 range와 lock target을 검증한다. replacement ID는 expected ID와 같아야 한다. action/lock 마지막 marker 삭제는 허용한다. full stale 비교에는 모든 semantic field를 포함한다.

- [ ] **Step 7: snapshot defensive-copy와 경계 회귀를 보강한다**

빈 track, first 이전, exact, between, last 이후, duplicate time/ID, document range, missing/self target, disabled target candidate, nonfinite offset, last delete, actor metadata/transform 보존을 각각 assertion한다.

- [ ] **Step 8: Domain 전체를 실행한다**

Run: `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo`

Expected: 모든 Domain 테스트 PASS.

- [ ] **Step 9: 메인 에이전트가 검토·커밋·푸시한다**

Commit: `feat: Action Lock-on 단계 상태와 CRUD 추가`

---

### Task 2: 저장 schema v2와 v1 migration

**Files:**
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `src/PvpGuide.Infrastructure/Serialization/SceneDocumentSerializer.cs`
- Modify: `src/PvpGuide.Infrastructure/Import/TopviewGuideV1Importer.cs`
- Modify: `tests/PvpGuide.Infrastructure.Tests/SceneRoundTripTests.cs`
- Modify: `tests/PvpGuide.Infrastructure.Tests/TopviewGuideV1ImporterTests.cs`

**Interfaces:**
- Consumes: Task 1 `LockOnKeyframe(..., yawOffsetDegrees, trackingMode)`와 현재 `/1` DTO.
- Produces: `/1` read migration, `/2` strict read/write, importer의 `0/Continuous` 기본값.

- [ ] **Step 1: v1 migration과 v2 round-trip RED를 작성한다**

```csharp
[Fact]
public void Version_one_lock_on_migrates_to_version_two_defaults()
{
    var document = DeserializeFixture("scene-v1.json");
    var frame = document.Actors.Single().LockOnKeyframes.Single();
    Assert.Equal(0, frame.YawOffsetDegrees);
    Assert.Equal(LockOnTrackingMode.Continuous, frame.TrackingMode);
}

[Fact]
public void Serialize_writes_version_two_lock_on_semantics()
{
    var json = _serializer.Serialize(CreateVersionTwoDocument());
    Assert.Contains("\"schema\": \"pvp-guide-scene/2\"", json);
    Assert.Contains("\"trackingMode\": \"keyframe_only\"", json);
    Assert.Contains("\"yawOffsetDegrees\": -15", json);
}
```

- [ ] **Step 2: migration RED를 확인한다**

Run: `dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Version_one|FullyQualifiedName~Version_two"`

Expected: FAIL — 현재 schema는 `/1`만 허용하고 새 필드를 쓰지 않음.

- [ ] **Step 3: schema별 validation과 DTO mapping을 구현한다**

```csharp
private const string LegacySchemaV1 = "pvp-guide-scene/1";
private const string CurrentSchemaV2 = "pvp-guide-scene/2";

private static LockOnTrackingMode ParseTrackingMode(string value) => value switch
{
    "snap" => LockOnTrackingMode.Snap,
    "continuous" => LockOnTrackingMode.Continuous,
    "keyframe_only" => LockOnTrackingMode.KeyframeOnly,
    _ => throw new InvalidDataException($"Unsupported lock-on tracking mode '{value}'."),
};
```

`/1`에서는 missing offset/mode만 기본화한다. `/2`에서는 두 필드를 필수로 검증한다. serializer는 항상 `/2`를 쓰고 enum은 위 snake_case 문자열만 사용한다.

- [ ] **Step 4: strict failure와 atomic load 회귀를 추가한다**

잘못된 enum, null mode, nonfinite offset, unknown member, unsupported schema가 실패하고 deserialize 실패가 기존 파일/문서를 변경하지 않음을 검증한다.

- [ ] **Step 5: importer 기본값과 합성 fixture를 갱신한다**

```csharp
new LockOnKeyframe(
    frameId,
    timeSeconds,
    lockEnabled,
    targetActorId,
    yawOffsetDegrees: 0,
    trackingMode: LockOnTrackingMode.Continuous)
```

serializer 테스트가 메모리에서 만드는 합성 저장 JSON은 `/2`와 명시적 새 필드를 사용한다. `samples/guides/synthetic-topview-v1.scene.json`은 importer 원본 형식이므로 수정하지 않는다.

- [ ] **Step 6: Infrastructure 전체를 실행한다**

Run: `dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo`

Expected: 모든 Infrastructure 테스트 PASS.

- [ ] **Step 7: 메인 에이전트가 검토·커밋·푸시한다**

Commit: `feat: 장면 schema v2와 Lock-on migration 추가`

---

### Task 3: Application 명령·active track selection·shared history

**Files:**
- Create: `src/PvpGuide.Application/Commands/AddActionKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/UpdateActionKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/RemoveActionKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/AddLockOnKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/UpdateLockOnKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Commands/RemoveLockOnKeyframeCommand.cs`
- Create: `src/PvpGuide.Application/Sessions/TimelineTrackKind.cs`
- Create: `src/PvpGuide.Application/Sessions/TimelineTrackEditAvailability.cs`
- Create: `src/PvpGuide.Application/Sessions/ActionKeyframeSelectionChangedEventArgs.cs`
- Create: `src/PvpGuide.Application/Sessions/LockOnKeyframeSelectionChangedEventArgs.cs`
- Create: `src/PvpGuide.Application/Sessions/TimelineEditAvailabilityChangedEventArgs.cs`
- Modify: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Modify: `tests/PvpGuide.Application.Tests/DocumentSessionTests.cs`

**Interfaces:**
- Consumes: Task 1 Domain CRUD/evaluation, 기존 `ISceneEditCommand`, history reconciliation과 최신-payload guard.
- Produces: active track, action/lock selection, CRUD, availability, track-independent paused Undo/Redo.

- [ ] **Step 1: command Execute/Undo/Redo RED를 작성한다**

```csharp
[Fact]
public void Action_and_lock_commands_share_one_monotonic_history()
{
    var session = CreateSemanticSession(out var document);
    session.SelectActor("host");

    Assert.Equal(SceneEditResult.Applied,
        session.AddActionKeyframeAtCurrentTime("attack"));
    Assert.Equal(SceneEditResult.Applied,
        session.AddLockOnKeyframeAtCurrentTime(
            true, "invader", 0, LockOnTrackingMode.Continuous));

    Assert.True(session.Undo());
    Assert.True(session.Undo());
    Assert.True(session.Redo());
    Assert.True(session.Redo());
    Assert.Equal(6, document.Revision);
}
```

- [ ] **Step 2: command RED를 확인한다**

Run: `dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~Action_and_lock_commands"`

Expected: FAIL — semantic commands/session API가 없음.

- [ ] **Step 3: 여섯 command를 transform command와 대칭으로 구현한다**

각 command는 actor ID와 immutable full keyframe을 저장한다. Add Undo는 정확한 postimage를 제거하고, Update/Remove Undo는 full preimage를 복원한다. stale이면 Domain 예외가 `Conflict`/false로 변환된다.

- [ ] **Step 4: selection·가용성 RED를 작성한다**

```csharp
[Fact]
public void Selecting_lock_marker_pauses_seeks_and_activates_lock_track()
{
    var session = CreateSemanticSession(out _);
    session.SelectActor("host");
    session.Playback.Play();

    Assert.Equal(SceneEditResult.Applied, session.SelectLockOnKeyframe("host-lock-1"));

    Assert.False(session.Playback.IsPlaying);
    Assert.Equal(1, session.Playback.CurrentTimeSeconds);
    Assert.Equal(TimelineTrackKind.LockOn, session.ActiveTimelineTrack);
    Assert.Equal("host-lock-1", session.SelectedLockOnKeyframeId);
}
```

- [ ] **Step 5: active track와 availability를 구현한다**

```csharp
public TimelineTrackKind ActiveTimelineTrack { get; private set; }
public TimelineTrackEditAvailability ActionEditAvailability { get; private set; }
public TimelineTrackEditAvailability LockOnEditAvailability { get; private set; }
public bool CanEditHistory => SelectedActorId is not null && !Playback.IsPlaying;
```

`TimelineTrackEditAvailability`는 `CanAdd/AddLockReason`, `CanUpdate/UpdateLockReason`, `CanDelete/DeleteLockReason`를 가진다. selection event payload는 actor ID, keyframe ID와 full immutable frame을 포함한다.

- [ ] **Step 6: action/lock CRUD와 deterministic ID를 구현한다**

```csharp
public SceneEditResult AddActionKeyframeAtCurrentTime(string actionKey);
public SceneEditResult UpdateSelectedActionKeyframe(double timeSeconds, string actionKey);
public SceneEditResult RemoveSelectedActionKeyframe();

public SceneEditResult AddLockOnKeyframeAtCurrentTime(
    bool enabled, string? targetActorId, double yawOffsetDegrees,
    LockOnTrackingMode trackingMode);
```

Update/Remove와 `GetSelectedActionKeyframe()`, `GetSelectedLockOnKeyframe()`, 선택 actor의 두 track read-only getter를 추가한다. ID ordinal은 현재 충돌 없는 가장 작은 양의 D4를 선택하는 transform 정책과 일치시킨다.

- [ ] **Step 7: shared history와 reconciliation을 일반화한다**

Undo/Redo는 active track과 무관하게 paused+actor selected이면 실행한다. transition 뒤 각 트랙은 `preserve ID → exact current → nearest → null`로 최신 문서를 읽는다. transform의 observer exception/reentrancy 회귀를 그대로 유지한다.

- [ ] **Step 8: 적대적 event 테스트를 추가한다**

Action Add와 Lock-on Update/Delete의 `Changed` observer 예외, `HistoryChanged` 내부 Undo/Redo, playback callback 재진입을 만들고 selection ID/full frame/payload/availability가 최종 문서와 일치함을 assertion한다.

- [ ] **Step 9: Application 전체를 실행한다**

Run: `dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo`

Expected: 모든 Application 테스트 PASS.

- [ ] **Step 10: 메인 에이전트가 검토·커밋·푸시한다**

Commit: `feat: Action Lock-on 명령과 세션 선택 추가`

---

### Task 4: 순수 step lane·Godot surface·semantic Inspector

**Files:**
- Create: `src/PvpGuide.Editor/Features/Timeline/StepTrackLayout.cs`
- Create: `src/PvpGuide.Editor/Features/Timeline/ActionTrackSurface.cs`
- Create: `src/PvpGuide.Editor/Features/Timeline/LockOnTrackSurface.cs`
- Create: `src/PvpGuide.Editor/Features/Timeline/SemanticTimelineController.cs`
- Create: `src/PvpGuide.Editor/Features/Inspector/ActionLockOnInspectorController.cs`
- Create: Godot-generated `.cs.uid` sidecars for all five new Editor C# files
- Create: `tests/PvpGuide.Editor.Tests/StepTrackLayoutTests.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.tscn`
- Modify: `scripts/Test-ProjectSkeleton.ps1`

**Interfaces:**
- Consumes: Task 3 selection/CRUD/availability API와 Task 1 stepped semantics.
- Produces: action/lock marker+segment draw/hit, active semantic Inspector, exact scene nodes for Main wiring.

- [ ] **Step 1: pure lane RED 테스트를 작성한다**

```csharp
[Fact]
public void Layout_builds_left_hold_segments_to_next_marker_or_document_end()
{
    var lane = StepTrackLayout.Create(
        durationSeconds: 4,
        width: 220,
        horizontalPadding: 10,
        [new("a0", 1, "idle", false), new("a1", 3, "attack", true)]);

    Assert.Equal((1d, 3d), (lane.Segments[0].StartTimeSeconds, lane.Segments[0].EndTimeSeconds));
    Assert.Equal((3d, 4d), (lane.Segments[1].StartTimeSeconds, lane.Segments[1].EndTimeSeconds));
    Assert.Equal("a1", lane.HitTest(lane.Markers[1].X, 6));
}
```

- [ ] **Step 2: lane RED를 확인한다**

Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~StepTrackLayout"`

Expected: FAIL — layout 타입이 없음.

- [ ] **Step 3: Godot 독립 layout을 구현한다**

```csharp
public sealed record StepTrackItem(
    string Id, double TimeSeconds, string Label, bool Emphasized);

public static StepTrackLane Create(
    double durationSeconds,
    double width,
    double horizontalPadding,
    IReadOnlyList<StepTrackItem> items);
```

빈 track, zero/narrow width, first marker 전 공백, end clipping, overlapping hit tie를 검증한다. Godot 타입을 참조하지 않는다.

- [ ] **Step 4: scene 구조 RED를 추가한다**

`Test-ProjectSkeleton.ps1`에서 다음 파일/node를 요구한다.

```text
ActionTrackSurface
LockOnTrackSurface
ActionToolbar / LockOnToolbar
ActionKeyInput / ActionTimeInput
LockEnabledInput / LockTargetInput / LockModeInput / LockYawOffsetInput / LockTimeInput
ActionApplyButton / LockApplyButton
```

Run: `& .\scripts\Test-ProjectSkeleton.ps1`

Expected: FAIL — 파일/node가 없음.

- [ ] **Step 5: lane surface와 controller를 구현한다**

surface는 `Attach(DocumentSession)`, `Detach()`, `_Draw()`, `_GuiInput()`만 소유한다. snapshot/document/playback/actor/action/lock selection event에 redraw한다. Action segment label과 Lock enabled/disabled·target/mode label은 layout item에서 받는다.

- [ ] **Step 6: semantic Inspector를 구현한다**

Action Apply는 time+trim하지 않은 nonblank action key를 한 command로 확정한다. Lock Apply는 time/enabled/target/offset/mode를 한 command로 확정한다. target 목록은 선택 actor 자신을 제외하고 document actor ID를 안정 정렬한다. 입력 변경은 Domain preview를 만들지 않는다.

- [ ] **Step 7: scene node와 수명주기 surface API를 완성한다**

`Main.tscn`에 두 lane 최소 높이 40, 각 toolbar와 Inspector section을 추가한다. controller 생성은 아직 `Main.cs`에 연결하지 않고 Task 6 exact lookup에서 수행한다.

- [ ] **Step 8: Editor와 skeleton을 실행한다**

Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo`

Run: `& .\scripts\Test-ProjectSkeleton.ps1`

Expected: Editor PASS, skeleton PASS.

- [ ] **Step 9: 메인 에이전트가 검토·커밋·푸시한다**

Commit: `feat: Action Lock-on lane과 Inspector 추가`

---

### Task 5: 단일 snapshot 기반 TopView·WorldView overlay

**Files:**
- Create: `src/PvpGuide.Editor/Features/Timeline/SemanticOverlayLayout.cs`
- Create: `tests/PvpGuide.Editor.Tests/SemanticOverlayLayoutTests.cs`
- Modify: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Modify: `src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs`
- Modify: `tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs`
- Modify: `tests/PvpGuide.Editor.Tests/WorldTransformMapperTests.cs`

**Interfaces:**
- Consumes: Task 1 `SceneSnapshot.ActorTimelineStates`; 기존 `ISceneProjectionConsumer.Apply(snapshot)`.
- Produces: 두 뷰의 동일 action label과 enabled lock target overlay, 순수 line/label layout.

- [ ] **Step 1: shared snapshot projection RED를 작성한다**

```csharp
[Fact]
public void Projection_delivers_same_semantic_state_to_both_consumers()
{
    var top = new CapturingConsumer();
    var world = new CapturingConsumer();
    using var controller = new SceneProjectionController(source, playback, top, world);

    playback.Seek(1.5);

    Assert.Same(top.LastSnapshot, world.LastSnapshot);
    Assert.Equal("attack", top.LastSnapshot!.ActorTimelineStates["host"].Action.ActionKey);
    Assert.Equal("invader", top.LastSnapshot.ActorTimelineStates["host"].LockOn.TargetActorId);
}
```

- [ ] **Step 2: overlay layout RED를 작성한다**

```csharp
[Fact]
public void Enabled_lock_layout_connects_actor_and_target_centers()
{
    var overlay = SemanticOverlayLayout.Create(
        actorPosition: new Position3(1, 0, 2),
        targetPosition: new Position3(4, 0, -1),
        actionKey: "attack",
        lockEnabled: true,
        targetActorId: "invader",
        trackingMode: LockOnTrackingMode.Continuous);

    Assert.Equal("행동: attack", overlay.ActionLabel);
    Assert.NotNull(overlay.LockLine);
}
```

- [ ] **Step 3: pure overlay read model을 구현한다**

없는 action은 label을 숨기고 disabled lock은 line을 만들지 않는다. enabled lock target이 snapshot transform dictionary에 없으면 mutation하지 않고 명시적 invalid-operation guard로 projection 실패를 드러낸다.

- [ ] **Step 4: TopView draw를 확장한다**

committed snapshot의 actor transform과 semantic state를 함께 보관한다. actor label 아래 action text를 그리고 enabled lock line과 target 끝 marker를 그린다. preview는 transform position만 덮고 semantic state는 committed snapshot을 유지한다.

- [ ] **Step 5: WorldView OverlayRoot를 확장한다**

actor별 기존 `OverlayRoot` 아래 `Label3D` action label, lock badge와 재사용 가능한 line mesh node를 만든다. Apply마다 node를 새로 누적하지 않고 actor ID별 cache를 갱신한다. actor removal 시 overlay node도 제거한다.

- [ ] **Step 6: projection·Editor 회귀를 실행한다**

Run: `dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~SceneProjectionController"`

Run: `dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo`

Expected: focused Application과 Editor 전체 PASS.

- [ ] **Step 7: 메인 에이전트가 검토·커밋·푸시한다**

Commit: `feat: Action Lock-on 교육 overlay 투영`

---

### Task 6: Main 조립·결정적 runtime·한글 문서

**Files:**
- Create: `src/PvpGuide.Editor/Scenes/Main/ActionLockOnRuntimeProbe.cs`
- Create: Godot-generated `src/PvpGuide.Editor/Scenes/Main/ActionLockOnRuntimeProbe.cs.uid`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-GodotRuntime.ps1`
- Modify: `README.md`
- Modify: `docs/03-system-architecture.md`
- Modify: `docs/04-data-architecture.md`
- Modify: `docs/05-editor-architecture.md`
- Modify: `docs/13-roadmap.md`
- Modify: `docs/superpowers/plans/2026-08-27-action-lock-on-track-foundation.md`

**Interfaces:**
- Consumes: Task 1~5의 공개 Domain/session/controller/surface/overlay 계약.
- Produces: 실제 UI signal 조립, exact Action/Lock-on runtime marker, 사용자 가이드와 완료 roadmap.

- [ ] **Step 1: Main exact node lookup과 역순 cleanup을 연결한다**

`Main._Ready()`는 새 lane, toolbar, semantic Inspector node를 exact type/path로 찾는다. session → surfaces attach → semantic controllers 생성 순으로 조립하고 실패 시 생성 역순으로 dispose/detach한다. `_ExitTree()` cleanup은 idempotent를 유지한다.

- [ ] **Step 2: 실제 UI runtime RED를 추가한다**

새 probe는 기존 transform probe 뒤 같은 deferred completion owner 안에서 실행한다. 기존 actor 상태를 보존한 채 lock target용 두 번째 actor를 추가하고 다음 실제 signal을 사용한다.

```text
Action Add → marker click → time/key Apply → Undo → Redo → Delete
Lock Add(enabled,target,continuous,offset) → marker click → Apply(mode/offset) → Undo → Redo → Delete
scrub left-hold state → playback lock → TopView/WorldView overlay 확인
```

`Button.Pressed`, `SpinBox.ValueChanged`, `LineEdit.TextSubmitted`, `OptionButton.ItemSelected`, surface viewport mouse input만 사용한다. wait, sleep, `_Process` 횟수, source-string assertion과 test-only production API를 사용하지 않는다.

- [ ] **Step 3: runtime script marker RED를 확인한다**

`Test-GodotRuntime.ps1`의 required marker 마지막에 다음 exact 문자열을 먼저 추가한다.

```text
ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1
```

Run: `& .\scripts\Test-GodotRuntime.ps1`

Expected: FAIL — 새 marker 없음.

- [ ] **Step 4: hand-derived runtime assertion을 완성한다**

문서 fixture의 marker time과 left-hold 결과를 코드 결과에서 읽어 기대값으로 사용하지 않는다. 각 성공 mutation/Undo/Redo의 revision, history event, selection ID/time, 두 projection apply count와 overlay node text/visibility를 독립 계산해 assertion한다. 실패·no-op·playback lock은 revision/history/apply count 불변을 검증한다.

- [ ] **Step 5: schema 검증 계층을 분리한다**

Godot Editor는 Infrastructure를 참조하지 않는다. `/1 → /2` migration과 `/2` round-trip은 Infrastructure 전체 테스트 출력으로만 완료 판정하고 Godot marker에 schema flag를 넣지 않는다.

- [ ] **Step 6: 한글 문서를 갱신한다**

README에 세 lane, Action/Lock-on Add·Apply·Delete·Undo/Redo, target/mode/offset, playback lock과 오류 흐름을 기록한다. system/data/editor architecture에는 v2 migration, stepped state, shared snapshot, 비영구 selection과 overlay 경계를 기록한다. roadmap은 foundation을 완료 목록으로 옮기고 다음 단위를 lock-on 방향 계산과 이동 궤적으로 지정한다.

- [ ] **Step 7: 계획 checkbox를 검증된 상태만 갱신한다**

각 Task review/commit/push와 fresh verification이 끝난 항목만 `[x]`로 바꾼다. Task 6 push SHA 확인 항목은 실제 push 뒤 별도 완료 기록 커밋에서 체크한다.

- [ ] **Step 8: 전체 직렬 검증을 실행한다**

```powershell
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
```

Expected: 모든 테스트 PASS, skeleton PASS, 두 startup failure probe PASS, Godot build 경고 0·오류 0, 모든 기존 marker와 새 Action/Lock-on exact marker PASS.

- [ ] **Step 9: 최종 diff와 추적 범위를 검사한다**

`git diff --check`, `git status --short`, `git diff --stat`를 확인한다. `.godot/`, build output, local-assets, tools, cache, exports, 로그와 임시 agent report를 추적하지 않는다. 새 Godot C# `.uid`는 runtime import 후 repository convention에 따라 포함한다.

- [ ] **Step 10: 메인 에이전트가 검토·커밋·푸시하고 SHA를 확인한다**

Commit: `docs: Action Lock-on 트랙 검증과 사용법 정리`

Push 뒤 local HEAD, upstream과 `git ls-remote` SHA가 모두 같아야 한다.
