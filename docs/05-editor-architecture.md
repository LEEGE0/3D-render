# 05. 편집기 아키텍처

## 화면 구성

초기 레이아웃은 한 창 안에서 탑뷰와 3D 뷰를 동시에 보여주는 분할 편집기다.

```text
┌──────────────────────────────────────────────────────────────────────┐
│ 메뉴 / 문서 탭 / 저장 상태 / 재생·렌더 상태                          │
├───────────────────────────────┬──────────────────────────────────────┤
│ 탑뷰                          │ 3D 뷰                               │
│ 배우·경로·방향·판정            │ 모델·지면·경로·효과·카메라             │
├───────────────────────────────┴───────────────────────┬──────────────┤
│ 타임라인                                              │ 속성 패널     │
│ 변환 / 행동 / 락온 / 카메라 / 오버레이 트랙           │ 선택값 편집   │
├───────────────────────────────────────────────────────┴──────────────┤
│ 상태 표시줄: 시간, FPS, 좌표, 작업 진행, 경고                        │
└──────────────────────────────────────────────────────────────────────┘
```

패널은 접거나 크기를 조절할 수 있지만 탑뷰와 3D의 동시 표시가 기본이다. 작은 해상도에서는 탭 전환 모드를 보조로 제공할 수 있다.

## 현재 기본 편집 구현 계약

최초 기본 편집은 다음 계층으로 구현되어 있다.

```text
TopViewSurface / TransformInspectorController    TimelineController / Main Space
                │ 선택·미리보기·확정 의도                  │ seek·toggle·stop
                ▼                                           ▼
          DocumentSession ───────────────────────────── PlaybackClock
                │ 검증된 변환 명령                           │ time + Changed
                ▼                                           │
          SceneDocument ── revision + Changed ──────────────┤
                                                            ▼
                                                SceneProjectionController
                                                    ┌─────┴─────┐
                                                    ▼           ▼
                                           TopViewSurface   WorldViewProjectionAdapter
```

- `DocumentSession`은 열린 문서, 단일 배우 선택, 변환 미리보기, Undo/Redo 스택과 문서 길이/FPS로 만든 `PlaybackClock`을 소유한다.
- `ActorDisplayInfo`는 actor ID, 표시 이름과 역할만 담은 Application 불변 계약이다. 탑뷰는 이 조회 API를 사용하며 `ActorTrack`이나 원본 문서를 노출받지 않는다.
- `TopViewSurface`와 `TransformInspectorController`는 `SceneDocument`를 직접 보유하거나 수정하지 않는다. 영구 변경은 세션 공개 API를 통해서만 수행한다.
- `SceneProjectionController`는 문서 revision 또는 playback time이 바뀌면 `SceneSnapshot`을 한 번 만들고 동일 인스턴스를 탑뷰와 3D 소비자에 전달한다. 중복 방지 키는 revision 단독이 아니라 `(revision, time)`이다.
- `TransformPreviewController`는 동일한 nullable `TransformPreview` 인스턴스를 두 뷰에 전달한다. 이 값은 저장·직렬화되지 않으며 문서 revision도 올리지 않는다.
- 마우스를 놓거나 Inspector에서 Apply/Enter를 제출할 때만 `ReplaceTransformCommand` 하나가 확정된다.
- `DocumentSession.CommitPreviewDetailed()`는 확정 결과를 공개 `SceneEditResult`로 반환하며, 기존 `CommitPreview()` bool API는 호환성을 위해 유지되고 `Applied`일 때만 `true`를 반환한다.
- `DocumentSession.CurrentRevision`은 `SceneDocument`를 UI에 노출하지 않고도 현재 monotonic revision을 읽게 한다. Inspector는 확정 전후 revision으로 문서 변경 후 알림 subscriber가 실패한 경우를 구분한다.
- 현재 편집 대상은 선택 배우의 시간상 최초 변환 키프레임이다. 읽기 전용 타임라인 3A는 임의의 t=0 키프레임을 자동 생성하거나 재생 헤드 시각에 새 키프레임을 만들지 않는다.
- Undo/Redo는 과거 revision 번호를 복원하지 않고 과거 의미 값을 새 변경으로 적용하므로 revision은 항상 증가한다.
- `HistoryChanged`는 성공한 Execute/Undo/Redo의 스택 이동이 끝난 뒤에만 발생한다. Inspector 버튼은 동기 `SceneDocument.Changed` 도중의 이전 stack 상태가 아니라 이 event에서 `CanUndo/CanRedo`를 읽는다.

