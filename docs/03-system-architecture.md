# 03. 시스템 아키텍처

## 아키텍처 형태

배포 단위는 하나의 오프라인 데스크톱 실행 프로그램인 모듈식 모놀리스다. 기능을 프로세스로 나누지 않고 어셈블리·네임스페이스·폴더 경계로 분리한다. 이렇게 하면 설치와 장애 복구가 단순하고, 온라인 서비스 없이도 전체 기능을 제공하면서 도메인 로직 테스트는 엔진과 독립적으로 수행할 수 있다.

```text
┌──────────────────────── Godot UI / Presentation ────────────────────────┐
│ Main Window │ Top View │ 3D View │ Timeline │ Inspector │ Render Panel │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │ Commands / Queries
┌──────────────────────── Application Layer ───────────────────────────────┐
│ Document Session │ Undo/Redo │ Playback │ Selection │ Import/Render Jobs │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │ Domain interfaces
┌────────────────────────── Domain Layer ──────────────────────────────────┐
│ SceneDocument │ Tracks │ Keyframes │ Interpolation │ Combat Evaluation   │
└──────────────────────────────┬───────────────────────────────────────────┘
                               │ Ports
┌──────────────────────── Infrastructure ──────────────────────────────────┐
│ JSON │ Atomic File Store │ Guide Import │ Asset Catalog │ FFmpeg Adapter │
└──────────────────────────────────────────────────────────────────────────┘
```

## 계층 책임

### 도메인

Godot의 `Node`, `Vector3`, `Resource`에 의존하지 않는 순수 C# 모델과 계산을 둔다. 장면 문서, 좌표계, 키프레임, 보간, 락온 방향, 뒤잡 평가와 검증 규칙이 여기에 속한다. 엔진을 실행하지 않고 단위 테스트할 수 있어야 한다.

### 애플리케이션

사용자 의도를 명령으로 바꾸고 문서 세션을 관리한다. 명령 실행·되돌리기, 선택, 현재 시간, 재생, 저장 필요 상태, 가져오기와 렌더 작업의 생명주기를 조율한다. 도메인 객체를 직접 화면에 노출하지 않고 읽기 모델 또는 변경 이벤트를 제공한다.

### 프레젠테이션

Godot 장면과 C# 스크립트로 창, 패널, 입력, 2D/3D 투영과 시각 효과를 구현한다. 화면 노드는 문서의 소유자가 아니며, 사용자 입력을 명령으로 전달하고 문서 스냅샷을 그리는 역할만 한다.

### 인프라

파일 시스템, JSON, 로컬 설정, 이미지 시퀀스, 외부 도구 실행과 자산 카탈로그를 구현한다. 모든 외부 경계는 인터페이스 뒤에 두어 테스트 대역으로 교체할 수 있게 한다.

## 현재 구현된 런타임 구성요소

| 구성요소 | 책임 |
| --- | --- |
| `DocumentSession` | 열린 문서, playback·actor/세 track 비영구 선택·transform preview, Transform/Action/Lock-on CRUD 공개 API와 단일 Undo/Redo 스택 관리 |
| `PlaybackClock` | 현재 시간과 playing/paused 상태, seek/play/pause/toggle/stop/advance 및 bounded FIFO 변경 알림 제공 |
| `SceneDocument.CreateSnapshot` | 지정 시간의 authored transform·단계 상태·resolved Lock-on facing을 하나의 불변 `SceneSnapshot`으로 계산 |
| `LockOnFacingEvaluator` | `snap`·`continuous`·`keyframe_only` 방향, provenance와 coincidence fallback 계산 |
| `MovementTrajectoryEvaluator` | 결정적 sample plan과 배우별 자유 방향/Lock-on 방향 paired trajectory 계산 |
| `SceneProjectionController` | stable `SceneProjectionFrame` 생성, 단일 trajectory cache, 재진입 직렬화와 TopView/WorldView 동일 frame 배포 |
| `SemanticTimelineController` | Action/Lock-on Add/Delete 버튼을 track별 가용성과 세션 command에 연결 |
| `ActionLockOnInspectorController` | active semantic track 전환, committed 입력 동기화와 Action/Lock-on 원자적 Apply |
| `RenderQueue` | 검증된 `RenderJob`의 thread-safe FIFO `Enqueue`·방어 복사 `Snapshot`·`TryPeek`·`TryDequeue`와 사용된 job ID 재사용 방지 |

