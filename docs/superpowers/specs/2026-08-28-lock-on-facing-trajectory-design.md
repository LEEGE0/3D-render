# Lock-on 방향 계산과 이동 궤적 설계

## 1. 목적

이번 마일스톤은 저장·편집·평가·오버레이 표시까지 연결된 Action/Lock-on foundation 위에 실제 방향 계산을 추가한다. 선택 배우가 같은 장면 시각의 대상 배우를 바라보는 회전을 결정하고, 자유 회전과 Lock-on 적용 회전을 동일한 위치·시간 샘플에서 TopView와 3D에 함께 표시한다.

여기서 이동 궤적은 새로운 이동 시뮬레이션이나 루트 모션이 아니다. 현재 문서가 가진 transform 키프레임만이 위치의 원본이며 자유 궤적과 Lock-on 궤적은 같은 위치를 공유한다. 두 결과의 차이는 방향이다. 화면에서는 공유 위치 경로와 자유/Lock-on 방향 표식을 함께 그려 어떤 회전 규칙이 적용되는지 교육용으로 비교한다.

## 2. 고정 제약

- Windows 11 전용, 오프라인 독립 실행을 유지한다.
- Godot 4.7.2 Stable .NET, .NET 8, C#, Forward Plus를 유지한다.
- 프로젝트·도구·캐시·로컬 게임 자산은 D 드라이브 정책을 유지한다.
- 게임패드 입력은 사용자의 철회 요청에 따라 포함하지 않는다.
- DSR 원본 자산, 개인 설치 경로와 비밀 정보는 저장소에 넣지 않는다.
- `SceneDocument`의 transform과 Lock-on 키프레임이 유일한 영구 원본이다.
- 계산된 방향과 궤적은 파생 데이터이며 JSON에 저장하지 않는다. schema는 `pvp-guide-scene/2`를 유지한다.
- 평가와 표시 갱신은 revision, history와 저장 상태를 바꾸지 않는다.
- 기존 `ActorTransforms`는 작성된 자유 transform이라는 의미를 유지한다.
- TopView와 WorldView는 같은 불변 투영 프레임 인스턴스를 소비한다.
- 미리보기는 저장되지 않으며 확정 전 궤적 원본을 바꾸지 않는다.
- 최종 검증·stage·commit·push는 메인 에이전트가 담당한다.

## 3. 현재 상태와 문제

`ActorTrack.Evaluate(time)`은 위치와 작성된 Yaw를 보간하고, `EvaluateLockOn(time)`은 마지막 Lock-on 키프레임을 left-hold한다. `SceneDocument.CreateSnapshot(time)`은 두 결과를 같은 불변 snapshot에 넣지만 `trackingMode`와 `yawOffsetDegrees`를 실제 회전 계산에 사용하지 않는다. TopView와 WorldView 역시 `ActorTransforms.YawDegrees`만 몸체에 적용한다.

현재 snapshot은 한 시각만 나타내므로 전체 시간 범위의 궤적을 스스로 만들 수 없다. 반대로 각 Godot surface가 문서를 직접 읽거나 `_Draw()`에서 궤적을 계산하면 두 뷰가 다른 시각·revision·샘플 정책을 사용할 수 있고, 재생 프레임마다 불필요한 계산과 노드 생성이 생긴다.

또한 다음 의미를 명시적으로 고정해야 한다.

- `Snap`, `Continuous`, `KeyframeOnly`의 갱신 시점
- 대상과 수평 위치가 같은 순간의 회전
- 대상이 없는 비정상 입력을 받았을 때의 안전한 표시
- 0/360 경계와 정확한 180도
- scrub, playback, preview가 같은 결과를 만드는 기준
- 같은 위치 경로 위에서 자유 방향과 Lock-on 방향을 오해 없이 비교하는 방식

## 4. 접근 비교와 선택

### 접근 A — 기존 `ActorTransforms.YawDegrees`를 Lock-on 결과로 덮어쓰기

기존 consumer 수정량은 작다. 그러나 작성된 자유 Yaw를 잃어 같은 snapshot에서 두 결과를 비교할 수 없고, 현재 시각 transform 추가가 Lock-on 파생값을 원본으로 잘못 복사할 수 있다. 기존 transform 평가 계약도 조용히 바뀐다.

### 접근 B — TopView와 WorldView에서 각각 방향·궤적 계산

