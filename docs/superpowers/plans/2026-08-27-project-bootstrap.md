# 3D PvP 편집기 기반 구축 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows 11 오프라인 환경에서 탑뷰와 3D 편집을 동시에 제공하는 DARK SOULS REMASTERED PvP 교육 영상 편집기의 문서화된 기반과 구현 순서를 확정한다.

**Architecture:** Godot 4.7.2 Stable .NET과 C#으로 단일 오프라인 데스크톱 애플리케이션을 만든다. `SceneDocument`를 단일 진실 공급원으로 두고 탑뷰와 3D 뷰를 같은 상태의 투영으로 구성하며, 게임 원본 자산은 Git과 배포본에서 분리한 선택적 로컬 입력으로 취급한다.

**Tech Stack:** Windows 11, Godot 4.7.2 Stable .NET, C#, Forward+, JSON, FFmpeg, 선택적 Blender 자산 파이프라인

**Spec:** `docs/superpowers/specs/2026-08-27-3d-pvp-editor-architecture-design.md`

## Global Constraints

- Windows 11 전용 오프라인 독립 실행 프로그램이다.
- 프로젝트와 관련 도구·자산·출력은 `D:\3D-render` 아래에서 관리한다.
- 탑뷰와 3D 편집기는 같은 `SceneDocument`를 동시에 편집한다.
- 런타임 네트워크, 원격 분석, 클라우드 저장 의존성을 두지 않는다.
- 게임 원본·추출 자산과 외부 도구는 Git 및 배포 패키지에서 제외한다.
- 작업 단위가 끝나면 검증 후 명시 경로만 스테이징하고 커밋·푸시한다.

---

### Task 1: 저장소 정책과 조사 기록

**Files:**
- Create: `.gitignore`
- Create: `AGENTS.md`
- Create: `docs/research/dsr-animation-assets.md`

**Interfaces:**
- Consumes: 사용자 환경, Git 원격, 로컬 DSR 설치 경로
- Produces: 후속 작업이 따를 자산 격리·검증·커밋·태그 정책

- [x] **Step 1: 로컬 전용 경로 제외 규칙을 작성한다**

  `local-assets/`, `tools/`, `cache/`, `exports/`, Godot/.NET 생성물을 `.gitignore`에 기록한다.

- [x] **Step 2: 작업 및 정상 버전 태그 규칙을 작성한다**

  사용자 정상 동작 보고 → 구체적 재확인 → 긍정 응답 → 검증·커밋·푸시 → 주석 태그 생성·푸시 순서를 `AGENTS.md`에 기록한다.

- [x] **Step 3: DSR 자산 조사 근거를 기록한다**

  파일 개수, 경로, 도구 버전·체크섬, 추출 결과, WitchyBND의 TAE 제어문자 제한과 후속 단계를 기록한다.

- [x] **Step 4: 검증한다**

  Run: `git check-ignore -v local-assets/project.json tools/WitchyBND-3.0.1.0/WitchyBND.exe`

  Expected: 두 파일 모두 `.gitignore` 규칙으로 제외된다.

- [x] **Step 5: 커밋하고 푸시한다**

  ```powershell
  git add -- .gitignore AGENTS.md docs/research/dsr-animation-assets.md docs/superpowers/plans/2026-08-27-project-bootstrap.md
  git commit -m "docs: 프로젝트 정책과 DSR 자산 조사 기록"
  git push -u origin codex/animation-assets-prep
  ```

### Task 2: 제품과 아키텍처 문서

**Files:**
- Create: `README.md`
- Create: `docs/01-project-overview.md`부터 `docs/14-git-release-policy.md`
- Create: `docs/superpowers/specs/2026-08-27-3d-pvp-editor-architecture-design.md`

**Interfaces:**
- Consumes: 가이드의 `gangqueen-topview-guide-v1` 좌표·키프레임 형식과 Task 1 정책
- Produces: 구현자가 따를 모듈 경계, 데이터 계약, 렌더·저장·테스트·릴리스 기준

- [x] **Step 1: 설계 명세를 작성한다**

  단일 `SceneDocument`, 명령 기반 Undo/Redo, 2D/3D 투영, 전투 시각화, 로컬 자산 어댑터와 오프라인 경계를 정의한다.

- [x] **Step 2: 상세 문서를 책임별로 분리한다**

  제품, 요구사항, 시스템·데이터·편집기·전투·렌더·네트워크·성능·저장소·품질·복구·로드맵·Git 정책을 각각 독립 문서로 만든다.

