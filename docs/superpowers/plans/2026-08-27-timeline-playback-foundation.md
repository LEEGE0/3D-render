# 타임라인 재생 기반 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Every task follows RED → GREEN → fresh spec/quality review → main-agent verification → exact-path commit/push.

**Goal:** 문서를 변경하지 않는 스크럽·재생·일시정지·처음 이동을 추가하고, 같은 문서 revision의 다른 시간도 탑뷰와 3D에 동기화하며 오해 가능한 최초 키프레임 편집을 잠근다.

**Architecture:** `PvpGuide.Application`의 Godot-free `PlaybackClock`이 비영구 현재 시간과 재생 상태를 소유한다. `DocumentSession`은 preview 취소와 편집 가능 상태를 조정하고, `SceneProjectionController`는 `(revision,time)` 기준으로 동일 snapshot을 두 뷰에 전달한다. Godot `TimelineController`는 controls와 clock을 연결하고 `_Process(delta)`는 시간 진행만 위임한다.

**Tech Stack:** Windows 11 x64, Godot 4.7.2 Stable .NET, C#/.NET 8, xUnit v3, Forward+, PowerShell 검증 스크립트

**Spec:** `docs/superpowers/specs/2026-08-27-timeline-playback-foundation-design.md`

## Global Constraints

- 프로젝트·도구·캐시·출력은 `D:\3D-render` 아래에서 관리한다.
- 앱은 오프라인 독립 실행이며 런타임 네트워크·분석·업로드를 추가하지 않는다.
- Domain/Application에는 Godot, 파일 시스템, JSON, process/timer 의존성을 넣지 않는다.
- 시간 탐색은 문서 revision, 저장 포맷, Undo/Redo를 변경하지 않는다.
- 투영 중복 키는 revision 단독이 아니라 정확한 `(revision,timeSeconds)`다.
- 재생 중이거나 현재 시간이 선택 배우의 최초 키프레임 시각과 다르면 변환 편집을 잠근다.
- 타임라인 키프레임 CRUD, Action/Lock-on, 루프, 렌더, 실제 애니메이션, 게임패드는 범위 밖이다.
- .NET 테스트 프로젝트는 Windows 공유 `obj` 잠금을 피하도록 반드시 직렬 실행한다.
- 하위 에이전트는 파일을 수정할 수 있지만 커밋·푸시는 하지 않는다. 메인 에이전트가 실제 diff와 테스트를 확인한다.
- 메인 에이전트는 `git add -- <명시 경로>`만 사용하고 각 Task 완료마다 커밋·푸시한다.
- 사용자의 실제 정상 동작 재확인 전에는 `working/...` 태그를 만들지 않는다.

---

### Task 1: Godot-free 재생 시계와 세션 편집 잠금

**Files:**
- Create: `src/PvpGuide.Application/Playback/PlaybackClock.cs`
- Create: `src/PvpGuide.Application/Playback/PlaybackChangedEventArgs.cs`
- Create: `src/PvpGuide.Application/Sessions/EditAvailabilityChangedEventArgs.cs`
- Modify: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Create: `tests/PvpGuide.Application.Tests/PlaybackClockTests.cs`
- Modify: `tests/PvpGuide.Application.Tests/DocumentSessionTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md` (Task 1 완료 체크)

**Produces:** deterministic playback state, preview cancellation on playback changes, explicit `CanEditSelectedTransform` and lock reason/event

- [x] **Step 1: PlaybackClock 경계 테스트를 RED로 작성한다**

  `PlaybackClockTests`는 생성 검증, seek clamp/no-op, 재생 advance, 끝점 자동 정지, 끝점 play 되감기, pause/stop, 음수·비유한 입력 거부를 검사한다.

  ```csharp
  [Fact]
  public void Advance_changes_time_only_while_playing_and_auto_pauses_at_end()
  {
      var clock = new PlaybackClock(durationSeconds: 1, framesPerSecond: 30);
      Assert.False(clock.Advance(0.25));
      Assert.True(clock.Play());
      Assert.True(clock.Advance(0.75));
      Assert.True(clock.Advance(0.5));
      Assert.Equal(1, clock.CurrentTimeSeconds);
      Assert.False(clock.IsPlaying);
  }
  ```

  `Changed`는 공개 호출당 최종 상태 한 번만 전달하고 같은 유효 상태는 전달하지 않는지 확인한다. RED 실행 결과가 새 타입 부재 또는 동작 불일치로 실패하는지 기록한다.