Domain 변경은 적지만 수학, 경계 처리와 샘플 정책이 두 번 생긴다. 두 뷰의 결과가 달라질 수 있고 headless 순수 테스트가 약해진다. `_Draw()`나 Godot 노드 계층에 의미 계산이 들어가 기존 계층 경계를 위반한다.

### 접근 C — Domain 파생 방향·궤적 + Application 불변 투영 프레임

작성된 transform은 보존하고 Domain이 파생 방향과 시간 샘플을 순수하게 계산한다. Application은 현재 snapshot과 revision별로 캐시한 궤적을 하나의 불변 투영 프레임으로 묶어 두 뷰에 동일 인스턴스로 전달한다. Editor는 계산하지 않고 표시만 한다.

접근 C를 채택한다. 기존 저장·편집 계약을 보존하면서 방향 의미를 한 곳에서 검증할 수 있고, 재생 중 전체 궤적을 매 tick 다시 만들지 않아 성능 경계도 분명하다.

## 5. Domain 방향 계약

### 5.1 좌표와 정규화

배우의 수평 위치를 `A=(Ax,Az)`, 대상의 같은 평가 시각 수평 위치를 `T=(Tx,Tz)`라 한다.

```text
Dx = Tx - Ax
Dz = Tz - Az
targetYaw = atan2(Dz, Dx) * 180 / π
resolvedYaw = Normalize360(targetYaw + yawOffsetDegrees)
```

- Y 높이는 방향 계산에 사용하지 않는다.
- 최종 Yaw는 기존 transform 규약과 같은 `[0, 360)`으로 정규화한다.
- `+X=0°`, `+Z=90°`, `-X=180°`, `-Z=270°`다.
- 정확한 180도는 `180°`로 고정한다.
- offset은 기존 Lock-on 값의 `[-180, 180)` 정규화 계약을 유지하되 결과만 `[0, 360)`으로 만든다.
- 유한하지 않은 입력은 기존 Domain 생성·평가 경계에서 거부한다.

### 5.2 tracking mode

foundation에서 승인된 의미를 그대로 구현한다.

| 모드 | 방향을 계산하는 시각 | 유지 규칙 |
|---|---|---|
| `Snap` | 활성 `SourceKeyframeId` Lock-on 키프레임의 정확한 시각 | 그 키프레임에서 계산한 결과를 다음 Lock-on 키프레임까지 유지한다. 이후 배우나 대상 이동은 회전을 바꾸지 않는다. |
| `Continuous` | 요청된 현재 평가 시각 | 배우와 대상의 같은 시각 보간 위치로 매번 다시 계산한다. |
| `KeyframeOnly` | 자동 계산하지 않음 | 작성·보간된 transform Yaw를 그대로 사용한다. offset도 자동 적용하지 않으며 Lock-on 상태·선·대상 표시는 유지한다. |

Lock-on이 꺼져 있거나 첫 Lock-on 키프레임 전이면 작성된 transform Yaw를 사용한다. 새 Lock-on 키프레임은 같은 mode와 target을 반복하더라도 새로운 방향 구간을 시작한다.

### 5.3 위치 일치와 직전 유효 방향

수평 거리 제곱이 `CoincidenceEpsilon²` 이하이면 `atan2` 결과를 방향으로 사용하지 않는다. `CoincidenceEpsilon`은 화면 줌, FPS와 모델 크기에 의존하지 않는 Domain 상수 `1e-6` world unit이며 경계값도 위치 일치에 포함한다. 현재 방향 계산과 과거 유효 방향 탐색이 반드시 같은 판정을 쓴다.

- `Snap`은 구간 시작 시각 자체에서 위치가 같으면 그 시각의 작성된 transform Yaw를 사용하고 그 값을 hold한다.
- `Continuous`는 현재 `SourceKeyframeId`가 시작한 Lock-on 구간 안에서 현재 시각보다 앞선 가장 최근의 유효 수평 상대 방향을 유지한다.
- 과거 렌더 프레임이나 호출 순서는 사용하지 않는다. 배우와 대상 transform 키프레임 시각의 합집합으로 만든 piecewise-linear 상대 위치 구간을 뒤로 탐색한다. 현재 점으로 접근하는 왼쪽 구간에 유효 방향이 있으면 그 방향의 극한을 사용한다.
- 현재 Lock-on 구간 시작부터 계속 같은 위치였다면 현재 시각의 작성된 transform Yaw로 돌아간다.
- 새 Lock-on 키프레임이 시작되면 이전 구간의 유효 방향은 이어받지 않는다.

