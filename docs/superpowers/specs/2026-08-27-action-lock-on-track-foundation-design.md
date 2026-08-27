# Action/Lock-on 트랙 기반 설계

## 1. 목적

이번 마일스톤은 이미 문서와 가져오기 형식에 존재하는 `ActionKeyframe`·`LockOnKeyframe`을 실제 시간 평가, 명령 기반 편집, 타임라인 선택, TopView·3D 교육 오버레이까지 연결한다. 사용자는 선택 배우의 Action과 Lock-on 트랙을 시간축에서 추가·수정·삭제하고 Undo/Redo할 수 있어야 하며, 두 뷰는 같은 `SceneSnapshot`에서 평가된 의미 상태를 표시해야 한다.

이 단계는 의미 기반 foundation이다. 실제 DARK SOULS REMASTERED 애니메이션 파일 재생, 루트 모션, 공격 판정, 락온에 의한 transform yaw 자동 변경, 뒤잡 성공식과 영상 렌더는 포함하지 않는다.

## 2. 고정 제약

- Windows 11 전용, 오프라인 독립 실행을 유지한다.
- Godot 4.7.2 Stable .NET, .NET 8, C#, Forward Plus를 유지한다.
- 프로젝트·도구·캐시·로컬 게임 자산은 D 드라이브 정책을 유지한다.
- 게임패드 입력은 이번 범위에 포함하지 않는다.
- 원본 DSR 자산이나 개인 경로를 저장소·문서·런타임 fixture에 넣지 않는다.
- `SceneDocument`가 유일한 영구 상태다. playback time, selection, preview, history stack은 저장하지 않는다.
- 성공 mutation만 revision을 증가시키고 `Changed`를 발행한다. 실패·충돌·동일 값은 문서와 history를 바꾸지 않는다.
- 기존 transform CRUD와 exact runtime marker는 그대로 보존한다.
- 모든 구현 단위는 검토 후 메인 에이전트가 정확한 파일만 stage하고 커밋·푸시한다.

## 3. 현재 상태와 문제

`ActorTrack`은 transform, action, lock-on 컬렉션을 정렬·보존하고 Infrastructure는 기본 action/lock 필드를 JSON과 가이드 V1에서 왕복한다. 그러나 action/lock은 생성 이후 변경 API가 없고, 지정 시각 상태를 평가하지 않으며, `SceneSnapshot`도 transform만 포함한다. `DocumentSession`, 타임라인 surface, Inspector와 projection consumer 역시 transform 전용이다.

또한 문서 설계에는 lock-on의 `yawOffsetDegrees`와 `trackingMode`가 있으나 현재 `LockOnKeyframe`과 저장 DTO에는 없다. 같은 `/1` schema 의미를 조용히 바꾸지 않고 명시적 v2 migration으로 정렬해야 한다.

## 4. 접근 비교

### 접근 A — transform 패턴을 Action·Lock-on에 그대로 복제

트랙마다 명령, selection, marker surface와 Inspector를 별도로 복제한다. 초기 구현은 빠르지만 `DocumentSession`과 Editor에 동일한 정렬·평가·가용성 규칙이 반복되고 이후 camera·overlay 트랙에서 중복이 커진다.

### 접근 B — 모든 트랙을 generic framework로 선행 리팩터링

`ITimelineKeyframe<T>`와 generic command/session/surface를 만들고 이미 검증된 transform까지 옮긴다. 장기적으로 통일되지만 transform preview·재진입·선택 계약을 동시에 다시 쓰게 되어 이번 foundation보다 위험과 범위가 크다.

### 접근 C — 공통 stepped 계산 + 명시적 트랙 API

transform 계약은 유지한다. action/lock은 타입별 Domain API, Command, selection event와 Inspector를 명시적으로 두고, left-hold 평가·구간 layout·nearest 선택 같은 순수 계산만 공유한다. 트랙별 불변조건이 드러나면서 검증된 transform 흐름을 보존하므로 이 접근을 채택한다.

## 5. Domain 모델과 평가

### 5.1 Action 상태

`ActionKeyframe`은 현재의 `Id`, `TimeSeconds`, `ActionKey`를 유지한다. `ActionKey`는 비어 있지 않은 의미 문자열이며 이번 단계에서는 자산 ID로 해석하지 않는다.

