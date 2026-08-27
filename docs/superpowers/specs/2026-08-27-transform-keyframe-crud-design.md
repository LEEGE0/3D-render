# 임의 시점 변환 키프레임 CRUD 설계

## 1. 목적과 범위

이번 마일스톤은 단계 3A의 읽기 전용 재생 헤드를 실제 변환 트랙 편집과 연결한다. 사용자는 배우를 선택하고, 일시정지된 현재 시각에 변환 키프레임을 추가하며, 마커로 키프레임을 선택해 위치·방향·시간을 수정하거나 삭제할 수 있다. 모든 영구 변경은 기존 `SceneDocument` revision, `Changed`, Command, Undo/Redo 계약을 그대로 따른다.

포함 범위는 다음과 같다.

- 배우별 transform keyframe ID/time/position/yaw 조회와 세션 선택 상태
- 현재 일시정지 시각의 평가 pose로 transform keyframe 추가
- 선택 keyframe의 time/position/yaw 원자적 수정
- 최소 한 개 transform keyframe을 보존하는 삭제
- 추가·수정·삭제 Command와 Undo/Redo
- 같은 시각/ID, 문서 범위 밖 시간, stale preimage, 재생 중 편집 거부
- 선택 배우의 transform marker 표시와 클릭 선택
- Inspector의 선택 keyframe 값·시간 편집
- TopView·3D·Inspector·marker·재생 헤드 동기화
- 단위 테스트, Godot 결정적 런타임 검사, 한글 문서 갱신

다음은 범위 밖이다.

- Action/Lock-on 트랙과 구간 편집
- marker drag, 복제, 다중 선택, box selection
- 타임라인 확대·스크롤·스냅
- 저장 형식 변경과 가져오기/내보내기 UI
- 실제 DSR 모델·애니메이션, 렌더 실행, 게임패드

대상 환경은 Windows 11, `D:\3D-render`, Godot 4.7.2 Stable .NET, C#, Forward+, 오프라인 독립 실행이다.

## 2. 접근 비교와 결정

### 접근 A — 명시적 Domain CRUD와 동작별 Command

`SceneDocument`와 `ActorTrack`에 transform 추가·시간/값 수정·삭제 계약을 명시하고 Application에는 동작별 Command를 둔다. 세션 선택은 저장되지 않는 편집 상태로 유지한다. 기존 optimistic preimage, revision/event, Undo/Redo와 projection 경계를 재사용할 수 있어 채택한다.

### 접근 B — transform track 전체 교체

Application이 전체 목록을 복사해 수정한 뒤 Domain에 통째로 전달하면 공개 메서드는 적다. 그러나 시간/ID/최소 개수 불변조건과 stale 판단이 Application으로 새고, 충돌 위치를 구체적으로 설명하기 어렵다. 채택하지 않는다.

### 접근 C — Editor에서 직접 문서 수정

버튼과 marker가 `SceneDocument`를 직접 호출하면 화면 구현은 짧아진다. 하지만 Undo/Redo, preview, stale conflict, observer 예외 복구가 여러 controller에 중복된다. 계층 의존 규칙을 위반하므로 채택하지 않는다.

## 3. Domain CRUD 계약

`TransformKeyframe`의 ID는 수정 중에도 바뀌지 않는다. 시간·위치·yaw만 한 번에 바꿀 수 있다. 같은 actor의 transform track에서는 ID와 시간이 각각 유일하며 actor에는 항상 최소 한 개의 transform keyframe이 남아야 한다.

`ActorTrack`은 immutable 재구성을 유지하며 다음 동작을 제공한다.

```csharp
public ActorTrack UpdateTransformKeyframe(
    TransformKeyframe expectedCurrent,
    TransformKeyframe replacement);

public ActorTrack RemoveTransformKeyframe(TransformKeyframe expectedCurrent);
```

