# 탑뷰 기본 편집과 3D 실시간 투영 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 탑뷰에서 배우의 가장 이른 변환 키프레임을 선택·이동·회전하고, 비영구 드래그 미리보기와 확정·Undo/Redo 결과를 3D 플레이스홀더에 실시간 동기화한다.

**Architecture:** `PvpGuide.Application`을 새로 만들고 편집 의도·선택·미리보기·Undo/Redo를 관리한다. Domain은 expected-preimage 기반 원자 키프레임 교체만 제공하며, Godot Editor는 Application 이벤트와 불변 snapshot을 탑뷰·3D·Inspector에 표현한다.

**Tech Stack:** Windows 11 x64, Godot 4.7.2 Stable .NET, C#/.NET 8, xUnit v3, Forward+, 순수 C# Domain/Application

**Spec:** `docs/superpowers/specs/2026-08-27-basic-topview-editing-design.md`

## Global Constraints

- 프로젝트·도구·캐시·출력은 `D:\3D-render` 아래에서 관리한다.
- 앱은 오프라인 독립 실행이며 런타임 네트워크·분석·업로드를 추가하지 않는다.
- Domain/Application에는 Godot, 파일 시스템, JSON, 외부 프로세스 의존성을 넣지 않는다.
- 탑뷰 좌표는 오른쪽 +X, 아래쪽 +Z, Yaw 0° 오른쪽·90° 아래쪽이다.
- 편집 대상은 각 actor의 시간상 가장 이른 Transform keyframe이며 새 t=0 keyframe을 만들지 않는다.
- 드래그 중 preview는 문서 revision·Undo 스택을 변경하지 않고 release에서 한 명령만 확정한다.
- 실제 DSR 자산, 게임패드/Xbox, 타임라인 재생, 키프레임 추가·이동·삭제, 렌더 실행은 범위 밖이다.
- `.NET` 테스트 프로젝트는 공유 `obj` 파일 잠금을 피하도록 반드시 직렬 실행한다.
- 하위 에이전트는 파일을 수정할 수 있지만 커밋·푸시는 하지 않는다. 메인 에이전트가 diff·테스트를 확인하고 정확한 경로만 스테이징한다.
- 각 Task는 RED→GREEN, fresh spec/quality review, 메인 전체 검증, 별도 커밋·푸시까지 끝낸 뒤 다음 Task로 진행한다.

---

### Task 1: Domain 변환 키프레임 원자 교체

**Files:**
- Modify: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`
- Modify: `docs/superpowers/plans/2026-08-27-basic-topview-editing.md` (Task 1 완료 체크)

**Interfaces:**
- Consumes: `ActorTrack`, `TransformKeyframe`, `SceneDocument.Revision`, `SceneDocument.Changed`
- Produces: `ActorTrack.GetTransformKeyframe`, `ActorTrack.ReplaceTransformKeyframe`, `SceneDocument.GetTransformKeyframe`, `SceneDocument.ReplaceTransformKeyframe`

- [x] **Step 1: 성공·no-op·실패 원자성 테스트를 먼저 작성한다**

  `SceneDocumentTests.cs`에 다음 실제 행동 테스트를 추가한다.

  ```csharp
  [Fact]
  public void ReplaceTransformKeyframe_changes_pose_once_and_preserves_track_meaning()
  {
      var document = CreateEditableDocument();
      var before = document.GetTransformKeyframe("host", "host-first");
      var after = new TransformKeyframe(before.Id, before.TimeSeconds, new Position3(4, 2, 6), 90);
      var notifications = 0;
      document.Changed += (_, _) => notifications++;

      var changed = document.ReplaceTransformKeyframe("host", before, after);

      Assert.True(changed);
      Assert.Equal(1, document.Revision);
      Assert.Equal(1, notifications);
      Assert.Equal(after.Position, document.CreateSnapshot(before.TimeSeconds).ActorTransforms["host"].Position);
      Assert.Equal(90, document.CreateSnapshot(before.TimeSeconds).ActorTransforms["host"].YawDegrees);
      Assert.Equal(["idle"], document.Actors.Single(a => a.ActorId == "host").ActionKeyframes.Select(k => k.ActionKey));
  }
  ```

  별도 Theory/Fact로 missing actor, missing keyframe, stale expected position/yaw, changed ID, changed time을 검사한다. 각 실패에서 actor 목록·키프레임 값·revision·notification이 그대로인지 단언한다. 같은 값을 교체하면 `false`, revision 0, notification 0인지 검사한다.

- [x] **Step 2: Domain 테스트를 실행해 정확한 RED를 확인한다**

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~ReplaceTransformKeyframe"
  ```

  Expected: 새 메서드가 없어서 컴파일 실패하거나 새 행동 테스트가 실패한다. 기존 테스트 실패나 테스트 0개는 올바른 RED가 아니다.