현재 장면 노드 경계는 다음과 같다.

```text
TopViewPanel/TopViewSurface
WorldViewPanel/WorldViewportContainer/WorldViewport/WorldRoot
  ├─ Camera3D
  ├─ DirectionalLight3D
  ├─ Ground
  └─ Actors
InspectorPanel/TransformInspector
  ├─ SelectedActorLabel
  ├─ XInput / YInput / ZInput / YawInput
  ├─ ApplyButton / UndoButton / RedoButton
  └─ ErrorLabel
TimelinePanel/TimelineControls
  ├─ PlaybackButtons/PlayPauseButton / StopButton
  ├─ TimeSlider
  ├─ CurrentTimeLabel
  └─ TimelineStatus
```

## 뷰포트 구성

- 탑뷰는 2D `SubViewport` 또는 전용 `Control`/`Node2D` 계층으로 구현한다.
- 3D는 별도 `SubViewport`와 `Node3D` 장면을 사용한다.
- 두 뷰는 렌더 트리를 공유하지 않고 `SceneSnapshot`을 각자의 표현 노드에 적용한다.
- 미리보기 품질, 그림자, MSAA와 해상도 배율은 뷰별 설정으로 둔다.
- 3D 선택 피킹은 물리 충돌 또는 렌더 ID 방식 중 단순하고 안정적인 방법을 먼저 사용한다.

## 선택 모델

선택은 문서 내용이 아니라 세션 상태다. 현재 구현은 actor ID 하나 또는 null만 보관한다.

- 선택 배우 ID
- 선택 트랙 ID
- 선택 키프레임 ID 집합
- 현재 시간
- 활성 도구와 좌표 모드

탑뷰에서 배우를 선택하면 속성 패널이 같은 ID를 사용한다. 3D actor 노드는 actor ID로만 매핑되며 화면 노드 참조를 선택의 원본으로 쓰지 않는다. 다중 선택과 트랙·키프레임 집합 선택은 후속 타임라인 단계다.

## 편집 도구

### 선택 도구

현재는 클릭 단일 선택과 빈 공간 클릭 해제를 제공한다. Shift 다중 선택, 겹친 객체 순환 선택 또는 작은 목록은 후속 기능이다.

### 이동 도구

- 탑뷰: 배우 몸체를 3px 이상 끌어 수평면 X/Z를 이동하며 Y와 Yaw를 보존한다.
- 3D: 현재는 탑뷰·Inspector 결과를 표시하며 직접 지면 드래그와 축 기즈모는 후속 기능이다.
- 숫자 입력: Inspector에서 X/Z ±1000, Y ±100 범위와 0.1 step으로 입력한다.
- 그리드·정점 스냅은 후속 옵션이다.
- 현재 시간에 키프레임을 만들지 않고 배우의 최초 키프레임을 편집한다. 자동 생성 정책은 타임라인 구현 때 결정한다.

### 회전 도구

수평 Yaw를 기본으로 하고 선택 배우 중심에서 28px 떨어진 방향 핸들과 숫자 입력을 제공한다. 0°는 +X, 90°는 +Z이며 3D actor root에는 `rotationY = -DegToRad(yaw)`를 적용한다. 락온 기반 방향 편집은 후속 단계다.

### 키프레임 도구

다음 구현 단위는 현재 평가값을 임의 시점 변환 키프레임으로 생성하고, 선택 키프레임을 조회·수정·삭제하는 CRUD다. 현재 3A에는 keyframe marker/track 편집 UI가 없고 기존 키프레임을 읽어 평가하기만 한다. 이후 선택 키프레임 이동·복제·보간 변경과, 여러 트랙을 함께 이동할 때 상대 시간 간격을 보존하는 기능을 확장한다.

## 명령과 Undo/Redo

