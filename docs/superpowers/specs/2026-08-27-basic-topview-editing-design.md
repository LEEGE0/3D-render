# 탑뷰 기본 편집과 3D 실시간 투영 설계

## 1. 목적과 승인된 방향

이번 마일스톤은 사용자가 탑뷰에서 배우를 선택하고 X/Z 위치와 방향각을 직접 편집하면, 같은 장면 상태가 3D 플레이스홀더에 즉시 반영되는 최초의 실제 편집 흐름을 만든다. 사용자가 승인한 기본 동작은 **탑뷰 직접 편집 + 3D 실시간 동기화**다.

이 설계는 다음 범위까지 포함한다.

- 순수 C# Application 계층과 `DocumentSession`
- 배우 선택과 세션 상태
- 위치·방향 편집 명령과 Undo/Redo
- 드래그 중 비영구 미리보기와 놓을 때 한 번의 확정
- 탑뷰 좌표 변환·선택·이동·회전 입력
- 3D 기본 메시 플레이스홀더와 동일 상태 투영
- 선택 배우의 X/Y/Z/Yaw 숫자 입력
- 구조 검사, 단위 테스트, Godot 헤드리스 런타임 표식

다음 항목은 후속 마일스톤으로 남긴다.

- 타임라인 스크럽·재생과 임의 시점 키프레임 편집
- 키프레임 추가·이동·삭제·복제
- 3D 화면에서의 직접 피킹·기즈모 편집
- 그리드 스냅·팬·줌·다중 선택
- 실제 DARK SOULS REMASTERED 모델·애니메이션
- 게임패드/Xbox 입력
- 자동 저장·복구와 실제 영상 렌더 실행

## 2. 접근 비교와 결정

### 접근 A — Godot 화면에서 `SceneDocument` 직접 변경

코드 양은 적지만 Presentation이 Domain을 직접 소유하고 Undo/Redo·오류 원자성·드래그 병합을 제각각 구현하게 된다. 드래그 픽셀마다 revision이 증가할 위험도 있다. 계층 계약을 깨므로 사용하지 않는다.

### 접근 B — Application 명령·세션을 통한 편집

Godot은 선택·드래그 의도를 Application에 전달하고, Application은 검증된 명령으로 Domain을 한 번만 변경한다. 선택과 미리보기는 저장되지 않는 세션 상태로 유지하고, 확정된 문서 변경만 기존 snapshot 흐름으로 두 뷰에 전달한다. 테스트와 후속 타임라인 확장에 가장 유리하므로 채택한다.

### 접근 C — 문서 전체 복제 또는 직렬화 Memento

Undo가 단순해 보이지만 작은 위치 변경마다 문서 전체를 복제하고, 세션 상태와 저장 포맷이 Undo 구현에 결합된다. 현재 규모에는 과하므로 사용하지 않는다.

## 3. 계층과 프로젝트 구조

의존성은 다음 방향으로 고정한다.

```text
PvpGuide.Editor (Godot Presentation)
        ↓
PvpGuide.Application (Session / Commands / Projection coordination)
        ↓
PvpGuide.Domain (SceneDocument / ActorTrack / Keyframes)
```

`PvpGuide.Infrastructure`는 이번 편집 흐름에 관여하지 않는다. Domain과 Application에는 Godot 타입, 파일 경로, JSON, `Process`를 넣지 않는다.

새 Application 프로젝트의 책임은 다음과 같다.

- 열린 `SceneDocument`와 선택 상태를 가진다.
- 위치·방향 명령을 실행하고 Undo/Redo 스택을 관리한다.
- 드래그 미리보기의 시작·갱신·취소·확정을 조율한다.
- 문서 snapshot 포트와 선택·미리보기 이벤트를 Presentation에 제공한다.
- 저장 가능한 의미 상태와 UI 세션 상태를 분리한다.

기존 `SceneProjectionController`는 Godot 타입이 없는 조정자이므로 Application 프로젝트로 이동한다. Editor 프로젝트는 Application을 참조하고 Application은 Domain을 참조한다. Editor는 Application 계약에 노출된 불변 Domain 값만 표현에 사용하며, 문서 변경은 Application 공개 API를 통해서만 수행한다.

## 4. Domain 편집 원자 연산

`ActorTrack`의 컬렉션은 계속 불변으로 취급한다. 기존 변환 키프레임을 편집할 때 새 `ActorTrack`을 만든 뒤 `SceneDocument`의 해당 항목을 교체한다.

핵심 계약은 다음 형태다.

