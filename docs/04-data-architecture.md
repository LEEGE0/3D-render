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
- 현재 설계의 내부 초기 버전은 `pvp-guide-scene/1`로 예약한다.
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

변환 CRUD는 `SceneDocument`의 다음 경계에서만 영구 데이터를 바꾼다.

| 연산 | Domain 경계 | preimage / 불변 조건 |
| --- | --- | --- |
| Add | `AddKeyframe(actorId, keyframe)` | actor 존재, time이 문서 범위 안, 새 ID·time이 해당 track에서 고유해야 한다. |
| Update | `UpdateTransformKeyframe(actorId, expectedCurrent, replacement)` | ID는 유지한다. 현재 값이 `expectedCurrent`의 ID·time·position·정규화 Yaw와 모두 같아야 하며, replacement time은 문서 범위 및 track 고유성 조건을 만족해야 한다. |
| Delete | `RemoveTransformKeyframe(actorId, expectedCurrent)` | 현재 값이 정확한 preimage와 같아야 하며 마지막 transform keyframe은 제거할 수 없다. |

`expectedCurrent`는 UI에서 편집을 시작했을 때의 immutable keyframe 값이다. 검증이 실패하면 Domain은 actor collection을 교체하거나 revision을 올리기 전에 예외로 끝난다. Application command는 이를 `Conflict`로 다루며, stale update/undo/redo가 최신 committed 값을 덮어쓰지 않는다. 의미적으로 같은 update는 `NoChange`이고 revision·Undo/Redo history를 만들지 않는다. 성공한 Domain 변경은 새 immutable `ActorTrack`을 만들어 교체하고 revision을 정확히 한 번 증가시킨다.

### 영구 문서와 비영구 세션 상태

다음 값은 `SceneDocument` 저장 모델이 아니라 `DocumentSession`의 런타임 상태다.

- `SelectedActorId`, `SelectedTransformKeyframeId`와 선택한 keyframe 객체
- `PlaybackClock`의 현재 time과 playing/paused 상태
- 활성 `TransformPreview`, Undo/Redo stack, 버튼 가능 여부와 잠금 이유

marker 클릭은 위 selection만 바꾸고 선택 keyframe time으로 seek한다. 새 Add 뒤에는 새 keyframe, Delete 뒤에는 가장 가까운 남은 keyframe을 선택한다. 문서 변경 또는 history 전환 뒤에도 ID가 유효하면 selection을 유지하고, 유효하지 않으면 현재 time의 marker 또는 가장 가까운 marker로 다시 맞춘다. 이 규칙은 저장 파일에 UI 선택을 섞지 않고 Inspector·marker·투영을 동기화한다.

### ActionKeyframe

- `timeSeconds`
- `actionKey`: `idle`, `move`, `attack` 같은 의미 기반 키
- `variant`: 무기·공격 형태 등 선택적 변형
- `playbackSpeed`
- `startOffsetSeconds`
- `syncGroupId`: 뒤잡 공격자·피격자처럼 동기화할 동작 묶음
- `assetOverride`: 로컬 카탈로그의 선택적 참조

### LockOnKeyframe

- `timeSeconds`
- `enabled`
- `targetActorId`
- `yawOffsetDegrees`
- `trackingMode`: `snap`, `continuous`, `keyframe_only`

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

행동, 락온 ON/OFF, 대상과 표시 토글은 다음 키프레임 이전까지 왼쪽 값을 유지한다. 공격처럼 지속 시간이 있는 행동은 카탈로그 또는 키프레임의 명시적 지속 시간으로 종료를 계산한다.

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
- 새 버전은 이전 버전을 읽는 순차 마이그레이션을 제공한다.
- 마이그레이션은 메모리에서 새 문서를 만든 뒤 검증하며 원본 파일을 덮어쓰지 않는다.
- 다운그레이드 저장은 지원하지 않고 내보내기 기능으로 분리한다.
- 알 수 없는 확장 필드는 가능한 한 `extensionData`에 보존한다.

## 파생 데이터와 캐시

이동 궤적 샘플, 뒤잡 포함 비율, 바운딩 박스, 렌더용 변환 행렬과 썸네일은 파생 데이터다. 저장 파일에 필수 원본으로 넣지 않는다. 캐시 키는 문서 리비전, 트랙 리비전, 시간 범위와 설정 해시를 포함해 잘못된 재사용을 막는다.