현재 모든 영구 변환 변경은 Application 내부에만 노출되는 다음 명령 인터페이스를 따른다. `ISceneEditCommand`는 public Editor 계약이 아니며 Editor는 `DocumentSession`의 공개 API만 사용한다.

```csharp
internal interface ISceneEditCommand
{
    bool Execute(SceneDocument document);
    bool Undo(SceneDocument document);
}
```

확정 결과는 명령 구현 유형을 노출하지 않는 공개 Application 계약이다.

```csharp
public enum SceneEditResult
{
    Applied,
    NoChange,
    Conflict,
}
```

`Applied`는 문서와 history가 함께 갱신된 경우, `NoChange`는 Yaw 정규화를 포함해 의미 값이 같아 revision과 history가 바뀌지 않은 경우, `Conflict`는 preview가 잡은 preimage가 최신 문서와 달라 적용하지 않은 경우다. `CommitPreviewDetailed()`이 이 값을 반환하고 bool `CommitPreview()`는 `Applied`에 대한 호환 wrapper로만 남는다.

`ReplaceTransformCommand`는 actor ID, keyframe ID, 실행 전 expected 값과 실행 후 값을 고정하며 다음 불변 조건을 지킨다.

- 실행 전 검증 실패는 문서를 부분 변경하지 않는다.
- Undo는 실행 전 의미 상태로 정확히 돌아간다.
- 드래그 중에는 문서를 바꾸지 않고 mouse release에서 하나의 명령만 만든다.
- 가져오기는 새 문서 생성 또는 하나의 복합 명령으로 처리한다.
- 재생 시간 변경과 패널 크기 같은 세션 상태는 문서 Undo에 넣지 않는다.

## 탑뷰 표현

- 현재 배우는 표시 이름과 `역할: ...` 텍스트, 방향선을 함께 그려 색만으로 의미를 구분하지 않게 한다. 일반 역할은 원, `Enemy`·`invader`·`target`·`적`을 포함한 적대 역할은 마름모 몸체를 사용한다.
- 고정 중심, 40px/world unit 배율을 사용하고 화면 오른쪽을 +X, 화면 아래쪽을 +Z로 매핑한다.
- 몸체 hit 반경은 16px, 방향 핸들 hit 반경은 10px이며 둘이 겹치면 핸들을 우선한다.
- 28px 원형 회전 핸들은 선택 actor에만 그려지고 hit-test 대상도 선택 actor 하나로 일치한다.
- 드래그 미리보기는 별도 색으로 표시하며 `Escape` 또는 선택 변경 시 즉시 committed snapshot으로 복원한다.
- 충돌원, 락온 선, 이동 궤적, 키프레임 마커와 뒤잡 부채꼴을 레이어로 분리한다.
- 줌과 팬은 좌표 데이터를 바꾸지 않는다.
- 화면 좌표와 문서 좌표 변환은 카메라 변환 하나에서 수행한다.
- 선택 허용 오차는 줌에 따라 화면 픽셀 기준으로 유지한다.

## 3D 표현

- 기본 캐릭터는 Capsule 몸체와 Box 방향 표식으로 만든 플레이스홀더다.
- `WorldViewProjectionAdapter`는 `Actors` 아래에 `Actor_<sanitized-id>` root를 actor ID별로 생성·재사용한다. snapshot에서 사라진 actor는 adapter가 소유한 root만 제거한다.
- actor root에 문서 위치와 Yaw를 적용하고 `VisualRoot`와 향후 판정·교육 표시용 `OverlayRoot`를 분리한다.
- 방향 Box는 로컬 +X를 바라본다. 실제 모델의 전방축 차이는 향후 `VisualRoot` offset으로만 보정한다.
- preview가 오면 대상 actor root의 표시 transform만 임시로 덮어쓰고 null clear 시 latest committed snapshot을 다시 적용한다. Godot 노드 transform을 문서 원본으로 사용하지 않는다.
- `WorldTransformMapper`는 Godot namespace와 `Vector*`를 참조하지 않고 double 기반 `WorldPosition`과 Y rotation radians만 반환한다. float `Godot.Vector3` 변환은 `WorldViewProjectionAdapter` 하나에서만 수행한다.
- 서로 다른 actor ID가 같은 sanitized base 이름을 만들면 첫 node는 exact base를 유지하고 다음 node는 원본 ID 기반 결정적 suffix를 사용한다.
- 의미 행동을 `AssetCatalog`를 통해 실제 또는 대체 애니메이션에 연결한다.
- 교육 오버레이는 모델 재질과 분리한 디버그/설명 렌더 계층을 사용한다.
- 지면, 조명과 배경은 영상 가독성을 우선해 단순하게 유지한다.