`RenderQueue`는 아직 프레임을 렌더하거나 FFmpeg를 실행하지 않는다. 취소·진행률·오류 상태와 실제 인코딩 생명주기도 현재 타입의 책임이 아니다. `RenderJob`은 D 드라이브 하위 출력 경로, 크기/FPS/구간, frame count, FFmpeg 실행 파일·인자 같은 불변 요청을 검증한다.

## 후속 계획 구성요소

아래 항목은 아키텍처 확장 지점이지 현재 source tree에 존재하는 production 타입이 아니다.

| 계획 경계 | 현재 상태와 후속 책임 |
| --- | --- |
| 독립 command dispatcher | 현재 immutable command 실행과 Undo/Redo는 `DocumentSession` 내부 경계가 담당한다. command 병합·복합 transaction이 필요할 때 별도 조정기로 분리한다. |
| combat evaluator | 공격·접촉·뒤잡 교육 판정 규칙과 provenance를 구현할 후속 Domain 서비스다. |
| asset catalog | 의미 Action key와 합법적인 로컬 placeholder/DSR 자산 참조를 연결할 후속 Infrastructure 경계다. |
| render execution coordinator | `RenderQueue`의 job을 읽어 결정적 frame 생성, 진행·취소·실패 복구와 FFmpeg 인코딩을 수행할 후속 서비스다. |
| recovery service | 자동 저장과 비정상 종료 복구 후보를 관리할 후속 Infrastructure 서비스다. |

## 상태 흐름

### 편집

```text
포인터 드래그
→ MoveActorPreview(임시 표시)
→ 드래그 종료
→ MoveActorCommand(이전값, 새값)
→ SceneDocument 검증 및 변경
→ DocumentChanged 이벤트
→ 탑뷰/3D/속성/타임라인 재투영
→ Undo 스택 기록
```

드래그 중 매 픽셀마다 영구 키프레임을 만들지 않는다. 임시 프리뷰를 표시하고 조작 종료 시 하나의 병합 가능한 명령으로 확정한다.

### 변환 키프레임 CRUD

```text
타임라인 marker click
→ DocumentSession.SelectTransformKeyframe(id)
→ pause + 해당 time seek + 비영구 keyframe selection 변경
→ Inspector/marker/두 view 재표시

paused 시각의 Add
→ 현재 SceneSnapshot에서 평가 pose 복사
→ AddTransformKeyframeCommand
→ SceneDocument.AddKeyframe 검증·revision 증가
→ 새 marker selection + Undo 기록

Time/pose Apply 또는 Delete
→ UpdateTransformKeyframeCommand / RemoveTransformKeyframeCommand (정확한 preimage 보유)
→ SceneDocument의 원자적 검증·변경
→ DocumentChanged + HistoryChanged
→ 동일 (revision,time) snapshot을 TopView/WorldView에 전달
```

Add command는 생성할 keyframe 자체를 postimage로 보관한다. Execute는 그 keyframe을 추가하고 Undo는 같은 ID·time·pose의 keyframe만 제거한다. Update command는 before와 after 전체 값을 보관해 Execute에서 before→after, Undo에서 after→before를 stale 검증한다. Delete command는 제거할 전체 keyframe preimage를 보관해 Execute에서 stale 검증하고 Undo에서 그 원본 marker를 다시 추가한다. 따라서 Update/Delete를 시작한 뒤 문서가 바뀌면 stale preimage가 실패하여 부분 변경이나 history 이동이 생기지 않으며, Add/Remove의 Domain 검증 실패도 Application에서는 `Conflict`로 변환되어 UI가 최신 상태를 유지한다.

### Action/Lock-on 단계 트랙 CRUD

