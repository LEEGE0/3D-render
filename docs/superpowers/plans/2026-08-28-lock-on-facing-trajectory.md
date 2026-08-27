# Lock-on 방향 계산과 이동 궤적 구현 계획

> **For Codex:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to execute this plan task-by-task. 각 작업의 구현 하위 에이전트와 독립 리뷰 하위 에이전트는 파일을 stage/commit/push하지 않는다. 최종 검증과 Git 작업은 메인 에이전트만 수행한다.

**Goal:** 기존 transform·Lock-on 키프레임에서 파생된 결정적 actor facing과 자유/Lock-on 방향 궤적을 같은 불변 투영 프레임으로 TopView와 WorldView에 표시한다.

**Architecture:** Domain은 작성 transform을 보존한 채 resolved facing, sample plan과 paired trajectory를 순수 계산한다. Application은 현재 snapshot과 motion-revision별 trajectory geometry를 하나의 `SceneProjectionFrame`으로 묶고 재진입을 직렬화한다. Editor는 불변 frame을 받아 2D draw data와 world-fixed reusable 3D mesh만 갱신한다. 파생 결과는 저장하지 않고 schema `pvp-guide-scene/2`를 유지한다.

**Tech Stack:** Windows 11, Godot 4.7.2 Stable .NET, C#/.NET 8, xUnit, PowerShell, Git/GitHub SSH.

**Design:** `docs/superpowers/specs/2026-08-28-lock-on-facing-trajectory-design.md`

**Execution rules:**

- 모든 `dotnet test`는 `$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'`를 먼저 지정하고 직렬 실행한다.
- 테스트 fixture의 기대값을 production 결과에서 다시 계산하지 않고 literal hand-derived 값으로 둔다.
- 새 public collection은 입력을 방어 복사하고 외부 mutation을 거부한다.
- 한 작업에서 RED를 실제로 확인한 뒤 GREEN 구현을 시작한다.
- 같은 파일이나 순차 의존성이 있는 작업은 병렬화하지 않는다. Task 7과 8처럼 기반이 끝난 뒤 파일 경계가 독립적인 작업만 하위 에이전트로 병렬화한다.
- 각 논리 단위가 GREEN과 독립 리뷰를 통과하면 메인 에이전트가 관련 파일만 stage하고 커밋·푸시한다.

**모든 Task의 PowerShell 실행 전제:** 각 명령 블록은 새 PowerShell process일 수 있으므로 테스트·build·script 실행 직전 같은 block에서 반드시 `$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'`를 설정한다. 아래 block에 이 줄이 보이지 않더라도 생략 권한을 뜻하지 않으며 실행자는 같은 shell의 첫 명령으로 추가해야 한다. 병렬 하위 에이전트는 test를 동시에 돌리지 않고 메인 에이전트가 통합 뒤 직렬 실행한다.

**모든 논리 단위의 메인 Git 체크리스트:**

```powershell
git status --short --untracked-files=all
git diff -- <이번 단위의 확인된 경로>
git add -- <이번 단위의 확인된 경로만>
git diff --cached --check
git diff --cached -- <이번 단위의 확인된 경로>
git commit -m "<계획에 적힌 메시지>"
git push origin HEAD
git status --short --untracked-files=all
```

하위 에이전트는 위 Git 명령을 실행하지 않는다. 메인 에이전트는 unrelated 사용자 변경, 이전 Task 파일과 예상하지 못한 untracked file을 stage하지 않는다.

---

## Task 1: 0초 문서 PlaybackClock 계약

**Files:**

- Modify: `tests/PvpGuide.Application.Tests/PlaybackClockTests.cs`
- Modify: `src/PvpGuide.Application/Playback/PlaybackClock.cs`

### Step 1: RED 테스트 추가

다음 동작을 한 테스트에 고정한다.

```csharp
[Fact]
public void Zero_duration_remains_paused_at_zero_for_all_controls()
{
    var clock = new PlaybackClock(0, 30);
    var changes = 0;
    clock.Changed += (_, _) => changes++;

    clock.Play();
    clock.Advance(1);
    clock.Seek(0);
    clock.Stop();

    Assert.Equal(0, clock.CurrentTimeSeconds);
    Assert.False(clock.IsPlaying);
    Assert.Equal(0, changes);
}
```