## 타임라인 재생 상태와 투영 소유권

완료된 단계 3A는 track 편집기가 아니라 읽기 전용 재생 헤드다. `SceneDocument`는 duration/FPS와 committed keyframe을 소유하고, `DocumentSession`은 해당 문서에서 만든 `PlaybackClock`을 세션 상태로 소유한다. 현재 시각과 재생 여부는 저장 문서, revision 또는 Undo/Redo history에 들어가지 않는다.

`TimelineController`는 Godot control을 Application 상태에 연결하는 adapter다.

- `TimeSlider.ValueChanged`는 playback을 pause한 뒤 slider 값을 범위 안 시각으로 seek한다. slider step은 `1 / FPS`다.
- `PlayPauseButton.Pressed`는 `PlaybackClock.Toggle()`을 호출하고, `StopButton.Pressed`는 0초 paused 상태를 요청한다.
- Main의 echo가 아닌 `Space` press는 controller의 같은 `TogglePlayback()` 경로를 사용한다.
- Main의 정상 `_Process(delta)`는 playing 상태에서 clock을 전진시킨다. 끝을 넘으면 duration에 clamp하고 자동 pause한다. 런타임 통합 검사는 프레임 수나 wait에 의존하지 않도록 `Advance()`만 결정적으로 직접 호출한다.
- controller는 clock 변경을 slider, `현재 ...초 / 전체 ...초 · 프레임 ...` label, 재생/일시정지 버튼 문구에 반영한다. controller 자신은 snapshot을 만들거나 view transform을 직접 바꾸지 않는다.

`SceneProjectionController`는 문서 변경과 clock 변경을 모두 구독하고 현재 시각의 snapshot 하나를 두 view consumer에 전달한다. 마지막 적용 키가 `(revision, time)`과 같으면 play/pause처럼 표시 상태만 바뀐 event에서 snapshot을 중복 적용하지 않는다. 따라서 같은 revision에서도 seek/advance/stop으로 time이 바뀌면 투영하고, 같은 time에서도 committed edit로 revision이 바뀌면 다시 투영한다.

시간 상태가 바뀌면 `DocumentSession`은 활성 `TransformPreview`를 clear한다. `TransformPreviewController`가 nullable preview를 두 consumer에 전달하므로 world는 마지막 committed 표시를 복원하고 top도 preview overlay를 제거한다. 이어 현재 `(revision,time)` snapshot이 두 view에 적용된다. seek 때문에 preview가 문서 mutation으로 오인되지 않으며 revision, 두 keyframe과 Undo/Redo stack은 그대로다.

편집 가능 여부는 선택 actor, 재생 여부와 현재 time을 함께 본다. 재생 중에는 잠그고, paused여도 현재 time이 선택 actor의 최초 transform keyframe time과 다르면 잠근다. 잠금 전이는 진행 중인 TopView drag/Inspector preview를 취소하고 입력·Apply·Undo/Redo를 비활성화하며 이유를 `TimelineStatus`와 Inspector에 표시한다. Stop은 언제나 0초로 이동하지만 최초 keyframe도 0초인 문서에서만 편집 가능 상태가 함께 복원된다.

후속 full timeline은 트랙 헤더, keyframe/행동 구간, 확대·스크롤·시간 스냅과 CRUD를 추가한다. 키프레임 drag 충돌 정책은 미리 보여주고 확정 시 하나의 명령으로 적용해야 한다. 현재 3A는 행동·락온 track 편집, 이동 궤적, 실제 DSR animation, render 실행이나 gamepad 입력을 구현한 단계가 아니다.

## 속성 패널