```csharp
public sealed record EvaluatedActionState(
    string? SourceKeyframeId,
    string? ActionKey);
```

지정 시각 이하에서 가장 늦은 keyframe 값을 다음 keyframe 직전까지 유지한다. 첫 keyframe 이전과 빈 트랙은 `SourceKeyframeId=null`, `ActionKey=null`이다. 명시적 duration을 저장하지 않으며 화면의 action 구간은 `[현재 keyframe time, 다음 keyframe time)`을 나타내는 의미 상태 구간이다.

### 5.2 Lock-on 상태

```csharp
public enum LockOnTrackingMode
{
    Snap,
    Continuous,
    KeyframeOnly,
}

public sealed class LockOnKeyframe
{
    public string Id { get; }
    public double TimeSeconds { get; }
    public bool Enabled { get; }
    public string? TargetActorId { get; }
    public double YawOffsetDegrees { get; }
    public LockOnTrackingMode TrackingMode { get; }
}

public sealed record EvaluatedLockOnState(
    string? SourceKeyframeId,
    bool Enabled,
    string? TargetActorId,
    double YawOffsetDegrees,
    LockOnTrackingMode TrackingMode);
```

Lock-on도 left-hold로 평가한다. 첫 keyframe 이전과 빈 트랙은 disabled, target null, offset 0, mode `Continuous`다. `Enabled=true`이면 다른 기존 actor target이 필수다. disabled 상태는 가져온 가이드의 후보 target을 보존할 수 있다. target은 자기 자신일 수 없다. `YawOffsetDegrees`는 유한해야 하고 저장 시 `[-180, 180)`으로 정규화한다. 공개 생성 경로는 `Enum.IsDefined`로 정의되지 않은 `LockOnTrackingMode` 값도 거부한다.

tracking mode의 foundation 의미는 다음과 같다.

- `Snap`: keyframe 시점에 목표 방향을 한 번 적용하는 후속 계산용 의미 값.
- `Continuous`: 매 평가 시점에 보간된 target 위치를 계속 추적하는 후속 계산용 의미 값.
- `KeyframeOnly`: transform 방향을 자동 변경하지 않고 락온 상태·대상 오버레이만 유지하는 의미 값.

이번 단계에서는 세 mode 모두 UI와 snapshot에 표시하지만 actor transform yaw를 변경하지 않는다.

### 5.3 Actor·문서 mutation

`ActorTrack`과 `SceneDocument`에 action/lock 각각의 Add, full-preimage Update, full-preimage Remove를 추가한다.

```csharp
public ActorTrack AddActionKeyframe(ActionKeyframe keyframe);
public ActorTrack UpdateActionKeyframe(ActionKeyframe expectedCurrent, ActionKeyframe replacement);
public ActorTrack RemoveActionKeyframe(ActionKeyframe expectedCurrent);
public EvaluatedActionState EvaluateAction(double timeSeconds);

public ActorTrack AddLockOnKeyframe(LockOnKeyframe keyframe);
public ActorTrack UpdateLockOnKeyframe(LockOnKeyframe expectedCurrent, LockOnKeyframe replacement);
public ActorTrack RemoveLockOnKeyframe(LockOnKeyframe expectedCurrent);
public EvaluatedLockOnState EvaluateLockOn(double timeSeconds);
```

각 트랙의 ID와 time은 고유하다. action/lock 트랙은 비어도 되므로 마지막 marker 삭제를 허용한다. replacement는 같은 ID를 유지한다. expected full preimage가 현재 값과 다르면 stale conflict다. 동일한 replacement는 `NoChange`이며 revision/event/history가 없다. 문서 범위 밖 time, 유한하지 않은 값, 중복 ID/time과 잘못된 target은 mutation 전에 거부한다.

### 5.4 단일 snapshot

```csharp
public sealed record EvaluatedActorTimelineState(
    EvaluatedActionState Action,
    EvaluatedLockOnState LockOn);

public IReadOnlyDictionary<string, EvaluatedActorTimelineState> ActorTimelineStates { get; }
```