기존 negative/non-finite duration 거부 테스트는 유지하고 `0`만 허용한다.

### Step 2: RED 확인

Run:

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~PlaybackClockTests
```

Expected: 생성자에서 duration 0을 거부해 새 테스트 FAIL.

### Step 3: 최소 구현

- 생성자 검증을 `durationSeconds < 0`으로 바꾼다.
- duration 0의 `Play`/`Toggle`은 playing 전이를 발행하지 않는다.
- `Advance`, `Seek(0)`, `Stop`은 시간·상태가 실제로 같으면 event를 만들지 않는다.
- duration 양수의 기존 end clamp 의미는 바꾸지 않는다.

### Step 4: GREEN 확인

같은 필터 테스트 후 Application 전체 테스트를 실행한다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
```

### Step 5: 리뷰·커밋·푸시

- 독립 리뷰가 0초 외 기존 clock semantics 회귀가 없는지 확인한다.
- Main이 exact files만 stage한다.
- Commit: `fix: 0초 문서 재생 시계 지원`
- Push: `git push origin HEAD`

---

## Task 2: 순수 Lock-on facing 수학

**Files:**

- Create: `src/PvpGuide.Domain/Timeline/FacingResolutionKind.cs`
- Create: `src/PvpGuide.Domain/Timeline/EvaluatedActorFacing.cs`
- Create: `src/PvpGuide.Domain/Timeline/LockOnFacingEvaluator.cs`
- Create: `tests/PvpGuide.Domain.Tests/LockOnFacingEvaluatorTests.cs`

### Step 1: RED fixture 작성

Godot 참조 없는 테스트로 다음 literal을 고정한다.

- actor `(0,0,0)`, target `(4,7,3)` → `36.86989764584402°`
- offset `-30°` → `6.86989764584402°`
- `+X/+Z/-X/-Z` → `0/90/180/270°`
- `Snap`: source lock time의 방향을 이후 actor/target 이동에도 hold
- `Continuous`: target `(4,0,0)→(0,0,4)`에서 `0/.5/1초` → `0/45/90°`
- `KeyframeOnly`: nonzero offset에도 authored yaw 유지
- disabled/before-first-lock → authored yaw
- `CoincidenceEpsilon=1e-6`의 안·경계·밖
- lock 시작부터 위치 일치 → 현재 authored yaw
- 유효 방향 뒤 위치 일치 → 같은 lock 구간의 latest valid 방향
- 이전 유효 `90°` 뒤 상대속도 0으로 epsilon 내부를 유지 → `CoincidentPrevious`, `90°`
- 상대 위치 `(-1+2t, CoincidenceEpsilon)`의 `t=.5` 접선/discriminant 0 → 경계 포함 coincidence, 왼쪽 극한 `90°`
- source부터 한 segment 전체가 epsilon 내부이고 authored yaw `37°` → `CoincidentAuthoredFallback`, `37°`
- transform key가 아닌 교차 시각의 왼쪽 piecewise-linear 극한
- target dictionary 누락 → `TargetUnavailableFallback`, finite authored yaw
- 새 lock keyframe에서 previous direction regime reset

테스트 helper는 `ActorTrack`과 dictionary를 직접 만들어 정상 문서 validation 밖의 missing-target seam도 검증한다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~LockOnFacingEvaluatorTests
```

Expected: 새 타입 미존재로 compile FAIL.

### Step 3: 최소 타입과 evaluator 구현

핵심 API:

```csharp
public static class LockOnFacingEvaluator
{
    public const double CoincidenceEpsilon = 1e-6;

    public static EvaluatedActorFacing Evaluate(
        ActorTrack actor,
        IReadOnlyDictionary<string, ActorTrack> actorsById,
        double timeSeconds);
}
```

- horizontal squared distance `<= 1e-12`를 coincidence로 판정한다.
- `Snap`은 `SourceKeyframeId`의 frame time에서 actor/target을 평가한다.
- `Continuous`의 coincidence는 source lock time, actor/target transform key time과 current time의 정렬 합집합을 뒤로 탐색한다.
- epsilon 경계 진입 interval에서는 상대 위치 선분과 epsilon 원의 마지막 교점을 quadratic으로 구해 그 왼쪽 극한 방향을 사용한다.
- quadratic은 `a=relativeVelocitySquared`가 epsilon 이하인 정지 상대속도, 음수/0 discriminant, 접선, segment 전체가 epsilon 내부/경계인 경우를 분기한다. root는 `[0,1)` 안의 마지막 valid→coincident 경계만 택하고 없으면 이전 segment 또는 authored fallback을 사용한다.
- 과거 render 호출 상태를 저장하지 않는다.
- 출력은 항상 finite `[0,360)`이다.

### Step 4: GREEN·회귀 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~LockOnFacingEvaluatorTests
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
```