- [x] **Step 3: ActorTrack의 불변 교체를 최소 구현한다**

  `ActorTrack.cs`에 다음 계약을 구현한다.

  ```csharp
  public TransformKeyframe GetTransformKeyframe(string keyframeId)
  {
      ArgumentException.ThrowIfNullOrWhiteSpace(keyframeId);
      return _transformKeyframes.SingleOrDefault(frame => frame.Id == keyframeId)
          ?? throw new ArgumentException($"Transform keyframe '{keyframeId}' does not exist.", nameof(keyframeId));
  }

  public ActorTrack ReplaceTransformKeyframe(
      TransformKeyframe expectedCurrent,
      TransformKeyframe replacement)
  {
      ArgumentNullException.ThrowIfNull(expectedCurrent);
      ArgumentNullException.ThrowIfNull(replacement);
      if (replacement.Id != expectedCurrent.Id || replacement.TimeSeconds != expectedCurrent.TimeSeconds)
      {
          throw new ArgumentException("Replacement identity and time must remain unchanged.", nameof(replacement));
      }

      var current = GetTransformKeyframe(expectedCurrent.Id);
      if (!SameTransform(current, expectedCurrent))
      {
          throw new InvalidOperationException("The transform keyframe changed after the edit began.");
      }

      return new ActorTrack(
          ActorId,
          DisplayName,
          Role,
          _transformKeyframes.Select(frame => frame.Id == current.Id ? replacement : frame),
          _actionKeyframes,
          _lockOnKeyframes);
  }
  ```

  비교는 ID, exact `TimeSeconds`, `Position3`, normalized `YawDegrees` 모두 사용한다. keyframe ID가 actor track 안에서도 고유하도록 생성자 검증을 추가하고 중복 ID 테스트를 작성한다.

- [x] **Step 4: SceneDocument의 검증 후 교체·event 원자성을 구현한다**

  `SceneDocument`는 actor를 찾고 current preimage를 확인한 뒤 새 track을 완성한다. 동일 replacement면 `false`를 반환한다. 새 track 완성 전에는 dictionary/list/revision을 바꾸지 않는다.

  ```csharp
  public bool ReplaceTransformKeyframe(
      string actorId,
      TransformKeyframe expectedCurrent,
      TransformKeyframe replacement)
  {
      var actor = GetRequiredActor(actorId);
      var current = actor.GetTransformKeyframe(expectedCurrent.Id);
      ValidateExpected(current, expectedCurrent);
      if (SameTransform(current, replacement))
      {
          return false;
      }

      var updated = actor.ReplaceTransformKeyframe(expectedCurrent, replacement);
      _actorsById[actorId] = updated;
      _actors[_actors.IndexOf(actor)] = updated;
      RaiseChanged();
      return true;
  }
  ```

- [x] **Step 5: Domain 전체 테스트와 정적 경계를 검증한다**

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  rg -n "Godot|System.Text.Json|System.Diagnostics.Process|File\\.|Directory\\." .\src\PvpGuide.Domain
  ```

  Expected: Domain 테스트 실패 0개, 금지 의존성 검색 결과 0개.

- [x] **Step 6: fresh 리뷰 후 메인이 Task 1만 커밋·푸시한다**

  리뷰는 identity/time 보존, duplicate ID, stale expected, no-op, 다른 track 보존, 실패 원자성을 확인한다. Critical/Important를 수정하고 fresh 재리뷰가 깨끗할 때 메인이 다음 경로만 스테이징한다.

  ```powershell
  git add -- 'src/PvpGuide.Domain/Actors/ActorTrack.cs' 'src/PvpGuide.Domain/SceneDocument.cs' 'tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs' 'docs/superpowers/plans/2026-08-27-basic-topview-editing.md'
  git commit -m 'feat: 변환 키프레임 원자 편집 추가'
  git push
  ```

---

### Task 2: Application 문서 세션·명령·Undo/Redo·미리보기

**Files:**
- Create: `src/PvpGuide.Application/PvpGuide.Application.csproj`
- Create: `src/PvpGuide.Application/Properties/AssemblyInfo.cs`
- Create: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Create: `src/PvpGuide.Application/Sessions/SelectionChangedEventArgs.cs`
- Create: `src/PvpGuide.Application/Editing/TransformPreview.cs`
- Create: `src/PvpGuide.Application/Editing/TransformPreviewChangedEventArgs.cs`
- Create: `src/PvpGuide.Application/Commands/ISceneEditCommand.cs`
- Create: `src/PvpGuide.Application/Commands/ReplaceTransformCommand.cs`
- Create: `tests/PvpGuide.Application.Tests/PvpGuide.Application.Tests.csproj`
- Create: `tests/PvpGuide.Application.Tests/DocumentSessionTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `docs/superpowers/plans/2026-08-27-basic-topview-editing.md` (Task 2 완료 체크)