```csharp
public TransformKeyframe GetTransformKeyframe(string keyframeId);

public ActorTrack ReplaceTransformKeyframe(
    TransformKeyframe expectedCurrent,
    TransformKeyframe replacement);

public TransformKeyframe GetTransformKeyframe(
    string actorId,
    string keyframeId);

public bool ReplaceTransformKeyframe(
    string actorId,
    TransformKeyframe expectedCurrent,
    TransformKeyframe replacement);
```

불변 조건은 다음과 같다.

- `replacement.Id`와 `replacement.TimeSeconds`는 기존 키프레임과 정확히 같아야 한다.
- 위치와 Yaw만 변경할 수 있다.
- `expectedCurrent`은 ID·시간·위치·Yaw가 현재 값과 모두 같아야 한다.
- stale expected 값, 없는 배우·키프레임, ID·시간 변경은 문서를 바꾸기 전에 실패한다.
- 새 track 생성과 전체 검증이 끝난 뒤에만 dictionary/list를 함께 교체한다.
- 성공한 의미 변경은 revision을 정확히 1 증가시키고 `Changed`를 정확히 한 번 발생시킨다.
- 같은 값으로의 no-op은 `false`를 반환하고 revision·이벤트를 변경하지 않는다.
- 표시 이름·역할·Action/LockOn 트랙·다른 변환 키프레임은 그대로 보존한다.

Undo도 이전 revision으로 되돌리지 않는다. 이전 의미 값을 새 변경으로 적용하므로 revision은 항상 단조 증가한다.

## 5. 편집 대상 정책

타임라인이 아직 없으므로 이번 마일스톤은 배우별로 시간상 가장 이른 변환 키프레임을 직접 편집 대상으로 삼는다. 가져온 V1 샘플처럼 첫 키프레임이 t=0.25여도 현재 화면의 초기 평가값과 편집 대상이 일치한다.

이 정책은 다음 이유로 임의 t=0 키프레임 자동 생성보다 안전하다.

- 가져온 원본의 시간 구조를 조용히 바꾸지 않는다.
- 첫 편집 때문에 보이지 않는 중복·추가 키프레임이 생기지 않는다.
- 키프레임 추가·충돌 정책은 타임라인 마일스톤에서 명시적으로 구현할 수 있다.

Application은 대상 actor의 `TransformKeyframes[0]` ID를 명령에 고정한다. 명령 실행 시 expected preimage를 다시 검증해 오래된 UI 상태가 최신 편집을 덮어쓰지 못하게 한다.

## 6. DocumentSession과 명령

공개 세션 계약은 다음 책임을 가진다.

```csharp
public sealed class DocumentSession
{
    public ISceneSnapshotSource SnapshotSource { get; }
    public string? SelectedActorId { get; }
    public bool CanUndo { get; }
    public bool CanRedo { get; }

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<TransformPreviewChangedEventArgs>? PreviewChanged;

    public void SelectActor(string? actorId);
    public bool MoveSelectedActor(Position3 destination);
    public bool RotateSelectedActor(double yawDegrees);
    public bool SetSelectedActorTransform(Position3 position, double yawDegrees);
    public bool Undo();
    public bool Redo();

    public void BeginPreview();
    public void UpdatePreview(Position3 position, double yawDegrees);
    public bool CommitPreview();
    public void CancelPreview();
}
```

명령은 actor ID, keyframe ID, 실행 전 키프레임과 실행 후 키프레임을 보유한다. 위치 명령은 Yaw를 보존하고, 회전 명령은 위치를 보존한다. 숫자 속성 편집은 위치와 Yaw를 한 명령으로 함께 확정할 수 있다.

히스토리 규칙은 다음과 같다.

- 성공한 non-noop 명령만 Undo 스택에 들어간다.
- 새 명령이 성공한 뒤에만 Redo 스택을 비운다.
- Undo는 before 값을 expected=current(after) 조건으로 적용한다.
- Redo는 after 값을 expected=current(before) 조건으로 적용한다.
- 실패한 Execute/Undo/Redo는 두 스택과 문서를 그대로 보존한다.
- 선택 변경·미리보기·미리보기 취소는 Undo 기록과 문서 revision을 만들지 않는다.
- 드래그 중에는 미리보기만 갱신하고 mouse release에서 명령 하나만 실행한다.

선택은 null 또는 현재 문서에 존재하는 actor ID만 허용한다. 알 수 없는 actor 선택은 현재 선택을 유지한 채 제어된 입력 오류로 거부한다.

## 7. snapshot과 미리보기 데이터 흐름

영구 문서 변경 흐름은 다음과 같다.