- [x] **Step 2: PlaybackClock을 최소 구현한다**

  `PlaybackChangedEventArgs`는 `CurrentTimeSeconds`, `IsPlaying`을 가진 불변 event args다. `Seek()`는 `[0,duration]` clamp, `Play()`는 끝점에서 0으로 되감은 최종 playing 상태 한 번 통지, `Advance()`는 끝점 clamp와 auto-pause를 한 번에 통지한다. 상태가 실제로 바뀐 경우에만 `true`를 반환한다.

- [x] **Step 3: DocumentSession 잠금 테스트를 RED로 작성한다**

  첫 키프레임이 t=0.25인 actor를 선택해 다음을 검증한다.

  - t=0은 paused여도 편집 불가
  - t=0.25 paused에서 편집 가능
  - 같은 시각의 playing 상태는 편집 불가
  - 비키 시점의 Move/Rotate/Set은 false이고 문서/history 불변
  - 잠긴 `BeginPreview()`는 명시적 `InvalidOperationException`
  - 활성 preview 상태에서 seek 시 `PreviewChanged(null)`이 외부 playback event보다 먼저 보임
  - seek/play/pause는 revision과 UndoCount/RedoCount 불변
  - 선택 변경도 편집 가능 상태를 다시 계산

- [x] **Step 4: 세션 조립과 편집 guard를 최소 구현한다**

  `DocumentSession` 생성 시 문서 길이/FPS로 clock을 만들고 먼저 내부 event를 구독한 뒤 `Playback`을 공개한다. `CanEditSelectedTransform`과 한글 `EditLockReason`을 제공하고 `EditAvailabilityChanged`는 실제 가능 여부/이유 변경 시만 발생시킨다. playback 상태 변화 handler는 `ClearPreview()` 후 편집 상태를 계산한다. 선택 변경도 preview 취소 후 상태를 계산한다.

  기존 edit API는 잠금 상태에서 mutation하지 않는다. `BeginPreview()`는 잠금 이유가 포함된 예외로 거부하고 Move/Rotate/Set은 false를 반환한다. Undo/Redo Application API의 역사적 계약은 유지하며 Presentation에서 잠근다.

- [x] **Step 5: Task 1 범위를 직렬 검증한다**

  ```powershell
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  ```

  Expected: Application 실패 0, 기존 37개와 새 playback/session 테스트 통과, 구조 PASS.

- [x] **Step 6: fresh spec/quality 리뷰 후 메인이 커밋·푸시한다**

  spec 리뷰는 clock 상태 전이·event 횟수·비영구성·first-key 시각 정책을, quality 리뷰는 부동소수 경계·event 재진입·기존 session 회귀·observer 예외를 확인한다. Critical/Important 지적 수정과 fresh 재리뷰 후 정확한 경로만 스테이징한다.

  ```powershell
  git add -- 'src/PvpGuide.Application/Playback/PlaybackClock.cs' 'src/PvpGuide.Application/Playback/PlaybackChangedEventArgs.cs' 'src/PvpGuide.Application/Sessions/EditAvailabilityChangedEventArgs.cs' 'src/PvpGuide.Application/Sessions/DocumentSession.cs' 'tests/PvpGuide.Application.Tests/PlaybackClockTests.cs' 'tests/PvpGuide.Application.Tests/DocumentSessionTests.cs' 'scripts/Test-ProjectSkeleton.ps1' 'docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md'
  git commit -m 'feat: 재생 시간과 편집 잠금 상태 추가'
  git push
  ```

---

### Task 2: `(revision,time)` 기반 2D/3D 동기 투영

**Files:**
- Create: `src/PvpGuide.Application/Playback/IPlaybackTimeSource.cs`
- Modify: `src/PvpGuide.Application/Playback/PlaybackClock.cs`
- Modify: `src/PvpGuide.Application/Projection/SceneProjectionController.cs`
- Modify: `tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md` (Task 2 완료 체크)