**Interfaces:**
- Consumes: Task 1 `SceneDocument.GetTransformKeyframe`, `ReplaceTransformKeyframe`, actor의 첫 `TransformKeyframes[0]`
- Produces: `DocumentSession`, `SelectionChanged`, `TransformPreview`, `PreviewChanged`, `MoveSelectedActor`, `RotateSelectedActor`, `SetSelectedActorTransform`, `Begin/Update/Commit/CancelPreview`, `Undo`, `Redo`

- [x] **Step 1: Application 프로젝트와 실패 테스트 골격을 만든다**

  Application 프로젝트는 `net8.0`, nullable, implicit usings, warnings-as-errors를 사용하고 Domain만 참조한다. 테스트 프로젝트는 xUnit v3와 Application 프로젝트를 참조한다. `InternalsVisibleTo("PvpGuide.Application.Tests")`는 internal command의 실패 스택 보존을 실제로 검증하는 용도로만 둔다.

  먼저 다음 테스트를 작성한다.

  ```csharp
  [Fact]
  public void Move_undo_redo_changes_only_the_first_transform_and_keeps_revision_monotonic()
  {
      var session = CreateSession(out var document);
      session.SelectActor("host");

      Assert.True(session.MoveSelectedActor(new Position3(5, 2, 7)));
      Assert.True(session.Undo());
      Assert.True(session.Redo());

      Assert.Equal(new Position3(5, 2, 7), document.Actors.Single(a => a.ActorId == "host").TransformKeyframes[0].Position);
      Assert.Equal(3, document.Revision);
      Assert.True(session.CanUndo);
      Assert.False(session.CanRedo);
  }
  ```

  추가 테스트는 known actor 선택/해제, unknown 선택 실패, no selection 편집 실패, Move의 yaw 보존, Rotate의 position 보존·yaw normalize, no-op history 없음, Undo 뒤 새 edit의 Redo 삭제를 포함한다.