- [x] **Step 3: README를 작성한다**

  프로젝트 목적, 핵심 기능, 기술 선택, 디렉터리, 개발 시작, 자산 정책, 문서 색인을 한글로 제공한다.

- [x] **Step 4: 문서 링크와 누락을 검증한다**

  Run: `rg -n "[T]BD|[T]ODO|작성[ ]예정" README.md AGENTS.md docs`

  Expected: 미완성 표식이 없다.

- [x] **Step 5: 커밋하고 푸시한다**

  변경된 문서 경로를 명시해 스테이징하고 `docs: 전체 아키텍처와 개발 가이드 작성`으로 커밋한 뒤 현재 브랜치를 푸시한다.

### Task 3: Godot 프로젝트 골격

**Files:**
- Create: `src/PvpGuide.Editor/PvpGuide.Editor.csproj`
- Create: `src/PvpGuide.Editor/PvpGuide.Editor.sln`
- Create: `src/PvpGuide.Editor/project.godot`
- Create: `src/PvpGuide.Editor/Scenes/Main/Main.tscn`
- Create: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Create: `src/PvpGuide.Domain/PvpGuide.Domain.csproj`
- Create: `src/PvpGuide.Domain/DomainAssembly.cs`
- Create: `tests/PvpGuide.Domain.Tests/PvpGuide.Domain.Tests.csproj`
- Create: `tests/PvpGuide.Domain.Tests/DomainAssemblyTests.cs`
- Create: `scripts/Test-ProjectSkeleton.ps1`
- Create: `scripts/Test-GodotRuntime.ps1`

**Interfaces:**
- Consumes: Task 2의 모듈 및 디렉터리 계약
- Produces: 실행 가능한 Windows 11 Godot .NET 편집기와 테스트 프로젝트

- [x] **Step 1: 실행 실패를 확인하는 스모크 테스트를 작성한다**

  `scripts/Test-ProjectSkeleton.ps1`을 작성해 `project.godot`, Godot `.csproj`와 `.sln`, 메인 장면·스크립트, Domain 프로젝트·어셈블리 소스, xUnit 테스트 프로젝트·소스가 모두 존재하는지 검사한다. 또한 C#·Forward Plus·메인 장면 경로, `Godot.NET.Sdk/4.7.2`, `net8.0`, Domain 프로젝트 참조와 네 UI 패널 이름을 확인해 빈 골격이나 0개 테스트 구성을 통과시키지 않는다.

- [x] **Step 2: 최소 Godot .NET 프로젝트와 메인 장면을 작성한다**

  `PvpGuide.Editor.csproj`와 `PvpGuide.Editor.sln`을 만들고 Godot .NET SDK `4.7.2`, 대상 프레임워크 `net8.0`을 고정한다. `project.godot`에는 C#·Forward Plus와 `res://Scenes/Main/Main.tscn`을 설정하고, 메인 장면의 루트 UI에 `TopViewPanel`, `WorldViewPanel`, `TimelinePanel`, `InspectorPanel`을 배치한다. `Main.cs`는 네 패널을 확인한 뒤 `PROJECT_RUNTIME_READY`를 출력한다.

  Godot과 무관한 `PvpGuide.Domain.csproj` 및 `DomainAssembly.cs`는 일반 .NET 8 라이브러리로 두고, `PvpGuide.Domain.Tests.csproj`와 `DomainAssemblyTests.cs`는 xUnit v3로 실제 Domain 어셈블리 이름을 단언한다.

- [x] **Step 3: 프로젝트 구조와 Domain 테스트를 검증한다**

  Run: `& .\scripts\Test-ProjectSkeleton.ps1`, `$env:NUGET_PACKAGES = 'D:\3D-render\tools\nuget-packages'`, `dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo`.

  Expected: 구조 검증 스크립트의 종료 코드가 0이고 `PROJECT_SKELETON_VERIFICATION=PASS`가 출력된다. `dotnet test`는 xUnit 테스트 실패 0개와 종료 코드 0이어야 하며, 오프라인 패키지 캐시 경로를 사용한다.