`SceneDocument.CreateSnapshot(t)`가 transform과 semantic state를 함께 평가한다. TopView와 WorldView는 별도 문서 조회를 하지 않고 같은 snapshot을 소비한다. `SceneProjectionController`의 `(revision,time)` 중복 억제 계약은 그대로 유지한다.

## 6. 저장 형식 v2와 마이그레이션

serializer의 top-level 필드명은 실제 구현과 같은 `schema`를 사용한다.

- 현재 `/1`: `pvp-guide-scene/1`
- 새 쓰기 형식 `/2`: `pvp-guide-scene/2`

v2 lock-on DTO에 다음을 추가한다.

```json
{
  "yawOffsetDegrees": 0.0,
  "trackingMode": "continuous"
}
```

허용 문자열은 `snap`, `continuous`, `keyframe_only`뿐이다. `/1` 읽기는 offset `0`, mode `continuous`로 메모리 migration한 뒤 동일 Domain 생성 경로를 사용한다. 새 저장은 항상 `/2`를 쓴다. `/2` unknown member, 잘못된 enum, nonfinite offset은 기존 strict 정책처럼 실패한다. 가이드 V1 importer는 기본 offset `0`, mode `continuous`를 생성한다. 원본 payload 보존 정책은 바꾸지 않는다.

## 7. Application 명령·선택·가용성

### 7.1 명령과 history

action/lock 각각 Add, Update, Remove command를 둔다. 하나의 `DocumentSession` undo/redo stack을 transform과 공유한다. Command는 immutable full pre/postimage를 보유하고 Execute/Undo 모두 optimistic stale 검증을 사용한다. mutation observer 예외와 `HistoryChanged` 재진입 뒤에는 현재 문서를 다시 읽어 selection과 availability를 조정한다.

### 7.2 트랙 selection

저장되지 않는 세션 상태를 추가한다.

```csharp
public enum TimelineTrackKind { Transform, Action, LockOn }

public TimelineTrackKind ActiveTimelineTrack { get; }
public string? SelectedActionKeyframeId { get; }
public string? SelectedLockOnKeyframeId { get; }
```

각 marker click은 playback을 pause하고 keyframe time으로 seek하며 해당 트랙을 active로 만든다. observer가 seek를 다른 시각으로 연속 리디렉션할 수 있으므로 최신 문서의 target actor/frame을 다시 읽는 bounded 안정화 루프를 사용하고, 최종 actor/time/playing/active track/ID/full payload가 모두 target과 일치할 때만 성공한다. 각 attempt 시작 시 track별 selection publication sequence와 active context를 캡처하고, 이후 게시된 마지막 actor/track/ID/immutable full-frame 서명이 target과 정확히 같은지를 확인한다. seek state change 자체는 게시 증거가 아니며 이 attempt 증거를 다음 attempt로 누적하지 않아 최종 안정 target payload를 정확히 한 번 게시한다. seek가 없는 same-time cross-track 전환과 rollback 뒤 다른 시각으로 이동한 frame의 ID/full selection 보존 상태는 실제 target 게시가 없으면 target payload를 한 번 강제하고, final event observer가 active track을 바꾸면 target context 복원 뒤 다시 게시한다. 안정화되지 않거나 target이 사라지면 호출 전 actor/time/playing/track/세 selection을 원자적으로 복구하고 성공을 반환하지 않는다. actor selection이 바뀌면 세 트랙 selection을 새 actor의 현재 시각 exact marker 기준으로 조정한다. playback seek는 현재 시각과 정확히 일치하지 않는 active marker selection을 해제하지만 evaluated state는 계속 제공한다.

Undo/Redo reconciliation은 트랙마다 `기존 ID 보존 → 현재 시각 exact → 가장 가까운 marker → null` 순이다. nearest tie는 더 이른 time, 그다음 ordinal ID다. action/lock track이 비면 null이 정상이다.

### 7.3 CRUD와 가용성