- [x] **Step 2: 새 Application 테스트를 실행해 RED를 확인한다**

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  ```

  Expected: `DocumentSession`과 관련 타입이 없어서 컴파일 실패한다.

- [x] **Step 3: 선택과 단일 명령 실행을 최소 구현한다**

  `DocumentSession`은 생성자에서 non-null `SceneDocument`를 받고 `SnapshotSource`로 읽기 포트만 공개한다. `SelectActor(null)`은 해제, non-null은 `document.Actors`에 정확히 존재해야 한다. 같은 선택은 event를 만들지 않는다.

  `GetSelectedTransform()`은 선택이 없으면 null, 선택이 있으면 해당 actor의 `TransformKeyframes[0]`을 반환해 Inspector와 preview가 같은 대상을 읽게 한다. 편집 대상은 항상 이 첫 keyframe이다. 위치·회전·통합 변환 메서드는 before/after `TransformKeyframe`을 만들고 internal `ReplaceTransformCommand`를 실행한다.

  ```csharp
  internal interface ISceneEditCommand
  {
      bool Execute(SceneDocument document);
      bool Undo(SceneDocument document);
  }

  internal sealed class ReplaceTransformCommand(
      string actorId,
      TransformKeyframe before,
      TransformKeyframe after) : ISceneEditCommand
  {
      public bool Execute(SceneDocument document) =>
          document.ReplaceTransformKeyframe(actorId, before, after);

      public bool Undo(SceneDocument document) =>
          document.ReplaceTransformKeyframe(actorId, after, before);
  }
  ```

- [x] **Step 4: Undo/Redo 스택 실패 원자성을 TDD로 구현한다**

  command가 성공한 뒤에만 undo stack에 push하고 redo를 clear한다. Undo/Redo는 `Peek()`으로 실행이 성공한 뒤에만 pop/push한다. internal test command가 stale preimage로 실패하게 만들어 stack count와 `CanUndo`/`CanRedo`가 보존되는지 검사한다.

  `Undo()`/`Redo()`는 빈 스택이면 `false`다. 명령의 domain 변경이 no-op `false`를 반환하면 history 이동도 하지 않는다.

- [x] **Step 5: preview RED 테스트와 최소 구현을 추가한다**

  다음 흐름을 테스트한다.

  ```csharp
  session.SelectActor("host");
  session.BeginPreview();
  session.UpdatePreview(new Position3(3, 2, 4), 90);

  Assert.Equal(0, document.Revision);
  Assert.False(session.CanUndo);
  Assert.Equal("host", receivedPreview.ActorId);

  Assert.True(session.CommitPreview());
  Assert.Equal(1, document.Revision);
  Assert.True(session.CanUndo);
  Assert.Null(lastPreview);
  ```

  `TransformPreview`는 actor ID, keyframe ID, Position3, normalized Yaw를 가진 불변 값이다. `BeginPreview`는 현재 선택과 첫 keyframe을 캡처하고, `UpdatePreview`는 ID/time을 바꾸지 않는다. `CancelPreview`와 selection 변경은 preview를 지운다. preview event subscriber에 같은 인스턴스를 전달한다.

- [x] **Step 6: Application·Domain 테스트와 구조 스크립트를 직렬 검증한다**

  `Test-ProjectSkeleton.ps1`에 Application 프로젝트·소스·테스트 파일과 Application→Domain 참조를 추가한다.

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  rg -n "Godot|System.Text.Json|System.Diagnostics.Process|File\\.|Directory\\." .\src\PvpGuide.Application
  ```

  Expected: 두 테스트 프로젝트 실패 0, 구조 PASS, 금지 의존성 0.

- [x] **Step 7: fresh 리뷰 후 메인이 Task 2만 커밋·푸시한다**

  리뷰는 selection과 document event 분리, first-key 정책, no-op, preview 비영구성, failed execute/undo/redo stack 보존을 확인한다. 승인 후 정확한 새 Application·Application.Tests·구조 스크립트 경로만 스테이징한다.

  ```powershell
  git add -- 'src/PvpGuide.Application/PvpGuide.Application.csproj' 'src/PvpGuide.Application/Properties/AssemblyInfo.cs' 'src/PvpGuide.Application/Sessions/DocumentSession.cs' 'src/PvpGuide.Application/Sessions/SelectionChangedEventArgs.cs' 'src/PvpGuide.Application/Editing/TransformPreview.cs' 'src/PvpGuide.Application/Editing/TransformPreviewChangedEventArgs.cs' 'src/PvpGuide.Application/Commands/ISceneEditCommand.cs' 'src/PvpGuide.Application/Commands/ReplaceTransformCommand.cs' 'tests/PvpGuide.Application.Tests/PvpGuide.Application.Tests.csproj' 'tests/PvpGuide.Application.Tests/DocumentSessionTests.cs' 'scripts/Test-ProjectSkeleton.ps1' 'docs/superpowers/plans/2026-08-27-basic-topview-editing.md'
  git commit -m 'feat: 문서 편집 세션과 실행 취소 추가'
  git push
  ```

---

### Task 3: Application 투영 조정자와 탑뷰 순수 좌표 계산

**Files:**
- Create: `src/PvpGuide.Application/Projection/ISceneProjectionConsumer.cs`
- Create: `src/PvpGuide.Application/Projection/SceneProjectionController.cs`
- Create: `src/PvpGuide.Application/Projection/ITransformPreviewConsumer.cs`
- Create: `src/PvpGuide.Application/Projection/TransformPreviewController.cs`
- Delete: `src/PvpGuide.Editor/Features/ViewportSync/SceneProjectionController.cs`
- Move/Rewrite: `tests/PvpGuide.Editor.Tests/SceneProjectionControllerTests.cs` → `tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs`
- Create: `tests/PvpGuide.Application.Tests/TransformPreviewControllerTests.cs`
- Create: `src/PvpGuide.Editor/Features/TopView/TopViewCoordinateMapper.cs`
- Create: `tests/PvpGuide.Editor.Tests/TopViewCoordinateMapperTests.cs`
- Modify: `src/PvpGuide.Editor/PvpGuide.Editor.csproj`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `docs/superpowers/plans/2026-08-27-basic-topview-editing.md` (Task 3 완료 체크)

