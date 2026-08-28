# 04. 데이터 아키텍처

## 단일 진실 공급원

`SceneDocument`가 저장·편집 가능한 모든 의미 데이터를 소유한다. 화면 노드 위치, 애니메이션 플레이어 상태, 선택 표시와 캐시는 저장 원본이 아니다. 문서에서 언제든 다시 생성할 수 있어야 한다.

```text
SceneDocument
├─ DocumentId / Name / Note
├─ DurationSeconds / FramesPerSecond
├─ ImportMetadata? (SourceFormat / RawSourcePayload)
├─ Actors[]
│  └─ ActorTrack (ActorId / DisplayName / Role)
│     ├─ TransformKeyframes[] (authored position/yaw)
│     ├─ ActionKeyframes[]
│     └─ LockOnKeyframes[]
├─ Revision (runtime 변경 순서, 비저장)
└─ MotionRevision (motion 변경 순서, 비저장)
```

위 트리는 현재 production `SceneDocument`의 실제 경계다. 카메라 트랙, combat rule set과 overlay 설정은 후속 모델이며 현재 `/2` 저장 파일의 필드인 것처럼 다루지 않는다.

## 식별자와 버전

- 문서, 배우, 트랙, 키프레임은 안정적인 ID를 가진다.
- 표시 이름은 변경할 수 있지만 참조는 ID로 연결한다.
- 시간 정렬 순서가 바뀌어도 ID는 유지한다.
- 저장 최상단에 문자열 필드 `schema`를 둔다.
- 현재 내부 저장 버전은 `pvp-guide-scene/2`다. legacy `pvp-guide-scene/1`은 읽기 migration 입력으로만 지원한다.
- 가져온 원본 형식과 버전은 `source` 메타데이터에 별도로 기록한다.

## 핵심 개념 모델

### SceneDocument

| 필드 | 형식 | 설명 |
| --- | --- | --- |
| `schema` | string | 현재 값 `pvp-guide-scene/2`인 내부 저장 포맷 버전 |
| `documentId` | string | 공백이 아닌 문서 안정 식별자 |
| `name` | string | 장면 이름 |
| `note` | string/null | 선택적인 교육 설명 (`SceneDocument.Note`는 nullable) |
| `durationSeconds` | double | 전체 길이 |
| `framesPerSecond` | int | 시간 표시·렌더 기준 FPS |
| `actors` | array | 배우와 트랙 |
| `importMetadata` | object/null | `sourceFormat`과 원본 `rawSourcePayload` |

### TransformKeyframe

- `id`: 키프레임 ID
- `timeSeconds`: 0 이상이며 트랙 안에서 고유한 시간
- `positionMeters`: 내부 3D 좌표 `(x, y, z)`
- `yawDegrees`: `[0, 360)`로 정규화한 수평 방향
- `positionInterpolation`: `step`, `linear`, `cubic`
- `rotationInterpolation`: `step`, `shortest_linear`, 향후 `squad`
- `note`: 해당 시점 설명

현재 구현의 `TransformKeyframe`은 ID, `timeSeconds`, `Position3`, `yawDegrees`만 영구 값으로 가진다. Yaw는 생성 시 `[0, 360)`로 정규화한다. time은 유한한 0 이상 값이어야 하며, `SceneDocument`에 넣거나 update할 때에는 문서 duration 안이어야 한다. actor의 transform track은 time 오름차순과 ID 고유성을 유지하고, 같은 time은 허용하지 않는다. 각 actor에는 최소 한 개의 transform keyframe이 반드시 남아야 한다.

### CRUD와 preimage 무결성

세 track CRUD는 `SceneDocument`의 명시적 경계에서만 영구 데이터를 바꾼다. 변환 경계는 다음과 같다.

| 연산 | Domain 경계 | preimage / 불변 조건 |
| --- | --- | --- |
| Add | `AddKeyframe(actorId, keyframe)` | actor 존재, time이 문서 범위 안, 새 ID·time이 해당 track에서 고유해야 한다. |
| Update | `UpdateTransformKeyframe(actorId, expectedCurrent, replacement)` | ID는 유지한다. 현재 값이 `expectedCurrent`의 ID·time·position·정규화 Yaw와 모두 같아야 하며, replacement time은 문서 범위 및 track 고유성 조건을 만족해야 한다. |
| Delete | `RemoveTransformKeyframe(actorId, expectedCurrent)` | 현재 값이 정확한 preimage와 같아야 하며 마지막 transform keyframe은 제거할 수 없다. |