### Step 5: 리뷰·커밋·푸시

- 수학 리뷰가 exact 180, epsilon quadratic, regime reset을 직접 대조한다.
- Commit: `feat: Lock-on 방향 평가 수학 추가`

---

## Task 3: SceneSnapshot facing과 MotionRevision

**Files:**

- Modify: `src/PvpGuide.Domain/SceneSnapshot.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Modify: `tests/PvpGuide.Domain.Tests/SceneDocumentTests.cs`

### Step 1: RED 테스트

- `CreateSnapshot(t).ActorFacings`가 모든 actor를 포함한다.
- `ActorTransforms.YawDegrees`는 기존 authored 값이고 `ActorFacings.YawDegrees`만 lock 결과다.
- dictionary defensive copy와 read-only mutation 거부.
- 기존 4-arg/5-arg snapshot 생성자는 authored facing과 `MotionRevision=Revision`을 만든다.
- Action Add/Update/Delete는 `Revision`만 증가시킨다.
- actor Add, transform Add/Update/Delete, Lock-on Add/Update/Delete는 `Revision`과 `MotionRevision`을 증가시킨다.
- no-op/stale/거부된 mutation은 둘 다 바꾸지 않는다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SceneDocumentTests
```

### Step 3: 최소 구현

- `SceneSnapshot`에 `MotionRevision`과 방어 복사 `ActorFacings`를 additive overload로 추가한다.
- facing map의 unknown actor key는 거부하고 누락 actor는 authored fallback으로 채운다.
- `SceneDocument`는 `_motionRevision`을 별도 관리한다.
- `RaiseChanged(bool affectsMotion)` 하나로 mutation 분류를 명시한다.
- `CreateSnapshot`은 모든 authored transform/timeline을 먼저 만든 뒤 같은 actor dictionary로 facing을 두 번째 pass에서 평가한다.

### Step 4: GREEN 확인

Domain 전체를 실행한다.

### Step 5: 리뷰·커밋·푸시

- 기존 snapshot constructor와 모든 mutation call site 분류를 독립 리뷰한다.
- Commit: `feat: snapshot에 Lock-on facing 상태 추가`

---

## Task 4: 결정적 sample plan과 paired trajectory

**Files:**

- Create: `src/PvpGuide.Domain/Timeline/TrajectoryAnchorKind.cs`
- Create: `src/PvpGuide.Domain/Timeline/TrajectorySamplingSettings.cs`
- Create: `src/PvpGuide.Domain/Timeline/TrajectorySamplePlan.cs`
- Create: `src/PvpGuide.Domain/Timeline/MovementTrajectorySample.cs`
- Create: `src/PvpGuide.Domain/Timeline/ActorMovementTrajectory.cs`
- Create: `src/PvpGuide.Domain/Timeline/MovementTrajectorySet.cs`
- Create: `src/PvpGuide.Domain/Timeline/MovementTrajectoryEvaluator.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Create: `tests/PvpGuide.Domain.Tests/MovementTrajectoryEvaluatorTests.cs`

### Step 1: RED 테스트

- 30FPS 1초 plan은 정수 `k/rate`, exact `0`·duration과 key time을 포함한다.
- 24/29/60FPS, 비정수 duration, grid와 정확히 같은/거의 같은 key time.
- 0초 문서는 `[0]` 하나.
- fingerprint는 policy version/rate/ordered double bits가 같으면 같고 하나라도 다르면 다르다.
- invalid/non-finite/out-of-range/비정렬/중복 plan 거부.
- actor transform/lock/active-target transform 동시 anchor는 flags OR.
- free와 lock sample의 time/position은 같고 yaw만 다르다.
- sample list/dictionary/plan 방어 복사와 mutation 거부.
- 평가가 revision/event를 바꾸지 않는다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~MovementTrajectoryEvaluatorTests"
```