현재 `TransformInspectorController`는 선택 배우의 최초 X/Y/Z/Yaw만 편집한다. 선택 event, 문서 변경 event, preview event와 edit-availability event를 구독하고 종료 시 모두 해제한다. 내부 값 반영 중 `ValueChanged`가 다시 미리보기를 만들지 않도록 guard를 사용한다. 입력값은 다음 세 단계로 처리한다.

1. 텍스트를 임시 입력 상태로 유지한다.
2. 숫자 범위·참조·시간 충돌을 검증한다.
3. 유효할 때 Apply 버튼 또는 SpinBox 내부 LineEdit의 Enter 제출로 명령 하나를 확정한다.

입력 중인 `-`, 빈 문자열을 즉시 0으로 바꿔 사용자의 편집을 방해하지 않는다.

선택 없음, stale actor, 유한하지 않은 값, 범위 초과와 Undo/Redo 충돌은 `ErrorLabel`에 한글로 표시하고 문서는 바꾸지 않는다. Apply/Enter는 `NoChange`에 `적용할 실제 변환 변경이 없습니다.`, `Conflict`에 `선택한 배우의 변경이 오래되어 최신 문서 상태와 충돌했습니다.`를 표시한다. 확정 호출이 예외를 던져도 `CurrentRevision`이 증가했다면 문서 mutation과 history 전이는 완료된 것이므로 `변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다.`로 안내해 적용 실패로 잘못 표시하지 않는다. SpinBox는 범위 밖 텍스트를 일단 받아 controller가 X/Z ±1000·Y ±100 위반을 명시적으로 거부하게 하며, 자동 clamp된 값으로 preview나 commit을 시작하지 않는다. 유효 값으로 시작된 preview 도중 범위 오류가 생기면 guarded clear로 top/world preview만 취소한다. `PreviewChanged(null)`이 invalid SpinBox 값과 ErrorLabel을 committed 값으로 덮어쓰지 않으며, invalid 상태의 Apply/Enter도 preview를 다시 만들지 않는다. Undo/Redo 버튼은 활성 preview를 먼저 취소한다. 선택 유형별 Inspector 전환은 후속 기능이다.

재생 중이거나 최초 keyframe 이외의 시각이면 네 SpinBox와 Apply를 비활성화하고 Undo/Redo도 잠근다. 프로그래밍 방식 signal이나 남아 있던 입력이 들어와도 controller가 `CanEditSelectedTransform`을 다시 확인해 committed 최초 keyframe 값을 복원하고 잠금 이유를 보여준다. 이 UI 잠금은 history를 삭제하는 기능이 아니므로 Stop 등으로 편집 가능한 최초 시각에 돌아오면 기존 `CanUndo/CanRedo`에 맞게 버튼 상태가 복원된다.

## 기본 편집 런타임 통합 검사

헤드리스 런타임은 단순 문자열 출력 전에 실제 Godot 입력·노드 경계를 실행한다.

1. `Actor_runtime_actor/VisualRoot`와 `OverlayRoot`, 초기 committed 위치·회전을 확인한다.
2. 선택 actor의 28px 핸들을 `_GuiInput`으로 끌어 3D 회전 preview를 확인하고 Escape로 revision 증가 없이 복원한다.
3. 몸체를 40px 끌고 release해 최초 키프레임 이동을 revision 2에서 한 번만 확정한다.
4. 실제 Undo/Redo 버튼 signal로 revision 3/4, Domain 값, 3D transform과 버튼 disabled 상태를 확인한다.
5. Inspector X=2 valid preview가 world에 보인 뒤 X=1001을 입력하면 preview가 취소되고 X=1001/ErrorLabel은 보존되는지 확인한다. invalid Apply도 revision·history·키프레임·3D 상태를 바꾸거나 preview를 다시 만들면 안 된다.
6. 유효값 복원과 no-op Apply 뒤 preview가 남지 않고 최종 committed projection count가 top/world 각 4인지 확인한다.
7. 별도 임시 `Node3D`와 adapter에 sanitize 결과가 같은 actor 둘을 적용해 실제 child node 두 개의 이름이 distinct/stable인지 확인하고 임시 root만 해제한다.
8. 별도 임시 문서·세션·surface에 회전 drag/release를 보내 preview Yaw가 명령 하나로 확정되는지 확인한다.
9. 별도 임시 Inspector SpinBox의 `LineEdit.TextSubmitted`를 발생시켜 Enter 제출이 preview를 명령 하나로 확정하는지 확인한다.
10. actor가 사라진 snapshot을 적용할 때 adapter가 소유한 actor root만 제거하고 `Actors` 아래의 외부 child를 보존하는지 확인한다.