**Produces:** same-revision time reprojection, shared snapshot instance delivery, dual-event disposal

- [x] **Step 1: 시간 변경 투영 테스트를 RED로 작성한다**

  두 변환 키를 가진 실제 document/session으로 다음을 검증한다.

  ```csharp
  [Fact]
  public void Time_change_projects_same_revision_at_new_time_to_both_consumers()
  {
      // t=0 project, then seek t=0.5 without a document mutation.
      // Both consumers receive a second, shared snapshot with the same revision.
      // Position is midpoint and yaw 350° -> 10° midpoint is 0°.
  }
  ```

  동일 `(revision,time)` 명시 요청은 억제하고, time만 변경·revision만 변경하면 각각 한 번 전달하며, Dispose 후 두 event 모두 전달하지 않는 테스트를 추가한다.

- [x] **Step 2: 시간 소스와 projection dedupe를 최소 구현한다**

  `IPlaybackTimeSource`는 현재 시간과 `Changed` event만 노출하고 `PlaybackClock`이 구현한다. `SceneProjectionController`는 fixed `_timeSeconds`를 제거하고 source와 playback 모두 구독한다. 마지막 키를 `(long Revision, double TimeSeconds)?`로 보관하며 매 투영에서 snapshot 한 번을 만들고 두 distinct consumer에 같은 인스턴스를 전달한다.

  문서 변경 handler는 현재 playback 시간을 사용한다. playback handler도 같은 경로를 사용한다. `ProjectCurrent()`는 같은 key면 no-op이며 Dispose는 두 구독을 모두 해제한다.

- [x] **Step 3: Main 조립을 새 생성자 계약으로 옮긴다**

  기존 runtime 동작과 표식은 바꾸지 않고 `session.Playback`을 projection controller에 전달한다. 최초 `ProjectCurrent()`와 기존 basic editing projection count가 그대로 유지되는지 컴파일과 Application 테스트로 확인한다.

- [x] **Step 4: 전체 순수 테스트와 구조를 직렬 검증한다**

  ```powershell
  $projects = @(
    '.\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj',
    '.\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj',
    '.\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj',
    '.\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj'
  )
  foreach ($project in $projects) {
    dotnet test $project -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
  & .\scripts\Test-ProjectSkeleton.ps1
  ```

- [x] **Step 5: fresh spec/quality 리뷰 후 메인이 커밋·푸시한다**

  리뷰는 동일 snapshot instance, shortest-yaw 실제 평가, exact double key, 중복 억제, observer/Dispose, 기존 projection count를 확인한다.

  ```powershell
  git add -- 'src/PvpGuide.Application/Playback/IPlaybackTimeSource.cs' 'src/PvpGuide.Application/Playback/PlaybackClock.cs' 'src/PvpGuide.Application/Projection/SceneProjectionController.cs' 'tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs' 'src/PvpGuide.Editor/Scenes/Main/Main.cs' 'scripts/Test-ProjectSkeleton.ps1' 'docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md'
  git commit -m 'feat: 시간별 탑뷰와 3D 투영 동기화'
  git push
  ```

---

### Task 3: Godot 타임라인 UI와 편집 잠금 표현