### Step 3: 최소 구현

```csharp
public TrajectorySamplePlan CreateTrajectorySamplePlan(TrajectorySamplingSettings settings);
public MovementTrajectorySet CreateMovementTrajectories(TrajectorySamplePlan plan);
```

- 누적 `t += step`을 사용하지 않는다.
- ordered sample을 actor별 forward transform cursor로 평가해 매 sample마다 처음부터 선형 탐색하지 않는다.
- `LockOnFacingEvaluator` 하나를 snapshot과 trajectory가 공용한다.
- ordered trajectory는 `Evaluate`를 sample마다 다시 호출하지 않는다. actor/target transform, active lock regime과 union segment를 전진만 하는 `MovementTrajectoryFacingSweep`를 사용한다. sweep은 각 piecewise-linear segment의 마지막 유효 resolved yaw를 갱신해 coincidence를 해결하고, arbitrary-time evaluator와 같은 순수 segment resolver를 공유한다.
- point evaluator와 bulk sweep이 모든 동일 sample에서 같은 facing kind/yaw를 내는 fixture와 `segmentSteps <= canonicalSegments + samples + constant` 진단 상한을 고정한다.
- `MovementTrajectorySet.WithRevision(revision)`은 actor geometry collection을 재사용하는 얕은 immutable wrapper다.

### Step 4: GREEN·Domain 전체 확인

필터 후 Domain 전체를 실행한다.

### Step 5: 리뷰·커밋·푸시

- plan/fingerprint/anchor와 O(samples+keys)에 가까운 cursor 동작을 독립 리뷰한다.
- Commit: `feat: 결정적 이동 궤적 평가 추가`

---

## Task 5: 원자적 ProjectionFrame, cache와 재진입 직렬화

**Files:**

- Create: `src/PvpGuide.Domain/ProjectionSourceMetadata.cs`
- Create: `src/PvpGuide.Domain/ISceneProjectionSource.cs`
- Modify: `src/PvpGuide.Domain/SceneDocument.cs`
- Create: `src/PvpGuide.Application/Projection/SceneProjectionFrame.cs`
- Create: `src/PvpGuide.Application/Projection/TrajectorySamplingPolicy.cs`
- Modify: `src/PvpGuide.Application/Projection/ISceneProjectionConsumer.cs`
- Modify: `src/PvpGuide.Application/Projection/SceneProjectionController.cs`
- Modify: `src/PvpGuide.Application/Sessions/DocumentSession.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Modify: `src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs`
- Modify: `tests/PvpGuide.Application.Tests/SceneProjectionControllerTests.cs`

### Step 1: RED 테스트

- Top/World가 exact same `SceneProjectionFrame`, snapshot, trajectory instances를 받는다.
- frame constructor가 document ID/revision/MotionRevision/policy mismatch를 거부한다.
- 첫 project는 build 1회, seek/playback은 cache hit, Action-only revision은 `WithRevision` geometry reuse, motion revision은 build 1회.
- cache는 현재 한 항목만 유지하고 dispose 뒤 source event를 무시한다.
- metadata가 계산 전후 달라지면 stale frame을 게시하지 않고 최신으로 bounded retry한다.
- Top consumer Apply 중 source change를 발생시켜도 수신 순서가 `top old, world old, top new, world new`이며 world가 old로 회귀하지 않는다.
- 동일 `(revision,time)` event는 기존처럼 중복 투영하지 않는다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SceneProjectionControllerTests
```

### Step 3: 최소 구현

- `SceneDocument`가 `ISceneProjectionSource`를 구현한다.
- controller의 fixed settings는 version `lock-on-motion/v1`, uniform max 30Hz, tick 5Hz다.
- `_isProjecting`, `_hasPendingProjection`과 최신 요청으로 중첩 event를 coalesce한다.
- top/world 두 Apply가 끝난 뒤에만 pending frame을 평가한다.
- cache key는 `MotionRevision + fingerprint`, 저장 항목은 하나다.
- Apply 실패 시 pending 상태를 정리하고 기존 명시적 예외를 숨기지 않는다.
- `DocumentSession`은 기존 `SnapshotSource` 호환 API를 유지하고 새 read-only `ProjectionSource`를 노출한다.
- `Main`은 controller에 `ProjectionSource`를 전달한다.
- TopView/World adapter는 `Apply(SceneProjectionFrame)`로 최소 이관하되 이 Task에서는 `frame.Snapshot`으로 기존 actor/semantic 표시만 유지한다. trajectory 시각화는 Task 7/9가 확장한다.

