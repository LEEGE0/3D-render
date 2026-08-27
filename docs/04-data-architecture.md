# 04. 데이터 아키텍처

## 단일 진실 공급원

`SceneDocument`가 저장·편집 가능한 모든 의미 데이터를 소유한다. 화면 노드 위치, 애니메이션 플레이어 상태, 선택 표시와 캐시는 저장 원본이 아니다. 문서에서 언제든 다시 생성할 수 있어야 한다.

```text
SceneDocument
├─ Metadata
├─ CoordinateSpace
├─ TimelineSettings
├─ Actors[]
│  ├─ Identity / Role / Appearance
│  └─ ActorTrack
│     ├─ TransformKeyframes[]
│     ├─ ActionKeyframes[]
│     └─ LockOnKeyframes[]
├─ CameraTracks[]
├─ OverlaySettings
├─ CombatRuleSet
└─ ExtensionData
```

## 식별자와 버전

- 문서, 배우, 트랙, 키프레임은 안정적인 ID를 가진다.
- 표시 이름은 변경할 수 있지만 참조는 ID로 연결한다.
- 시간 정렬 순서가 바뀌어도 ID는 유지한다.
- 저장 최상단에 `schemaVersion`을 둔다.
- 현재 내부 저장 버전은 `pvp-guide-scene/2`다. legacy `pvp-guide-scene/1`은 읽기 migration 입력으로만 지원한다.
- 가져온 원본 형식과 버전은 `source` 메타데이터에 별도로 기록한다.

## 핵심 개념 모델

### SceneDocument

| 필드 | 형식 | 설명 |
| --- | --- | --- |
| `schemaVersion` | string | 내부 저장 포맷 버전 |
| `documentId` | UUID | 문서 안정 식별자 |
| `name` | string | 장면 이름 |
| `note` | string | 교육 설명 |
| `durationSeconds` | double | 전체 길이 |
| `framesPerSecond` | int | 시간 표시·렌더 기준 FPS |
| `coordinateSpace` | object | 단위, 축, 원점과 가져오기 변환 |
| `actors` | array | 배우와 트랙 |
| `cameraTracks` | array | 카메라 키프레임 |
| `combatRules` | object | 판정 규칙 세트 |
| `overlaySettings` | object | 표시·출력 여부 |
| `source` | object | 가져온 원본 정보와 해시 |
| `extensionData` | object | 알 수 없는 보존 필드 |

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

`expectedCurrent`는 UI에서 편집을 시작했을 때의 immutable keyframe 값이다. 검증이 실패하면 Domain은 actor collection을 교체하거나 revision을 올리기 전에 예외로 끝난다. Application command는 이를 `Conflict`로 다루며, stale update/undo/redo가 최신 committed 값을 덮어쓰지 않는다. 의미적으로 같은 update는 `NoChange`이고 revision·Undo/Redo history를 만들지 않는다. 성공한 Domain 변경은 새 immutable `ActorTrack`을 만들어 교체하고 revision을 정확히 한 번 증가시킨다.

Action에는 `AddActionKeyframe`/`UpdateActionKeyframe`/`RemoveActionKeyframe`, Lock-on에는 `AddLockOnKeyframe`/`UpdateLockOnKeyframe`/`RemoveLockOnKeyframe`이 있다. 두 track도 exact full-frame preimage를 비교하고 ID를 유지하며, 같은 time과 중복 ID를 거부한다. Lock-on Add/Update는 target이 self가 아니고 같은 문서에 존재하는 actor ID인지 추가로 검사한다. Transform과 달리 Action/Lock-on은 마지막 frame을 삭제해 빈 track이 될 수 있다. Application의 여섯 command는 이 경계를 호출하며 세 track이 단일 monotonic revision과 단일 Undo/Redo stack을 공유한다.

### 영구 문서와 비영구 세션 상태

다음 값은 `SceneDocument` 저장 모델이 아니라 `DocumentSession`의 런타임 상태다.

- `SelectedActorId`, `SelectedTransformKeyframeId`, `SelectedActionKeyframeId`, `SelectedLockOnKeyframeId`와 각 선택의 immutable frame 사본
- `ActiveTimelineTrack` (`Transform`, `Action`, `LockOn`)
- `PlaybackClock`의 현재 time과 playing/paused 상태
- 활성 `TransformPreview`, 공유 Undo/Redo stack, track별 버튼 가능 여부와 잠금 이유