```text
Action/Lock-on marker click
→ DocumentSession.SelectActionKeyframe / SelectLockOnKeyframe
→ bounded pause + marker time seek 안정화
→ active track과 해당 비영구 selection 변경
→ lane/Inspector 가용성과 두 view snapshot 동기화

Action/Lock-on lane background click
→ DocumentSession.ActivateSemanticTrack
→ 문서/history/playback 불변으로 target Inspector 표시

Add/Apply/Delete
→ track별 immutable full-frame command
→ SceneDocument의 time·ID·target·preimage 검증
→ revision 증가 + 단일 shared history 기록
→ action/lock selection reconciliation
→ 같은 SceneSnapshot의 stepped state를 TopView/WorldView에 전달
```

Action은 ID/time/원문 `ActionKey`, Lock-on은 ID/time/enabled/target/yaw offset/tracking mode 전체가 command preimage/postimage다. Add는 `{actorId}-action-{D4}` 또는 `{actorId}-lock-on-{D4}`에서 사용하지 않은 가장 작은 ordinal ID를 만든다. Update는 ID를 유지하고 모든 의미 필드를 원자적으로 교체한다. Delete는 Action/Lock-on track을 비우는 것도 허용한다. stale preimage, 같은 track의 동일 시각, 문서 범위 밖 time, 활성인데 target이 없는 Lock-on과 self/unknown target, 미정의 tracking mode는 mutation 전에 거부된다. Application은 호환용 `SceneEditResult` API와 함께 `SemanticEditOutcome`의 typed issue를 제공해 `NoChange`, duplicate time, stale preimage, range, target/yaw/mode validation과 일반 conflict를 문자열 파싱 없이 구분한다.

세 track은 하나의 `DocumentSession` history를 공유한다. `InspectorPanel/HistoryToolbar`는 세 Inspector section 밖에 있어 active track과 무관하게 항상 보인다. `TransformInspectorController`가 signal 수명주기를 소유하지만 handler와 Disabled 계산은 Transform 편집 가능성이 아니라 `CanEditHistory`와 `CanUndo`/`CanRedo`만 사용한다. 따라서 Action/Lock-on marker를 활성 상태로 유지한 채 global 버튼으로 마지막 semantic command를 직접 왕복한다.

`SceneDocument` revision은 성공한 영구 Add/Update/Delete와 Undo/Redo에만 증가한다. marker 선택, playback seek/pause, disabled 버튼 클릭과 preview clear는 문서를 바꾸지 않는다. `HistoryChanged`는 성공한 stack 이동 뒤에만 일어난다. global Undo/Redo는 선택 actor가 없는 경우와 재생 중에 잠기며, 정지 상태에서는 active track이나 exact Transform marker와 무관하게 stack을 왕복한다.

Action/Lock-on marker 선택은 target `ActiveTimelineTrack`을 pause/seek보다 먼저 확정한다. target actor/frame의 최신 full value, paused 상태, target time, active track, selected ID/full payload가 모두 일치할 때까지 최대 32회 안정화하고 그 뒤에만 `Applied`를 반환한다. 첫 target seek를 observer가 2초로, 두 번째 target seek를 3초로 유한 재지정해도 세 번째 target 시도에서 실제 target time과 payload가 맞아야 성공한다. 따라서 두 번째 redirect 뒤 다른 time인데도 `Applied`를 반환하지 않는다. 각 attempt는 시작 시 해당 track의 publication sequence와 active context를 캡처하고, 이후 실제로 게시된 마지막 selection 서명의 actor/active track/ID/immutable full frame이 최신 target과 모두 같은지 확인한다. `Seek`가 state를 바꿨다는 사실 자체는 payload 게시 증거로 사용하지 않는다. 앞 redirect의 강제 갱신 상태를 final attempt로 넘기지 않아 최종 안정 target full payload는 정확히 한 번 게시된다. 두 track marker가 같은 시각이라 seek event가 생기지 않거나 rollback이 다른 시각으로 옮겨진 ID/full frame을 보존해 seek event에서 selection change가 생기지 않아도 cross-track 전환은 target selection event를 정확히 한 번 강제 게시한다. final target event observer가 active track을 다시 바꾸면 target context를 복원한 payload를 한 번 더 게시한 뒤 안정성을 확인한다. 안정화 중 target이 사라지거나 actor가 달라지면 아래 rollback 계약을 사용하며, 선택 API 재진입은 `Conflict`로 막아 stack 재귀를 만들지 않는다.