- [x] **Step 4: Godot 런타임을 끝까지 검증한다**

  `scripts/Test-GodotRuntime.ps1`을 실행한다. 스크립트는 `D:\3D-render\tools\godot\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`를 사용해 (1) `dotnet build`로 C# 프로젝트를 빌드하고, (2) `--headless --import`로 리소스를 가져오고, (3) `--headless --build-solutions --quit`로 Godot 솔루션을 빌드한 뒤, (4) `--headless --scene res://Scenes/Main/Main.tscn --quit-after 2`로 실제 메인 장면을 실행한다.

  Expected: 네 단계 모두 종료 코드 0이고 오류 출력이 없으며, 장면 실행 출력에 `PROJECT_RUNTIME_READY`, 스크립트 최종 출력에 `GODOT_RUNTIME_VERIFICATION=PASS`가 포함된다.

- [x] **Step 5: 커밋하고 푸시한다**

  Task 3에서 변경한 모든 확인된 경로(프로젝트, Domain, 테스트, 검증 스크립트와 관련 문서)를 명시해 스테이징하고 `feat: Godot 편집기 골격 추가`로 커밋·푸시한다.

### Task 4: SceneDocument와 동시 뷰 투영

**Files:**
- Create: `src/PvpGuide.Domain/SceneDocument.cs`
- Create: `src/PvpGuide.Domain/SceneSnapshot.cs`
- Create: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Create: `src/PvpGuide.Domain/Timeline/TransformKeyframe.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/SceneProjectionController.cs`
- Modify: `src/PvpGuide.Editor/PvpGuide.Editor.csproj` (Domain ProjectReference)
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs` (런타임 조립 smoke)
- Test: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`
- Create: `tests/PvpGuide.Editor.Tests/PvpGuide.Editor.Tests.csproj`
- Create: `tests/PvpGuide.Editor.Tests/SceneProjectionControllerTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1` (Task 4 파일과 Editor→Domain 참조 검사)
- Modify: `scripts/Test-GodotRuntime.ps1` (projection marker 검증)

**Interfaces:**
- Consumes: 가이드 좌표계 `x=오른쪽+`, `y=아래쪽+`, 방향각 `0/90/180/270`
- Produces: `SceneDocument`, `SceneSnapshot`, `ActorTrack`, `TransformKeyframe`, 2D/3D 동기화 이벤트와 동일 snapshot 인스턴스 투영

- [x] **Step 1: Domain 및 Editor 테스트를 먼저 작성하고 RED를 확인한다**

  `SceneDocumentTests.cs`에서 유한 좌표·키프레임 입력 검증, 시간 오름차순과 동일 시간 거부, 첫 키 이전·마지막 키 이후의 고정 상태, 위치 선형 보간을 검사한다. yaw는 0/360 경계 최단 경로와 정확히 180° 동률의 양의 방향을 검사하며, 필수 사례로 (t=2, `(-2,4,10)`, 350°)와 (t=6, `(6,-4,2)`, 10°)를 t=3에서 `(0,2,8)`, 355°, t=4에서 `(2,0,6)`, 0°로 단언한다. 역방향 10→350의 중간값 0°, 0→180의 90°, 180→0의 270°도 포함한다. `SceneProjectionControllerTests.cs`에서는 snapshot 1회 생성, top/world 각각 동일 인스턴스 1회 전달, 같은 revision 중복 억제, 다음 revision 전달, Dispose 이후 미전달을 검사한다. 테스트를 먼저 실행해 누락 타입·행동 때문에 실패하는 실제 RED 결과를 기록한다.

- [x] **Step 2: SceneDocument와 평가 snapshot 도메인 모델을 구현한다**

  `Position3`, `TransformKeyframe`, `ActorTrack`, `SceneDocument`, `SceneSnapshot`을 구현한다. 문서 ID·`pvp-guide-scene/1` 스키마·길이·FPS·고유 배우 목록·monotonic revision을 보유하고, 성공한 배우/키프레임 추가마다 revision을 1 증가시키며 변경 이벤트를 정확히 한 번 발생시킨다. 실패한 변경은 revision·이벤트·기존 데이터를 보존한다. `CreateSnapshot(timeSeconds)`는 문서 ID, revision, 평가 시간과 배우별 평가 변환을 불변·방어 복사 형태로 반환하며 Godot `Node`, `Vector*`, `Resource`나 선택 배우·현재 시간·활성 도구 같은 세션 상태를 노출하지 않는다.