### Step 4: GREEN·Application 전체 확인

필터 후 Application 전체를 실행한다.

### Step 5: 리뷰·커밋·푸시

- 재진입 event 순서와 cache identity를 독립 리뷰한다.
- Commit: `feat: facing 궤적 투영 프레임 추가`

---

## Task 6: 대상 누락 semantic fallback

**Files:**

- Modify: `src/PvpGuide.Editor/Features/Timeline/SemanticOverlayLayout.cs`
- Modify: `tests/PvpGuide.Editor.Tests/SemanticOverlayLayoutTests.cs`

### Step 1: RED 테스트

enabled lock state가 target ID를 갖지만 target transform이 없고 facing kind가 `TargetUnavailableFallback`인 synthetic snapshot을 만든다. 기대값:

- `LockBadge == "LOCK · missing-target · 대상 없음"`
- `LockLine == null`
- `TargetMarkerPosition == null`
- action label과 다른 actor overlay는 정상
- 예외 없음

정상 enabled lock과 target 없는 잘못된 state의 기존 validation 기대를 새 방어 계약에 맞게 명시한다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SemanticOverlayLayoutTests
```

Expected: 기존 `Create`가 missing target에서 `InvalidOperationException`을 던져 FAIL.

### Step 3: 최소 구현과 GREEN

`CreateScene`가 facing provenance를 함께 읽어 missing badge를 만들고 line/marker를 생략한다. public low-level `Create`는 target 상태를 enum으로 명시적으로 받게 해 null의 의미를 추측하지 않는다. 같은 필터와 Editor 전체 tests를 직렬 실행한다.

### Step 4: 리뷰·커밋·푸시

- Commit: `fix: 누락 Lock-on 대상 표시 안전화`

---

## Task 7: TopView 궤적 read model과 표시

> Task 6 이후 Task 8과 병렬 실행 가능. 서로의 파일을 수정하지 않는다.

**Files:**

- Create: `src/PvpGuide.Editor/Features/TopView/TrajectoryOverlayLayout.cs`
- Modify: `src/PvpGuide.Editor/Features/TopView/TopViewSurface.cs`
- Create: `tests/PvpGuide.Editor.Tests/TrajectoryOverlayLayoutTests.cs`

### Step 1: RED 순수 layout 테스트

- layer order: shared path → free tick → lock tick → lock line → bodies → target marker → text.
- `q_n=n/5` nearest sample, tie earlier, rate<5 all, anchor always include.
- transform 원, Lock-on 마름모, combined flags.
- current time 전/후 brightness `1.0/0.45`.
- same position에 free/lock tick yaw만 달라지고 fake position offset 없음.
- selection 변경은 geometry identity를 유지하고 presentation 강조만 변경.
- preview는 committed `DisplayedTrajectories` identity를 유지.
- 모든 출력 collection read-only.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~TrajectoryOverlayLayoutTests
```

Expected: 새 layout 타입/동작이 없어 compile FAIL.

### Step 3: 최소 구현

- `TopViewSurface.Apply(SceneProjectionFrame)`에서 semantic/trajectory immutable state를 교체한다.
- actor body committed 방향은 `Snapshot.ActorFacings`, preview 방향은 authored preview를 사용한다.
- `_Draw()`는 layout 결과와 selection/current time만 소비한다.
- body hit/drag는 계속 authored `ActorTransforms`를 편집하며 resolved facing을 문서에 쓰지 않는다.
- Godot import가 새 Editor C# script의 `.uid`를 생성하게 하며 UID를 임의 작성하지 않는다.

### Step 4: GREEN·Editor 전체 확인

필터 후 Editor 전체 tests를 실행한다.

### Step 5: 리뷰 결과만 반환

이 병렬 단위에서는 commit하지 않는다. 메인 에이전트가 Task 8과 통합 검증 뒤 커밋한다.

---

## Task 8: World 궤적 geometry와 좌표 변환

> Task 6 이후 Task 7과 병렬 실행 가능. `WorldViewProjectionAdapter.cs`는 Task 9에서만 수정한다.

