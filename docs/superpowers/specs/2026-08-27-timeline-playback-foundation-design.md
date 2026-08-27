# 타임라인 재생 기반 설계

## 1. 목적과 결정

이번 마일스톤은 시간 데이터를 이미 보간할 수 있는 `SceneDocument`를 실제 편집 화면의 시간축과 연결한다. 사용자는 타임라인 슬라이더로 임의 시점을 확인하고 재생·일시정지·처음으로 이동을 실행할 수 있으며, 탑뷰와 3D 화면은 항상 같은 시점의 동일한 `SceneSnapshot`을 표시한다.

사용자는 이후 작업의 판단과 승인을 메인 에이전트에 위임하고 중단 없이 진행하도록 요청했다. 이에 따라 단계 3 전체를 한 번에 구현하지 않고, 의존성이 가장 적고 후속 기능의 기반이 되는 **3A: 읽기 전용 시간 탐색**을 다음 구현 단위로 채택한다.

이번 범위는 다음을 포함한다.

- Godot에 의존하지 않는 재생 시간 상태와 제어
- 스크럽, 재생, 일시정지, 처음으로 이동, 끝점 자동 정지
- 같은 문서 revision에서 시간만 바뀌어도 수행되는 2D/3D 재투영
- 현재 시간과 재생 상태를 표시하는 타임라인 UI
- 버튼과 Space 키의 동일한 재생 토글
- 시간 이동 전 활성 변환 preview 취소
- 재생 중 또는 선택 배우의 최초 키프레임 시각이 아닌 경우 기존 최초 키프레임 편집 잠금
- 단위 테스트, 구조 검사, Godot 런타임 통합 표식, 한글 문서 갱신

다음은 후속 마일스톤으로 남긴다.

- 키프레임 선택·추가·이동·삭제·복제
- 현재 시점 자동 키프레임 삽입
- Action/Lock-on 단계 평가와 트랙 UI
- 루프, 재생 구간, 재생 속도 변경, 오디오 동기화
- 실제 DSR 모델·애니메이션 연결
- 결정적 영상 렌더와 FFmpeg 실행
- 게임패드 입력

## 2. 접근 비교

### 접근 A — 단계 3 전체 타임라인 편집을 한 번에 구현

재생과 키프레임 CRUD, Action/Lock-on, 궤적을 함께 구현하면 완성된 화면에 빨리 가까워 보인다. 그러나 현재 Domain에는 변환 키프레임 시간 이동·삭제 API가 없고, Application은 항상 최초 변환 키프레임만 편집한다. 선택·충돌·Undo 정책까지 동시에 정해야 하므로 결함 원인을 분리하기 어렵다. 이번에는 사용하지 않는다.

### 접근 B — 읽기 전용 재생 기반을 먼저 구현

기존 `ActorTrack.Evaluate(timeSeconds)`와 `SceneDocument.CreateSnapshot(timeSeconds)`를 재사용하고, 새로운 비영구 시간 상태만 Application에 추가한다. 저장 데이터 구조를 바꾸지 않으면서 시간축, 투영, UI, 편집 잠금의 경계를 먼저 고정할 수 있다. 이후 모든 타임라인·락온·렌더 기능이 같은 시간 계약을 사용하므로 채택한다.

### 접근 C — 실제 애니메이션 또는 렌더부터 구현

외부 HKX 변환·검증이나 프레임 렌더러를 먼저 만들면 시간 평가 계약이 여러 계층에 중복된다. Action 평가와 자산 카탈로그도 아직 구현되지 않아 독립된 사용자 흐름을 완성하기 어렵다. 시간축 기반 이후로 미룬다.

## 3. 계층과 상태 소유권

의존 방향은 유지한다.

```text
PvpGuide.Editor (Godot UI / _Process delta 전달)
        ↓
PvpGuide.Application (PlaybackClock / DocumentSession / Projection)
        ↓
PvpGuide.Domain (SceneDocument / ActorTrack interpolation)
```

시간 탐색 상태는 저장 가능한 문서가 아니라 열린 편집기의 세션 상태다. 따라서 `SceneDocument.Revision`, Undo/Redo 스택, JSON 포맷에는 포함하지 않는다.

새 `PlaybackClock`은 다음 값과 동작을 소유한다.