모든 assertion을 통과한 경우에만 다음 통합 표식과 최종 `BASIC_EDITING_READY ...` 표식을 순서대로 출력한다.

```text
BASIC_EDITING_INTEGRATION_READY rotation_preview=1 escape_restore=1 drag_commit=1 undo_button=1 redo_button=1 inspector_reject=1 invalid_preview_cancel=1 stale_error_clear=1 inspector_apply_noop=1 collision_nodes=1 final_ui_clean=1 rotation_commit=1 enter_commit=1 removal_ownership=1
```

그 뒤 timeline runtime probe는 runtime actor의 t=0, t=1 두 transform을 사용해 다음 순서로 실제 경계를 검증한다.

1. t=0 Inspector `ValueChanged`로 active preview를 만든다.
2. `HSlider.ValueChanged`로 0.5초를 seek해 preview clear를 확인한다. t=0의 basic-edit 결과 `(1,0,0), 0°`와 t=1 `(5,2,-4), 90°`에서 hand-derived midpoint는 `(3,1,-2), 45°`이며, world rotation Y는 `-π/4`다.
3. TopView의 midpoint hit와 top/world apply count를 함께 확인해 두 view가 같은 `(revision,time)` snapshot을 소비했음을 증명한다.
4. 중간 시각 TopView drag와 Inspector signal이 잠금에 막히고 revision, 두 keyframe, history와 표시 midpoint가 보존되는지 확인한다.
5. 실제 Play/Pause button signal과 Main의 두 번의 `Space` 입력이 같은 toggle 상태를 만드는지 확인한다. 같은 time의 상태 toggle은 projection count를 늘리면 안 된다.
6. `PlaybackClock.Advance(10)`을 직접 호출해 1초 clamp, 자동 pause, 마지막 transform과 두 view 동기화를 확인한다.
7. 실제 Stop button signal로 0초, basic-edit된 첫 transform과 편집 가능 UI/history 상태가 복원되는지 확인한다.

성공 시 기존 표식 뒤에 다음 exact marker를 출력한다.

```text
TIMELINE_PLAYBACK_READY scrub_midpoint=1 top_world_sync=1 revision_unchanged=1 history_unchanged=1 preview_cancel=1 edit_guard=1 play_button=1 space_toggle=1 end_clamp=1 stop_restore=1
```

## 재생 모드와 편집 모드

- 최초 keyframe 시각의 정지 편집: 선택 actor의 최초 transform 편집 가능
- 다른 시각의 정지 상태: 지정 시간을 읽기 전용 평가하고 transform 편집 잠금
- 실시간 재생: 문서 변경 잠금, `PlaybackClock` 시간 전진과 두 view 평가
- 스크럽: 먼저 pause하고 지정 시간 즉시 평가
- Stop: 0초 paused로 복귀; 최초 keyframe 시각도 0초일 때 편집 잠금 해제
- 렌더(후속): 결정적 시간 샘플을 사용하고 문서를 읽기 전용 스냅샷으로 고정

렌더 중 원본 문서를 수정할 수 있게 하더라도 현재 렌더는 시작 시점 스냅샷만 사용한다.

## 단축키 상태와 초안

현재 구현된 playback 단축키는 `Space`뿐이다. 다음 표의 나머지는 후속 기본 키 제안이며 아직 사용자 입력 계약이 아니다.

| 기능 | 기본 키 |
| --- | --- |
| 저장 | Ctrl+S |
| 다른 이름으로 저장 | Ctrl+Shift+S |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| 선택 | Q |
| 이동 | W |
| 회전 | E |
| 키프레임 추가 | K |
| 재생/일시정지 | Space |
| 선택 삭제 | Delete |
| 프레임 맞춤 | F |

Windows 및 Godot 기본 단축키와 충돌하면 구현 단계에서 사용자 설정 가능한 키맵으로 조정한다.