**Files:**

- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldTrajectoryGeometry.cs`
- Create: `tests/PvpGuide.Editor.Tests/WorldTrajectoryGeometryTests.cs`
- Modify: `tests/PvpGuide.Editor.Tests/WorldTransformMapperTests.cs`

### Step 1: RED 순수 테스트

- path/tick vertex는 world X/Z를 그대로 쓰고 Y는 `sourcePosition.Y + TrajectoryLiftY`로 변환된다. lift는 모든 sample에 같은 작은 양수다.
- 4방위 Domain yaw가 `ToRotationYRadians` 뒤 local +X facing과 일치한다.
- model forward offset은 world actor root가 아니라 visual local yaw에만 더해진다.
- normalized time은 `time/duration`, duration 0이면 항상 0.
- UV와 vertex collection read-only.
- current time 변화는 geometry output을 바꾸지 않는다.

### Step 2: RED 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~WorldTrajectoryGeometryTests|FullyQualifiedName~WorldTransformMapperTests"
```

Expected: 새 world trajectory geometry 타입/동작이 없어 compile FAIL.

### Step 3: 최소 구현과 GREEN

Godot 타입을 필요로 하지 않는 record/mapper로 geometry를 만든다. renderer adapter가 이 값을 `ImmediateMesh`에 옮기게 한다. Godot import가 새 Editor C# script의 `.uid`를 생성하게 하며 UID를 임의 작성하지 않는다. 같은 필터 테스트는 메인 에이전트가 Task 7과 통합한 뒤 직렬 실행한다.

### Step 4: 병렬 단위 리뷰 결과만 반환

메인 에이전트가 Task 7과 충돌을 조정하고 두 단위를 함께 검증·커밋한다.

### Step 5: 통합 커밋·푸시

- Main only: `feat: TopView와 3D 궤적 표시 모델 추가`

---

## Task 9: WorldView world-fixed 재사용 mesh

**Files:**

- Modify: `src/PvpGuide.Editor/Features/ViewportSync/WorldViewProjectionAdapter.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/WorldTrajectoryRenderState.cs`
- Create: `src/PvpGuide.Editor/Features/ViewportSync/TrajectoryTimeFade.gdshader`
- Create generated UID through Godot import: `src/PvpGuide.Editor/Features/ViewportSync/TrajectoryTimeFade.gdshader.uid`
- Modify: `tests/PvpGuide.Editor.Tests/WorldTrajectoryGeometryTests.cs`

### Step 1: adapter seam RED 테스트

순수 tests에 renderer input이 다음을 제공하는지 먼저 고정한다.

- actor별 path/free/lock vertices와 UV
- geometry key `(MotionRevision,Fingerprint)`
- 현재 normalized time

실제 Node lifecycle은 Task 10 runtime probe에서 검증한다.

### Step 2: RED 확인

Task 8의 pure geometry test에 아직 없는 `WorldTrajectoryRenderState` 요구를 추가해 geometry key, 세 mesh payload와 normalized current time을 검증한다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~WorldTrajectoryGeometryTests
```

Expected: `WorldTrajectoryRenderState` 타입/동작이 없어 compile FAIL. 이 실패를 확인한 뒤 adapter와 render state를 구현한다.

### Step 3: 구현

- adapter 생성 시 `_actorsRoot/TrajectoryOverlayRoot`를 한 번 만든다.
- actor별 고정 container 아래 `SharedTrajectory`, `FreeFacingTicks`, `LockOnFacingTicks`를 한 번 만든다.
- actor root의 transform을 상속하지 않는 world-fixed sibling 구조를 유지한다.
- geometry key가 같으면 Action-only/tick에서 `ClearSurfaces`를 호출하지 않는다.
- time 변화는 세 material의 `current_time_normalized` uniform만 갱신한다.
- actor 제거 시 body root와 trajectory container를 각각 정확히 한 번 `QueueFree`한다.
- committed actor root yaw는 `ActorFacings`, preview는 authored preview, Escape는 committed facing을 적용한다.

### Step 4: build·Editor 테스트

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
dotnet build .\src\PvpGuide.Editor\PvpGuide.Editor.csproj -c Debug --nologo
```

### Step 5: 리뷰·커밋·푸시