```csharp
public sealed class PlaybackClock
{
    public double DurationSeconds { get; }
    public int FramesPerSecond { get; }
    public double CurrentTimeSeconds { get; }
    public bool IsPlaying { get; }

    public event EventHandler<PlaybackChangedEventArgs>? Changed;

    public bool Seek(double timeSeconds);
    public bool Play();
    public bool Pause();
    public bool Toggle();
    public bool Stop();
    public bool Advance(double deltaSeconds);
}
```

`PlaybackClock`은 Godot `Node`, wall clock, 파일 시스템, 비동기 timer를 참조하지 않는다. Godot의 `_Process(delta)`는 단지 `Advance(delta)`를 호출한다. 단위·런타임 테스트는 실제 시간 대기 없이 `Advance()`를 직접 호출한다.

## 4. 재생 시간 계약

- 생성 시 문서 길이는 유한하고 0보다 커야 하며 FPS는 양수여야 한다.
- 최초 시간은 0초이고 일시정지 상태다.
- `Seek()`는 유한한 입력만 허용하고 `[0, DurationSeconds]`로 clamp한다.
- 같은 유효 시간으로의 seek는 이벤트를 만들지 않는다.
- 사용자의 scrub은 먼저 일시정지한 뒤 seek한다.
- `Play()`는 끝점에 있으면 0초로 되감은 뒤 재생을 시작한다.
- `Advance()`는 재생 중일 때만 시간을 증가시킨다.
- 끝점을 넘으면 정확히 끝점에 clamp하고 자동으로 일시정지한다.
- `Pause()`는 현재 시간을 보존한다.
- `Stop()`은 일시정지하고 0초로 돌아간다.
- 음수 또는 비유한 delta는 명시적 예외로 거부한다.
- 한 공개 호출의 최종 상태는 불변 event args 하나로 한 번 통지한다. 소비자는 중간 상태를 관찰하지 않는다.
- 표시 시간은 소수 초를 유지하고 UI slider step은 `1 / FramesPerSecond`를 사용한다. 결정적 영상 렌더의 `start + n/fps` 계산은 별도 후속 책임이다.

## 5. DocumentSession 편집 잠금

기존 편집 대상은 선택 배우의 `TransformKeyframes[0]`이다. 중간 보간 위치를 보고 드래그했는데 최초 키프레임이 바뀌는 오해를 막기 위해 다음 정책을 고정한다.

- 선택 배우가 있고, 재생이 멈춰 있으며, 현재 시간이 해당 배우의 최초 변환 키프레임 시간과 허용 오차 안에서 같을 때만 변환 편집이 가능하다.
- 선택이 없으면 편집할 수 없다.
- 재생 중에는 현재 시간이 키프레임과 같아도 편집할 수 없다.
- 시간이나 재생 상태가 바뀌기 전에 활성 preview를 취소한다.
- 잠긴 상태의 `MoveSelectedActor`, `RotateSelectedActor`, `SetSelectedActorTransform`, `BeginPreview`, Undo/Redo UI 입력은 문서와 history를 바꾸지 않는다.
- 선택과 snapshot 표시는 잠금 중에도 유지한다.
- 잠금 사유는 `DocumentSession`의 명시적 상태와 이벤트로 Presentation에 전달하며, Godot 컨트롤이 시간을 독자적으로 비교하지 않는다.
- 현재 첫 키프레임 시간이 0이 아닐 수 있으므로 `Stop()`의 0초는 항상 편집 가능하다는 뜻이 아니다. slider로 첫 키프레임 시간에 정확히 이동하면 편집할 수 있다.

`DocumentSession`은 문서의 길이와 FPS로 `PlaybackClock`을 조립하거나 소유하고, `Playback`과 `CanEditSelectedTransform`을 공개한다. 시간/재생 변경 전 preview 취소와 편집 가능 상태 통지는 세션 경계에서 원자적으로 조정한다.

## 6. 시간 기반 투영

현재 `SceneProjectionController`는 생성자 시각을 고정하고 마지막 revision만 기억한다. 이를 다음 계약으로 변경한다.

```text
Document Changed ─┐
                  ├─> SceneProjectionController
Playback Changed ─┘        │
                            ├─ snapshot 1개 생성
                            ├─ TopView에 같은 인스턴스 전달
                            └─ WorldView에 같은 인스턴스 전달
```