따라서 임의 시각 scrub, 순차 playback과 궤적 일괄 샘플은 호출 순서와 관계없이 같은 결과를 만든다.

### 5.4 대상 누락

정상 `SceneDocument`의 생성·편집·역직렬화는 enabled Lock-on의 자기 자신·미존재 target을 검증 오류로 거부한다. 그러나 삭제 기능 추가 중인 일시 상태, 오래된 외부 snapshot이나 방어 테스트가 비정상 입력을 줄 수 있다.

파생 방향 평가기는 검증된 `SceneDocument` 경로와 별도로, 이미 평가된 actor transform/timeline dictionary를 받는 순수 방어 seam을 제공한다. 이 seam이 대상 없는 상태를 받으면 화면 투영 전체를 예외로 중단하지 않고 작성된 transform Yaw로 낮추며 `TargetUnavailableFallback` 근거와 `TargetUnavailable` 진단을 남긴다. 오버레이는 `LOCK · <id> · 대상 없음` badge를 표시하되 lock line과 target marker를 숨긴다. 문서는 이 상태를 새로 저장하지 않으며 정상 생성·편집·load 검증 규칙은 완화하지 않는다.

### 5.5 출력 모델

다음과 동등한 불변 타입을 Domain에 둔다.

```csharp
public enum FacingResolutionKind
{
    AuthoredDisabled,
    AuthoredKeyframeOnly,
    SnapTarget,
    ContinuousTarget,
    CoincidentPrevious,
    CoincidentAuthoredFallback,
    TargetUnavailableFallback,
}

public sealed record EvaluatedActorFacing(
    double YawDegrees,
    FacingResolutionKind ResolutionKind,
    string? SourceLockOnKeyframeId);
```

`SceneSnapshot.ActorFacings`는 actor별 결과를 방어 복사한 read-only dictionary다. 기존 생성자 호출은 `ActorTransforms`의 Yaw로 authored facing을 자동 구성해 호환한다. `ActorTransforms`와 `ActorTimelineStates`의 의미와 인스턴스 불변성은 바꾸지 않는다.

## 6. Domain 궤적 계약

### 6.1 paired sample

```csharp
public sealed record MovementTrajectorySample(
    double TimeSeconds,
    Position3 Position,
    double FreeYawDegrees,
    EvaluatedActorFacing LockOnFacing,
    TrajectoryAnchorKind AnchorKind);

[Flags]
public enum TrajectoryAnchorKind
{
    None = 0,
    ActorTransform = 1,
    ActorLockOn = 2,
    ActiveTargetTransform = 4,
}

public sealed class TrajectorySamplePlan
{
    public string PolicyVersion { get; }
    public int UniformRate { get; }
    public IReadOnlyList<double> OrderedTimes { get; }
    public string Fingerprint { get; }
}

public sealed class ActorMovementTrajectory
{
    public string ActorId { get; }
    public IReadOnlyList<MovementTrajectorySample> Samples { get; }
}

public sealed class MovementTrajectorySet
{
    public string DocumentId { get; }
    public long Revision { get; }
    public long MotionRevision { get; }
    public string SamplingPolicyFingerprint { get; }
    public IReadOnlyDictionary<string, ActorMovementTrajectory> Actors { get; }
}
```

한 sample의 `Position`과 `TimeSeconds`는 자유/Lock-on 비교에 공통이다. `FreeYawDegrees`는 `ActorTrack.Evaluate(time)`의 기존 Yaw이고 `LockOnFacing`은 5장의 규칙으로 해결한 결과다. 위치가 다른 두 경로를 생성하지 않는다.

### 6.2 순수 평가 API

Application의 sampling policy가 source metadata의 duration/FPS/canonical motion anchor time으로 불변 `TrajectorySamplePlan`을 만든다. `SceneDocument.CreateMovementTrajectories(plan)`는 모든 배우를 그 plan의 같은 sample time 목록에서 평가하고 불변 `MovementTrajectorySet`을 반환한다. Domain은 plan 생성 규칙을 다시 추측하지 않고 plan의 policy version, rate, ordered time bit pattern과 fingerprint 일치를 검증한 뒤 결과에 그대로 보존한다. actor별 `AnchorKind`는 Domain이 원본 track과 그 시각의 active target track을 대조해 채운다.