- `UpdateTransformKeyframe`은 replacement ID가 expected ID와 같아야 한다.
- 현재 값은 ID/time/position/정규화 yaw가 expected와 모두 같아야 한다.
- 새 시간의 중복은 `ActorTrack` 생성자의 기존 정렬·유일성 검증으로 거부한다.
- `RemoveTransformKeyframe`은 같은 preimage 검증 뒤 삭제하며, transform 수가 한 개이면 거부한다.
- 기존 `ReplaceTransformKeyframe`은 시간 불변 pose 교체 호환 API로 남고 내부에서 update 계약을 재사용한다.

`SceneDocument`는 다음 공개 계약을 제공한다.

```csharp
public void AddKeyframe(string actorId, TransformKeyframe keyframe);

public bool UpdateTransformKeyframe(
    string actorId,
    TransformKeyframe expectedCurrent,
    TransformKeyframe replacement);

public void RemoveTransformKeyframe(
    string actorId,
    TransformKeyframe expectedCurrent);
```

- 추가와 수정의 새 시간은 유한한 `[0, DurationSeconds]`여야 한다.
- actor, keyframe, ID, 시간, 최소 개수 오류는 mutation 전에 검출한다.
- 동일한 time/position/정규화 yaw 수정은 `false`이며 revision/event를 만들지 않는다.
- 성공한 각 mutation은 immutable actor 교체, `Revision + 1`, `Changed` 한 번이다.
- 실패와 no-op은 actor 인스턴스, revision, event를 바꾸지 않는다.
- 기존 `ReplaceTransformKeyframe`은 replacement 시간이 expected와 같은지 확인한 뒤 `UpdateTransformKeyframe`을 호출한다.

삭제 Undo가 다른 변경을 지우지 않도록 삭제도 ID만 보지 않고 전체 expected preimage를 검증한다.

## 4. Command와 history 계약

Application에는 다음 internal Command를 둔다.

```csharp
internal sealed class AddTransformKeyframeCommand(
    string actorId,
    TransformKeyframe keyframe) : ISceneEditCommand;

internal sealed class UpdateTransformKeyframeCommand(
    string actorId,
    TransformKeyframe before,
    TransformKeyframe after) : ISceneEditCommand;

internal sealed class RemoveTransformKeyframeCommand(
    string actorId,
    TransformKeyframe keyframe) : ISceneEditCommand;
```

- Add Execute는 추가, Undo는 생성된 전체 preimage 삭제다.
- Update Execute는 before→after, Undo는 after→before다.
- Remove Execute는 전체 preimage 삭제, Undo는 동일 ID/time/pose 추가다.
- Redo는 같은 Command 인스턴스의 Execute를 다시 사용한다.
- duplicate ID/time, 범위 오류, 최소 개수, stale preimage는 기존 `SceneEditResult.Conflict`로 변환한다.
- observer가 문서 mutation 뒤 예외를 던지면 기존 revision 비교가 history 전이를 완료하고 예외를 표면화한다.
- 성공 한 번당 document revision 한 번, document event 한 번, history stack 전이 한 번이다.

## 5. 세션 선택과 편집 가능 상태

선택 keyframe은 문서에 저장하지 않는 `DocumentSession` 상태다.

```csharp
public string? SelectedTransformKeyframeId { get; private set; }
public TransformKeyframe? GetSelectedTransform();
public IReadOnlyList<TransformKeyframe> GetSelectedActorTransformKeyframes();
public bool CanAddTransformKeyframe { get; private set; }
public string? AddTransformKeyframeLockReason { get; private set; }
public bool CanDeleteSelectedTransformKeyframe { get; private set; }
public string? DeleteTransformKeyframeLockReason { get; private set; }

public void SelectTransformKeyframe(string? keyframeId, bool seekToKeyframe = true);
public SceneEditResult AddTransformKeyframeAtCurrentTime();
public SceneEditResult UpdateSelectedTransformKeyframe(
    double timeSeconds,
    Position3 position,
    double yawDegrees);
public SceneEditResult RemoveSelectedTransformKeyframe();
```