- 투영 키는 revision 단독이 아니라 `(revision, timeSeconds)`다.
- 시간 비교는 clock이 보유한 clamp 결과의 정확한 `double` 값을 사용한다.
- 문서 변경 시 현재 재생 시간으로 새 snapshot을 만든다.
- 시간 변경 시 같은 revision이어도 새 snapshot을 만든다.
- 같은 `(revision, time)` 요청은 중복 전달하지 않는다.
- 한 번 만든 동일 `SceneSnapshot` 인스턴스를 탑뷰와 3D 소비자에 전달한다.
- controller 생성 후 `ProjectCurrent()`로 최초 상태를 명시적으로 투영한다.
- Dispose 후 document와 playback event를 모두 해제한다.
- preview는 시간 변경 전에 취소되므로 새 committed snapshot 위에 이전 preview가 남지 않는다.

`WorldViewProjectionAdapter`와 Domain 보간 코드는 수정하지 않는 것이 목표다. 이들은 전달받은 snapshot만 표현한다.

## 7. Godot 타임라인 UI

`TimelinePanel`은 다음 최소 구조를 가진다.

```text
TimelinePanel
└─ TimelineControls : VBoxContainer
   ├─ PlaybackButtons : HBoxContainer
   │  ├─ PlayPauseButton
   │  └─ StopButton
   ├─ TimeSlider : HSlider
   ├─ CurrentTimeLabel
   └─ TimelineStatus
```

`TimelineController`는 Godot control과 Application 세션 사이의 adapter다.

- slider 범위는 0부터 문서 길이, step은 `1/FPS`다.
- slider 사용자 변경은 재생을 일시정지하고 seek한다.
- Play/Pause 버튼과 Space 키는 같은 `Toggle()` 경로를 호출한다.
- Stop은 0초로 이동하고 정지한다.
- label은 `현재 / 전체 초`와 frame 번호를 한글로 표시한다.
- 재생 중 버튼 문구는 `일시정지`, 정지 중에는 `재생`이다.
- 편집 잠금 상태에는 TimelineStatus와 Inspector 오류/상태 영역에 이유를 표시한다.
- 프로그램이 clock 상태를 UI에 반영할 때 slider signal 재진입을 guard한다.
- `TimelineController.Dispose()`는 모든 Godot signal과 Application event를 해제한다.

`Main._Process(delta)`는 준비된 clock에 delta를 전달할 뿐 UI 상태나 문서를 직접 계산하지 않는다. `_UnhandledKeyInput` 또는 동등한 Godot 입력 경계는 echo와 key release를 무시하고 Space press만 controller에 전달한다. Joypad 입력은 추가하지 않는다.

## 8. 탑뷰와 Inspector 잠금 표현

- `TopViewSurface`는 선택과 그리기를 계속 허용하지만 이동·회전 preview 시작/갱신/확정을 차단한다.
- 잠금으로 전환될 때 진행 중인 로컬 drag 상태를 정리한다.
- `TransformInspectorController`는 X/Y/Z/Yaw 입력, Apply, Undo, Redo를 비활성화한다.
- 잠금 해제 시 선택 배우의 committed 최초 키프레임 값을 다시 표시하고 history 버튼 상태를 복원한다.
- 잠금 중 프로그램이 snapshot을 표시하는 과정이 Inspector preview를 만들면 안 된다.
- 사용자에게는 `재생 중에는 편집할 수 없습니다` 또는 `최초 키프레임 시각에서만 편집할 수 있습니다`를 구분해 표시한다.

## 9. 오류 처리와 수명주기

- NaN/Infinity 시간, 음수/비유한 delta, 잘못된 문서 길이/FPS는 Application 경계에서 즉시 거부한다.
- 사용자 slider는 정상 범위로 구성하므로 Application 예외를 정상 흐름으로 사용하지 않는다.
- 시간 변경 observer가 예외를 던져도 문서 변경을 롤백한 것처럼 처리하지 않는다. runtime 검증에서 오류가 표면화되게 한다.
- `_Ready()`가 중간에 실패하면 생성된 controller만 역순으로 해제한다.
- `_ExitTree()`는 timeline, projection, preview, inspector 순서의 의존성을 고려해 모든 구독을 해제한다.
- 현재 실행 중인 별도 Godot 인스턴스의 상태를 테스트가 전제로 삼지 않는다.

## 10. 테스트와 검증

### Application 테스트