`expectedCurrent`는 UI에서 편집을 시작했을 때의 immutable keyframe 값이다. 검증이 실패하면 Domain은 actor collection을 교체하거나 revision을 올리기 전에 예외로 끝난다. 기존 공개 API는 이를 `SceneEditResult.Conflict`로 유지한다. semantic 상세 API는 같은 결과에 typed `SemanticEditIssue`를 붙여 duplicate time, stale preimage, time range, ActionKey, Lock-on target/yaw/tracking mode와 일반 conflict를 구분하며 예외 메시지 문자열을 파싱하지 않는다. 의미적으로 같은 update는 `NoChange`이고 revision·Undo/Redo history를 만들지 않는다. 성공한 Domain 변경은 새 immutable `ActorTrack`을 만들어 교체하고 revision을 정확히 한 번 증가시킨다.

Action에는 `AddActionKeyframe`/`UpdateActionKeyframe`/`RemoveActionKeyframe`, Lock-on에는 `AddLockOnKeyframe`/`UpdateLockOnKeyframe`/`RemoveLockOnKeyframe`이 있다. 두 track도 exact full-frame preimage를 비교하고 ID를 유지하며, 같은 time과 중복 ID를 거부한다. Lock-on Add/Update는 target이 self가 아니고 같은 문서에 존재하는 actor ID인지 추가로 검사한다. Transform과 달리 Action/Lock-on은 마지막 frame을 삭제해 빈 track이 될 수 있다. Application의 여섯 command는 이 경계를 호출하며 세 track이 단일 monotonic revision과 단일 Undo/Redo stack을 공유한다.

### 영구 문서와 비영구 세션 상태

다음 값은 `SceneDocument` 저장 모델이 아니라 `DocumentSession`의 런타임 상태다.

- `SelectedActorId`, `SelectedTransformKeyframeId`, `SelectedActionKeyframeId`, `SelectedLockOnKeyframeId`와 각 선택의 immutable frame 사본
- `ActiveTimelineTrack` (`Transform`, `Action`, `LockOn`)
- `PlaybackClock`의 현재 time과 playing/paused 상태
- 활성 `TransformPreview`, 공유 Undo/Redo stack, track별 버튼 가능 여부와 잠금 이유

marker 클릭은 target active track을 먼저 확정한 뒤 bounded pause/seek loop에서 target actor/frame의 최신 값, paused target time, active track, selected ID와 full payload가 모두 맞는지 확인한다. observer가 첫 target 시도를 2초, 두 번째를 3초로 재지정하는 유한 재진입도 다음 target 시도로 흡수하며 실제 state가 다른 동안에는 `Applied`를 반환하지 않는다. 매 attempt 시작 시 track별 publication sequence와 active context를 캡처하고, 이후 실제 selection 게시의 actor/active track/ID/immutable full frame 서명이 최신 target과 같은지를 확인한다. seek state change 자체를 게시 증거로 간주하지 않고 이전 redirect의 force 상태도 누적하지 않으므로 최종 안정 target full payload는 한 번만 게시된다. 같은 시각 cross-track 전환뿐 아니라 rollback이 선택 ID/full frame을 보존한 채 frame time을 옮긴 상태도 실제 target 게시가 없으면 payload를 정확히 한 번 강제 게시하며, final event observer가 active track을 바꾼 경우에만 복원된 target context로 다시 게시한다. 선택 호출 전에는 actor, playback time/playing, active track과 세 selected ID를 비영구 rollback snapshot으로 보관한다. 재진입 중 target frame이 사라진 `Conflict`는 frame의 새 시각을 따라가지 않고 호출 time/playing을 복구한다. 이전 actor가 계속 선택돼 있으면 캡처 ID마다 최신 document full frame을 독립적으로 다시 읽으므로 다른 시각으로 이동한 frame도 같은 ID selection으로 남고, 삭제된 ID만 null이 된다. availability는 이 최신 selection과 복구된 playback 상태에서 다시 계산한다. actor selection이 해제·변경됐으면 snapshot actor를 강제로 되살리지 않고 null/현재 actor의 호출 시각 exact 상태로 재조정한다. rollback의 임시 playback 알림은 최종 payload로 합치고 nested actor 알림은 bounded FIFO 뒤 최종 active-track payload로 끝낸다. payload 게시 중 수락된 actor 변경은 뒤 observer 예외가 있어도 FIFO에서 적용·게시한 다음 그 예외를 다시 낸다. 새 semantic Add 뒤에는 새 keyframe을 선택한다. 빈 lane background 활성화는 `ActiveTimelineTrack`과 표시용 target selection payload만 바꾸며 document, history와 playback은 그대로 둔다. Delete/history 전환 뒤에는 세 track 각각 `기존 ID 유지 → 현재 time exact marker → time 거리·이른 time·ordinal ID 순 nearest → null` 규칙으로 다시 맞춘다. active track marker의 time만 재생 헤드를 안정화하며 다른 track selection은 full frame을 최신 문서에서 다시 읽는다. 이 규칙은 저장 파일에 UI 선택을 섞지 않고 Inspector·marker·투영을 동기화한다.