- plan의 sample time은 유한하고 문서 범위 안이어야 한다.
- 목록은 엄격한 오름차순이며 중복이 없어야 한다.
- 호출자가 준 순서를 조용히 정렬하거나 중복 제거하지 않는다.
- 빈 목록은 빈 sample 컬렉션을 가진 actor 결과로 허용한다.
- 반환 dictionary와 sample list는 방어 복사하고 외부 mutation을 거부한다.
- 평가는 document revision, history와 event를 변경하지 않는다.

현재 snapshot 방향과 궤적의 같은 시각 sample은 같은 순수 방향 평가기를 사용한다. 두 구현을 따로 두지 않는다.

### 6.3 표시 sample 정책

Application의 고정 `TrajectorySamplingPolicy`가 다음 sample time 합집합을 만든다.

- 문서 시작 `0`과 끝 `DurationSeconds`
- `rate=min(document FPS, 30)` Hz의 균일한 시각 표본
- 모든 배우의 transform 및 Lock-on 키프레임 시각

균일 표본은 누적 덧셈을 금지하고 정수 `k`에서 매번 `t_k=k/rate`로 계산한다. `t_k < DurationSeconds`인 값만 넣은 뒤 원본 `0`, 정확한 `DurationSeconds`와 원본 키프레임 time을 추가한다. 정렬·중복 제거는 `double` 값의 정확한 동등성으로 수행하여 의미 있는 원본 키프레임 time을 반올림하거나 이동시키지 않는다. grid와 거의 같지만 정확히 다른 키프레임은 두 sample로 보존한다. 0초 문서는 `0` 하나만 만든다.

결과는 오름차순·고유 목록이다. 키프레임과 mode 전환은 균일 sample 사이에 있어도 빠지지 않는다. 30Hz 상한은 화면 선의 시각 밀도 정책이며 현재 시각 actor facing이나 문서 의미를 낮추지 않는다. policy 버전, rate, ordered sample time의 IEEE 754 bit pattern으로 안정적인 `SamplingPolicyFingerprint`를 만들고 trajectory 결과에 넣는다. 향후 줌·렌더 품질별 sample 정책을 추가할 수 있지만 sample 수가 판정 결과를 바꾸면 안 된다.

방향 tick은 별도 순수 `TrajectoryTickSelectionPolicy`가 고른다. 정규 tick 목표 시각 `q_n=n/5`마다 가장 가까운 균일 sample 하나를 선택하고 거리가 같으면 더 이른 sample을 택한다. `rate<5`면 존재하는 모든 균일 sample을 사용한다. 여기에 해당 actor의 transform/Lock-on 키프레임과 활성 target의 transform 키프레임 anchor를 합친다. anchor는 0.2초 간격보다 가까워도 생략하지 않는다. sample의 `[Flags] AnchorKind`가 원본 anchor 조합을 기록하고 TopView와 WorldView가 같은 tick 선택 결과를 사용한다.

## 7. Application 투영과 캐시

### 7.1 원자적 투영 프레임

```csharp
public sealed record SceneProjectionFrame(
    SceneSnapshot Snapshot,
    MovementTrajectorySet Trajectories);
```

생성 시 document ID, revision과 sampling policy가 반드시 같아야 한다. `ISceneProjectionConsumer.Apply`는 `SceneSnapshot` 대신 `SceneProjectionFrame`을 받는다. `SceneProjectionController`가 frame 하나를 만들고 TopView와 WorldView에 같은 인스턴스를 순서대로 전달한다.

controller는 기존 `ISceneSnapshotSource`를 모호하게 확장하지 않고 `ISceneProjectionSource` 입력 계약을 사용한다. source metadata는 document ID, duration, FPS, revision, `MotionRevision`을 한 번에 제공하며 snapshot과 trajectory 생성 API를 함께 가진다. Application은 policy version과 최대 균일/tick rate만 담은 Domain `TrajectorySamplingSettings`를 source의 `CreateTrajectorySamplePlan(settings)`에 전달한다. source가 내부 actor track을 노출하지 않고 canonical transform/Lock-on/target anchor를 포함한 plan을 만든다. controller는 계산 전후 metadata가 같은지 확인한다. 다르면 결과를 게시하지 않고 최신 metadata로 다시 평가하며 bounded retry가 소진되면 명시적 오류를 낸다.