```text
TopView/Inspector 의도
→ DocumentSession 명령
→ SceneDocument 원자 교체
→ Changed(revision + 1)
→ SceneProjectionController가 snapshot 한 번 생성
→ 동일 SceneSnapshot 인스턴스를 TopView와 WorldView에 각 1회 전달
```

드래그 중 흐름은 다음과 같다.

```text
pointer 이동
→ DocumentSession.UpdatePreview
→ 불변 TransformPreview 한 번 생성
→ 동일 preview 인스턴스를 TopView와 WorldView에 전달
→ 문서와 Undo 스택은 변경하지 않음
```

release에서는 preview의 최종값을 명령 하나로 확정하고 preview를 지운다. Escape 또는 포인터 취소는 preview만 지운다. Preview에는 actor ID, 기준 keyframe ID, Position3, Yaw가 들어가며 저장·직렬화하지 않는다.

초기 문서도 이벤트를 기다리지 않고 표시할 수 있도록 `SceneProjectionController.ProjectCurrent()`를 제공한다. 같은 revision의 중복 변경 이벤트 억제와 명시적 초기 투영은 별도 계약으로 테스트한다.

## 8. 탑뷰 좌표와 입력

탑뷰는 별도 렌더 파이프라인이 필요 없는 Godot `Control`의 `_Draw()`와 `_GuiInput()`으로 구현한다. 좌표 계산은 Godot 없는 `TopViewCoordinateMapper`에 둔다.

좌표 계약은 기존 가이드와 Domain 규칙을 그대로 따른다.

```text
screen.x = panelCenter.x + (worldX - centerX) * pixelsPerUnit
screen.y = panelCenter.y + (worldZ - centerZ) * pixelsPerUnit

worldX = centerX + (screen.x - panelCenter.x) / pixelsPerUnit
worldZ = centerZ + (screen.y - panelCenter.y) / pixelsPerUnit
```

- 화면 오른쪽은 +X다.
- 화면 아래쪽은 +Z다.
- Y는 탑뷰 이동 중 보존한다.
- Yaw 0°는 오른쪽(+X), 90°는 아래(+Z), 180°는 왼쪽, 270°는 위다.
- 선택 반경은 줌과 무관한 화면 픽셀 기준 16px다.
- 이동은 actor body를 3px 이상 끌 때 시작한다.
- 회전은 선택 actor 중심에서 28px 떨어진 방향 handle을 끌어 수행한다.
- 회전각은 `NormalizeDegrees(atan2(deltaZ, deltaX) * 180 / PI)`로 계산한다.

최초 버전은 고정 중심과 고정 배율을 사용한다. 팬·줌·스냅은 후속 기능이며 문서 좌표에 섞지 않는다.

## 9. Godot 화면 구조

기존 네 최상위 패널 이름과 1280×720 기본 분할은 유지한다.

```text
Main : Control
├─ TopViewPanel : Panel
│  └─ TopViewSurface : Control
├─ WorldViewPanel : Panel
│  └─ WorldViewportContainer : SubViewportContainer
│     └─ WorldViewport : SubViewport
│        └─ WorldRoot : Node3D
│           ├─ Camera3D
│           ├─ DirectionalLight3D
│           ├─ Ground : MeshInstance3D
│           └─ Actors : Node3D
├─ TimelinePanel : Panel
└─ InspectorPanel : Panel
   └─ TransformInspector : VBoxContainer
      ├─ SelectedActorLabel : Label
      ├─ XInput / YInput / ZInput : SpinBox
      ├─ YawInput : SpinBox
      ├─ ApplyButton : Button
      ├─ UndoButton : Button
      └─ RedoButton : Button
```

3D actor는 Godot 기본 mesh만 사용한다. actor root에 문서 위치를 적용하고, `VisualRoot`와 향후 `OverlayRoot`를 분리한다. 최초 플레이스홀더의 방향 표시는 로컬 +X를 앞쪽으로 만든다. Domain의 0°=+X·90°=+Z 계약을 Godot Y축 회전에 적용할 때는 `rotationYRadians = -DegToRad(yawDegrees)`를 사용하고 0/90/180/270°를 회귀 테스트한다. 실제 모델의 전방축이 다르면 향후 `VisualRoot`에만 별도 model forward offset을 적용한다.

숫자 입력 범위는 X/Z ±1000, Y ±100, Yaw는 입력 후 `[0,360)` 정규화로 고정한다. SpinBox의 값 변경 자체는 preview로만 보이고 Apply 또는 Enter에서 명령 하나로 확정한다. 잘못된 값은 문서를 바꾸지 않고 Inspector 오류 Label로 표시한다.