`PlaybackClock`은 공개 state와 아직 게시하지 않은 요청 tail을 분리한다. `Changed(time, isPlaying)`의 모든 observer가 실행되는 동안 공개 state는 payload와 같고, 재진입 `Pause`/`Seek`/`Advance`는 FIFO tail을 기준으로 다음 state를 만든다. 뒤 observer 예외는 앞 observer가 이미 수락시킨 state를 취소하지 않으며, 수락된 FIFO를 적용·게시한 뒤 최초 예외를 다시 낸다. 32회 내 안정화되지 않으면 bounded 비안정화 예외가 observer 예외보다 우선한다.

### ActionKeyframe

- `id`: 안정 keyframe ID
- `timeSeconds`: 유한한 0 이상 값이며 문서 duration 안, Action track 안에서 고유
- `actionKey`: 공백이 아닌 원문 의미 키. 현재 구현은 trim하거나 자산 ID로 바꾸지 않는다.

현재 영구 모델은 위 세 필드만 가진다. `variant`, playback speed/start offset, sync group과 local asset override는 실제 애니메이션 카탈로그 단계의 후속 확장이다.

### LockOnKeyframe

- `id`: 안정 keyframe ID
- `timeSeconds`: 유한한 0 이상 값이며 문서 duration 안, Lock-on track 안에서 고유
- `enabled`: 이 marker부터 Lock-on을 표시할지 여부
- `targetActorId`: null 또는 같은 문서의 다른 actor ID. `enabled=true`이면 null일 수 없다.
- `yawOffsetDegrees`: 유한한 값이며 생성 시 `[-180, 180)`으로 정규화
- `trackingMode`: `snap`, `continuous`, `keyframe_only`; 공개 Domain 생성 경로는 이 enum에 정의되지 않은 값도 거부

mode와 offset은 저장·Inspector·lane label·snapshot·overlay뿐 아니라 resolved facing과 paired trajectory 평가 입력으로 사용한다. authored `TransformKeyframe.YawDegrees` 자체는 바꾸지 않는다.

### EvaluatedActorFacing과 세 tracking mode

`SceneDocument.CreateSnapshot(time)`은 모든 authored transform과 단계 상태를 먼저 평가하고, 같은 actor 사전으로 `LockOnFacingEvaluator.Evaluate`를 호출해 `ActorFacings`를 만든다. `EvaluatedActorFacing`은 다음 세 값을 갖는 불변 record다.

- `YawDegrees`: `[0, 360)`로 정규화된 표시 방향
- `ResolutionKind`: 방향을 어떤 규칙으로 얻었는지 나타내는 `FacingResolutionKind`
- `SourceLockOnKeyframeId`: 현재 left-hold Lock-on 상태의 출처 ID 또는 첫 marker 전의 `null`

mode별 의미는 다음과 같다.