**Interfaces:**
- Consumes: `ISceneSnapshotSource`, `DocumentSession.PreviewChanged`, `TransformPreview`
- Produces: explicit initial `ProjectCurrent()`, revision-deduplicated shared snapshot delivery, shared preview/clear delivery, screen/world mapping and hit-test helpers

- [x] **Step 1: projection 이동과 초기 투영 실패 테스트를 작성한다**

  기존 controller 테스트를 Application.Tests로 옮기고 namespace를 `PvpGuide.Application.Projection`으로 바꾼다. 다음 테스트를 추가한다.

  ```csharp
  [Fact]
  public void ProjectCurrent_delivers_one_shared_snapshot_before_any_change_event()
  {
      var source = new RecordingSnapshotSource(revision: 5);
      var top = new RecordingConsumer();
      var world = new RecordingConsumer();
      using var controller = new SceneProjectionController(source, top, world);

      controller.ProjectCurrent();
      controller.ProjectCurrent();

      Assert.Single(top.Received);
      Assert.Single(world.Received);
      Assert.Same(top.Received[0], world.Received[0]);
  }
  ```

  같은 revision에서 명시적 초기 투영은 한 번만, 다음 document revision은 한 번씩, Dispose 후에는 모두 중단되는 기존 계약을 유지한다.

- [x] **Step 2: controller 이동·ProjectCurrent를 최소 구현하고 테스트한다**

  `ISceneProjectionConsumer`와 controller를 Application으로 이동한다. `ProjectCurrent()`는 disposed가 아닐 때마다 현재 snapshot을 만들고, `ProjectSnapshot()`이 snapshot revision으로 전달만 중복 억제한다. event handler는 private `ProjectRevision()`을 사용해 같은 revision event를 먼저 억제한다.

- [x] **Step 3: preview controller RED 테스트와 최소 구현을 추가한다**

  top/world preview consumer는 서로 다른 인스턴스여야 한다. `DocumentSession.PreviewChanged`에서 전달받은 같은 `TransformPreview?` 인스턴스를 양쪽에 한 번씩 전달하고 Dispose 후에는 전달하지 않는다.

  ```csharp
  public interface ITransformPreviewConsumer
  {
      void ApplyPreview(TransformPreview? preview);
  }
  ```

  `TransformPreviewController`는 preview를 생성·수정하지 않고 distribution만 담당한다.

- [x] **Step 4: 탑뷰 mapper 테스트를 RED로 작성한다**

  `TopViewCoordinateMapperTests`는 Godot 없이 double 기반 `ScreenPoint`를 사용한다.

  ```csharp
  var mapper = new TopViewCoordinateMapper(
      panelWidth: 640,
      panelHeight: 360,
      centerX: 0,
      centerZ: 0,
      pixelsPerUnit: 40);

  Assert.Equal(new ScreenPoint(360, 220), mapper.WorldToScreen(new Position3(1, 7, 1)));
  Assert.Equal(new Position3(1, 7, 1), mapper.ScreenToWorld(new ScreenPoint(360, 220), preservedY: 7));
  Assert.Equal(0, mapper.PointerYawDegrees(new ScreenPoint(360, 180), new ScreenPoint(320, 180)));
  Assert.Equal(90, mapper.PointerYawDegrees(new ScreenPoint(320, 220), new ScreenPoint(320, 180)));
  ```

  16px actor hit, 10px rotation handle hit, actor body와 handle이 겹칠 때 handle 우선, 경계 밖 miss를 검사한다.

- [x] **Step 5: mapper를 최소 구현한다**

  `ScreenPoint`는 유한 double X/Y record struct다. mapper는 +Z를 화면 아래로 변환하고 `atan2(deltaScreenY, deltaScreenX)`를 `[0,360)`로 정규화한다. Godot `Vector2` 변환은 이후 adapter에만 둔다.