semantic marker 선택을 시작할 때 actor, 호출 시점 playback time/playing, active track과 세 track의 selected ID를 rollback snapshot으로 잡는다. pause/seek observer가 target frame을 제거해 `Conflict`가 되면 호출 시각과 playing 상태를 frame 이동과 무관하게 그대로 복구하고, 이전 actor가 여전히 선택된 경우 세 ID를 각각 최신 document에서 다시 읽는다. 캡처 ID가 남아 있으면 frame이 다른 시각으로 이동했어도 최신 immutable full frame을 선택·게시하며 availability는 복구된 time/playing과 최신 document에서 다시 계산한다. observer가 actor selection을 해제하거나 바꿨다면 snapshot actor를 되살리지 않고 null/현재 actor를 유지하며, 현재 actor가 다르면 호출 시각의 exact selection으로 재조정한다.

rollback 중 playback에서 생긴 임시 selection event는 외부에 내보내지 않고 최종 세 selection·availability payload를 함께 게시한다. 게시 observer가 actor를 null로 바꿨다가 원래 actor를 다시 선택해 Transform 문맥 알림을 만들면 rollback 전용 FIFO가 이를 재귀 없이 순서대로 처리하고, 그 뒤 원래 active Action/Lock-on의 actor/ID/최신 full-frame payload를 다시 게시한다. rollback notification 중 Transform/Action/Lock-on marker 선택은 `Conflict`로 차단한다. rollback payload observer가 actor 변경을 FIFO에 넣은 뒤 같은 payload의 다음 observer가 예외를 내더라도 이미 수락된 actor 변경과 그 selection payload를 먼저 끝낸 뒤 원래 observer 예외를 다시 낸다.

`PlaybackClock.Changed`의 재진입 state도 FIFO로 처리한다. 각 callback 동안 공개 `CurrentTimeSeconds`/`IsPlaying`은 해당 event payload와 일치하고, callback이 연속으로 `Pause()`와 `Seek()`를 요청할 때 다음 요청은 아직 게시 전인 FIFO tail 상태를 기준으로 계산한다. 앞 observer가 수락시킨 state가 있으면 뒤 observer가 예외를 내도 그 state의 적용·알림을 모두 끝낸 뒤 첫 observer 예외를 다시 낸다. playback과 rollback 알림은 32회 안에 안정화되지 않으면 observer 예외보다 bounded 비안정화 예외를 우선하므로 stack 재귀나 수락된 정상 작업의 묵시적 소실이 없다.

### 편집 가능성 경계

`DocumentSession`이 actor/track selection, current time과 playback state를 함께 검사한다. actor가 없거나 playback 중이면 세 track CRUD가 모두 잠긴다. 정지 상태의 Add는 해당 track의 현재 시각에 marker가 없을 때만 가능하고, Update/Delete는 해당 track marker selection이 있어야 한다. Update는 selection time과 재생 헤드가 같아야 한다. Transform Delete만 actor마다 최소 한 marker를 남겨야 하며 Action/Lock-on은 마지막 marker도 지울 수 있다. 이 정책은 Presentation의 `Disabled` 표시에만 의존하지 않는다. 남아 있던 Godot signal도 세션 API에서 다시 거부하므로 revision, history와 두 projection apply count가 불변이다.

빈 Action/Lock-on lane의 background pointer는 marker 선택과 별도로 semantic track만 활성화한다. 이 경로는 재생 시각·playing 상태, 세 selection, revision과 shared history를 바꾸지 않으며 target track의 현재 full selection payload를 강제 게시해 빈 track Inspector도 즉시 보이게 한다. 두 semantic controller는 operation 전후 `CurrentRevision`을 비교한다. 호출이 예외를 던져도 revision이 증가했다면 history/reconciliation까지 완료된 mutation으로 보고 `변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: ...`를 표시한다. revision이 그대로인 예기치 않은 예외는 catch하지 않고 원래 stack과 함께 전파한다. 예상 가능한 validation·conflict·no-op만 typed outcome을 통해 UI 문구가 된다.

