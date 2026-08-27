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
TopView/세 Track Surface + Transform/Action/Lock Inspector + Toolbar Controllers
                │ 선택·preview·CRUD·확정 의도              │ seek·toggle·stop
                ▼                                           ▼
          DocumentSession ───────────────────────────── PlaybackClock
                │ 검증된 세 track 명령                       │ time + Changed
                ▼                                           │
          SceneDocument ── revision + Changed ──────────────┤
                │ Transform 보간 + Action/Lock left-hold     ▼
                └──────────────────────────────── SceneProjectionController
                                                    ┌─────┴─────┐
                                                    ▼           ▼
                                           TopViewSurface   WorldViewProjectionAdapter
```

- `DocumentSession`은 열린 문서, actor와 Transform/Action/Lock-on 선택, active track, 변환 미리보기, 세 track 공유 Undo/Redo 스택과 문서 길이/FPS로 만든 `PlaybackClock`을 소유한다.
- `ActorDisplayInfo`는 actor ID, 표시 이름과 역할만 담은 Application 불변 계약이다. 탑뷰는 이 조회 API를 사용하며 `ActorTrack`이나 원본 문서를 노출받지 않는다.
- `TopViewSurface`와 `TransformInspectorController`는 `SceneDocument`를 직접 보유하거나 수정하지 않는다. 영구 변경은 세션 공개 API를 통해서만 수행한다.
- `SceneProjectionController`는 문서 revision 또는 playback time이 바뀌면 `SceneSnapshot`을 한 번 만들고 동일 인스턴스를 탑뷰와 3D 소비자에 전달한다. 중복 방지 키는 revision 단독이 아니라 `(revision, time)`이다.
- `TransformPreviewController`는 동일한 nullable `TransformPreview` 인스턴스를 두 뷰에 전달한다. 이 값은 저장·직렬화되지 않으며 문서 revision도 올리지 않는다.
- 마우스를 놓거나 Inspector에서 Apply/Enter를 제출할 때만 `ReplaceTransformCommand` 하나가 확정된다.
- `DocumentSession.CommitPreviewDetailed()`는 확정 결과를 공개 `SceneEditResult`로 반환하며, 기존 `CommitPreview()` bool API는 호환성을 위해 유지되고 `Applied`일 때만 `true`를 반환한다.
- `DocumentSession.CurrentRevision`은 `SceneDocument`를 UI에 노출하지 않고도 현재 monotonic revision을 읽게 한다. Inspector는 확정 전후 revision으로 문서 변경 후 알림 subscriber가 실패한 경우를 구분한다.
- 현재 편집 대상은 선택 배우의 현재 재생 헤드 시각에 있는 선택 transform keyframe이다. marker click은 pause·seek·선택을 함께 수행한다. Add는 현재 시각의 평가 pose를 새 keyframe으로 만들며, 임의의 t=0 keyframe을 자동 생성하지 않는다.
- Action/Lock-on marker도 pause·seek와 해당 track selection을 함께 수행한다. semantic Add/Apply/Delete는 transform preview를 만들지 않고 full immutable frame command 하나를 실행한다.
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
InspectorPanel/ActionInspector
  ├─ ActionSelectedKeyframeLabel / ActionTimeInput / ActionKeyInput
  └─ ActionApplyButton / ActionErrorLabel
InspectorPanel/LockOnInspector
  ├─ LockOnSelectedKeyframeLabel / LockTimeInput / LockEnabledInput
  ├─ LockTargetInput / LockModeInput / LockYawOffsetInput
  └─ LockApplyButton / LockErrorLabel
TimelinePanel/TimelineControls
  ├─ PlaybackButtons/PlayPauseButton / StopButton
  ├─ TransformTrackSurface
  ├─ KeyframeToolbar/AddKeyframeButton / DeleteKeyframeButton
  ├─ ActionTrackSurface
  ├─ ActionToolbar/ActionAddButton / ActionDeleteButton
  ├─ LockOnTrackSurface
  ├─ LockOnToolbar/LockOnAddButton / LockOnDeleteButton
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

선택은 문서 내용이 아니라 세션 상태다. 현재 구현은 actor ID 하나와 그 actor의 Transform/Action/Lock-on keyframe ID 및 immutable frame 사본(각각 null 가능), active track을 보관한다.

- 선택 배우 ID
- 선택 transform keyframe ID
- 선택 Action keyframe ID
- 선택 Lock-on keyframe ID
- active timeline track
- 현재 시간
- 활성 도구와 좌표 모드

탑뷰에서 배우를 선택하면 현재 time에 있는 세 track marker selection을 다시 계산한다. 각 surface marker를 클릭하면 track별 `Select*Keyframe(id)`가 playback을 pause하고 해당 time으로 seek한 뒤 active track, Inspector section, marker 강조와 두 view snapshot을 같은 세션 상태로 맞춘다. 문서/history 변경 뒤에는 기존 ID, exact current marker, nearest marker, null 순으로 selection을 최신 immutable frame에 reconcile한다. 3D actor 노드는 actor ID로만 매핑되며 화면 노드 참조를 선택의 원본으로 쓰지 않는다. 다중 배우/marker 선택은 후속 타임라인 단계다.

## 편집 도구

### 선택 도구

현재는 클릭 단일 선택과 빈 공간 클릭 해제를 제공한다. Shift 다중 선택, 겹친 객체 순환 선택 또는 작은 목록은 후속 기능이다.

### 이동 도구

- 탑뷰: 배우 몸체를 3px 이상 끌어 수평면 X/Z를 이동하며 Y와 Yaw를 보존한다.
- 3D: 현재는 탑뷰·Inspector 결과를 표시하며 직접 지면 드래그와 축 기즈모는 후속 기능이다.
- 숫자 입력: Inspector에서 X/Z ±1000, Y ±100 범위와 0.1 step으로 입력한다.
- 그리드·정점 스냅은 후속 옵션이다.
- 현재 재생 헤드 시각에 marker가 있으면 그 선택 keyframe을 편집하고, marker가 없으면 `키프레임 추가`가 현재 평가 pose로 하나를 만든다.

### 회전 도구

수평 Yaw를 기본으로 하고 선택 배우 중심에서 28px 떨어진 방향 핸들과 숫자 입력을 제공한다. 0°는 +X, 90°는 +Z이며 3D actor root에는 `rotationY = -DegToRad(yaw)`를 적용한다. 락온 기반 방향 편집은 후속 단계다.

### 키프레임 도구

`TransformTrackSurface`는 selected actor의 transform keyframe만 마름모 marker로 그리는 Presentation surface다. `TransformTrackLayout`은 순수 C#에서 duration, surface width, horizontal padding, `(ID,time)`만 받아 marker의 X를 계산하고 hit-test한다. Godot `Control`, `DocumentSession`, selection, command, revision, drawing color나 node는 layout 경계 밖이다. duration이 0이면 marker는 padding에 놓고, hit-test가 겹치면 포인터에 더 가까운 marker, 동거리면 더 이른 time, 그 다음 ordinal ID를 선택한다. 이 결정성은 UI frame rate와 무관하다.

surface는 marker 좌표를 문서 원본으로 쓰거나 marker drag로 시간을 바꾸지 않는다. left click hit만 `DocumentSession.SelectTransformKeyframe`으로 전달한다. time 이동은 Inspector `TimeInput`과 `변환 적용`으로 after keyframe을 만들 때 하나의 Update command로 확정한다.

- `키프레임 추가`: 선택 actor가 있고 paused이며 현재 time에 marker가 없을 때만 활성화된다. 현재 평가 snapshot pose를 복사해 Add command 하나로 확정하고 새 marker를 선택한다.
- `변환 적용`/Enter: 선택 marker와 playback time이 일치할 때만 활성화된다. Time/X/Y/Z/Yaw는 하나의 atomic Update command이고, 같은 시각 conflict·range·stale preimage는 commit하지 않는다.
- `키프레임 삭제`: 선택 marker가 있고 paused이며 actor에 marker가 둘 이상일 때만 활성화된다. 성공 뒤 가까운 남은 marker를 선택한다.
- 재생 중에는 CRUD와 Inspector Apply/Undo/Redo를 모두 잠근다. selection·scrub·preview 취소는 문서 mutation이 아니다.

`ActionTrackSurface`와 `LockOnTrackSurface`는 같은 horizontal padding과 순수 `StepTrackLayout`을 사용한다. marker X/hit-test와 함께 각 marker에서 다음 marker 또는 문서 끝까지의 left-hold segment를 만든다. 첫 marker 전에는 segment가 없다. Action segment label은 원문 key, Lock-on segment label은 `ON/OFF · target/없음 · SNAP/CONT/KEY`이며 enabled segment만 강조한다. 두 surface는 document, playback, actor와 세 track selection event를 구독해 redraw하고 `Detach()`가 모든 구독을 idempotent하게 해제한다.

- `SemanticTimelineController`는 Action/Lock-on Add/Delete 버튼을 세션 API와 가용성에 연결한다. Action Add는 공백 key를, 활성 Lock-on Add는 target 없음 상태를 UI에서 먼저 설명한다.
- `ActionLockOnInspectorController`는 active track에 따라 Transform/Action/Lock-on section 하나만 보이게 하고 marker selection/document/playback event마다 committed full frame을 다시 읽는다.
- Action Apply는 time+원문 nonblank key, Lock-on Apply는 time+enabled+target+mode+offset을 각각 command 하나로 확정한다. semantic 입력 변경은 Domain preview를 만들지 않는다.
- target OptionButton은 `없음` 뒤에 self를 제외한 actor ID를 ordinal 안정 정렬한다. mode OptionButton ID는 Domain enum 값과 같고 offset은 유한한 각도 입력을 허용한다.

이후 선택 keyframe drag/복제·보간 변경, 여러 track 이동의 상대 time 보존, timeline 확대·스크롤·스냅을 확장한다. Action/Lock-on 구간 표현과 Inspector CRUD는 현재 foundation에 포함된다.

## 명령과 Undo/Redo

현재 모든 영구 세 track 변경은 Application 내부에만 노출되는 다음 명령 인터페이스를 따른다. `ISceneEditCommand`는 public Editor 계약이 아니며 Editor는 `DocumentSession`의 공개 API만 사용한다.

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

`ReplaceTransformCommand`는 기존 pose-only preview 확정용이다. CRUD에는 Transform/Action/Lock-on 각각 Add/Update/Remove command가 actor ID와 immutable full keyframe preimage/postimage를 고정하며 다음 불변 조건을 지킨다.

- 실행 전 검증 실패는 문서를 부분 변경하지 않는다.
- Undo는 실행 전 의미 상태로 정확히 돌아간다.
- 드래그 중에는 문서를 바꾸지 않고 mouse release에서 하나의 명령만 만든다.
- 가져오기는 새 문서 생성 또는 하나의 복합 명령으로 처리한다.
- 재생 시간 변경과 패널 크기 같은 세션 상태는 문서 Undo에 넣지 않는다.

Update는 Execute에서 before→after, Undo에서 after→before를 검증한다. Add Undo는 추가 keyframe을 정확히 제거하고, Delete Undo는 제거한 keyframe을 정확히 복원한다. Domain이 preimage 불일치, duplicate time, invalid Lock-on target 또는 마지막 Transform keyframe 삭제를 거부하면 command는 history를 움직이지 않고 `Conflict`가 된다. Action/Lock-on 마지막 marker 삭제는 허용한다. `SceneEditResult.Applied`만 revision과 history를 바꾸고 `NoChange`는 값을 다시 쓰지 않는다.

세 track은 한 history stack을 공유한다. `UndoButton`/`RedoButton`은 `InspectorPanel/HistoryToolbar` 아래에 있어 Transform/Action/Lock-on section 전환과 무관하게 항상 보인다. signal 구독·해제는 기존 `TransformInspectorController`가 소유하지만 handler guard와 Disabled 상태는 `DocumentSession.CanEditHistory && CanUndo/CanRedo`를 사용한다. Action/Lock-on runtime 검증도 semantic marker를 활성 상태로 유지한 채 실제 global 버튼으로 preimage/postimage를 왕복한다.

## 탑뷰 표현

- 현재 배우는 표시 이름과 `역할: ...` 텍스트, 방향선을 함께 그려 색만으로 의미를 구분하지 않게 한다. 일반 역할은 원, `Enemy`·`invader`·`target`·`적`을 포함한 적대 역할은 마름모 몸체를 사용한다.
- 고정 중심, 40px/world unit 배율을 사용하고 화면 오른쪽을 +X, 화면 아래쪽을 +Z로 매핑한다.
- 몸체 hit 반경은 16px, 방향 핸들 hit 반경은 10px이며 둘이 겹치면 핸들을 우선한다.
- 28px 원형 회전 핸들은 선택 actor에만 그려지고 hit-test 대상도 선택 actor 하나로 일치한다.
- 드래그 미리보기는 별도 색으로 표시하며 `Escape` 또는 선택 변경 시 즉시 committed snapshot으로 복원한다.
- semantic overlay는 lock line/target marker를 actor body 아래, actor body/선택 표시를 중간, action label을 위에 그리는 고정 draw order를 사용한다. 이동 궤적·충돌원·뒤잡 부채꼴은 후속 레이어다.
- `Apply`/`ApplyPreview`는 preview 위치까지 반영한 immutable `DisplayedSemanticOverlays`를 atomic하게 교체하고 `_Draw()`는 다시 계산하지 않고 이 동일 상태를 소비한다. 이 public read-only state는 실제 surface가 받은 action label/lock badge/line/target marker를 runtime과 진단 도구가 확인하는 production seam이다.
- 줌과 팬은 좌표 데이터를 바꾸지 않는다.
- 화면 좌표와 문서 좌표 변환은 카메라 변환 하나에서 수행한다.
- 선택 허용 오차는 줌에 따라 화면 픽셀 기준으로 유지한다.

## 3D 표현

- 기본 캐릭터는 Capsule 몸체와 Box 방향 표식으로 만든 플레이스홀더다.
- `WorldViewProjectionAdapter`는 `Actors` 아래에 `Actor_<sanitized-id>` root를 actor ID별로 생성·재사용한다. snapshot에서 사라진 actor는 adapter가 소유한 root만 제거한다.
- actor root에 문서 위치와 Yaw를 적용하고 `VisualRoot`와 판정·교육 표시용 `OverlayRoot`를 분리한다.
- 방향 Box는 로컬 +X를 바라본다. 실제 모델의 전방축 차이는 향후 `VisualRoot` offset으로만 보정한다.
- preview가 오면 대상 actor root의 표시 transform만 임시로 덮어쓰고 null clear 시 latest committed snapshot을 다시 적용한다. Godot 노드 transform을 문서 원본으로 사용하지 않는다.
- `WorldTransformMapper`는 Godot namespace와 `Vector*`를 참조하지 않고 double 기반 `WorldPosition`과 Y rotation radians만 반환한다. float `Godot.Vector3` 변환은 `WorldViewProjectionAdapter` 하나에서만 수행한다.
- 서로 다른 actor ID가 같은 sanitized base 이름을 만들면 첫 node는 exact base를 유지하고 다음 node는 원본 ID 기반 결정적 suffix를 사용한다.
- `OverlayRoot`에는 billboard `ActionLabel`, billboard `LockBadge`, 재사용 `ImmediateMesh` 기반 `LockLine`을 actor마다 한 번 만들고 snapshot Apply마다 text/visibility/vertices만 갱신한다. enabled target이 없으면 pure overlay layout이 명시적으로 실패한다.
- Action label은 `행동: <key>`, Lock badge는 `LOCK · <target> · SNAP/CONT/KEY`를 사용한다. preview는 actor/target 표시 위치만 덮고 semantic state는 latest committed snapshot을 유지한다.
- 의미 행동을 `AssetCatalog`를 통해 실제 또는 대체 애니메이션에 연결하는 일은 후속이다. 현재는 label만 표시한다.
- 교육 오버레이는 모델 재질과 분리한 디버그/설명 렌더 계층을 사용한다.
- 지면, 조명과 배경은 영상 가독성을 우선해 단순하게 유지한다.

## 타임라인 재생 상태와 투영 소유권

완료된 3A는 읽기 전용 playback foundation이고, 3B는 transform marker/CRUD, 현재 foundation은 Action/Lock-on marker·left-hold·CRUD·overlay를 추가한다. `SceneDocument`는 duration/FPS와 세 track committed keyframe을 소유하고, `DocumentSession`은 해당 문서에서 만든 `PlaybackClock`·actor/세 track 비영구 selection을 세션 상태로 소유한다. 현재 시각, 재생 여부, active track과 selection은 저장 문서, revision 또는 Undo/Redo history에 들어가지 않는다.

`TimelineController`는 Godot control을 Application 상태에 연결하는 adapter다.

- `TimeSlider.ValueChanged`는 playback을 pause한 뒤 slider 값을 범위 안 시각으로 seek한다. slider step은 `1 / FPS`다.
- `PlayPauseButton.Pressed`는 `PlaybackClock.Toggle()`을 호출하고, `StopButton.Pressed`는 0초 paused 상태를 요청한다.
- Main의 echo가 아닌 `Space` press는 controller의 같은 `TogglePlayback()` 경로를 사용한다.
- Main의 정상 `_Process(delta)`는 playing 상태에서 clock을 전진시킨다. 끝을 넘으면 duration에 clamp하고 자동 pause한다. 런타임 통합 검사는 프레임 수나 wait에 의존하지 않도록 `Advance()`만 결정적으로 직접 호출한다.
- controller는 clock 변경을 slider, `현재 ...초 / 전체 ...초 · 프레임 ...` label, 재생/일시정지 버튼 문구에 반영한다. controller 자신은 snapshot을 만들거나 view transform을 직접 바꾸지 않는다.

`SceneProjectionController`는 문서 변경과 clock 변경을 모두 구독하고 현재 시각의 snapshot 하나를 두 view consumer에 전달한다. snapshot에는 보간 transform과 stepped Action/Lock-on state가 함께 있다. 마지막 적용 키가 `(revision, time)`과 같으면 play/pause처럼 표시 상태만 바뀐 event에서 snapshot을 중복 적용하지 않는다. 따라서 같은 revision에서도 seek/advance/stop으로 time이 바뀌면 투영하고, 같은 time에서도 committed edit로 revision이 바뀌면 다시 투영한다.

시간 상태가 바뀌면 `DocumentSession`은 활성 `TransformPreview`를 clear한다. `TransformPreviewController`가 nullable preview를 두 consumer에 전달하므로 world는 마지막 committed 표시를 복원하고 top도 preview overlay를 제거한다. 이어 현재 `(revision,time)` snapshot이 두 view에 적용된다. seek 때문에 preview가 문서 mutation으로 오인되지 않으며 revision, 두 keyframe과 Undo/Redo stack은 그대로다.

편집 가능 여부는 선택 actor, track별 selected marker, 재생 여부와 현재 time을 함께 본다. 재생 중에는 세 track Update/Add/Delete와 Apply/Undo/Redo를 잠근다. paused여도 현재 time이 해당 selected marker time과 다르면 Update를 잠그며, Add는 같은 track/current time에 marker가 있을 때 잠긴다. Delete는 selection을 요구하고 Transform에만 마지막 marker guard가 있다. 잠금 전이는 진행 중인 TopView drag/Transform Inspector preview를 취소하고 모든 관련 control을 비활성화하며 이유를 `TimelineStatus`와 track별 ErrorLabel에 표시한다. Stop은 언제나 0초로 이동하며, 그 시각의 세 track exact marker selection을 다시 계산한다.

Action/Lock-on marker 선택은 observer가 seek를 다시 바꿀 수 있다는 전제에서 최대 32회의 bounded 안정화 루프를 사용한다. 매 회 최신 문서에서 target actor와 full keyframe을 다시 읽고 pause·target time seek를 수행한 뒤 actor, time, playing, active track, selected ID와 full payload가 모두 target과 일치할 때만 `Applied`를 반환한다. 두 번 이상 유한하게 다른 시각으로 리디렉션돼도 다시 target을 시도하며, attempt 시작 이후 실제 게시된 selection의 actor/track/ID/full-frame 서명을 사용해 이전 attempt의 force 상태를 누적하지 않고 최종 안정 target full payload를 정확히 한 번만 게시한다. 같은 시각 무-seek cross-track과 rollback 뒤 이동된 frame selection 보존 상태는 실제 target 게시가 없으면 한 번 강제 게시하고, final event observer가 active track을 바꾸면 복원된 target context를 다시 게시한다. target이 사라지거나 32회 안에 안정화되지 않으면 호출 전 actor/time/playing/track/세 selection을 원자적으로 복구하고 성공을 보고하지 않는다.

후속 full timeline은 확대·스크롤·시간 스냅과 선택 keyframe drag/복제 정책을 추가한다. 현재 단계는 Action/Lock-on 단계 track 편집과 교육 overlay까지 구현했지만, Lock-on target 방향에 따른 actor Yaw 계산, 자유 방향/Lock-on 이동 궤적, 실제 DSR animation, render 실행이나 gamepad 입력은 구현하지 않았다.

## 속성 패널

현재 `TransformInspectorController`는 선택한 transform keyframe의 Time/X/Y/Z/Yaw를 편집한다. 선택 event, keyframe selection event, 문서 변경 event, preview event와 edit-availability event를 구독하고 종료 시 모두 해제한다. 내부 값 반영 중 `ValueChanged`가 다시 미리보기를 만들지 않도록 guard를 사용한다. 입력값은 다음 세 단계로 처리한다.

1. 텍스트를 임시 입력 상태로 유지한다.
2. time 문서 범위, 좌표 범위, 선택·재생 잠금과 같은 time 충돌을 검증한다.
3. 유효할 때 Apply 버튼 또는 SpinBox 내부 LineEdit의 Enter 제출로 명령 하나를 확정한다.

입력 중인 `-`, 빈 문자열을 즉시 0으로 바꿔 사용자의 편집을 방해하지 않는다.

선택 없음, stale actor, 유한하지 않은 값, 범위 초과와 Undo/Redo 충돌은 `ErrorLabel`에 한글로 표시하고 문서는 바꾸지 않는다. Apply/Enter는 `NoChange`에 `적용할 실제 변환 변경이 없습니다.`, `Conflict`에 `선택한 키프레임의 변경이 오래되었거나 같은 시각의 키프레임과 충돌했습니다.`를 표시한다. 확정 호출이 예외를 던져도 `CurrentRevision`이 증가했다면 문서 mutation과 history 전이는 완료된 것이므로 `변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다.`로 안내해 적용 실패로 잘못 표시하지 않는다. SpinBox는 범위 밖 텍스트를 일단 받아 controller가 time 0~문서 길이, X/Z ±1000·Y ±100 위반을 명시적으로 거부하게 하며, 자동 clamp된 값으로 preview나 commit을 시작하지 않는다. 유효 값으로 시작된 preview 도중 범위 오류가 생기면 guarded clear로 top/world preview만 취소한다. `PreviewChanged(null)`이 invalid SpinBox 값과 ErrorLabel을 committed 값으로 덮어쓰지 않으며, invalid 상태의 Apply/Enter도 preview를 다시 만들지 않는다. Undo/Redo 버튼은 활성 preview를 먼저 취소한다.

재생 중이거나 selected marker와 다른 시각이면 Time 포함 다섯 SpinBox와 Apply를 비활성화하고 Undo/Redo도 잠근다. 프로그래밍 방식 signal이나 남아 있던 입력이 들어와도 controller가 `CanEditSelectedTransform`을 다시 확인해 committed selected keyframe 값을 복원하고 잠금 이유를 보여준다. 이 UI 잠금은 history를 삭제하는 기능이 아니므로 marker를 선택해 편집 가능한 시각으로 돌아오면 기존 `CanUndo/CanRedo`에 맞게 버튼 상태가 복원된다.

`ActionLockOnInspectorController`는 `ActiveTimelineTrack`에 따라 세 section의 `Visible`을 전환한다. session은 semantic marker 선택에서 target active track을 pause/seek보다 먼저 바꾸므로 controller가 seek 중 받은 selection event에도 올바른 section을 표시한다. Action↔Lock-on marker가 같은 시각이라 seek event가 없는 경우에는 target selection payload를 강제로 다시 받아 visibility를 전환한다. 아직 marker가 없는 트랙은 해당 Action/Lock-on lane의 빈 배경을 클릭하면 활성화된다. 이 경로는 marker를 선택하지 않고 현재 actor의 full selection payload만 다시 게시하므로 문서 revision, history, playback time/playing을 바꾸지 않으면서 첫 Add용 Inspector를 표시한다. Action section은 selection label, Time, ActionKey, Apply, ErrorLabel을 가지며 key/time은 command 하나로 제출한다. Lock-on section은 selection label, Time, enabled, target, mode, yaw offset, Apply, ErrorLabel을 가지며 다섯 의미 값을 command 하나로 제출한다. target 목록은 document/actor selection 변경마다 self를 제외해 다시 만들고 committed target을 재선택한다. semantic 입력은 Apply 또는 Enter 전에는 로컬 UI 값일 뿐 preview/revision/history를 만들지 않는다.

semantic session API는 `SemanticEditOutcome`과 `SemanticEditIssue`로 성공, no-op, duplicate time, stale preimage, time range, ActionKey, Lock-on target/yaw/mode와 기타 conflict를 구분한다. controller는 예외 문자열을 해석하지 않고 이 typed outcome을 다음과 같은 실제 한글 메시지로 변환한다.

- no-op: `<작업>: 적용할 실제 Action/Lock-on 변경이 없습니다.`
- duplicate time: `<작업> 실패: 해당 시각에는 이미 Action/Lock-on 키프레임이 있습니다.`
- stale preimage: `<작업> 실패: 선택 정보가 오래되어 최신 문서와 충돌했습니다.`
- time range: `<작업> 실패: 시각은 0초 이상 <문서 길이>초 이하여야 합니다.`
- invalid target: `<작업> 실패: Lock-on 대상은 같은 문서의 다른 배우여야 하며 활성 상태에는 대상이 필요합니다.`

`SemanticTimelineController`와 `ActionLockOnInspectorController`는 semantic 명령 호출 직전 revision을 캡처한다. observer 예외가 발생했더라도 revision이 증가했다면 mutation과 history 전이가 완료된 것이므로 `<작업>: 변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다: <예외 메시지>`를 표시하고 최신 selection/availability를 다시 읽는다. revision이 그대로인 예기치 않은 예외나 프로그래밍 오류는 UI 문자열로 삼키지 않고 원래 예외 그대로 전파한다. 예상 가능한 validation·conflict·no-op은 예외가 아니라 typed outcome으로 표시한다. 성공한 다음 작업은 해당 track ErrorLabel을 비운다. 재생 중에는 input과 Apply가 잠기며 signal을 강제로 발생시켜도 session availability guard가 mutation을 거부한다. semantic controller들은 생성 시 연결한 Button/LineEdit/session/playback/document event를 `Dispose()`에서 정확히 해제한다.

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

## 변환 키프레임 CRUD 런타임 통합 검사

CRUD probe는 source 문자열이나 test-only API가 아니라 실제 `TransformTrackSurface` click, Add/Delete/Undo/Redo button signal, Inspector `SpinBox`와 Apply signal을 사용한다. fixture의 t=0 `(1,0,0), 0°`, t=1 `(5,2,-4), 90°`에서 독립 계산한 t=0.5 pose `(3,1,-2), 45°`를 기준으로 다음 계약을 확인한다.

1. t=0.5 scrub 뒤 Add가 평가 pose를 가진 marker 하나를 만들고 selection·Inspector ID/time·TopView/WorldView snapshot을 동기화한다.
2. 실제 marker click이 pause·seek·selection을 동기화한다.
3. Inspector Time=0.6, pose `(3.5,1.5,-2.5)`, Yaw=60° Apply가 하나의 atomic update로 marker 위치와 committed pose를 함께 바꾼다.
4. 실제 Undo/Redo 버튼이 time·pose·selection/history를 command 단위로 왕복한다.
5. Delete, Undo 복원, Redo 재삭제가 selection의 가장 가까운 marker 규칙과 최소 한 marker 규칙을 깨지 않는다.
6. duplicate Add, 범위 밖 time, 마지막 marker Delete와 stale preimage가 revision/history/최신 committed data를 바꾸지 않는다.
7. active preview 뒤 scrub이 preview만 취소하고, 재생 중 CRUD·Inspector·TopView transform edit가 잠겨 있는 동안 revision/history가 불변이다.

모든 assertion 뒤에만 다음 exact marker가 출력된다.

```text
TIMELINE_KEYFRAME_CRUD_READY add=1 update=1 time_move=1 delete=1 undo=1 redo=1 duplicate_reject=1 range_reject=1 min_keyframe_guard=1 stale_conflict=1 selection_sync=1 preview_cancel=1 playback_lock=1
```

## Action/Lock-on 런타임 통합 검사

`Main._Ready()`는 새 node를 exact type/path로 찾은 뒤 `DocumentSession` 생성, TopView/Transform/Action/Lock-on surface attach, projection/preview/Transform Inspector/timeline/semantic Inspector/semantic toolbar controller 생성 순으로 조립한다. 실패와 `_ExitTree()`는 semantic toolbar → semantic Inspector → timeline → Transform Inspector → preview → projection → Lock-on/Action/Transform/TopView surface의 생성 역순으로 idempotent cleanup한다. 새 probe는 기존 transform CRUD와 같은 deferred completion owner에서 그 marker 다음에 실행된다.

probe는 transform 검증이 남긴 `revision=15`, Top/World apply `32/32`, runtime actor의 단일 transform `(1,0,0), 0°`와 기존 history를 hand-derived 시작점으로 고정한다. 기존 actor를 바꾸지 않고 `(4,0,3)`의 `runtime-target`을 두 번째 actor로 추가한 뒤 새 history event만 별도로 센다. 기대값은 실행 결과에서 다시 계산하지 않고 각 mutation/document event/seek를 독립 계산한 literal revision/history/apply count로 비교한다.

1. marker가 없는 Action lane의 빈 배경에 실제 viewport mouse press/release를 보내 Action Inspector와 보이는 ActionKey/Add control이 나타나되 revision/history/playback이 불변인지 확인한다. ActionKey `windup` 첫 Add에는 one-shot document observer 예외를 넣어 mutation/history는 확정되고 `변경은 저장되었지만 화면 표시 알림 처리에 실패했습니다:`가 표시되는지 검증한다. Action surface viewport marker click 뒤 범위 밖 time Apply의 typed 오류를 확인하고, SpinBox time `0.2`와 ActionKey `attack` Apply 성공이 오류를 지우는지 확인한다. 이어 같은 값을 LineEdit TextSubmitted로 다시 제출해 no-op의 revision/history/apply count 불변과 전용 오류 문구를 확인한다.
2. Action Inspector와 marker를 활성 상태로 유지한 채 항상 보이는 global Undo/Redo 버튼으로 Action Apply의 preimage/postimage와 selection/time을 왕복한다.
3. HSlider grabber geometry에서 hand-derived `0.75` 위치로 viewport mouse press/release를 보내 scrub하고 Action `attack` left-hold, TopView의 실제 `DisplayedSemanticOverlays`, World `ActionLabel` text/visibility를 확인한 뒤 Action marker를 다시 클릭하고 Delete한다.
4. marker가 없는 Lock-on lane의 빈 배경을 실제 viewport input으로 클릭해 Lock-on Inspector와 입력/Add control을 표시하되 상태가 불변인지 확인한다. enabled인데 target이 없는 실제 Add signal의 typed target 오류를 확인한 뒤 target OptionButton `runtime-target`, mode `Continuous`, enabled와 offset `15°`로 첫 Lock-on을 Add하고 Lock-on surface viewport marker click으로 Inspector의 full frame을 확인한다.
5. SpinBox offset `20°` Lock Apply에 one-shot document observer 예외를 넣어 확정 mutation과 observer 실패 안내를 함께 검증한다. 이어 OptionButton `Snap`, offset `-30°` Apply 성공이 오류를 지우는지 확인하고, Lock Inspector를 활성 상태로 유지한 채 global Undo/Redo로 `20°/Continuous` preimage와 `-30°/Snap` postimage를 왕복한다.
6. Lock-on Delete 뒤 global Undo로 overlay 확인용 frame을 복원한다. `0.75` scrub에서 Action 없음, Lock-on `runtime-target/Snap/-30°` left-hold, 실제 Top surface line `(1,0,0)→(4,0,3)`, World `LockBadge`와 `LockLine` visibility를 확인한다.
7. `0.75`의 빈 exact time에서 Action/Lock Add와 global Undo/Redo가 paused 상태에서 enabled임을 먼저 확인한다. 재생 중 유효 입력을 다시 채워 여섯 semantic Add/Apply/Delete와 global Undo/Redo의 실제 signal을 모두 보내고 revision/history/Top/World apply count 불변 및 각 잠금 사유를 확인한다. 검증용 Action 하나를 paused 상태에서 추가한 뒤 playback guard 종료 상태는 `revision=29`, semantic history event `13`, Top/World apply `54/54`이다.
8. Action이 활성인 `0.75`초에서 실제 Lock-on surface의 `0.2`초 marker를 클릭하고 Lock-on Inspector만 표시되는지 확인한다. 이어 실제 Action surface의 `0.75`초 marker를 클릭해 Action Inspector만 표시되는지 확인한다. 두 cross-track·cross-time seek 뒤 revision/history는 `29/13`으로 불변이고 hand-derived Top/World apply count는 `56/56`이다.
9. 실제 slider를 `0.2`초로 scrub하고 Action Add signal로 Lock-on과 같은 시각의 두 번째 Action marker를 만든다. Lock-on surface → Action surface를 같은 시각에서 클릭해 seek/apply 없이 각 target Inspector만 표시되는지 확인한다. 같은 시각 Action Add 재시도는 typed duplicate 오류를 표시하고 상태를 바꾸지 않는다. 최종 hand-derived revision/history는 `30/14`, Top/World apply count는 `58/58`이다. 전체 probe는 wait, sleep, `_Process` 횟수, source-string assertion과 test-only production API를 사용하지 않는다.

모든 assertion을 통과해야 다음 exact marker가 출력된다. `/1→/2` migration과 `/2` round-trip은 Editor가 Infrastructure를 참조하지 않으므로 이 marker가 아니라 Infrastructure 전체 테스트로 검증한다.

```text
ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1
ACTION_LOCK_ON_PLAYBACK_GUARDS_READY action_add=1 action_apply=1 action_delete=1 lock_add=1 lock_apply=1 lock_delete=1 undo=1 redo=1
ACTION_LOCK_ON_REVIEW_FIXES_READY empty_action_add=1 empty_lock_add=1 detailed_errors=1 observer_commit=1
```

## 재생 모드와 편집 모드

- marker 시각의 정지 편집: 선택 actor의 selected transform keyframe 편집 가능
- marker 없는 다른 시각의 정지 상태: 지정 시간을 읽기 전용 평가하고 Add만 가능, transform Update/Delete는 잠금
- 실시간 재생: 문서 변경 잠금, `PlaybackClock` 시간 전진과 두 view 평가
- 스크럽: 먼저 pause하고 지정 시간 즉시 평가
- Stop: 0초 paused로 복귀; 0초 marker가 있으면 그 marker를 선택해 편집 상태 복원
- 렌더(후속): 결정적 시간 샘플을 사용하고 문서를 읽기 전용 스냅샷으로 고정

다음 구현 단위는 `LockOnTrackingMode`와 offset/target을 실제 방향 계산에 연결하고, Lock-on 이동과 자유 방향 이동의 궤적을 동일 snapshot 경계에서 비교 표시하는 것이다.

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