```csharp
SceneEditResult AddActionKeyframeAtCurrentTime(string actionKey);
SceneEditResult UpdateSelectedActionKeyframe(double timeSeconds, string actionKey);
SceneEditResult RemoveSelectedActionKeyframe();

SceneEditResult AddLockOnKeyframeAtCurrentTime(
    bool enabled,
    string? targetActorId,
    double yawOffsetDegrees,
    LockOnTrackingMode trackingMode);
SceneEditResult UpdateSelectedLockOnKeyframe(
    double timeSeconds,
    bool enabled,
    string? targetActorId,
    double yawOffsetDegrees,
    LockOnTrackingMode trackingMode);
SceneEditResult RemoveSelectedLockOnKeyframe();
```

ID는 `{actorId}-action-{ordinal:D4}`, `{actorId}-lock-on-{ordinal:D4}`로 충돌 없이 생성한다. 이미 저장된 `lock-on` 규약은 변경하거나 migration하지 않는다. 재생 중 모든 semantic CRUD를 잠근다. Add는 현재 시각에 같은 트랙 marker가 있으면 잠긴다. Update/Delete는 해당 active marker가 현재 시각에 정확히 선택됐을 때만 가능하다. action/lock은 마지막 marker도 삭제할 수 있다.

Undo/Redo 자체의 가용성은 transform 편집 가능 여부에 종속시키지 않는다. 선택 actor가 있고 playback이 정지됐으며 stack이 있으면 active track과 무관하게 실행할 수 있다. 실행 후 트랙별 reconciliation이 실제 문서 상태를 복원한다.

## 8. Editor UX

### 8.1 타임라인 세 lane

선택 actor에 대해 Transform, Action, Lock-on lane을 세로로 표시한다. transform lane은 기존 동작을 유지한다.

- Action lane: marker와 다음 marker/document end까지의 step segment, `ActionKey` label.
- Lock-on lane: enabled segment는 강조색과 target label, disabled segment는 muted 색, mode 축약 label.
- 선택 marker는 fill, 현재 head exact marker는 별도 outline으로 구분한다.
- marker와 segment 좌표·clipping·hit-test는 Godot 독립 `StepTrackLayout`에서 계산한다.
- marker가 없는 Action/Lock-on lane의 빈 배경 click은 해당 트랙만 active로 바꿔 첫 Add Inspector를 표시하며 document/history/playback을 바꾸지 않는다.

Godot surface는 draw와 실제 pointer event 전달만 담당하며 mutation 규칙을 재계산하지 않는다.

### 8.2 Inspector

기존 Transform Inspector는 유지하고 active track에 따라 Action 또는 Lock-on 편집 section을 표시한다.

Action section:

- selected keyframe ID/time
- Time SpinBox
- ActionKey LineEdit
- Add, Apply, Delete

Lock-on section:

- selected keyframe ID/time
- Time SpinBox
- Enabled CheckBox
- Target OptionButton: 자기 자신 제외 actor 목록과 disabled용 `없음`
- TrackingMode OptionButton
- YawOffset SpinBox
- Add, Apply, Delete

Time step은 문서 FPS에서 계산한다. Enter/Apply는 time과 semantic 값을 하나의 command로 확정한다. semantic 필드는 transform처럼 뷰 preview를 만들지 않고 Apply 전까지 로컬 입력으로만 유지한다. 선택·playback·문서 변경 시 committed 값을 다시 읽으며 typed outcome으로 stale, duplicate, range, target/mode, no-op을 한글로 구분한다. mutation observer가 예외를 던지면 controller는 호출 전후 revision을 비교해 mutation이 확정된 경우 `변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: ...`로 안내한다. revision이 증가하지 않은 예기치 않은 예외는 catch하지 않고 그대로 전파한다.

### 8.3 TopView·3D overlay

TopView는 actor 옆에 현재 action label을 표시하고 evaluated lock-on이 enabled이면 actor 중심에서 target 중심까지 선과 target marker를 그린다. 자기 자신·존재하지 않는 target은 Domain에서 차단되므로 Editor는 fallback mutation을 하지 않는다.

WorldView는 기존 actor `OverlayRoot` 아래에 action `Label3D`, lock target/mode badge와 단순 방향 선용 mesh를 재사용한다. 실제 모델, AnimationPlayer, 공격 hitbox와 transform yaw 변경은 만들지 않는다. 두 뷰의 표시 값은 같은 `SceneSnapshot.ActorTimelineStates`에서 온다.