### 재생

```text
PlaybackClock 시간 갱신
→ SceneProjectionController가 최신 (revision, time) 요청 수집
→ SceneDocument.CreateTrajectorySamplePlan(policy)
→ MotionRevision·fingerprint 단일 cache 조회 또는 MovementTrajectorySet 재평가
→ SceneDocument.CreateSnapshot(time)
→ snapshot·trajectory·fingerprint를 동일 SceneProjectionFrame으로 검증
→ 동일 frame 인스턴스를 TopViewSurface와 WorldViewProjectionAdapter에 순서대로 전달
```

재생은 문서를 변경하지 않는다. Action/Lock-on은 해당 시각 이하의 마지막 marker 값을 왼쪽 유지하고 첫 marker 전에는 비어 있음/OFF를 평가한다. 현재 시간의 snapshot과 presentation만 갱신하므로 Undo 기록이나 변경 표시를 오염시키지 않는다. TopView는 `Apply`/`ApplyPreview`에서 immutable 표시 모델을 갱신하고, WorldView는 actor node·mesh·material을 재사용한다. 두 뷰는 서로를 읽지 않으며 반드시 같은 `SceneProjectionFrame`을 소비한다.

### Lock-on 방향과 이동 궤적 투영

`LockOnFacingEvaluator`는 authored transform을 수정하지 않고 `EvaluatedActorFacing`을 계산한다. 결과에는 정규화된 `YawDegrees`뿐 아니라 `FacingResolutionKind`와 `SourceLockOnKeyframeId`가 있어 화면과 테스트가 결과의 출처를 구분할 수 있다.

- `keyframe_only`: 현재 authored Yaw를 그대로 사용한다.
- `snap`: Lock-on source keyframe 시각의 actor/target 위치로 방향과 offset을 한 번 계산해 다음 Lock-on marker 전까지 고정한다.
- `continuous`: 현재 시각의 actor/target 위치로 계속 갱신한다.
- target이 없으면 `TargetUnavailableFallback`, XZ 거리가 `1e-6` 이하로 겹치면 직전 유효 방향 또는 authored Yaw로 결정적으로 후퇴한다.

`SceneSnapshot.ActorTransforms`는 편집 원본 방향, `ActorFacings`는 resolved 표시 방향을 나란히 가진다. Transform 또는 Lock-on/actor 구조 변경은 `Revision`과 `MotionRevision`을 함께 올리고, Action-only 변경은 `Revision`만 올린다. 따라서 `SceneProjectionController`는 `(MotionRevision, TrajectorySamplePlan.Fingerprint)`가 같을 때 `MovementTrajectorySet.WithRevision`으로 최신 문서 revision만 맞추고 동일 actor trajectory geometry를 재사용한다. motion 입력이나 sampling policy가 바뀌면 cache 한 항목을 교체해 전체 궤적을 다시 만든다.

controller는 projection 중 들어온 문서/playback 이벤트를 재귀 호출하지 않고 pending flag 하나로 합친다. 현재 time과 source metadata를 매 반복에서 다시 읽어 최신 요청을 처리하며, plan 생성·평가 전후 metadata가 달라지면 최대 3회 새 stable frame을 얻는다. 생성된 `SceneProjectionFrame`은 document/revision/motion revision/fingerprint 일치를 생성자와 metadata 검사에서 보장한다. TopView와 WorldView에는 이 동일 frame 객체를 전달하므로 snapshot과 trajectory가 서로 다른 revision에서 섞이지 않는다.

### 저장

```text
SceneDocument 스냅샷
→ Infrastructure에서 현재 `/2` 스키마 검증·직렬화
→ 임시 파일 직렬화
→ 다시 읽어 기본 검증
→ 기존 파일 백업 선택
→ 원자적 교체
→ 저장 기준 리비전 갱신
```