- [x] **Step 6: 프로젝트 참조·전체 순수 테스트·구조를 직렬 검증한다**

  Editor csproj은 기존 직접 Domain ProjectReference를 Application ProjectReference로 교체하고 기존 controller 파일을 제거한다. Domain의 불변 값 타입은 Application 공개 계약을 통해 전이 참조하며 문서 mutation API를 Editor에서 호출하지 않는다. Application.Tests에는 이동된 controller 테스트를 포함한다.

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  ```

  Expected: 네 프로젝트 실패 0, 구조 PASS.

- [x] **Step 7: fresh 리뷰 후 메인이 Task 3만 커밋·푸시한다**

  리뷰는 초기 snapshot 중복 억제, same-instance delivery, preview null clear, Dispose, +Z down, yaw 0/90/180/270, pixel hit boundary를 확인한다. 승인 후 이동·생성·삭제 경로를 정확히 스테이징한다.

  ```powershell
  git add -- 'src/PvpGuide.Application/Projection/ISceneProjectionConsumer.cs' 'src/PvpGuide.Application/Projection/SceneProjectionController.cs' 'src/PvpGuide.Application/Projection/ITransformPreviewConsumer.cs' 'src/PvpGuide.Application/Projection/TransformPreviewController.cs' 'src/PvpGuide.Editor/Features/ViewportSync/SceneProjectionController.cs' 'src/PvpGuide.Editor/Features/ViewportSync/SceneProjectionController.cs.uid' 'tests/PvpGuide.Editor.Tests/SceneProjectionControllerTests.cs' 'tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs' 'tests/PvpGuide.Application.Tests/TransformPreviewControllerTests.cs' 'src/PvpGuide.Editor/Features/TopView/TopViewCoordinateMapper.cs' 'tests/PvpGuide.Editor.Tests/TopViewCoordinateMapperTests.cs' 'src/PvpGuide.Editor/PvpGuide.Editor.csproj' 'src/PvpGuide.Editor/Scenes/Main/Main.cs' 'scripts/Test-ProjectSkeleton.ps1' 'docs/superpowers/plans/2026-08-27-basic-topview-editing.md'
  git commit -m 'refactor: 편집 투영과 탑뷰 좌표 경계 정리'
  git push
  ```

---

### Task 4: Godot 탑뷰 입력·3D 플레이스홀더·Inspector 조립

**Files:**
- Create: `src/PvpGuide.Application/Sessions/ActorDisplayInfo.cs`
- Modify: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Modify: `tests/PvpGuide.Application.Tests/DocumentSessionTests.cs`
- Create: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Create: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs.uid`
- Create: `src/PvpGuide.Editor/Features/TopView/TopViewCoordinateMapper.cs.uid`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs.uid`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldTransformMapper.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldTransformMapper.cs.uid`
- Create: `src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs`
- Create: `src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs.uid`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.tscn`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Create: `tests/PvpGuide.Editor.Tests/WorldTransformMapperTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Modify: `scripts/Test-GodotRuntime.ps1`
- Modify: `README.md`
- Modify: `docs/05-editor-architecture.md`
- Modify: `docs/13-roadmap.md`
- Modify: `docs/superpowers/plans/2026-08-27-basic-topview-editing.md`

**Interfaces:**
- Consumes: `DocumentSession`, committed `SceneSnapshot`, `TransformPreview?`, `TopViewCoordinateMapper`
- Produces: mouse selection/move/rotate, same-preview top/world rendering, actor-ID keyed Godot placeholders, numeric transform commit, exact runtime marker

- [x] **Step 1: 3D transform 변환 RED 테스트를 작성한다**

  `WorldTransformMapperTests`는 Domain pose를 Godot 타입 없는 double 기반 `WorldPosition`과 Y rotation radians로 바꾸는 순수 계산을 검사한다. `WorldViewProjectionAdapter`만 이 값을 Godot `Vector3`로 변환한다.

  ```csharp
  [Theory]
  [InlineData(0, 0)]
  [InlineData(90, -Math.PI / 2)]
  [InlineData(180, -Math.PI)]
  [InlineData(270, -Math.PI * 3 / 2)]
  public void Domain_yaw_maps_to_negative_godot_y_rotation(double yaw, double radians)
  {
      Assert.Equal(radians, WorldTransformMapper.ToRotationYRadians(yaw), 10);
  }
  ```

  Position3 X/Y/Z가 보존되고 non-finite 입력이 Domain에서 이미 거부됨을 확인한다.