**Files:**
- Create: `src/PvpGuide.Editor/Features/Timeline/TimelineController.cs`
- Create: `src/PvpGuide.Editor/Features/Timeline/TimelineController.cs.uid`
- Create: `src/PvpGuide.Editor/Features/Timeline/TimelineTimeFormatter.cs`
- Create: `src/PvpGuide.Editor/Features/Timeline/TimelineTimeFormatter.cs.uid`
- Create: `tests/PvpGuide.Editor.Tests/TimelineTimeFormatterTests.cs`
- Modify: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Modify: `src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.tscn`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md` (Task 3 완료 체크)

**Produces:** slider/buttons/Space playback UI, lock-aware TopView and Inspector, lifecycle-safe controller

- [x] **Step 1: 시간 표시 formatter 테스트를 RED로 작성한다**

  `TimelineTimeFormatter`는 Godot 없이 현재/전체 시간과 frame을 결정적으로 표시한다. 0초, 중간 frame, 끝점, 29.97 같은 비정수 시각에서 invariant 숫자 포맷과 한국어 label을 검증한다.

- [x] **Step 2: TimelineController와 장면 노드를 구현한다**

  `Main.tscn`에 설계의 `TimelineControls`, `PlaybackButtons`, `PlayPauseButton`, `StopButton`, `TimeSlider`, `CurrentTimeLabel`, `TimelineStatus`를 정확한 이름으로 추가한다.

  controller는 slider max/step, button text, label, status를 초기화하고 signal을 clock/session에 연결한다. slider 사용자 변경은 Pause→Seek, Play/Pause와 Space는 같은 `TogglePlayback()` 경로, Stop은 `Stop()`을 호출한다. programmatic slider update 중에는 signal guard를 사용한다. Dispose는 모든 Godot signal과 Application event를 해제한다.

- [x] **Step 3: TopView/Inspector 편집 잠금을 구현한다**

  `TopViewSurface`는 session의 edit availability를 구독해 잠금 전환 시 local drag와 preview를 취소한다. 잠금 중 actor 선택은 유지하되 이동/회전 preview를 시작하지 않는다.

  `TransformInspectorController`는 잠금 중 SpinBox 4개, Apply, Undo, Redo를 disabled로 만들고 이유를 표시한다. 잠금 해제 시 committed 최초 키 값을 재반영하며 history 상태에 맞춰 Undo/Redo를 복구한다. programmatic 값 반영이 preview를 만들지 않는 기존 guard를 유지한다.

- [x] **Step 4: Main lifecycle과 키 입력을 조립한다**

  `_Ready()`에서 timeline controls를 검증하고 controller를 만든다. `_Process(delta)`는 준비된 playback에 `Advance(delta)`만 호출한다. Space press는 echo/release를 무시하고 timeline controller의 공개 toggle 경로를 사용한다. `_ExitTree()`는 controller와 기존 event 구독을 모두 해제한다.

- [x] **Step 5: Editor 테스트·구조·기존 runtime을 직렬 검증한다**

  ```powershell
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  & .\scripts\Test-GodotRuntime.ps1
  ```

  기존 runtime 표식과 projection count는 그대로 통과해야 한다.

- [x] **Step 6: fresh spec/quality 리뷰 후 메인이 커밋·푸시한다**

  리뷰는 signal 재진입, Space echo, Godot lifecycle, 잠금 중 선택 유지, stale preview 정리, unlock history 상태를 확인한다.

  ```powershell
  git add -- 'src/PvpGuide.Editor/Features/Timeline/TimelineController.cs' 'src/PvpGuide.Editor/Features/Timeline/TimelineController.cs.uid' 'src/PvpGuide.Editor/Features/Timeline/TimelineTimeFormatter.cs' 'src/PvpGuide.Editor/Features/Timeline/TimelineTimeFormatter.cs.uid' 'tests/PvpGuide.Editor.Tests/TimelineTimeFormatterTests.cs' 'src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs' 'src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs' 'src/PvpGuide.Editor/Scenes/Main/Main.tscn' 'src/PvpGuide.Editor/Scenes/Main/Main.cs' 'scripts/Test-ProjectSkeleton.ps1' 'docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md'
  git commit -m 'feat: 타임라인 재생 UI와 편집 잠금 연결'
  git push
  ```

---

### Task 4: 결정적 런타임 통합 검사와 사용자 문서

**Files:**
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `scripts/Test-GodotRuntime.ps1`
- Modify: `README.md`
- Modify: `docs/05-editor-architecture.md`
- Modify: `docs/13-roadmap.md`
- Modify: `docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md` (Task 4 완료 체크)

**Produces:** exact `TIMELINE_PLAYBACK_READY` evidence, current Korean operation/architecture/roadmap documentation

- [x] **Step 1: runtime 문서를 두 키프레임으로 확장한다**

  기존 basic editing smoke가 먼저 완료되게 순서를 보존한다. runtime actor에 t=1 변환 키프레임을 추가하되 기존 최초 키프레임 편집 결과와 `BASIC_EDITING_*` exact marker를 유지한다.

- [x] **Step 2: 실제 UI signal과 결정적 clock 호출을 검증한다**

  runtime self-test는 실제 대기나 `_Process` 횟수에 의존하지 않고 다음을 직접 검증한다.

  - active preview를 만든 뒤 slider signal로 0.5초 seek
  - preview clear와 midpoint top/world transform
  - revision/keyframe/Undo/Redo 불변
  - 중간 시점 TopView/Inspector edit guard
  - Play button signal과 Space input의 같은 toggle
  - 직접 `Advance()`로 end clamp/auto-pause
  - Stop button으로 0초와 편집 가능 상태 복귀

  성공 후 정확히 다음을 출력한다.

  ```text
  TIMELINE_PLAYBACK_READY scrub_midpoint=1 top_world_sync=1 revision_unchanged=1 history_unchanged=1 preview_cancel=1 edit_guard=1 play_button=1 space_toggle=1 end_clamp=1 stop_restore=1
  ```

- [x] **Step 3: 검증 스크립트와 한글 문서를 갱신한다**

  `Test-ProjectSkeleton.ps1`은 새 Application/Editor 파일과 scene node, README/architecture/roadmap의 핵심 계약을 검사한다. `Test-GodotRuntime.ps1`은 기존 exact marker에 새 exact marker를 추가한다.

  README에는 slider·Play/Pause·Stop·Space 사용법과 읽기 전용 시점 잠금을 기록한다. editor architecture에는 playback 상태 소유권, preview cancellation, `(revision,time)` projection을 기록한다. roadmap은 단계 3A를 완료로 표시하고 다음 단위를 임의 시점 변환 키프레임 CRUD로 명확히 한다.

- [x] **Step 4: 모든 자동 검증을 메인에서 새로 직렬 실행한다**

  ```powershell
  $projects = @(
    '.\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj',
    '.\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj',
    '.\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj',
    '.\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj'
  )
  foreach ($project in $projects) {
    dotnet test $project -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  }
  & .\scripts\Test-ProjectSkeleton.ps1
  & .\scripts\Test-GodotRuntime.ps1
  ```

  Expected: 기존 133개와 모든 새 테스트 실패 0, 구조 PASS, 기존 marker와 `TIMELINE_PLAYBACK_READY`, `GODOT_RUNTIME_VERIFICATION=PASS`.

- [x] **Step 5: Forward+ GUI와 시각 상태를 확인한다**

  현재 사용자가 실행해 둔 Godot 인스턴스를 조작하지 않는다. 검증용 별도 프로세스를 Godot console executable로 실행해 Vulkan Forward+, marker, ERROR 부재를 확인하고, 새 타임라인 control·두 뷰·Inspector가 1280×720에서 겹치거나 잘리지 않는지 별도 화면 캡처로 확인한다.

- [ ] **Step 6: fresh spec/quality 리뷰 후 메인이 최종 커밋·푸시한다**

  spec 리뷰는 정확한 marker의 각 항목과 문서 범위를, quality 리뷰는 runtime self-test의 결함 탐지력·UI signal 실재성·cleanup·기존 표식 회귀를 확인한다. 수정 후 fresh 재리뷰와 전체 재검증을 반복한다.

  ```powershell
  git add -- 'src/PvpGuide.Editor/Scenes/Main/Main.cs' 'scripts/Test-ProjectSkeleton.ps1' 'scripts/Test-GodotRuntime.ps1' 'README.md' 'docs/05-editor-architecture.md' 'docs/13-roadmap.md' 'docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md'
  git commit -m 'docs: 타임라인 재생 검증과 사용법 정리'
  git push
  ```

- [ ] **Step 7: 원격 상태와 clean tree를 확인한다**

  `git status`, local HEAD, tracking ref, `git ls-remote` SHA가 일치하는지 확인한다. 사용자가 실제 조작 결과를 정상이라고 말하면 어떤 기능·시나리오인지 재확인한 뒤에만 `working/timeline-playback-YYYYMMDD-HHmm` 주석 태그를 생성·푸시한다.