## 10. 오류 처리와 원자성

예상 가능한 입력 오류는 UI 경계에서 한글 메시지로 표시하고 앱을 종료하지 않는다.

- 선택 없음: 편집 명령을 실행하지 않는다.
- 없는 actor/keyframe: stale selection 오류로 표시하고 선택을 새 snapshot에 맞춰 갱신한다.
- expected preimage 불일치: 다른 변경이 먼저 적용된 stale command로 보고 문서·히스토리를 보존한다.
- 유한하지 않은 좌표·각도 또는 Inspector 범위 초과: 확정 전에 거부한다.
- Undo/Redo 실패: 스택 항목을 이동하지 않고 오류를 표시한다.
- Godot 표현 노드 오류: Domain을 롤백한 것처럼 가장하지 않고 Presentation 오류로 기록한다.

문서 변경 validation과 새 track 생성은 기존 actor 참조를 바꾸기 전에 모두 끝낸다. 이벤트 소비자 예외는 이미 확정된 문서 변경 이후 발생하는 observer 오류이므로 이번 Domain 원자성의 롤백 범위에 포함하지 않는다.

## 11. 테스트와 검증

테스트는 Windows 공유 `obj` 파일 잠금을 피하기 위해 프로젝트별로 직렬 실행한다.

### Domain 테스트

- 성공 교체가 ID·시간·표시 정보·다른 keyframe·Action/LockOn을 보존한다.
- 성공 시 revision +1, event 1회, snapshot 위치·Yaw가 갱신된다.
- missing actor/keyframe, stale expected, ID/time 변경은 데이터·revision·event를 보존한다.
- no-op은 `false`, revision/event 변화 없음이다.

### Application 테스트

- 선택·해제는 Selection 이벤트만 만들고 문서 revision을 바꾸지 않는다.
- 위치·회전 명령은 수정하지 않은 성분을 보존한다.
- Move→Undo→Redo가 A→B→A→B이며 revision은 단조 증가한다.
- Undo 뒤 새 명령은 Redo를 지운다.
- 실패한 명령·Undo·Redo는 히스토리를 보존한다.
- preview 갱신은 문서·Undo를 바꾸지 않고 commit만 한 번 변경한다.
- 초기 `ProjectCurrent()`와 이후 revision이 동일 snapshot 인스턴스를 두 consumer에 전달한다.

### Editor 순수 계산 테스트

- X/Z 화면 좌표 왕복과 +Z 아래 방향을 검증한다.
- 0/90/180/270° handle 좌표와 포인터 각도를 검증한다.
- hit-test가 16px 경계와 actor/회전 handle 우선순위를 지킨다.
- Domain `Position3`와 Godot `Vector3`, 방향 offset 변환을 검증한다.

### Godot 런타임 검사

기존 표식을 유지하고 실제 Application 명령, Undo, Redo, 두 뷰 적용 횟수와 placeholder 수를 검증한 새 표식을 추가한다.

```text
PROJECT_RUNTIME_READY
PROJECTION_SYNC_READY revision=1 top=1 world=1
BASIC_EDITING_READY revision=4 selected=runtime-actor moved=1 undo=1 redo=1 top=4 world=4 actors=1
```

런타임 smoke 순서는 actor 추가로 revision 1 생성 → 초기 `ProjectCurrent()`로 top/world count 1 → 이동으로 revision/count 2 → Undo로 3 → Redo로 4다. `Test-ProjectSkeleton.ps1`은 Application 프로젝트, 새 소스·테스트와 핵심 Godot 노드를 검사하고 `Test-GodotRuntime.ps1`은 위 marker를 정확히 확인한다.

## 12. 완료 기준

- 탑뷰에서 배우를 선택하고 이동·회전할 수 있다.
- 드래그 중 탑뷰와 3D가 같은 preview를 표시하며 문서 revision은 변하지 않는다.
- 드래그 종료 또는 Inspector Apply에서 의미 변경 1회와 Undo 기록 1개만 생긴다.
- Undo/Redo가 위치·방향과 두 뷰를 함께 되돌리고 다시 적용한다.
- 3D에는 actor ID별 플레이스홀더가 생성·재사용·제거되고 문서 좌표를 반영한다.
- Domain/Application은 Godot에 의존하지 않는다.
- 기존 저장·가져오기·렌더 큐 계약과 60개 테스트가 회귀하지 않는다.
- 모든 새 테스트, 구조 검사, Godot 헤드리스 런타임과 Forward+ GUI 검증이 통과한다.
- 변경은 명시된 경로만 스테이징해 커밋하고 원격 기능 브랜치에 푸시한다.