| mode/상태 | 평가 시각과 결과 | provenance |
| --- | --- | --- |
| Lock-on OFF | 현재 시각 authored Yaw | `AuthoredDisabled` |
| `keyframe_only` | 현재 시각 authored Yaw; target 방향 계산 안 함 | `AuthoredKeyframeOnly` |
| `snap` | source Lock-on keyframe 시각의 actor→target XZ 방향에 offset을 더해 고정 | `SnapTarget` |
| `continuous` | 현재 시각 actor→target XZ 방향에 offset을 더해 갱신 | `ContinuousTarget` |
| target 누락 | 현재 authored Yaw | `TargetUnavailableFallback` |
| 위치 일치 | 직전 유효 방향 또는 authored Yaw | `CoincidentPrevious` / `CoincidentAuthoredFallback` |

위치 일치는 XZ 상대 벡터 길이가 `LockOnFacingEvaluator.CoincidenceEpsilon = 1e-6` 이하일 때다. `continuous`는 actor와 target의 transform anchor를 source marker부터 현재 시각까지 정렬해 마지막 유효 segment 방향을 찾는다. 유효 방향에서 일치점으로 들어가는 segment는 epsilon 원 경계를 수치적으로 안정된 방식으로 계산한다. 이전 방향이 없으면 authored Yaw로 후퇴한다. 이 규칙은 NaN/무한 방향을 만들지 않고 같은 입력에 같은 provenance를 낸다.

### MotionRevision과 일반 Revision

`Revision`은 모든 성공한 영구 변경에 증가한다. `MotionRevision`은 actor 추가, Transform Add/Update/Delete, Lock-on Add/Update/Delete처럼 위치·자유 방향·resolved 방향·궤적을 바꿀 수 있는 변경에만 함께 증가한다. Action-only Add/Update/Delete는 `Revision`만 증가한다. 선택, 재생 헤드와 preview는 둘 다 바꾸지 않는다.

`SceneSnapshot`과 `MovementTrajectorySet`은 `Revision`과 `MotionRevision`을 함께 운반한다. Action-only 변경에서는 `MovementTrajectorySet.WithRevision(newRevision)`이 새 wrapper를 만들되 기존 `Actors` dictionary와 actor trajectory를 그대로 공유한다. 따라서 consumer는 최신 semantic snapshot과 이전과 동일한 motion geometry를 안전하게 조합할 수 있다. motion 변경에서는 `MotionRevision`이 달라져 재평가한다.

### TrajectorySamplePlan과 paired trajectory

`TrajectorySamplingSettings`는 `PolicyVersion`과 `MaximumUniformRate`를 가진다. 현재 Application 정책은 `lock-on-motion/v1`, 최대 30Hz이며 실제 `UniformRate`는 `min(document FPS, 30)`이다. `MovementTrajectoryEvaluator.CreatePlan`은 0초·문서 끝·uniform grid에 모든 actor의 Transform/Lock-on keyframe 시각을 합치고 중복을 제거해 엄격히 증가하는 `OrderedTimes`를 만든다. 0초 문서는 `[0]` 하나를 가진다.

`TrajectorySamplePlan.Fingerprint`는 domain 구분 문자열, policy version, uniform rate, sample 수와 각 double의 정확한 bit 표현을 SHA-256으로 계산한 소문자 16진수다. 생성자에 외부 fingerprint가 주어지면 payload와 다시 계산한 값이 정확히 같아야 한다. 따라서 rate나 anchor 하나가 달라진 plan을 같은 cache key로 오인하지 않는다.

배우별 `ActorMovementTrajectory.Samples`의 각 `MovementTrajectorySample`은 다음 paired 값을 한 위치·시각에 묶는다.

- `Position`: authored 이동 경로
- `FreeYawDegrees`: authored 자유 방향
- `LockOnFacing`: 같은 시각 resolved Lock-on 방향과 provenance
- `AnchorKind`: `ActorTransform`, `ActorLockOn`, `ActiveTargetTransform`의 flags 조합

`MovementTrajectorySet`은 document/revision/motion revision, `SamplingPolicyFingerprint`, `UniformRate`, actor dictionary와 전체 `SegmentSteps`를 갖는다. evaluator는 정렬된 canonical time을 forward cursor로 한 번 훑으며 sample마다 전체 key 목록을 다시 스캔하지 않는다. `SegmentSteps`는 actor별 canonical visit, Transform/Lock-on cursor 이동과 continuous facing segment 진행 횟수를 합친 결정적 진단값이다. wall-clock 시간이 아니며 key/sample 규모에 선형인지 회귀 테스트하는 데 쓴다.