기존 `(revision,time)` 중복 방지 계약은 유지한다. 현재 snapshot은 time 변화마다 새로 평가하지만 trajectory geometry는 이 controller 인스턴스 안에서 현재 `(MotionRevision, SamplingPolicyFingerprint)` 한 항목만 캐시한다. source 교체는 새 controller를 만들기 때문에 이전 cache를 공유하지 않고 dispose에서 즉시 비운다. seek, play/pause와 정상 playback tick은 궤적을 다시 계산하지 않는다.

`SceneDocument.MotionRevision`은 actor 구성, transform 또는 Lock-on mutation에만 증가하고 Action-only mutation에는 증가하지 않는다. Action 편집으로 document revision만 바뀌면 현재 revision을 가진 얕은 `MovementTrajectorySet` wrapper를 만들되 기존 actor trajectory geometry를 재사용한다. motion mutation은 이번 4-actor 범위에서 전체 trajectory geometry를 한 번 다시 만든다. actor별·변경 구간 증분 재평가는 진단 기준을 넘을 때 적용할 후속 최적화이며, 현재 구현은 Action-only 변경까지 재계산하는 전체 revision cache를 사용하지 않는다. 기존 요구사항·성능 문서의 “가능한 한 변경 구간만 재평가”는 단계별 장기 정책임을 이번 마일스톤 문서 갱신에서 명확히 하고, 현재 예외와 측정 기반 승격 조건을 함께 기록한다.

source의 changed/playback 알림이나 첫 consumer `Apply` 중 재진입하면 controller는 중첩 투영을 즉시 실행하지 않고 최신 요청을 pending으로 합친다. 현재 frame을 두 consumer에 같은 순서로 끝까지 전달한 뒤 최신 `(revision,time)`을 다시 평가한다. 따라서 TopView가 새 frame을 받은 뒤 WorldView가 오래된 frame으로 되돌아가는 순서 역전이 없다.

`SceneSnapshot`에도 additive `MotionRevision`을 넣고 기존 호환 생성자는 전달받은 revision을 기본값으로 쓴다. `SceneProjectionFrame` 생성은 snapshot과 trajectory의 document ID, revision, `MotionRevision` 및 controller가 요청한 sampling fingerprint를 모두 검사한다. 문서 변경 알림 처리 중 하나라도 다르면 불완전한 frame을 게시하지 않는다.

### 7.2 preview

`TransformPreview`는 기존 별도 consumer 경로를 유지한다.

- 현재 배우 몸체는 drag 중 preview 위치와 preview Yaw를 즉시 보여준다.
- semantic lock line은 기존처럼 preview 위치를 반영한다.
- 전체 궤적과 committed facing sample은 preview로 다시 계산하지 않는다.
- Apply 성공으로 revision이 바뀌면 새 궤적을 한 번 계산한다.
- Escape, 선택 변경과 playback 시작은 기존 규칙대로 preview를 취소하고 committed frame으로 복원한다.

이 범위는 임시 한 점을 전체 이동 시뮬레이션처럼 보이게 하지 않으며 preview가 문서 cache key를 오염시키지 않게 한다.

## 8. TopView 표시

`TopViewSurface.Apply(frame)`은 semantic overlay와 `(MotionRevision, SamplingPolicyFingerprint)`별 trajectory geometry를 미리 계산해 각 immutable public read-only 상태를 원자적으로 교체한다. 선택 강조와 현재 time 기준 명도는 geometry에 굳히지 않고 별도 presentation state로 적용한다. `_Draw()`는 저장된 표시 상태만 소비하며 Domain 계산이나 문서 접근을 하지 않는다. 선택만 바뀌면 trajectory set과 geometry 인스턴스를 보존한 채 강조만 다시 그린다. Action-only frame은 action label/badge만 갱신하고 trajectory geometry 참조를 유지한다.

draw layer는 아래에서 위 순서다.

1. 공유 위치 궤적
2. 자유 방향 표식
3. Lock-on 방향 표식
4. 기존 lock line
5. actor body와 현재 방향선
6. target marker
7. action/lock 텍스트

표시 규칙:

- 공유 위치 궤적은 채도가 낮은 파랑 `#6ea8fe`, 2px 선으로 그린다.
- 자유 방향 표식은 파란 짧은 tick/화살표다.
- Lock-on 파생 방향 표식은 황색 `#ffd166`, 더 굵은 tick/화살표다.
- 두 방향이 같아도 색과 끝 모양으로 구분한다. 위치가 같다는 이유로 가짜 평행 경로 offset을 만들지 않는다.
- `TrajectoryTickSelectionPolicy`의 최대 5Hz 표본과 원본 transform/Lock-on/target transform anchor에만 화살표를 그려 혼잡을 제한한다.
- 원본 transform 키프레임은 큰 원, Lock-on mode 전환은 작은 마름모로 표시한다.
- 현재 frame 시각 이하의 경로·표식은 기본 명도, 미래 sample은 45% 명도로 그려 시간 경계를 나타낸다. 방향 화살표와 sample time 증가 방향으로 시간 진행을 읽게 한다.
- 현재 actor body 방향선은 preview 중이면 preview Yaw, 아니면 `ActorFacings` Yaw를 사용한다.
- 선택 actor의 궤적을 강조하고 다른 actor는 낮은 명도로 표시한다.
- `DisplayedTrajectories`를 production 검증 seam으로 공개하되 외부 mutation은 허용하지 않는다.

## 9. WorldView 표시

3D actor root의 실제 회전은 committed frame에서 `ActorFacings`의 Domain/world Yaw를 사용한다. 위치는 계속 `ActorTransforms.Position`을 사용한다. preview 중인 actor는 authored preview 위치·Yaw가 일시적으로 우선하고 Escape에서는 committed resolved facing으로 돌아가며, Apply 뒤에는 새 revision의 resolved facing을 다시 적용한다.

Domain Yaw에서 Godot `Rotation.Y`로 가는 기존 `WorldTransformMapper.ToRotationYRadians`를 단일 변환 경계로 재사용한다. 실제 DSR 모델의 로컬 전방축이 다르면 `modelForwardOffsetDegrees`는 actor의 world yaw를 오염시키지 않고 `VisualRoot`에만 적용한다. 기본 capsule의 `FacingPositiveX`와 향후 모델 모두 4방위 target fixture에서 TopView 방향과 최종 3D 전방 벡터가 일치해야 한다.

이동·회전하는 actor root 아래에는 world-space 궤적을 두지 않는다. adapter가 받은 고정 `_actorsRoot` 아래에 `TrajectoryOverlayRoot`를 하나 만들고, 그 아래 actor ID별 고정 container와 재사용 가능한 궤적 자원을 둔다. actor body와 별도의 소유 dictionary로 수명을 관리하되 actor가 사라지면 대응 container도 `QueueFree`한다.

- `SharedTrajectory`: unshaded `ImmediateMesh`
- `FreeFacingTicks`: unshaded `ImmediateMesh`
- `LockOnFacingTicks`: unshaded `ImmediateMesh`

actor 생성 시 한 번 만들고 `(MotionRevision, SamplingPolicyFingerprint)` 또는 trajectory geometry 참조가 바뀔 때만 surface를 `ClearSurfaces` 후 갱신한다. Action-only revision, playback tick, `_Process`와 sample마다 surface를 다시 쓰거나 노드를 만들지 않는다. 지면 z-fighting을 피하도록 아주 작은 고정 Y 높이를 사용하되 X/Z 좌표를 바꾸지 않는다.

현재 lock line은 actor root의 회전에 이중 영향을 받지 않도록 기존과 같이 world vertex를 actor local로 역변환한다. 반면 궤적 mesh는 움직이지 않는 `TrajectoryOverlayRoot`의 world 좌표를 직접 사용한다. playback 중 actor가 이동·회전해도 이미 그린 경로 vertex의 world 위치가 고정됨을 실제 Godot 검사로 보장한다.

3D도 TopView와 같은 shared path, free/Lock-on tick, transform/Lock-on anchor와 과거/미래 구분을 표현한다. vertex의 `UV.x`에 정규화 sample time을 넣고 unshaded `ShaderMaterial`의 `current_time_normalized` uniform과 비교해 미래 명도를 45%로 만든다. duration이 0이면 sample UV와 uniform을 모두 정확히 `0`으로 고정하고 유일 sample을 현재/과거로 취급한다. 현재 시각만 바뀔 때는 uniform만 갱신하고 mesh surface나 node를 바꾸지 않는다.

## 10. 오류와 경계 처리