## 9. 이벤트·오류·수명주기

- 모든 session event payload는 현재 actor/track/keyframe의 full immutable 값을 포함하고 발행 직전에 최신 상태와 일치하는지 검사한다.
- `HistoryChanged`, document `Changed`, playback, selection callback의 동기 재진입 뒤 stale payload를 발행하지 않는다.
- controller와 surface는 Attach/Detach 또는 `IDisposable`로 Godot signal과 .NET event를 정확히 해제한다.
- observer 예외가 mutation 뒤 발생하면 문서와 history transition을 실제 mutation 방향으로 마치고 selection을 현재 문서와 재조정한 뒤 예외를 보존한다.
- Inspector는 `Conflict`, `NoChange`, 범위/target validation, mutation-after-observer를 서로 다른 한글 메시지로 표시한다.
- headless runtime 실패는 반복 횟수 watchdog 없이 명시적 nonzero `SceneTree.Quit`로 종료한다.

## 10. 검증

### Domain·Infrastructure

- action/lock left-hold 평가: 빈 트랙, 첫 marker 전, exact, 사이, 마지막 이후.
- Add/Update/Delete, last delete, duplicate ID/time, no-op, stale preimage, range와 target validation.
- snapshot defensive copy와 transform/action/lock 동일 시각 평가.
- `/1 → /2` migration, `/2` round-trip, enum 문자열·offset strict failure, importer 기본값.

### Application

- action/lock selection의 pause·seek·active track·revision/history 불변.
- deterministic ID, CRUD/Undo/Redo, last delete, nearest reconciliation.
- playback lock과 active track 독립 global Undo/Redo.
- document/HistoryChanged observer 예외·재진입에서 selection/payload/availability 최신성.

### Editor·런타임

- `StepTrackLayout`의 marker/segment 좌표, clipping, tie-break, zero/narrow width.
- 실제 Action/Lock-on marker click, Add/Apply/Delete, Undo/Redo signal.
- 실제 빈 lane background click, 보이는 입력·Add signal, typed validation/no-op과 mutation-after-observer 안내.
- transform exact marker를 그대로 보존하고 새 exact marker를 추가한다.

```text
ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1
ACTION_LOCK_ON_REVIEW_FIXES_READY empty_action_add=1 empty_lock_add=1 detailed_errors=1 observer_commit=1
```

- schema v2 round-trip과 v1 migration은 Infrastructure 테스트가 증명하며 Editor가 serializer를 참조하지 않는다.
- 네 테스트 프로젝트 직렬 PASS, skeleton PASS, Godot build 경고 0·오류 0, 두 startup failure probe와 모든 기존 exact marker PASS가 필요하다.

## 11. 구현 단위

1. Domain stepped evaluation과 action/lock CRUD.
2. schema v2 serializer migration과 importer 기본값.
3. Application command, active-track selection, availability와 shared history.
4. 순수 step layout, Action/Lock-on lane과 Inspector.
5. 단일 snapshot 기반 TopView·WorldView overlay.
6. 실제 UI signal runtime 검증, 한글 문서와 roadmap 완료 기록.

Task 1과 기존 `/1` fixture 기반 Task 2 migration RED는 파일이 겹치지 않는 범위에서 병렬 조사할 수 있지만, 실제 serializer GREEN은 Task 1의 새 Domain 생성자를 소비하므로 순차 통합한다. Editor lane과 overlay는 Application/Domain 공개 계약 확정 뒤 서로 다른 파일에서 병렬 구현할 수 있다. 최종 검증·커밋·푸시는 메인 에이전트가 담당한다.

## 12. 완료 기준

사용자는 선택 actor의 Action과 Lock-on marker를 실제 Godot UI에서 추가·선택·수정·삭제하고 shared Undo/Redo로 왕복할 수 있다. scrub/playback 시 left-hold semantic state가 같은 snapshot으로 TopView와 3D에 일치하게 표시된다. `/1` 문서는 손실 없이 열리고 새 저장은 `/2`로 round-trip한다. 실제 DSR 애니메이션·루트 모션·전투 판정 없이도 action/lock 의미 트랙 편집과 교육 오버레이가 결정적으로 검증된다.