`SelectActor(actorId)`는 현재 시각과 정확히 일치하는 keyframe이 있으면 그것을 선택한다. 없으면 actor만 선택하고 keyframe 선택은 `null`이다. 기존 테스트와 첫 시각 편집 흐름은 t=0에 첫 keyframe이 있는 문서에서 그대로 유지된다.

`SelectTransformKeyframe`은 선택 actor 안에서 ID를 검증한다. 기본 호출은 playback을 pause하고 선택 keyframe 시각으로 seek한다. selection event는 최종 selection과 keyframe 정보를 불변 event args로 한 번 전달한다.

```csharp
public sealed class TransformKeyframeSelectionChangedEventArgs(
    string? actorId,
    string? keyframeId,
    TransformKeyframe? keyframe) : EventArgs;
```

다음 세 가지 가능 상태를 구분한다.

- `CanAddTransformKeyframe`: actor 선택, paused, 현재 시각에 기존 keyframe 없음
- `CanEditSelectedTransform`: actor/keyframe 선택, paused, playback time이 선택 keyframe time과 허용 오차 내 일치
- `CanDeleteSelectedTransformKeyframe`: 편집 가능하며 해당 actor의 transform keyframe이 두 개 이상

각 boolean의 false 원인은 `EditLockReason`, `AddTransformKeyframeLockReason`, `DeleteTransformKeyframeLockReason`에 한글로 보관한다. Editor는 같은 시간·최소 개수 규칙을 다시 계산하지 않고 이 값을 그대로 표시한다.

재생 중에는 세 동작을 모두 잠근다. 현재 시각에 keyframe이 없으면 TopView/Inspector pose 편집은 잠그되 Add는 허용한다. 현재 시각에 기존 keyframe이 있으면 Add를 거부하고 그 keyframe을 선택할 수 있게 상태 이유를 표시한다.

추가는 현재 `SceneDocument.CreateSnapshot(CurrentTimeSeconds)`의 선택 actor 평가 pose를 사용한다. 새 ID는 세션이 actor별 기존 ID와 충돌하지 않는 `{actorId}-transform-{ordinal:D4}` 형식으로 결정적으로 만든다. 성공 후 생성 keyframe을 선택하고 playback time은 유지한다.

수정 성공 후 같은 keyframe ID를 유지하고 새 time으로 seek한다. 삭제 성공 후 삭제 시간과 가장 가까운 남은 keyframe을 선택하며, 거리가 같으면 더 이른 keyframe을 선택하고 그 시각으로 seek한다. Undo/Redo 뒤에는 선택 ID가 여전히 존재하면 유지하고, 사라졌으면 현재 시각의 정확한 keyframe, 없으면 가장 가까운 keyframe, actor가 없으면 `null` 순으로 조정한다.

시간 변경이나 selection 전환 전에 active preview는 취소한다. preview는 선택 keyframe ID를 보유하며 Commit은 해당 keyframe의 전체 before를 사용하는 Update Command 하나를 만든다.

## 6. 이벤트와 원자성 순서

새 `TransformKeyframeSelectionChanged` event를 추가한다. 정상 UI 흐름의 관찰 순서는 다음과 같다.

```text
preview clear (필요한 경우)
→ document mutation / Changed
→ history stack 전이 / HistoryChanged
→ selection reconcile / TransformKeyframeSelectionChanged
→ playback seek (시간 이동·삭제 시 필요한 경우)
→ edit availability 최종 상태
```

외부 observer 예외 때문에 이미 완료된 문서 mutation을 실패로 오인하지 않는다. selection/playback 후속 알림이 실패해도 revision으로 저장 여부를 구분할 수 있어야 한다. 재진입 callback에서 오래된 selection 또는 availability payload를 최신 상태 뒤에 발행하지 않는다.