- [x] **Step 2: TopViewSurface의 그리기와 입력을 구현한다**

  `TopViewSurface : Control, ISceneProjectionConsumer, ITransformPreviewConsumer`는 latest snapshot, preview, selected actor ID를 보관하고 `_Draw()`에서 grid·actor body·이름·방향 handle을 그린다. Application의 불변 `ActorDisplayInfo` 조회로 DisplayName과 `역할: Role`을 표시하며, 적대 역할은 마름모·나머지는 원을 사용한다. 28px 원형 handle은 selected actor에만 그려 hit-test와 일치시킨다.

  `_GuiInput()` 규칙은 다음과 같다.

  - LMB handle hit: 선택 후 `BeginPreview`, 회전 drag
  - LMB body hit: 선택 후 3px 이상에서 `BeginPreview`, X/Z 이동 drag
  - 빈 공간 LMB: 선택 해제
  - mouse motion: mapper로 preview 갱신, 이동은 기존 Y·Yaw 보존, 회전은 기존 position 보존
  - mouse release: `CommitPreview()` 한 번
  - Escape: `CancelPreview()`

  surface가 committed snapshot과 preview consumer 포트를 직접 구현한다. SceneDocument를 surface에 주입하거나 직접 수정해서는 안 된다.

- [x] **Step 3: WorldViewProjectionAdapter를 actor ID keyed로 구현한다**

  adapter는 `Actors : Node3D` 아래 `Actor_<sanitized-id>` root를 생성·재사용한다. sanitize collision의 첫 actor는 exact base 이름을 유지하고 다음 actor에는 원본 ID 기반 결정적 suffix를 붙인다. root에 순수 `WorldTransformMapper` 결과를 Godot `Vector3`로 바꿔 적용한다. `VisualRoot` 아래 기본 Capsule/Box body와 로컬 +X facing marker를 만들고 `OverlayRoot`를 분리한다.

  snapshot에 사라진 actor ID는 해당 adapter 소유 노드만 `QueueFree()`하고 dictionary에서 제거한다. preview는 대상 actor root의 표시 transform만 임시로 덮어쓰며 preview clear 시 latest committed snapshot 값을 복원한다. 다른 Godot node 상태를 문서 원본으로 읽지 않는다.

- [x] **Step 4: Inspector 컨트롤러와 장면 노드를 구현한다**

  `Main.tscn`에 설계 문서의 `TopViewSurface`, `WorldViewportContainer/WorldViewport/WorldRoot/Camera3D/DirectionalLight3D/Ground/Actors`, Inspector label·SpinBox 4개·Apply/Undo/Redo button을 정확한 이름으로 추가한다.

  `TransformInspectorController`는 selection event와 committed/preview 값을 입력에 반영한다. X/Z 범위 ±1000, Y ±100, step 0.1, Yaw 입력은 확정 시 `[0,360)`로 정규화한다. SpinBox는 범위 밖 text/value를 받아 controller가 오류를 표시하고 preview/commit 전에 거부하게 한다. valid preview 뒤 invalid 값이 들어오면 guarded preview clear로 두 뷰를 committed 상태로 복원하되 invalid SpinBox 값과 ErrorLabel은 보존하며, invalid Apply/Enter는 preview를 다시 만들지 않는다. 내부 반영 중 `ValueChanged` 재진입을 막는 guard를 두고, 사용자 값 변경은 preview를 시작·갱신한다. Apply 버튼 또는 각 SpinBox 내부 LineEdit의 Enter 제출은 preview를 명령 하나로 확정한다. Undo/Redo는 활성 preview를 취소한 뒤 session 메서드를 호출한다. `DocumentSession.HistoryChanged`는 성공한 stack transition 뒤에만 발생하며 Inspector는 이 event에서 버튼 상태를 갱신한다. 선택 없음·stale actor·범위 오류는 ErrorLabel에 한글로 표시하고 문서를 바꾸지 않는다.

- [x] **Step 5: Main 조립과 exact runtime smoke를 구현한다**

  `_Ready()` 순서를 고정한다.

  1. 필수 네 panel과 새 child node를 검증한다.
  2. runtime document에 `runtime-actor` t=0 actor를 추가해 revision 1을 만든다.
  3. `DocumentSession`, top/world adapter, committed/preview controller, Inspector를 조립한다.
  4. `ProjectCurrent()`로 top/world count 1과 actor count 1을 만든다.
  5. 기존 marker `PROJECTION_SYNC_READY revision=1 top=1 world=1`을 출력한다.
  6. actor root/VisualRoot/OverlayRoot와 committed transform을 확인한다.
  7. 실제 `_GuiInput` 회전 preview와 Escape 복원, body drag/release 이동 확정, Undo/Redo button signal, Inspector valid preview→X=1001 취소·invalid Apply·no-op Apply를 실행해 revision 4와 top/world count 4를 만든다.
  8. 별도 임시 Node3D/adapter/synthetic snapshot으로 sanitize collision actor 두 개의 실제 child 이름이 distinct/stable인지 확인하고 임시 root만 해제한다.
  9. 통합 assertion 성공 marker 뒤 다음 exact marker를 출력한다.

  ```text
  BASIC_EDITING_INTEGRATION_READY rotation_preview=1 escape_restore=1 drag_commit=1 undo_button=1 redo_button=1 inspector_reject=1 invalid_preview_cancel=1 inspector_apply_noop=1 collision_nodes=1
  ```

  ```text
  BASIC_EDITING_READY revision=4 selected=runtime-actor moved=1 undo=1 redo=1 top=4 world=4 actors=1
  ```

  `_ExitTree()`는 committed/preview controller와 Inspector event 구독을 모두 해제한다.