## 좌표 변환

기존 가이드 좌표 `(guideX, guideY)`는 다음 기본식으로 내부 3D 좌표에 매핑한다.

```text
worldX = (guideX - originX) * metersPerGuideUnit
worldY = defaultGroundHeight
worldZ = (guideY - originY) * metersPerGuideUnit
worldYaw = normalizeDegrees(facingDeg)
```

가이드의 Y가 아래쪽 양수이고 Godot 탑뷰에서 내부 Z를 아래쪽 양수로 표시하므로 기본 매핑은 부호 반전 없이 `guideY → worldZ`다. 3D 카메라와 모델 전방축은 Godot의 관례와 자산 실제 전방축을 고려한 `modelForwardOffsetDegrees`로 보정한다. 화면 방향각과 모델 리깅 방향을 같은 값으로 억지로 취급하지 않는다.

`metersPerGuideUnit`은 실제 게임 거리 검증 전에는 문서에 명시되는 가져오기 배율이다. 기존 36px 캐릭터 지름을 곧바로 실제 미터로 주장하지 않는다.

## 보간 규칙

### 위치

선형 보간의 기본식은 다음과 같다.

```text
t = clamp((currentTime - left.time) / (right.time - left.time), 0, 1)
position = left.position + (right.position - left.position) * t
```

동일 시간 키프레임은 저장 전에 거부하거나 명시적 교체 명령으로 처리한다. 분모 0을 묵시적으로 허용하지 않는다.

### 방향

각도는 0/360 경계에서 최단 경로를 사용한다.

```text
delta = ((rightYaw - leftYaw + 540) % 360) - 180
yaw = normalizeDegrees(leftYaw + delta * t)
```

180°가 정확히 같은 양방향인 경우 현재 `ActorTrack`과 trajectory forward cursor는 양의 180° 방향을 선택한다. 이 tie-break는 같은 입력에서 snapshot과 trajectory가 같은 Yaw를 내도록 테스트로 고정한다.

### 단계 상태

현재 `ActorTrack.EvaluateAction(time)`과 `EvaluateLockOn(time)`은 해당 time 이하에서 가장 늦은 marker 하나를 선택하는 left-hold 평가다. 첫 Action marker 전에는 `(SourceKeyframeId=null, ActionKey=null)`, 첫 Lock-on marker 전이나 빈 track에는 `(null, false, null, 0, Continuous)`를 반환한다. 선택 marker 이후 값은 다음 같은 track marker 전까지 유지되고 마지막 값은 문서 끝까지 유지된다. Transform처럼 두 값 사이를 보간하지 않는다.

`SceneDocument.CreateSnapshot(time)`은 배우별 `EvaluatedTransform`, `EvaluatedActorTimelineState(Action, LockOn)`와 `EvaluatedActorFacing`을 같은 불변 snapshot에 넣고 입력 dictionary를 방어 복사한다. TopView와 WorldView가 trajectory와 함께 하나의 `SceneProjectionFrame`으로 이 snapshot을 공유하므로 서로 다른 time/revision의 body 방향, action label, lock line과 path를 조합할 수 없다. 공격 지속 시간, animation clip 종료와 event window는 아직 카탈로그가 없으므로 단순 left-hold를 대체하지 않는다.

## 가이드 V1 가져오기

`gangqueen-topview-guide-v1`에서 다음을 변환한다.

| 원본 | 내부 |
| --- | --- |
| `scene.name`, `scene.note` | 문서 메타데이터 |
| `coordinate_system` | 지원 축 선언을 검증하고 X/Z 변환에 사용; 선언 원문은 raw source metadata에도 보존 |
| `scene.keyframes[].actors[]` | 배우별 같은 시각의 Transform/Action/Lock-on keyframe |
| actor `action` | 의미 기반 Action key |
| actor `lock_on`, `lock_target` | offset 0·`Continuous`인 Lock-on keyframe |
| `backstab_rules`, `evaluations` | 현재 `SceneDocument`로 해석하지 않고 `ImportMetadata.RawSourcePayload`에 보존하며 warning 제공 |