Projection은 변경하지 않는다. 문서 `Changed`가 현재 playback time의 `(revision,time)` snapshot을 다시 만들며 같은 인스턴스를 TopView와 WorldView에 전달한다. selection과 marker는 projection snapshot에 섞지 않는다.

## 7. Godot UI

타임라인은 기존 재생 컨트롤 아래에 다음 구조를 추가한다.

```text
TimelinePanel/TimelineControls
├─ PlaybackButtons
├─ TimeSlider
├─ CurrentTimeLabel
├─ TransformTrackSurface
├─ KeyframeToolbar
│  ├─ AddKeyframeButton
│  └─ DeleteKeyframeButton
└─ TimelineStatus
```

`TransformTrackSurface : Control`은 선택 actor의 transform keyframe만 그린다.

- 전체 document duration을 가로축으로 사용한다.
- 각 keyframe은 다이아몬드 marker로 표시한다.
- 선택 marker는 강조색, 현재 head와 정확히 일치하는 marker는 외곽선으로 구분한다.
- 클릭은 허용 반경 안의 가장 가까운 marker 하나를 선택하고 그 시각으로 seek한다.
- marker가 겹칠 수 없다는 Domain 시간 유일성 때문에 tie는 화면 x 거리, time, ID 순으로 결정한다.
- draw와 hit-test 계산은 Godot 독립 `TransformTrackLayout`에 두어 단위 테스트한다.

`TimelineController`는 Add/Delete 버튼, track surface, playback/selection/document event를 조립한다. Add는 현재 평가 pose를 생성하고 Delete는 선택 keyframe을 제거한다. 버튼 enabled 상태와 한글 상태는 세션의 가능 상태를 사용하며 시간·중복 규칙을 Godot에서 재계산하지 않는다.

Inspector는 다음처럼 확장한다.

```text
InspectorPanel/TransformInspector
├─ SelectedActorLabel
├─ SelectedKeyframeLabel
├─ TimeInput
├─ XInput / YInput / ZInput / YawInput
├─ ApplyButton
├─ UndoButton / RedoButton
└─ ErrorLabel
```

- `TimeInput` 범위는 `0..DurationSeconds`, step은 `1/FPS`다.
- label은 actor ID, keyframe ID, 현재 keyframe time을 표시한다.
- 값 변경은 선택 keyframe의 비영구 pose preview만 갱신한다. time은 Apply/Enter에서 함께 확정하며 playback은 commit 전까지 움직이지 않는다.
- Apply는 선택 keyframe의 time/position/yaw를 Update Command 하나로 확정한다.
- Undo/Redo 전 preview를 취소한다.
- Add 가능하지만 keyframe 미선택인 시각에는 pose input과 Apply는 비활성화하고 Add만 활성화한다.
- Delete는 타임라인 toolbar에서만 제공해 Inspector의 기존 세로 공간을 늘리지 않는다.

TopView는 선택 keyframe이 현재 head와 일치할 때 기존 drag/rotate preview를 사용한다. Commit 대상은 더 이상 `TransformKeyframes[0]`이 아니라 선택 keyframe이다. keyframe이 없는 보간 시각에는 선택과 보기만 가능하고 drag 편집은 잠긴다.

## 8. 오류 문구

사용자에게 다음 상황을 구분해 한글로 표시한다.

- `배우를 선택해야 키프레임을 편집할 수 있습니다`
- `재생 중에는 키프레임을 편집할 수 없습니다`
- `현재 시각에는 선택된 변환 키프레임이 없습니다`
- `현재 시각에 이미 변환 키프레임이 있습니다`
- `최소 하나의 변환 키프레임은 남아 있어야 합니다`
- `키프레임 시간이 문서 범위를 벗어났습니다`
- `선택한 키프레임의 변경이 오래되어 최신 문서 상태와 충돌했습니다`
- `적용할 실제 변환 변경이 없습니다`