- [x] **Step 3: Editor 참조와 동시 투영 컨트롤러를 연결한다**

  `PvpGuide.Editor.csproj`에 Domain ProjectReference를 추가하고 `ISceneProjectionConsumer.Apply(SceneSnapshot)` 포트와 `SceneProjectionController`를 구현한다. controller는 하나의 snapshot source와 서로 다른 top/world consumer를 생성자 주입받으며 문서 변경 event 1회마다 `CreateSnapshot`을 한 번 호출하고 반환된 동일 snapshot을 두 소비자에게 각각 한 번 전달한다. 동일 revision 이벤트는 중복 전달하지 않고, 다음 revision은 각각 한 번 전달하며 Dispose 이후 전달하지 않는다. 뷰 소비자가 문서를 직접 수정하거나 서로의 상태를 복사하지 않도록 경계를 유지한다.

- [x] **Step 4: Main smoke, 구조 검사와 전체 테스트를 검증한다**

  `Main.cs`에서 최소 실제 Domain 문서와 두 Panel 소비자를 조립하고 변경 1회 후 `PROJECTION_SYNC_READY revision=1 top=1 world=1`을 출력한다. controller는 `_ExitTree`에서 해제한다. `Test-ProjectSkeleton.ps1`은 새 Domain·Editor 소스/테스트 파일과 Editor→Domain ProjectReference를 검사하고, `Test-GodotRuntime.ps1`은 projection marker를 검증하도록 갱신한다.

  Run:

  ```powershell
  $env:NUGET_PACKAGES = 'D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  & .\scripts\Test-GodotRuntime.ps1
  ```

  Expected: Domain·Editor 테스트 실패 0, 구조 검사 PASS, Godot 헤드리스 장면 로드 종료 코드 0, `PROJECT_RUNTIME_READY`, `PROJECTION_SYNC_READY revision=1 top=1 world=1`, `GODOT_RUNTIME_VERIFICATION=PASS` 출력.

- [x] **Step 5: 변경 경로를 확인하고 커밋·푸시한다**

  Task 4에서 변경한 Domain·Editor·Main·테스트·검증 스크립트와 이 계획 문서 및 README만 명시적으로 스테이징하고, 메인 에이전트가 최신 검증 후 커밋·푸시한다. `bin`, `obj`, `.godot`, 도구·캐시는 포함하지 않는다.

### Task 5: 저장·가이드 가져오기·렌더링 기반

**Files:**
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Create: `src/PvpGuide.Domain/Timeline/ActionKeyframe.cs`
- Create: `src/PvpGuide.Domain/Timeline/LockOnKeyframe.cs`
- Create: `src/PvpGuide.Infrastructure/PvpGuide.Infrastructure.csproj`
- Create: `src/PvpGuide.Infrastructure/Properties/AssemblyInfo.cs`
- Create: `src/PvpGuide.Infrastructure/Serialization/SceneDocumentSerializer.cs`
- Create: `src/PvpGuide.Infrastructure/Import/TopviewGuideV1Importer.cs`
- Create: `src/PvpGuide.Editor/Features/Rendering/RenderQueue.cs`
- Create: `src/PvpGuide.Editor/Features/Rendering/RenderQueue.cs.uid`
- Create: `samples/guides/synthetic-topview-v1.scene.json`
- Create: `tests/PvpGuide.Infrastructure.Tests/PvpGuide.Infrastructure.Tests.csproj`
- Create: `tests/PvpGuide.Infrastructure.Tests/TopviewGuideV1ImporterTests.cs`
- Create: `tests/PvpGuide.Infrastructure.Tests/SceneRoundTripTests.cs`
- Create: `tests/PvpGuide.Editor.Tests/RenderQueueTests.cs`
- Modify: 구조 검사·README·계획 문서

**Interfaces:**
- Consumes: `gangqueen-topview-guide-v1` JSON과 `SceneDocument`
- Produces: 버전형 JSON 저장, 원자적 저장, 가이드 가져오기, 프레임 렌더 작업 큐