- `SceneSnapshot`에 actor transform은 있으나 facing이 없으면 호환 fallback으로 transform Yaw를 사용한다.
- facing dictionary에 알 수 없는 actor만 있는 입력은 snapshot 생성 시 거부한다.
- 궤적 document ID/revision 불일치는 `SceneProjectionFrame` 생성 시 거부한다.
- target 누락은 5.4의 표시 fallback으로 처리한다.
- 빈 trajectory sample은 actor 현재 표시를 막지 않고 경로 mesh만 숨긴다.
- 한 sample뿐이면 polyline은 숨기고 그 sample의 방향 표식만 표시할 수 있다.
- 0초 길이 문서는 time `0` 하나만 sample한다. 이를 정상 Editor에 열 수 있도록 `PlaybackClock`도 duration `0`을 허용하며 seek/stop/advance는 계속 `0`, play 요청은 즉시 paused 상태를 유지한다.
- 출력 Yaw는 항상 유한하고 `[0,360)`이다.
- 경계 비교는 숨은 화면 FPS나 호출 순서가 아니라 입력 시각과 piecewise-linear 원본으로 결정한다.

## 11. TDD와 검증 전략

### 11.1 Domain

- `+X=0`, `+Z=90`, `-X=180`, `-Z=270`과 offset wrap
- 배우 `(0,0,0)`, 대상 `(4,7,3)`의 `36.86989765°`, offset `-30°`의 `6.86989765°`
- `Snap`이 시작 방향을 hold하고 배우·대상 이동에 반응하지 않음
- `Continuous`가 이동 대상의 `0/45/90°`를 `0/.5/1초`에 계산
- `KeyframeOnly`가 nonzero offset에도 작성 Yaw를 유지
- first marker 전, disabled 전환과 새 mode marker 정확한 전후 시각
- 시작부터 위치 일치한 fallback과 유효 방향 후 위치 일치한 previous 유지
- `CoincidenceEpsilon` 경계의 안/밖과 키프레임이 아닌 시각에 두 선형 경로가 교차할 때의 왼쪽 극한
- 350→10 자유 회전의 midpoint 0도와 정확한 180도
- target 누락 방어 fallback
- trajectory에서 time/position은 같고 free/resolved Yaw만 다름
- 잘못된 sample 목록, 빈 목록과 read-only 방어 복사
- 기존 `ActorTransforms` 결과가 전혀 바뀌지 않음

### 11.2 Application

- Top/World가 같은 `SceneProjectionFrame`, snapshot과 trajectory 인스턴스를 받음
- `(revision,time)` 중복 방지 유지
- seek/playback time 변화에는 snapshot만 새로 평가하고 trajectory cache 재사용
- revision 변경에는 trajectory를 정확히 한 번 다시 계산
- Action-only revision은 geometry를 재사용하고 transform/Lock-on revision은 rebuild
- `TrajectorySamplePlan`의 policy/rate/time/fingerprint 불일치 거부
- snapshot/trajectory의 document revision과 `MotionRevision` 불일치 거부
- consumer 적용 중 재진입해도 두 뷰 frame 순서가 역전되지 않음
- frame 안 document ID/revision 일치
- dispose 뒤 알림 무시와 기존 구독 해제

### 11.3 Editor 순수 계산과 실제 Godot

- TopView layer order, 좌표 매핑, actor 선택 강조와 방향 tick layout
- 현재/미래 명도, 원본 키프레임 marker와 시간 방향 표시
- 선택만 변경하면 geometry 참조를 유지하고 강조만 바뀜
- preview는 authored body/lock line만 바꾸고 committed trajectory는 같은 인스턴스 유지; Escape는 resolved facing 복원, Apply는 한 번 rebuild
- World polyline/tick vertex 변환 순수 검사
- 4방위 Domain yaw가 mapper와 선택적 model forward offset 뒤 실제 3D 전방과 일치
- 실제 TopView `DisplayedTrajectories`와 실제 World `ImmediateMesh` node/visibility/vertex 검사
- seek를 반복해도 trajectory node 수가 늘지 않음
- actor 이동·회전 중 world trajectory vertex가 고정되고 과거/미래 명도 경계만 이동
- 0초 문서의 UV/uniform이 0이며 유일 sample이 미래로 흐려지지 않음
- lock enabled→disabled와 `Snap`→`Continuous`의 실제 actor rotation 변화
- exact runtime marker `LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1`
- 기존 세 runtime marker를 계속 요구

### 11.4 저장과 성능