- [x] **Step 6: 구조·런타임 스크립트와 문서를 갱신한다**

  `Test-ProjectSkeleton.ps1`은 새 `.cs/.uid`, Application 표시 계약, Application 참조, scene node 이름을 검사한다. `Test-GodotRuntime.ps1`은 기존 marker와 exact integration/BASIC marker를 모두 요구한다.

  README에 사용 가능한 탑뷰 선택·이동·회전·Inspector·Undo/Redo, 현재 first-keyframe 정책, 실행/검증 명령을 한글로 기록한다. `docs/05-editor-architecture.md`는 최초 구현 계약을 반영하고 `docs/13-roadmap.md` 단계 2에서 이번에 완료된 항목과 후속 항목을 구분한다.

- [x] **Step 7: 모든 자동 검증을 메인에서 직렬 실행한다**

  Run:

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  & .\scripts\Test-GodotRuntime.ps1
  ```

  Expected: 모든 테스트 실패 0, 구조 PASS, 기존 두 marker와 integration/BASIC marker, `GODOT_RUNTIME_VERIFICATION=PASS`.

- [x] **Step 8: 실제 Forward+ GUI와 시각 상태를 확인한다**

  Godot console executable로 headless 없이 Main scene을 2초 실행한다. 출력에 RTX 5060의 `Vulkan ... Forward+`, 기존 marker와 BASIC marker가 있고 `ERROR:`가 없어야 한다. 화면에서 탑뷰 actor·방향 표시, 3D ground·placeholder, Inspector 입력이 동시에 보이는지 스크린샷으로 확인한다.

- [x] **Step 9: fresh 리뷰 후 메인이 Task 4를 커밋·푸시한다**

  spec reviewer는 좌표·first-key 정책·preview 비영구성·Undo/Redo·exact marker·게임패드 제외를 확인하고, quality reviewer는 Godot lifecycle, event dispose, node ownership, stale preview, 테스트 결함 탐지력을 확인한다. 모든 Critical/Important 수정과 fresh 재리뷰 후 Task 4 경로만 정확히 스테이징한다.

  ```powershell
  git add -- 'src/PvpGuide.Application/Sessions/ActorDisplayInfo.cs' 'src/PvpGuide.Application/Sessions/DocumentSession.cs' 'tests/PvpGuide.Application.Tests/DocumentSessionTests.cs' 'src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs' 'src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs.uid' 'src/PvpGuide.Editor/Features/TopView/TopViewCoordinateMapper.cs.uid' 'src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs' 'src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs.uid' 'src/PvpGuide.Editor/Features/ViewportSync/WorldTransformMapper.cs' 'src/PvpGuide.Editor/Features/ViewportSync/WorldTransformMapper.cs.uid' 'src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs' 'src/PvpGuide.Editor/Features/Inspector/TransformInspectorController.cs.uid' 'src/PvpGuide.Editor/Scenes/Main/Main.tscn' 'src/PvpGuide.Editor/Scenes/Main/Main.cs' 'tests/PvpGuide.Editor.Tests/WorldTransformMapperTests.cs' 'scripts/Test-ProjectSkeleton.ps1' 'scripts/Test-GodotRuntime.ps1' 'README.md' 'docs/05-editor-architecture.md' 'docs/13-roadmap.md' 'docs/superpowers/plans/2026-08-27-basic-topview-editing.md'
  git commit -m 'feat: 탑뷰 편집과 3D 플레이스홀더 연결'
  git push
  ```

  푸시 후 local/tracking/remote SHA 일치와 clean status를 확인한다. 사용자가 실제 조작이 정상이라고 보고하기 전에는 `working/...` 태그를 만들지 않는다.