현재 Domain schema는 `pvp-guide-scene/2`다. Infrastructure serializer만 legacy `/1`을 읽고, Lock-on에 없던 `yawOffsetDegrees=0`, `trackingMode=continuous` 기본값을 적용해 메모리 `/2` 모델로 migration한다. `/2` 저장은 두 필드를 필수로 쓰고 strict round-trip을 검증한다. Godot Editor 프로젝트는 Infrastructure를 참조하지 않으므로 migration/round-trip 완료 여부는 Infrastructure 전체 테스트 결과로만 판정하며 Editor runtime marker에 schema flag를 섞지 않는다.

resolved facing, `MotionRevision`, sample plan/fingerprint, `MovementTrajectorySet`, geometry cache와 현재 playback time은 모두 다시 계산 가능한 파생·세션 상태다. 평가 전후 serialize 문자열이 byte 단위로 같아야 하며 `/2` JSON에는 이 필드를 추가하지 않는다.

## 네트워크 아키텍처

현재 제품은 Windows 11용 오프라인 독립 실행 프로그램이다. 문서 편집, Lock-on 방향·궤적 평가, TopView/3D 표시와 검증은 모두 같은 프로세스와 로컬 파일 경계에서 끝난다. 런타임에 서버, 계정, 원격 DB, 원격 분석, 자동 업로드나 원격 자산 CDN을 호출하지 않는다. `SceneProjectionController`의 source/consumer 관계는 프로세스 내부 C# 인터페이스이며 네트워크 프로토콜이 아니다.

향후 업데이트 확인이나 협업 기능이 필요해져도 현재 저장 schema와 Domain evaluator에 네트워크 상태를 섞지 않는다. 별도 사용자 승인과 명시적 opt-in 경계, 실패 시 완전한 오프라인 동작을 먼저 설계해야 한다. 현재 마일스톤에는 네트워크 동기화와 멀티플레이 재현이 포함되지 않는다.

## 스레딩

- Godot 노드 생성·변경과 렌더 관련 호출은 메인 스레드에서 수행한다.
- JSON 파싱, 자산 인덱싱, 체크섬, FFmpeg 대기와 무거운 경로 샘플링은 작업 스레드에서 수행할 수 있다.
- 백그라운드 작업은 Godot 객체를 직접 소유하지 않고 순수 데이터 결과를 메인 스레드 큐로 전달한다.
- 문서 편집 명령은 한 직렬 실행 경로를 사용한다. 여러 스레드가 문서를 동시에 변경하지 않는다.
- 취소는 `CancellationToken`과 작업 상태를 사용하며 프로세스 강제 종료는 자식 FFmpeg에 한해 명시적으로 처리한다.

## 의존성 방향

`Presentation → Application → Domain` 방향만 허용한다. Infrastructure는 Application 또는 Domain이 정의한 포트를 구현한다. Editor는 Infrastructure를 직접 참조하지 않는다. Domain이 Godot, 파일 경로, JSON 라이브러리 또는 외부 실행 파일을 참조하면 안 된다.

## 확장 지점

- `ISceneImporter`: 새 좌표·가이드 형식 추가
- `IAssetSource`: 플레이스홀더, DSR 로컬 자산, 사용자 GLB 자산 연결
- `ICombatRuleSet`: 교육용 기본 규칙과 검증된 실제 규칙 교체
- `IRenderEncoder`: FFmpeg 프리셋 또는 이미지 전용 출력
- `IOverlayRenderer`: 거리, 각도, 공격 범위 등 새 교육 오버레이

현재 완료 범위는 세 Lock-on mode의 resolved facing, provenance/fallback, 결정적 paired trajectory, `SceneProjectionFrame`, TopView/WorldView 표시와 cache/node/resource 재사용까지다. 후속 범위는 실제 DSR animation clip 연결, 충돌·뒤잡 판정 고도화, full timeline 확대·스크롤, 렌더 실행과 배포 패키징이다. 저장 schema v3, 파생 facing/trajectory 영구 저장, 온라인 동기화, 원격 분석, 저작권 자산 번들은 현재 범위에서 제외한다.

플러그인 시스템이나 스크립트 실행 환경은 초기 범위에 넣지 않는다. 실제로 여러 구현을 배포해야 할 때 위 인터페이스를 내부 모듈로 먼저 검증한 뒤 확장한다.