- 방향·궤적 평가 전후 serialize 결과가 같고 schema `/2`에 파생 필드가 없음
- 4 actors, 10초, 60FPS playback에서 trajectory는 motion revision당 한 번 생성하고 Action-only revision에는 geometry를 재사용
- Domain 현재 snapshot 평가는 기존 2ms/frame 예산을 별도 기록하고, 60FPS 두 view 적용을 포함한 전체 p95가 16.67ms 안쪽인지 진단 probe로 기록
- cache hit/miss, trajectory build count, sample 수와 evaluator segment step 수를 출력해 알고리즘 복잡도 회귀를 확인
- 16 actors/1,000 keys는 절대 시간으로 flaky하게 실패시키지 않고 baseline 대비 회귀를 기록

이번 마일스톤은 대표 4 actors/각 100 transform·Lock-on keys 이하에서 full motion-revision trajectory rebuild p95 `8ms` 이하를 임시 허용 기준으로 둔다. 이 수치를 넘으면 actor/시간 구간 증분 재평가는 후속이 아니라 이번 완료의 blocker로 승격한다. 16 actors/1,000 keys 지원을 완료로 선언하기 전에는 영향 actor·변경 구간 증분 cache를 구현해야 한다. `docs/02-requirements.md`, `docs/09-performance-strategy.md`와 로드맵에 이 단계적 예외, 8ms 승격 기준과 장기 증분 요구를 함께 기록한다.

## 12. 구현 순서와 독립 경계

1. Domain 방향 수학과 경계 fixture를 RED로 만든 뒤 최소 evaluator를 구현한다.
2. snapshot facing map과 궤적 evaluator를 RED→GREEN으로 추가한다.
3. 0초 `PlaybackClock`, Application 투영 frame, motion revision cache와 재진입 직렬화를 구현하고 같은 인스턴스 계약을 고정한다.
4. TopView 순수 layout·layer와 surface 표시를 구현한다.
5. WorldView 재사용 mesh와 actor resolved facing 적용을 구현한다.
6. target 누락 표시, runtime probe, skeleton/harness와 serialization 회귀 검증을 추가한다.
7. README와 데이터·Editor·시각화·성능·테스트·로드맵 문서를 실제 구현 결과에 맞춘다.
8. 전체 xUnit, skeleton, Godot build/startup/runtime probe를 새로 실행하고 독립 리뷰 후 커밋·푸시한다.

Domain 기반이 확정되기 전 Application/Editor를 병렬 구현하지 않는다. 이후 서로 다른 파일을 수정하는 TopView, WorldView, 검증 문서 작업은 하위 에이전트로 병렬화할 수 있으나 최종 통합과 Git 작업은 메인 에이전트만 수행한다.

## 13. 제외 범위

- 실제 DSR 애니메이션 clip, root motion과 게임 프레임 데이터 추출
- Lock-on이 위치나 속도 자체를 바꾸는 이동 시뮬레이션
- 공격 판정, 충돌원과 뒤잡 성공 계산
- 궤적 전체/꼬리/숨김 사용자 설정과 렌더 export
- timeline 확대·스크롤·marker drag/복제·스냅 UX
- actor 삭제 기능 자체
- motion mutation 뒤 actor별·시간 구간 증분 궤적 재평가. 이번 범위는 Action-only cache reuse와 motion revision 전체 rebuild까지 구현한다.
- gamepad 입력
- 저장 schema v3

timeline 확대·스크롤·프레임/키프레임/구간 스냅의 우선순위는 이번 설계에서 검토했으며 방향·궤적과 파일/상태 경계가 겹치지 않으므로 별도 후속 마일스톤으로 구현한다.

## 14. 완료 기준

- 세 tracking mode가 승인된 의미대로 순수 Domain 테스트와 실제 Godot에서 구분된다.
- target 방향, offset, 위치 일치, 대상 누락, 0/360과 정확한 180도가 결정적이다.
- 기존 `ActorTransforms`와 schema `/2`가 보존된다.
- 같은 위치·시간 sample에서 자유/Lock-on 방향을 TopView와 WorldView가 같은 투영 프레임으로 표시한다.
- playback tick은 전체 궤적을 재계산하거나 Godot 노드를 새로 만들지 않는다.
- Action-only 편집은 trajectory geometry를 재사용하고 cache는 현재 source의 한 항목만 유지한다.
- preview 취소·확정 경계가 기존 문서/history 의미를 지킨다.
- 기존 테스트와 runtime marker가 모두 통과하고 새 exact marker가 추가된다.
- 관련 한글 문서, 테스트 수와 검증 명령이 실제 저장소 상태와 일치한다.
- 독립 리뷰에서 Critical/Important 지적이 0건이고 완료 커밋이 remote에 push된다.