정상적인 중복/범위/stale 거부는 문서와 history를 바꾸지 않는다. 프로그래밍 오류와 mutation 후 observer 예외는 삼키지 않고 기존 runtime 검증에서 표면화한다.

## 9. 테스트 전략과 완료 기준

### Domain

- add 성공·정렬·보간, duplicate ID/time, 범위 밖 시간, 실패 원자성
- update pose/time 성공, 정렬 재구성, same-value no-op, duplicate target time, stale expected, ID 변경 거부
- delete 성공, 최소 한 개 거부, stale expected, 삭제 후 clamp/interpolation
- 성공 mutation당 revision/event 정확히 한 번, 실패/no-op는 0회

### Application

- 현재 paused time의 평가 pose로 add하고 생성 keyframe 선택
- add/update/delete 각각 Execute→Undo→Redo와 redo clear
- 재생 중, 선택 없음, 같은 시각 중복, 범위 밖, 최소 개수, stale preimage 거부
- marker selection에 해당하는 session selection과 seek
- time 이동 뒤 selection 유지, 삭제 뒤 deterministic nearest selection
- seek/play 시 preview 취소, revision/history 불변
- Undo/Redo 뒤 selection 항상 유효
- mutation observer 예외 뒤 문서/history 전이 완료

### Editor와 런타임

- layout 단위 테스트로 marker 좌표, 선택 강조, hit-test tie 결정
- 실제 marker click이 session keyframe selection과 Inspector 값을 바꿈
- 실제 Add/Apply/Delete/Undo/Redo signal이 각각 문서·history·두 view·marker를 동기화
- active preview 뒤 scrub이 preview를 취소하고 CRUD를 확정하지 않음
- 재생 중 Add/Delete/Inspector/TopView가 모두 잠기고 이유 표시
- duplicate add, 범위 밖 time update, 마지막 keyframe delete가 UI에 오류를 표시하고 상태 불변
- 기존 exact marker를 모두 보존하고 다음 표식을 추가

```text
TIMELINE_KEYFRAME_CRUD_READY add=1 update=1 time_move=1 delete=1 undo=1 redo=1 duplicate_reject=1 range_reject=1 min_keyframe_guard=1 stale_conflict=1 selection_sync=1 preview_cancel=1 playback_lock=1
```

네 .NET 테스트 프로젝트를 직렬 실행하고 `Test-ProjectSkeleton.ps1`, `Test-GodotRuntime.ps1`이 모두 종료 코드 0이어야 한다. Godot build는 경고 0, 오류 0이어야 하며 기존 `PROJECT_RUNTIME_READY`, `PROJECTION_SYNC_READY`, `BASIC_EDITING_INTEGRATION_READY`, `BASIC_EDITING_READY`, `TIMELINE_PLAYBACK_READY`, `GODOT_RUNTIME_VERIFICATION=PASS` 문자열은 바꾸지 않는다.

## 10. 문서와 Git 완료 계약

- `README.md`에 marker 선택, Add, Time/pose Apply, Delete, Undo/Redo 사용법을 한글로 기록한다.
- `docs/03-system-architecture.md`, `docs/04-data-architecture.md`, `docs/05-editor-architecture.md`, `docs/13-roadmap.md`에서 Domain CRUD, 세션 selection, marker UI와 완료 범위를 연결한다.
- 저장 형식, 네트워크, 렌더, 자산 문서는 실제 계약이 바뀌지 않으므로 수정하지 않는다.
- 설계, 계획, 각 독립 구현 단위를 정확한 경로로 스테이징해 커밋하고 현재 기능 브랜치에 푸시한다.
- 사용자가 실제 CRUD 시나리오가 잘 된다고 보고하면 즉시 태그하지 않고 구체적 기능을 재확인한 뒤 긍정 응답에만 `working/<기능>-YYYYMMDD-HHmm` 주석 태그를 생성한다.