- [x] **Step 1: 합성 V1 fixture와 importer RED 테스트를 고정한다**

  `samples/guides/synthetic-topview-v1.scene.json`을 저작권 없는 합성 입력으로 작성하고 `format`, `coordinate_system`, `backstab_rules`, `scene`, `evaluations` 및 알 수 없는 원본 필드를 포함한다. importer 테스트는 네 역할(`host`, `invader`, `phantom1`, `phantom2`), keyframe ID `10/20/30`, t=`0.25/0.9/1.4`, 첫 frame의 guide 좌표/yaw와 origin `(100,200)`·scale `0.1`·ground `0`·FPS `30` 변환 결과 `(0,0,0)`, `(1,0,0)`, `(-2,0,2)`, `(3,0,2)`를 단언한다. scene name/note, displayName/role, actions, lock_on/target, duration `1.4`, phantom1의 disabled target 보존, phantom2의 attack/idle 키를 확인하고 `current_index`는 문서에 들어가지 않는 선택 힌트로 검증한다. 잘못된 format·좌표 선언·중복 actor/time은 실패하고 지원 한계 warning과 raw metadata는 보존되는지 검사한다. 먼저 테스트를 실행해 실제 RED를 기록한다.

- [x] **Step 2: 저장 왕복·원자 저장 실패 테스트를 작성한다**

  `SceneRoundTripTests.cs`에서 `SceneDocument`의 ID/name/note/duration/FPS/actors/Transform·Action·LockOn 트랙과 ImportMetadata가 roundtrip 후 동일하고 revision 0인지 검사한다. serializer는 System.Text.Json 내부 DTO, camelCase, indented UTF-8, strict numbers, unknown member disallow, 정확한 `pvp-guide-scene/1` 스키마를 사용하며 revision/event/current time과 선택·UI·Godot 상태는 저장하지 않는다. `SaveAtomicAsync` 테스트는 destination 절대 경로·`.pvpscene.json`·존재하는 부모, `D:\3D-render\cache\tests\<guid>` exact root, 같은 디렉터리 CreateNew temp·flush·재 Deserialize·`File.Move(..., true)`를 검사한다. 실패/취소 시 기존 destination byte 보존과 temp best-effort 삭제, move 실패 시 검증된 temp 보존, 성공 시 temp 부재를 단언한다.

- [x] **Step 3: Domain 확장과 최소 importer/serializer를 구현한다**

  `SceneDocument`/`ActorTrack`에 방어 복사와 시간순 목록을 확장하고 `ActionKeyframe`의 비어 있지 않은 ID/actionKey, 유한·0 이상 시간·동일 Action 시간 중복 검증을 구현한다. `LockOnKeyframe`은 ID·시간·Enabled·nullable TargetActorId를 검증하고 disabled target 후보를 보존하며 enabled이면 target 필수, 다른 존재 배우만 허용한다. 모든 트랙 시간은 문서 범위 안이어야 한다. importer는 guide x→world X, guide y→world Z, world Y=ground로 변환하고 duration을 max time으로 정하며 metadata/raw payload와 warning을 반환한다. serializer는 버전 확인과 위 원자 저장 계약을 구현한다.

- [x] **Step 4: RenderQueue와 Godot/FFmpeg 인계 구조를 구현한다**

  `RenderJob`은 Guid ID, document ID/revision, `D:\3D-render` 하위 output, width/height/FPS, decimal start/end를 검증한다. `[start,end)`에 대해 `FrameCount=ceil((end-start)*fps)`, `GetTimeSeconds(n)=start+n/fps`를 사용하고 누적 덧셈을 금지한다. 기본 `frame_%06d.png`, start number 1, FFmpeg `.exe` 절대 경로와 defensive-copied argument array(`-n` 포함)를 보관하며 셸 문자열/수동 quoting과 Godot/Process 호출은 금지한다. GetFullPath+GetRelativePath containment로 root 자체·상대경로·`..`·`D:\3D-render-other`를 거부하고, lock 기반 FIFO `Snapshot`/`Count`/`TryPeek`/`TryDequeue` 및 dequeue 후 ID 재사용 거부를 테스트한다.

- [x] **Step 5: 정확한 전체 검증을 실행하고 결과를 기록한다**

  저장소 루트(`D:\3D-render`)에서 다음 명령을 순서대로 실행한다.

  ```powershell
  $env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
  dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
  dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
  & .\scripts\Test-ProjectSkeleton.ps1
  & .\scripts\Test-GodotRuntime.ps1
  ```

  세 테스트 프로젝트와 두 스크립트 모두 종료 코드 0, importer/roundtrip/atomic save/RenderQueue 실패 0개여야 한다. 실제 FFmpeg/Godot Movie Maker 프로세스는 실행하지 않는다. 최종 변경은 Task 5 파일 목록과 README·계획 문서만 명시적으로 스테이징하며, 커밋·푸시는 메인 에이전트가 수행한다.