- node ownership, fixed coordinates, mesh/material reuse를 독립 리뷰한다.
- Commit: `feat: world-fixed Lock-on 궤적 mesh 추가`

---

## Task 10: Main 배선과 실제 Godot runtime probe

**Files:**

- Modify: `src/PvpGuide.Editor/Scenes/Main/Main.cs`
- Modify: `src/PvpGuide.Editor/Scenes/Main/ActionLockOnRuntimeProbe.cs`
- Modify: `scripts/Test-GodotRuntime.ps1`

### Step 1: marker 요구를 먼저 RED로 추가

`Test-GodotRuntime.ps1` required output에 정확히 추가한다.

```text
LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1
```

Run:

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
& .\scripts\Test-GodotRuntime.ps1
```

Expected: marker 누락으로 FAIL. 기존 marker까지 도달하는지 함께 확인한다.

### Step 2: 실제 UI/node probe 구현

기존 Action/Lock-on fixture의 최종 상태를 보존하고 별도 bounded probe에서 다음을 hand-derived로 확인한다.

- real slider seek에서 Snap hold, Continuous 변화, KeyframeOnly authored yaw
- exact actor root `Rotation.Y = -yaw*pi/180`
- TopView `DisplayedTrajectories`의 free/lock samples와 current/future presentation
- Top/World가 받은 같은 `SceneProjectionFrame` reference 진단 seam
- 위치 일치 fallback과 synthetic missing-target badge
- 실제 `TrajectoryOverlayRoot/<actor>/...` 세 mesh 존재
- actor 이동·회전 전후 mesh world vertex 동일
- seek 여러 번 뒤 node/resource identity와 node count 동일
- Action-only mutation 뒤 geometry build count/mesh resource identity 동일
- motion mutation 뒤 build count 정확히 +1
- duration 0 fixture UV/uniform 0, paused 0초

모든 assertion 뒤에만 exact marker를 출력한다. wait/sleep, `_Process` 횟수와 source-string assertion은 금지한다.

### Step 3: GREEN 확인

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
& .\scripts\Test-GodotRuntime.ps1
```

Expected: build warning/error 0, 두 startup failure probe PASS, 기존 marker 전부와 새 marker exact PASS, 마지막 `GODOT_RUNTIME_VERIFICATION=PASS`.

### Step 4: 리뷰·커밋·푸시

- probe가 production seam과 실제 nodes를 읽는지 독립 리뷰한다.
- Commit: `test: Lock-on 방향 궤적 런타임 검증 추가`

---

## Task 11: schema·skeleton·성능 회귀

**Files:**

- Modify: `tests/PvpGuide.Infrastructure.Tests/SceneRoundTripTests.cs`
- Modify: `scripts/Test-ProjectSkeleton.ps1`
- Create: `scripts/Measure-TrajectoryPerformance.ps1`
- Create: `src/PvpGuide.Domain/Timeline/TrajectoryEvaluationDiagnostics.cs`
- Create: `tests/PvpGuide.Domain.Tests/TrajectoryPerformanceContractTests.cs`

### Step 1: RED 회귀 테스트

- 평가 전후 serialize 문자열 동일.
- `/2` JSON에 facing/trajectory/motion revision/cache 필드가 없음.
- 기존 Lock-on fields round-trip 유지.
- skeleton required files와 새 runtime exact marker 확인.
- deterministic operation count가 sample+key 규모에 선형으로 증가하고 per-sample 전체 key scan을 하지 않음.

### Step 1a: RED 확인

먼저 skeleton required list에 아직 만들지 않은 performance script와 diagnostics source를 추가하고 새 diagnostics test를 실행한다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~TrajectoryPerformanceContractTests
& .\scripts\Test-ProjectSkeleton.ps1
```

Expected: 새 diagnostics type 또는 required performance script 누락으로 FAIL. 실패 확인 뒤 Step 2를 진행한다.

### Step 2: GREEN 구현

- serializer production 코드는 schema 회귀가 실제로 없으면 수정하지 않는다.
- skeleton은 새 source/test/shader/plan 파일과 marker를 검증한다.
- 성능 스크립트는 4 actors/각 100 keys/10초 plan을 warm-up 후 여러 번 측정해 build p95, snapshot p95, samples, evaluator segment steps를 출력한다.
- p95 8ms는 기준 PC의 diagnostic gate다. 초과하면 이 Task를 완료하지 않고 영향 actor/시간 구간 증분 cache를 별도 RED→GREEN 하위 Task로 즉시 추가한다.
- 16 actors/1,000 keys는 수치를 기록하되 machine-independent xUnit wall-clock failure로 만들지 않는다.

### Step 3: 전체 관련 검증

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Measure-TrajectoryPerformance.ps1
```