- seek가 범위를 clamp하고 같은 유효 시간 이벤트를 억제한다.
- paused advance는 no-op이고, 재생 중 advance는 증가하며 끝점에서 자동 정지한다.
- 끝점 Play는 0초로 되감아 재생한다.
- 시간/재생 변경이 활성 preview를 먼저 취소하고 revision·Undo/Redo를 바꾸지 않는다.
- 선택 배우의 최초 키프레임 시각에서만 paused 편집이 가능하다.
- 잠긴 편집 API가 문서와 history를 보존한다.
- 같은 revision에서 시간 변경이 두 consumer에 새 snapshot을 전달한다.
- 두 consumer가 매 투영마다 같은 snapshot 인스턴스를 받는다.
- 동일 `(revision,time)`는 억제하고 document 또는 time 한쪽만 바뀌어도 투영한다.
- Dispose 뒤 document/time 이벤트 전달이 없다.
- 350°→10°의 최단 Yaw 보간 중간값과 위치 중간값을 실제 snapshot에서 확인한다.

### Editor 테스트

- 시간 label과 frame 번호 포맷을 순수 formatter에서 검증한다.
- slider/programmatic update 재진입 guard와 버튼/Space의 같은 상태 전환을 검사한다.
- 잠금 상태가 TopView preview 진입과 Inspector 입력을 막고 선택/표시는 유지한다.
- 잠금 해제 후 최초 키프레임 편집이 다시 가능하다.

### Godot 런타임 통합 검사

런타임 문서는 t=0과 t=1 변환 키프레임을 가진다. 기존 `BASIC_EDITING_*` 표식을 먼저 그대로 만족한 뒤 별도 결정적 호출로 다음을 검증한다.

- slider 0.5초 이동 시 탑뷰와 3D가 정확한 위치/Yaw 중간값을 표시한다.
- revision, Undo/Redo count와 기존 committed keyframe이 변하지 않는다.
- 활성 preview가 시간 이동 전에 취소된다.
- 중간 시점의 탑뷰/Inspector 편집이 잠긴다.
- Play 버튼과 Space가 같은 상태를 바꾼다.
- `Advance()`가 끝점을 clamp하고 자동 정지한다.
- Stop 뒤 0초 화면과 편집 가능 상태가 복구된다.

정확한 새 표식은 다음과 같다.

```text
TIMELINE_PLAYBACK_READY scrub_midpoint=1 top_world_sync=1 revision_unchanged=1 history_unchanged=1 preview_cancel=1 edit_guard=1 play_button=1 space_toggle=1 end_clamp=1 stop_restore=1
```

테스트 프로젝트는 Windows 공유 `obj` 잠금을 피하도록 직렬 실행한다. 구조 검사와 headless runtime 뒤 Forward+ GUI 실행 로그와 화면도 확인한다.

## 11. 문서 변경

- `README.md`: 재생/스크럽 사용법, Space/버튼, 읽기 전용 시점 정책, 검증 명령
- `docs/05-editor-architecture.md`: PlaybackClock, 세션 시간 상태, `(revision,time)` 투영 흐름, 편집 잠금
- `docs/13-roadmap.md`: 단계 3A 완료와 다음 단계인 임의 시점 키프레임 편집 분리
- 구현 계획: `docs/superpowers/plans/2026-08-27-timeline-playback-foundation.md`

## 12. 완료 기준

- 사용자가 slider, 버튼, Space로 시간 탐색과 재생을 제어할 수 있다.
- 탑뷰와 3D가 같은 revision의 다른 시간도 즉시 같은 snapshot으로 표시한다.
- 재생과 scrub은 문서 revision, 키프레임, Undo/Redo를 변경하지 않는다.
- 활성 preview는 시간 이동 전에 취소되고 중간 시점의 오해 가능한 최초 키프레임 편집은 잠긴다.
- 끝점 clamp·자동 정지·Stop 복귀가 결정적이다.
- Domain과 Infrastructure 저장 포맷은 변경하지 않는다.
- 기존 133개 테스트와 모든 새 테스트, 구조 검사, Godot runtime, Forward+ GUI 검증이 통과한다.
- 변경은 명시된 경로만 스테이징해 작업 단위별로 커밋하고 새 원격 브랜치에 푸시한다.
- 사용자의 실제 정상 동작 재확인 전에는 새 `working/...` 태그를 만들지 않는다.
