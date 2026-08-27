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
- Create: `src/PvpGuide.Domain/Actors/ActorTrack.cs`
- Create: `src/PvpGuide.Domain/Timeline/TransformKeyframe.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/SceneProjectionController.cs`
- Test: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`

**Interfaces:**
- Consumes: 가이드 좌표계 `x=오른쪽+`, `y=아래쪽+`, 방향각 `0/90/180/270`
- Produces: `SceneDocument`, `ActorTrack`, `TransformKeyframe`, 2D/3D 동기화 이벤트

- [ ] **Step 1: 좌표와 키프레임 보간 테스트를 먼저 작성하고 실패를 확인한다**

  두 키프레임 사이 위치·각도 보간, 각도 360도 경계의 최단 회전, 같은 시간 키프레임 거부를 검사한다.

- [ ] **Step 2: 최소 도메인 모델을 구현한다**

  문서 ID, 스키마 버전, 시간축, 배우 트랙과 선택 상태를 분리하며 Godot 노드 타입을 도메인에 노출하지 않는다.

- [ ] **Step 3: 탑뷰와 3D 투영 컨트롤러를 연결한다**

  문서 변경 한 번이 두 뷰를 갱신하고, 한 뷰의 조작은 명령을 통해 문서에 반영되도록 한다.

- [ ] **Step 4: 테스트와 헤드리스 장면 로드를 검증한다**

  Expected: 모든 보간·동기화 테스트 통과, Godot 장면 로드 종료 코드 0.

- [ ] **Step 5: 커밋하고 푸시한다**

  도메인, 동기화, 테스트 파일만 커밋하고 현재 브랜치를 푸시한다.

### Task 5: 저장·가이드 가져오기·렌더링 기반

**Files:**
- Create: `src/PvpGuide.Infrastructure/Serialization/SceneDocumentSerializer.cs`
- Create: `src/PvpGuide.Infrastructure/Import/TopviewGuideV1Importer.cs`
- Create: `src/PvpGuide.Editor/Features/Rendering/RenderQueue.cs`
- Test: `tests/PvpGuide.Infrastructure.Tests/TopviewGuideV1ImporterTests.cs`
- Test: `tests/PvpGuide.Infrastructure.Tests/SceneRoundTripTests.cs`

**Interfaces:**
- Consumes: `gangqueen-topview-guide-v1` JSON과 `SceneDocument`
- Produces: 버전형 JSON 저장, 원자적 저장, 가이드 가져오기, 프레임 렌더 작업 큐

- [ ] **Step 1: 실제 가이드 고정 샘플로 가져오기 실패 테스트를 작성한다**

  네 캐릭터 역할, 좌표, 방향각, 키프레임과 잠금 대상이 보존되는지 검사한다.

- [ ] **Step 2: 저장 왕복 실패 테스트를 작성한다**

  저장 후 다시 연 문서가 ID, 시간축, 배우·키프레임·효과 데이터를 잃지 않는지 검사한다.

- [ ] **Step 3: 최소 가져오기와 직렬화를 구현한다**

  임시 파일 쓰기 → 검증 → 원자적 교체를 사용하고 스키마 버전을 검사한다.

- [ ] **Step 4: Godot Movie Maker와 FFmpeg 인계 구조를 구현한다**

  렌더 큐는 프레임 출력 위치, 해상도, FPS, 시작·종료 시간과 FFmpeg 인코딩 명령을 명시적으로 보관한다.

- [ ] **Step 5: 전체 테스트를 검증하고 커밋·푸시한다**

  Expected: 가져오기·왕복·렌더 작업 검증 테스트 실패 0개.