marker 클릭은 target active track을 먼저 확정한 뒤 필요하면 pause/seek하고 해당 track의 full-frame selection payload를 게시한다. 다른 시각으로 seek할 때 observer는 target active track과 selection을 함께 보며, 같은 시각 cross-track 전환은 seek notification이 없어도 target payload를 정확히 한 번 다시 게시한다. 선택 호출 전에는 actor, playback time/playing, active track과 세 selected ID를 비영구 rollback snapshot으로 보관한다. 재진입 중 target frame이 사라진 `Conflict`는 frame의 새 시각을 따라가지 않고 호출 time/playing을 복구한다. 이전 actor가 계속 선택돼 있으면 캡처 ID마다 최신 document full frame을 독립적으로 다시 읽으므로 다른 시각으로 이동한 frame도 같은 ID selection으로 남고, 삭제된 ID만 null이 된다. availability는 이 최신 selection과 복구된 playback 상태에서 다시 계산한다. actor selection이 해제·변경됐으면 snapshot actor를 강제로 되살리지 않고 null/현재 actor의 호출 시각 exact 상태로 재조정한다. rollback의 임시 playback 알림은 최종 payload로 합치고 nested actor 알림은 bounded FIFO 뒤 최종 active-track payload로 끝낸다. payload 게시 중 수락된 actor 변경은 뒤 observer 예외가 있어도 FIFO에서 적용·게시한 다음 그 예외를 다시 낸다. 새 semantic Add 뒤에는 새 keyframe을 선택한다. Delete/history 전환 뒤에는 세 track 각각 `기존 ID 유지 → 현재 time exact marker → time 거리·이른 time·ordinal ID 순 nearest → null` 규칙으로 다시 맞춘다. active track marker의 time만 재생 헤드를 안정화하며 다른 track selection은 full frame을 최신 문서에서 다시 읽는다. 이 규칙은 저장 파일에 UI 선택을 섞지 않고 Inspector·marker·투영을 동기화한다.

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
- `trackingMode`: `snap`, `continuous`, `keyframe_only`

현재 mode와 offset은 저장·Inspector·lane label·snapshot·overlay까지 전달되는 의미 데이터다. 아직 target 방향으로 actor transform을 회전시키거나 이동 궤적을 생성하지 않는다.

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

180°가 정확히 같은 양방향인 경우 정책상 양의 방향 또는 이전 회전 방향을 선택하고 테스트로 고정한다.

### 단계 상태

현재 `ActorTrack.EvaluateAction(time)`과 `EvaluateLockOn(time)`은 해당 time 이하에서 가장 늦은 marker 하나를 선택하는 left-hold 평가다. 첫 Action marker 전에는 `(SourceKeyframeId=null, ActionKey=null)`, 첫 Lock-on marker 전이나 빈 track에는 `(null, false, null, 0, Continuous)`를 반환한다. 선택 marker 이후 값은 다음 같은 track marker 전까지 유지되고 마지막 값은 문서 끝까지 유지된다. Transform처럼 두 값 사이를 보간하지 않는다.

`SceneDocument.CreateSnapshot(time)`은 배우별 `EvaluatedTransform`과 `EvaluatedActorTimelineState(Action, LockOn)`을 같은 불변 snapshot에 넣고 입력 dictionary를 방어 복사한다. TopView와 WorldView가 이 한 snapshot을 공유하므로 서로 다른 time/revision의 action label과 lock line을 조합할 수 없다. 공격 지속 시간, animation clip 종료와 event window는 아직 카탈로그가 없으므로 단순 left-hold를 대체하지 않는다.

## 가이드 V1 가져오기

`gangqueen-topview-guide-v1`에서 다음을 변환한다.

| 원본 | 내부 |
| --- | --- |
| `scene.name`, `scene.note` | 문서 메타데이터 |
| `coordinate_system` | 원본 좌표 설명과 변환 설정 |
| `backstab_rules` | `CombatRuleSet` |
| `characters.*` | 배우와 같은 시간의 세 트랙 키프레임 |
| `action` | 의미 기반 행동 키 |
| `evaluations` | 비교·회귀 검증용 원본 평가 스냅샷 |

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

이동 궤적 샘플, 뒤잡 포함 비율, 바운딩 박스, 렌더용 변환 행렬과 썸네일은 파생 데이터다. 저장 파일에 필수 원본으로 넣지 않는다. 캐시 키는 문서 리비전, 트랙 리비전, 시간 범위와 설정 해시를 포함해 잘못된 재사용을 막는다.