원본의 `current_index`는 편집 UI 상태이므로 저장 의미 데이터와 분리해 가져오기 완료 후 선택 시간 힌트로만 사용한다.

`lock_on=false`인데 `lock_target`이 있는 샘플처럼 모순처럼 보이는 값은 삭제하지 않는다. 대상 후보는 보존하고 활성 여부는 `lock_on`으로 판단한다.

## 저장과 마이그레이션

- JSON은 UTF-8, 고정 소수점 정책이 아닌 왕복 가능한 숫자 직렬화를 사용한다.
- 필드 순서는 사람이 읽기 좋게 안정화하지만 의미 비교는 순서에 의존하지 않는다.
- serializer 출력 schema는 항상 `pvp-guide-scene/2`이며 Action의 ID/time/key와 Lock-on의 ID/time/enabled/target/offset/mode를 왕복한다.
- `/2` 입력은 모든 Lock-on frame에 유한 `yawOffsetDegrees`와 지원 mode 문자열을 요구한다. 누락, null, 비유한 수, unknown mode, unknown JSON member는 경로가 포함된 오류로 거부한다.
- `/1` 입력만 Lock-on의 새 두 멤버가 없을 수 있다. Deserialize 단계에서 offset `0`, mode `continuous`를 적용해 현재 Domain 객체를 만들고, 다시 Serialize하면 `/2`와 두 명시 필드가 나온다. legacy 파일 자체는 덮어쓰지 않는다.
- 마이그레이션과 `/2` round-trip은 Infrastructure `SceneDocumentSerializer` 책임이다. Editor 프로젝트는 Infrastructure를 참조하지 않으며 Godot runtime marker에 schema 성공 flag를 넣지 않는다. 완료 판정은 Infrastructure 전체 테스트의 migration/round-trip/strict failure/atomic load 결과로 한다.
- 다운그레이드 저장은 지원하지 않고 내보내기 기능으로 분리한다.
- 현재 scene serializer는 알 수 없는 멤버를 보존하지 않고 strict하게 거부한다. 가져온 원본 payload의 알 수 없는 값은 별도 `ImportMetadata.RawSourcePayload`에 보존한다.

## 파생 데이터와 캐시

`ActorFacings`, `MotionRevision`, `TrajectorySamplePlan`과 fingerprint, `MovementTrajectorySet`, `SegmentSteps`, TopView/World geometry, node/resource cache와 현재 playback time은 모두 파생 또는 세션 데이터다. Infrastructure 회귀 테스트는 실제 snapshot/facing/trajectory 평가 전·후의 `SceneDocumentSerializer.Serialize` 문자열과 deserialize 후 재직렬화 문자열이 byte-for-byte 같은지 확인한다. JSON property tree에는 `facing`, `trajectory/trajectories`, `motionRevision`, `cache/cacheKey`, `currentTime`, `revision` 의미의 필드가 없어야 한다. schema는 계속 `pvp-guide-scene/2`다.

Application의 trajectory cache key는 `(MotionRevision, TrajectorySamplePlan.Fingerprint)` 한 항목이다. Action-only revision에서는 `WithRevision`으로 geometry identity를 보존하고, Transform/Lock-on/actor 변경이나 sampling plan 변경에서만 rebuild한다. Editor의 TopView는 동일 actor trajectory dictionary 참조면 immutable geometry를 재사용하고 presentation만 다시 만든다. WorldView는 `(MotionRevision, SamplingPolicyFingerprint)`의 `WorldTrajectoryGeometryKey`가 같으면 actor geometry dictionary, 세 `ImmediateMesh`와 material을 유지하고 shader의 현재 시간 uniform만 바꾼다.

뒤잡 판정 결과, 향후 렌더용 변환 행렬과 썸네일도 같은 원칙의 파생 데이터다. 실제 DSR animation clip 참조와 collision/backstab 결과를 저장 모델에 넣을지는 별도 schema 설계 전까지 확정하지 않는다. 현재 마일스톤은 새 schema를 만들지 않는다.