### Step 4: 리뷰·커밋·푸시

- Commit: `test: 궤적 schema와 성능 회귀 고정`

---

## Task 12: 한글 문서 동기화

**Files:**

- Modify: `README.md`
- Modify: `docs/02-requirements.md`
- Modify: `docs/03-system-architecture.md`
- Modify: `docs/04-data-architecture.md`
- Modify: `docs/05-editor-architecture.md`
- Modify: `docs/06-combat-visualization.md`
- Modify: `docs/09-performance-strategy.md`
- Modify: `docs/11-testing-and-quality.md`
- Modify: `docs/13-roadmap.md`

### Step 1: 실제 구현 증거 수집

다음 값을 command output에서 기록한다.

- 각 xUnit project pass count
- skeleton/runtime marker 결과
- trajectory performance p95/sample/step 수
- 최종 public type/file 이름

### Step 2: 문서 수정

- “아직 방향 계산 없음” 문구를 실제 완료 상태로 바꾼다.
- 세 mode, epsilon/fallback, sample plan/fingerprint와 schema `/2` 유지 이유를 설명한다.
- 위치 경로는 동일하고 free/Lock-on은 yaw/표식만 다르며 root motion은 후속이라고 명시한다.
- Top/World layer, fixed world root, shader time fade, preview/cache/reentry 계약을 기록한다.
- 4 actors/100 keys p95 8ms 임시 full-rebuild 예외와 16 actors/1,000 keys 전 증분 cache 의무를 requirements/performance/roadmap에 정렬한다.
- timeline 확대·스크롤·스냅은 우선순위만 검토했고 별도 후속 구현임을 기록한다.
- 새 exact marker와 전체 검증 명령을 README/testing 문서에 추가한다.
- 로드맵의 다음 구현 단위를 실제 남은 작업으로 이동한다.

### Step 3: 문서 검증·리뷰·커밋·푸시

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
git diff --check
& .\scripts\Test-ProjectSkeleton.ps1
```

- 독립 reviewer가 코드와 문서의 숫자/marker/제외 범위를 대조한다.
- Commit: `docs: Lock-on 방향 궤적 완료 상태 기록`

---

## Task 13: 전체 검증, 독립 최종 리뷰와 완료 push

**Files:**

- Modify only if review finds a verified issue.

### Step 1: fresh 직렬 검증

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
& .\scripts\Measure-TrajectoryPerformance.ps1
```

### Step 2: 독립 병렬 최종 리뷰

세 reviewer가 서로 독립적으로 다음을 읽기 전용 검토한다.

1. Domain 수학·immutability·경계·복잡도
2. Application cache·재진입·preview와 Editor node lifecycle
3. runtime/schema/docs/AGENTS·Git 정책과 범위 충족

Critical/Important는 반드시 고치고 관련 RED→GREEN과 전체 검증을 다시 실행한다. Minor도 사용자 흐름·정확성에 영향이 있으면 반영한다.

### Step 3: 작업 트리·remote 검증

```powershell
git status --short --untracked-files=all
git rev-parse HEAD
git rev-parse '@{upstream}'
git ls-remote origin refs/heads/codex/timeline-playback-foundation
```

Expected: tracked/untracked 작업 파일 0, local/upstream/remote hash 일치.

### Step 4: 최종 수정이 있었다면 커밋·푸시

- Commit: `fix: Lock-on 방향 궤적 최종 리뷰 반영`
- `git push origin HEAD`
- 최종 수정이 없으면 빈 커밋을 만들지 않는다.

### Step 5: 태그 정책

이번 완료만으로 tag를 만들지 않는다. 사용자가 실제 실행 후 “잘 된다”는 취지로 보고하면 어떤 기능·재현 시나리오가 정상인지 재확인한다. 사용자가 다시 긍정 응답한 뒤에만 현재 정상 commit에 `working/<기능>-YYYYMMDD-HHmm` 주석 태그를 만들고 push한다.
