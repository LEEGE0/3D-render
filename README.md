# 3D Render — DARK SOULS REMASTERED PvP 교육 영상 편집기

Windows 11에서 오프라인으로 실행되는 DARK SOULS REMASTERED PvP 상황 재현·교육 영상 제작 프로그램이다. 일반적인 3D 제작 도구보다 전투 참여자의 위치, 방향, 거리, 타이밍, 락온, 공격과 뒤잡 관계를 빠르고 정확하게 설명하는 데 초점을 둔다.

현재 저장소에는 실행 가능한 Godot 프로젝트 골격, `SceneDocument` 기반 동시 뷰 동기화, 탑뷰 기본 편집, 3D 플레이스홀더, 숫자 Inspector, 공유 Undo/Redo, 시간 스크럽·재생, Transform/Action/Lock-on 세 트랙의 marker·CRUD와 단계 상태 교육 오버레이가 구현되어 있다. 실제 게임 애니메이션 연결, lock-on 방향에 따른 배우 회전, 이동 궤적과 최종 렌더 실행은 후속 마일스톤에서 다룬다.

## 핵심 목표

- 탑뷰와 3D 뷰를 한 화면에 동시에 표시한다.
- 어느 뷰에서 편집하더라도 같은 장면 문서가 갱신되며 다른 뷰에 즉시 반영된다.
- 외부 좌표 파일 가져오기, 직접 편집, 가져온 장면 수정의 세 작업 방식을 모두 제공한다.
- 키프레임 사이 위치·회전을 보간하고 이동 경로를 시각화한다.
- 락온 대상, 행동, 공격 타이밍과 교육용 설명을 시간축에 기록한다.
- 뒤잡시전, 뒤잡각, 접촉과 포함 비율을 계산·표시한다.
- 실제 게임에서 추출한 애니메이션은 사용자가 합법적으로 보유한 로컬 설치에서 선택적으로 연결한다.
- 편집 결과를 프레임 또는 동영상으로 렌더링한다.

## 대상 환경과 기술 선택

| 항목 | 기준 |
| --- | --- |
| 운영체제 | Windows 11 전용 |
| 동작 형태 | 오프라인 독립 실행 데스크톱 프로그램 |
| 엔진 | Godot 4.7.2 Stable .NET |
| 주 언어 | C# |
| 렌더러 | Forward+ |
| 최종 인코딩 | Godot Movie Maker 프레임 출력 + FFmpeg |
| 선택적 자산 도구 | Blender, 검증된 DSR 자산 분석 도구 |
| 기준 PC | Intel Core Ultra 5 225F, RAM 15.6GB, NVIDIA RTX 5060 8GB |
| 작업 루트 | `D:\3D-render` |

Godot을 선택한 이유는 오프라인 배포가 단순하고, 2D UI·3D 장면·SubViewport를 한 애플리케이션에서 결합하기 좋으며, C# 도메인 계층을 엔진 노드와 분리할 수 있기 때문이다. Blender는 편집기 런타임이 아니라 선택적 자산 변환·검수 도구로 사용한다. C++ GDExtension은 처음부터 도입하지 않고 프로파일링으로 병목이 증명된 계산에만 제한한다.

## 편집 개념

```text
외부 가이드/좌표 데이터 ─┐
                          ├─> SceneDocument ─┬─> 탑뷰 투영
사용자 직접 편집 ─────────┘                  ├─> 3D 투영
                                             ├─> 타임라인/속성 패널
                                             ├─> 전투 판정 오버레이
                                             └─> 렌더 작업
```

`SceneDocument`가 유일한 원본 상태다. 탑뷰와 3D 뷰는 서로의 상태를 직접 복사하지 않고 문서를 읽어 그린다. 이동, 회전, 키프레임 추가, 락온 변경은 모두 명령으로 수행해 Undo/Redo와 저장 상태 추적을 일관되게 유지한다.

## 현재 사용할 수 있는 기본 편집과 세 트랙 타임라인

Godot 메인 장면을 실행하면 왼쪽 위 탑뷰, 오른쪽 위 3D 뷰, 아래쪽 타임라인과 Inspector가 동시에 열린다. 현재 기본 편집 흐름은 다음과 같다.

- 탑뷰의 배우 몸체를 클릭하면 배우를 선택한다. 각 표식은 Application의 불변 `ActorDisplayInfo`에서 받은 표시 이름과 `역할: ...` 텍스트를 함께 보여주며, 적대 역할은 마름모 몸체로 구분한다. 빈 공간을 클릭하면 선택을 해제한다.
- 선택한 배우 몸체를 3px 이상 끌면 X/Z 위치 미리보기가 시작된다. 이동 중 Y와 Yaw는 보존된다.
- 배우 앞쪽의 원형 방향 핸들을 끌면 위치를 보존한 채 Yaw를 회전한다. 0°는 +X(화면 오른쪽), 90°는 +Z(화면 아래쪽)다.
- 드래그 중 변화는 탑뷰와 3D에 동시에 보이지만 문서와 Undo 기록에는 아직 저장되지 않는다. 마우스를 놓을 때 명령 하나로 확정되며 `Escape`는 미리보기를 취소한다.
- Inspector의 X/Y/Z/Yaw 값을 바꾸면 같은 비영구 미리보기가 표시된다. `변환 적용` 버튼 또는 SpinBox의 Enter 제출로 한 번에 확정한다.
- Apply/Enter 결과는 실제 변경 없음과 최신 문서 상태 충돌을 구분해 안내한다. 문서 변경은 저장됐지만 후속 알림 처리에서 예외가 나면 적용 실패로 오해하지 않도록 저장 완료와 알림 실패를 함께 알린다.
- `실행 취소`와 `다시 실행` 버튼은 활성 미리보기를 먼저 취소한 뒤 확정된 변환 명령을 되돌리거나 다시 적용한다.
- X/Z ±1000, Y ±100 범위 밖 숫자는 입력칸에서 먼저 받아 오류를 설명하지만 preview나 문서 변경을 시작하지 않는다. 이미 유효한 숫자로 preview 중이었다면 범위 오류 순간 두 뷰를 committed 상태로 복원하되 잘못 입력한 값과 ErrorLabel은 보존한다. 범위를 바로 clamp해 잘못된 입력을 정상값처럼 확정하지 않는다.
- 3D 뷰는 actor ID별 기본 Capsule/Box 플레이스홀더를 재사용하고 로컬 +X 방향 표식을 표시한다. 실제 게임 모델이나 애니메이션이 없어도 편집 흐름을 확인할 수 있다.

### 시간 탐색과 재생

- 시간 slider를 끌거나 클릭하면 재생을 일시정지하고 해당 시각을 즉시 평가한다. 활성 변환 preview가 있었다면 먼저 취소하고, 같은 committed 문서를 그 시각에서 다시 평가해 탑뷰와 3D 뷰에 함께 표시한다.
- `재생`/`일시정지` 버튼과 `Space`는 같은 playback toggle을 사용한다. 문서 끝에 도달하면 끝 시각에 고정되고 자동으로 일시정지한다. 끝에서 다시 재생하면 0초부터 시작한다.
- `처음으로` 버튼은 0초의 일시정지 상태로 돌아간다. 현재 시간과 프레임 표시는 slider와 재생 상태를 따라 갱신된다.
- 재생 중에는 키프레임 생성·수정·삭제와 Inspector Apply, Undo/Redo가 잠긴다. 슬라이더 조작은 먼저 재생을 멈추고 해당 시각을 즉시 평가한다.
- 정지 중에는 선택 배우의 현재 재생 헤드 시각에 정확히 있는 변환 키프레임만 Inspector에서 수정할 수 있다. 현재 시각에 키프레임이 없으면 평가 결과는 볼 수 있지만 transform 편집은 잠긴다.

### 변환 키프레임 CRUD 사용법

1. 탑뷰에서 배우를 선택한다. 타임라인의 마름모 marker를 클릭하면 그 키프레임이 선택되고, 재생은 일시정지하며 재생 헤드는 marker 시각으로 이동한다. Inspector의 `선택된 키프레임`에는 ID와 시간이 함께 표시된다.
2. 새 시각으로 슬라이더를 옮긴 뒤 `키프레임 추가`를 누른다. 선택 배우의 **현재 평가 pose**(위치와 Yaw)가 그 시각의 새 변환 키프레임으로 하나 추가되고, 새 marker와 Inspector가 즉시 그 ID/시간을 선택한다. 이미 같은 시각에 marker가 있으면 추가하지 않는다.
3. 선택 marker의 `Time`, X/Y/Z, Yaw를 바꾸고 `변환 적용` 또는 입력칸 Enter를 누른다. time과 pose는 하나의 원자적 update command로 함께 확정된다. time이 바뀌면 marker도 새 시각으로 이동하고, Inspector는 확정된 시간으로 재생 헤드를 맞춘다. 입력 중 pose preview는 두 뷰에만 보이며, Apply 전에는 문서·revision·Undo/Redo history를 바꾸지 않는다.
4. 선택 marker를 `키프레임 삭제`로 제거한다. 삭제 뒤에는 삭제한 시각에 가장 가까운 marker(같으면 더 이른 시간, 그다음 ID 순)가 선택되고 그 시각으로 이동한다. 배우마다 변환 키프레임은 적어도 하나여야 하므로 마지막 marker는 삭제할 수 없다.
5. `실행 취소`/`다시 실행`은 선택 marker의 수정뿐 아니라 Add, Delete까지 같은 command history에서 왕복한다. Undo/Redo 전에 활성 pose preview는 취소된다. history 전환은 과거 revision 번호를 되돌리는 방식이 아니므로 성공한 각 전환은 새 revision을 만든다.

### Action/Lock-on 트랙 CRUD 사용법

타임라인에는 Transform lane 아래에 Action lane과 Lock-on lane이 있다. 세 lane은 같은 선택 배우와 `PlaybackClock`을 사용하지만 marker ID, 동일 시각 충돌과 Inspector selection은 트랙별로 독립적이다. Action/Lock-on 상태는 보간하지 않고 해당 시각 이하에서 가장 가까운 왼쪽 marker 값을 다음 marker 또는 문서 끝까지 유지한다. 첫 marker 이전에는 Action이 없고 Lock-on은 꺼진 상태다.

1. 탑뷰에서 배우를 선택하고 slider로 정지 시각을 정한다. Action의 `ActionKey`에 의미 키를 입력한 뒤 `Action 추가`를 누르면 `{actorId}-action-{D4}` ID의 marker가 생긴다. Action marker를 클릭하면 재생 헤드가 그 시각으로 이동하고 Action Inspector가 선택 ID/time/key를 표시한다.
2. Action marker를 선택한 뒤 `시각`과 `ActionKey`를 바꾸고 `Action 적용` 또는 ActionKey/Time 입력의 Enter를 제출한다. time과 key는 하나의 command로 함께 바뀐다. 공백 key, 문서 범위 밖 time, 같은 Action track의 동일 시각, stale preimage와 의미상 같은 Apply는 문서/history를 변경하지 않고 Action 오류 label에 이유를 남긴다. `선택 Action 삭제`는 선택 marker 하나를 제거하며 Action track은 비어 있어도 된다.
3. Lock-on을 추가하려면 `Lock-on 활성` 여부, self를 제외한 target actor ID, mode와 Yaw offset을 고른 뒤 `Lock-on 추가`를 누른다. 활성 Lock-on에는 대상이 반드시 필요하다. mode는 `Snap`, `Continuous`, `Keyframe only`를 지원하며 저장 값은 각각 `snap`, `continuous`, `keyframe_only`다. offset은 유한한 각도만 받고 `[-180, 180)`으로 정규화한다.
4. Lock-on marker를 클릭하면 Lock-on Inspector가 ID/time/enabled/target/mode/offset을 committed 값으로 다시 읽는다. 값을 바꾸고 `Lock-on 적용` 또는 Time/offset 입력의 Enter를 제출하면 모든 필드가 하나의 command로 확정된다. `선택 Lock-on 삭제`는 마지막 marker도 삭제할 수 있으며, 삭제 후 첫 marker 이전/빈 track 평가는 Lock-on OFF다.
5. Action/Lock-on Add·Update·Delete는 Transform과 같은 단일 session history를 사용한다. `InspectorPanel/HistoryToolbar`의 `실행 취소`/`다시 실행`은 Transform/Action/Lock-on section 전환과 무관하게 항상 보인다. 선택 actor가 있고 정지 상태라면 active track이나 Transform exact marker 유무와 관계없이 `CanEditHistory`와 실제 Undo/Redo stack으로 활성화되므로, Action/Lock-on marker를 유지한 채 semantic command를 바로 왕복할 수 있다.
6. 재생 중에는 두 semantic Add/Delete, Inspector Apply/Enter와 공유 Undo/Redo가 모두 잠긴다. 남아 있던 signal이 들어와도 `DocumentSession`이 다시 검사하므로 revision, history와 두 projection apply count는 바뀌지 않는다. 정지하면 해당 actor/time/selection 기준으로 가용성을 다시 계산한다.

현재 의미 오버레이는 같은 `(revision,time)`의 단일 `SceneSnapshot`에서 나온다. TopView는 `Apply`/`ApplyPreview` 때 immutable `DisplayedSemanticOverlays`를 만들고 실제 `_Draw()`가 그 동일 read model로 `행동: <ActionKey>` label, 활성 Lock-on의 actor→target line과 target marker를 그린다. 이 production read-only state는 진단과 runtime 검증에도 쓰인다. WorldView는 actor의 `OverlayRoot` 아래 재사용 가능한 `ActionLabel`, `LockBadge`, `LockLine`을 갱신한다. disabled Lock-on이나 Action 없음은 해당 overlay를 숨긴다. 여기서 `Snap`/`Continuous`/`Keyframe only`는 저장·표시되는 의미 모드이며, 아직 배우 Yaw를 target 방향으로 계산하거나 이동 경로를 바꾸지는 않는다.

이 흐름은 Windows 11 오프라인 Godot 실행 환경에서 로컬 문서만 바꾼다. marker를 누르거나 시간을 scrub하고 재생/정지하는 행위는 저장 문서, revision, Undo/Redo history에 들어가지 않는다.

### 오류와 잠금 문구

UI는 잠긴 조작을 성공처럼 보이게 하거나 값을 자동 clamp해 확정하지 않는다. 주요 상태와 메시지는 다음과 같다.

| 상황 | 버튼/입력 상태와 표시 |
| --- | --- |
| 배우를 선택하지 않음 | Add/Delete와 transform 편집이 잠기며 `배우를 선택해야 편집할 수 있습니다`를 사용한다. |
| 재생 중 | Add/Delete/Inspector Apply/Undo/Redo가 잠기며 `재생 중에는 편집할 수 없습니다`를 사용한다. |
| 현재 정지 시각에 선택 marker 없음 | Inspector transform 편집은 `선택한 키프레임 시각에서만 편집할 수 있습니다`, Delete는 `변환 키프레임을 선택해야 편집할 수 있습니다`를 표시한다. |
| 현재 시각에 이미 marker가 있음 | Add는 `현재 시각에는 이미 변환 키프레임이 있습니다`로 잠긴다. |
| 마지막 변환 marker | Delete는 `마지막 변환 키프레임은 삭제할 수 없습니다`로 잠긴다. |
| Apply time이 0~문서 길이 밖, X/Z가 ±1000 밖, Y가 ±100 밖, 또는 유한하지 않음 | commit/preview를 시작하지 않거나 활성 preview를 취소하고 `시각은 0~…초 범위 안이어야 합니다`, `X/Z는 ±1000, Y는 ±100 범위 안이어야 합니다`, 또는 `시각, 좌표와 방향각은 유한한 숫자여야 합니다`를 ErrorLabel에 남긴다. |
| 동일 시각 충돌 또는 stale preimage | Apply는 `선택한 키프레임의 변경이 오래되었거나 같은 시각의 키프레임과 충돌했습니다.`를, timeline Add/Delete는 `키프레임 추가 실패:`/`키프레임 삭제 실패:` 뒤에 최신 lock 이유 또는 `선택한 키프레임 변경이 최신 문서 상태와 충돌했습니다.`를 표시한다. |
| 의미 값이 같은 Apply | `적용할 실제 변환 변경이 없습니다.`를 표시하고 revision/history를 만들지 않는다. |
| ActionKey가 공백 | `ActionKey는 공백일 수 없습니다.`를 표시하고 Action command를 만들지 않는다. |
| 활성 Lock-on target이 없음/self/문서에 없음 | UI는 `활성 Lock-on에는 대상 배우가 필요합니다.`를 표시하고, Domain은 다른 actor의 안정 ID가 아닌 target을 거부한다. |
| Action/Lock-on 동일 시각·stale·no-op | track별 충돌/오래된 변경 또는 `적용할 실제 ... 변경이 없습니다.`를 표시하고 revision/history를 보존한다. |

stale preimage란 조작을 시작할 때 잡은 ID·time·position·정규화된 Yaw가 다른 변경으로 이미 달라진 경우다. 이 경우 command는 부분 변경 없이 `Conflict`가 되며 외부의 최신 committed 값은 보존된다.

Transform/Action/Lock-on keyframe Add/Update/Delete, marker 선택, 단계 상태와 교육 overlay까지 제공한다. 실제 DSR 애니메이션 재생, Lock-on 방향 계산, Lock-on과 자유 방향 이동 궤적, 영상 렌더 실행과 게임패드 조작은 아직 제공하지 않는다. 다음 구현 단위는 Lock-on 방향 계산과 이동 궤적이다.

### 기본 편집과 타임라인 실행·검증

저장소 루트에서 메인 장면을 실행한다.

```powershell
& 'D:\3D-render\tools\godot\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --path .\src\PvpGuide.Editor --scene res://Scenes/Main/Main.tscn
```

전체 자동 검증은 Windows 공유 `obj` 파일 잠금을 피하도록 반드시 다음 순서대로 직렬 실행한다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
```

런타임 검증은 기존 준비·동시 투영 표식과 함께 다음 기본 편집 표식을 정확히 요구한다.

```text
BASIC_EDITING_READY revision=4 selected=runtime-actor moved=1 undo=1 redo=1 top=4 world=4 actors=1
```

이 표식 전에는 탑뷰 회전 preview와 Escape 복원, 3px 이상 body drag 확정, 실제 Undo/Redo 버튼, Inspector 범위 거부와 no-op Apply를 통과해야만 출력되는 다음 통합 표식이 있어야 한다.

```text
BASIC_EDITING_INTEGRATION_READY rotation_preview=1 escape_restore=1 drag_commit=1 undo_button=1 redo_button=1 inspector_reject=1 invalid_preview_cancel=1 stale_error_clear=1 inspector_apply_noop=1 collision_nodes=1 final_ui_clean=1 rotation_commit=1 enter_commit=1 removal_ownership=1
```

두 기본 편집 표식 뒤에는 실제 `HSlider.ValueChanged`, Play/Pause·Stop 버튼 signal, Main의 `Space` 입력 경로와 결정적 `PlaybackClock.Advance()`를 실행한 다음 표식이 정확히 있어야 한다. 이 검사는 t=0 `(1,0,0), 0°`와 t=1 `(5,2,-4), 90°` 사이의 hand-derived 0.5초 값 `(3,1,-2), 45°`, preview 취소, 두 뷰 동기화, read-only 잠금, revision/history/keyframe 불변, 끝 clamp·자동 pause와 0초 복귀를 함께 증명한다.

```text
TIMELINE_PLAYBACK_READY scrub_midpoint=1 top_world_sync=1 revision_unchanged=1 history_unchanged=1 preview_cancel=1 edit_guard=1 play_button=1 space_toggle=1 end_clamp=1 stop_restore=1
```

CRUD 통합 검사는 marker click, Add, time/pose Apply, Delete와 실제 Undo/Redo 버튼, 중복/범위/마지막 marker/stale preimage 거부, preview 취소와 playback lock을 모두 통과한 뒤에만 다음 표식을 출력한다.

```text
TIMELINE_KEYFRAME_CRUD_READY add=1 update=1 time_move=1 delete=1 undo=1 redo=1 duplicate_reject=1 range_reject=1 min_keyframe_guard=1 stale_conflict=1 selection_sync=1 preview_cancel=1 playback_lock=1
```

마지막 Action/Lock-on probe는 기존 transform probe의 최종 actor 상태를 보존하고 target actor 하나를 추가한다. 실제 Action/Lock-on Add/Delete/Apply 버튼, SpinBox `ValueChanged`, LineEdit `TextSubmitted`, OptionButton `ItemSelected`, 두 semantic track과 slider의 viewport mouse input, 항상 보이는 global Undo/Redo 버튼을 사용한다. Action/Lock 활성 상태를 유지한 Undo/Redo, 각 mutation의 hand-derived revision/history, marker ID/time, TopView/WorldView apply count, 0.75초 left-hold snapshot, TopView가 실제 소비한 `DisplayedSemanticOverlays`와 WorldView overlay node의 text/visibility를 검증한다. 이어 paused에서 각 조작이 가능함을 먼저 확인하고 재생 중 Action/Lock Add·Apply·Delete와 global Undo/Redo 실제 signal이 모두 no-op인지 별도 exact marker로 증명한다. 마지막에는 실제 surface mouse signal로 0.75초 Action → 0.2초 Lock-on → 0.75초 Action marker를 왕복해 cross-time active track, selection/time과 Inspector visibility를 확인한다. 이어 slider로 0.2초를 선택하고 실제 Action Add signal로 Lock-on과 같은 시각 marker를 만든 뒤 Lock-on → Action surface를 왕복해 seek 없이도 target Inspector만 표시되는지 확인한다. 최종 hand-derived 상태는 revision/history `29/13`, Top/World apply count `57/57`이다. schema migration은 Editor가 Infrastructure를 참조하지 않도록 이 marker에 포함하지 않는다.

```text
ACTION_LOCK_ON_TRACK_READY action_crud=1 lock_crud=1 step_eval=1 selection_sync=1 undo_redo=1 playback_lock=1 top_overlay=1 world_overlay=1
ACTION_LOCK_ON_PLAYBACK_GUARDS_READY action_add=1 action_apply=1 action_delete=1 lock_add=1 lock_apply=1 lock_delete=1 undo=1 redo=1
```

## 기준 좌표와 뒤잡 규칙

기존 `gangqueen-topview-guide-v1` 샘플은 다음 좌표계를 사용한다.

- X축: 오른쪽이 양수
- Y축: 아래쪽이 양수
- 방향각: 0° 오른쪽, 90° 아래, 180° 왼쪽, 270° 위
- 탑뷰 1픽셀의 실제 3D 거리 비율은 가져오기 설정으로 명시하며 임의로 고정하지 않는다.

현재 교육용 뒤잡 시각화의 기본값은 다음과 같다. 이는 DSR 내부 판정을 완전히 복제했다는 뜻이 아니며 프로젝트 문서에서 보정 가능한 규칙으로 취급한다.

- 뒤잡각: 대상 후방 정중앙 기준 좌우 55°, 총 110°
- 뒤잡시전 길이: 샘플 기준 캐릭터 지름 36px
- 유효 포함 비율: 선분의 60% 이상
- 접촉: 대상 충돌원 접촉 필요
- 성공식: `inside_ratio >= 0.60 AND cast_segment_contacts_target_circle`

## 저장소 구조

```text
D:\3D-render
├─ AGENTS.md                 작업·검증·커밋·정상 버전 태그 규칙
├─ README.md                 프로젝트 입문 문서
├─ docs\                     설계, 운영, 조사, 구현 계획
├─ src\                      실행 가능한 애플리케이션 코드
├─ tests\                    Domain·Application·Infrastructure·Editor 자동화 테스트
├─ local-assets\             로컬 게임 자산과 추출 결과, Git 제외
├─ tools\                    Godot/FFmpeg/자산 도구, Git 제외
├─ cache\                    재생성 가능한 캐시, Git 제외
└─ exports\                  렌더·배포 출력, Git 제외
```

## 로컬 자산 정책

게임 파일은 저작권과 용량 때문에 저장소 또는 배포본에 포함하지 않는다. 원본 Steam 경로를 수정하지 않으며 필요한 파일은 `local-assets/`에 복사한 후 사본만 읽는다. 프로그램은 게임 자산이 없어도 자체 플레이스홀더 모델로 실행되어야 하며, 실제 애니메이션 연결은 사용자가 직접 선택한 로컬 설치에 대한 선택 기능이다.

현재 조사에서 `c0000.anibnd.dcx`, `c0000_a00_lo.anibnd.dcx`, `c0000_a0x.anibnd.dcx` 등에서 스켈레톤, TAE와 수백 개의 HKX 애니메이션 클립이 확인됐다. 정확한 이동·공격·뒤잡 ID는 시각 검증 후 의미 기반 카탈로그로 확정한다. 자세한 내용은 [DSR 애니메이션 자산 조사](docs/research/dsr-animation-assets.md)를 참고한다.

## 개발 시작 전 준비

1. Windows 11에서 저장소를 `D:\3D-render`에 둔다.
2. Git 사용자 정보를 확인한다.
3. Godot 4.7.2 Stable .NET을 다음 콘솔 실행 파일 경로에 설치·확인한다: `D:\3D-render\tools\godot\4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`. 호환 .NET SDK와 D 드라이브의 로컬 NuGet 패키지 캐시도 `D:\3D-render\tools` 아래에 준비한다.
4. FFmpeg 정식 빌드의 버전과 체크섬을 기록하고 `tools/`에 둔다.
5. 실제 게임 자산이 필요하면 Steam 설치 경로를 읽기 전용 입력으로 선택한다.
6. `local-assets/`, `tools/`, `cache/`, `exports/`가 Git에서 제외되는지 확인한다.

### Task 3 프로젝트 골격 개발·검증

Godot 프로젝트 골격과 Domain 테스트를 개발하거나 검증할 때 저장소 루트(`D:\3D-render`)에서 다음 명령을 순서대로 실행한다.

```powershell
& .\scripts\Test-ProjectSkeleton.ps1
$env:NUGET_PACKAGES = 'D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
& .\scripts\Test-GodotRuntime.ps1
```

`Test-ProjectSkeleton.ps1`은 `.sln`, `project.godot`, 메인 장면·스크립트, Domain/Application/Editor의 playback·세 track 파일, 테스트 소스와 사용자·아키텍처·roadmap 문서의 존재, C#·Forward Plus·`Godot.NET.Sdk/4.7.2`·`net8.0` 설정, 네 패널과 timeline control 같은 구조를 검사한다. 문서 내용의 의미적 정확성은 자동화된 사람용 문장 grep 대상이 아니며 사람이 리뷰에서 직접 확인한다. `Test-GodotRuntime.ps1`은 위의 D 드라이브 Godot 콘솔을 사용해 .NET 빌드, 리소스 import, Godot 솔루션 빌드, 두 startup failure probe와 메인 장면 실행을 차례로 수행한다. 모든 명령은 종료 코드 0이어야 하며, xUnit 테스트 실패는 0개, 장면 출력에는 기존 exact marker와 `TIMELINE_PLAYBACK_READY ...`, `TIMELINE_KEYFRAME_CRUD_READY ...`, `ACTION_LOCK_ON_TRACK_READY ...`, 최종 출력에는 `GODOT_RUNTIME_VERIFICATION=PASS`가 있어야 한다.

### Task 4 SceneDocument와 동시 뷰 투영 개발·검증

Task 4의 Domain은 Godot 타입에 의존하지 않는 `SceneDocument`를 단일 진실 공급원으로 사용한다. 가이드 좌표는 x축 오른쪽 양수·y축 아래쪽 양수이며, 내부 3D에서는 guide x를 world X로, guide y를 world Z로 매핑하고 world Y는 높이로 사용한다. 방향각은 0° 오른쪽·90° 아래·180° 왼쪽·270° 위다.

- `Position3`은 유한한 `double X/Y/Z` 성분을 가진다.
- `TransformKeyframe`은 비어 있지 않은 ID, 유한하고 0 이상인 시간, 유한한 위치와 yaw를 요구하며 yaw를 `[0, 360)`으로 정규화한다.
- `ActorTrack`은 안정적인 배우 ID와 시간 오름차순의 읽기 전용 키프레임 목록을 가지며, 동일한 정확한 시간의 키프레임은 거부한다. 빈 트랙 평가는 허용하지 않는다.
- 위치는 선형 보간하고 yaw는 0/360 경계에서 최단 경로로 보간한다. 정확히 180°가 동률이면 양의 방향을 선택한다. 첫 키 이전은 첫 상태, 마지막 키 이후는 마지막 상태다.
- `SceneDocument`는 현재 `pvp-guide-scene/2` 스키마, 문서 ID, 길이, FPS, 고유 배우와 Transform/Action/Lock-on track, monotonic revision을 소유한다. 문서 길이와 평가 시간은 유한하고 범위 안이어야 한다.
- 성공한 배우·키프레임 추가는 revision을 정확히 1 올리고 변경 이벤트를 정확히 한 번 발생시킨다. 실패한 변경은 revision·이벤트·기존 데이터를 바꾸지 않는다. 선택 배우·현재 시간·활성 도구 등 세션 상태와 Godot `Node`·`Vector*`·`Resource`는 Domain에 넣지 않는다.
- `CreateSnapshot(timeSeconds)`는 문서 ID, revision, 평가 시간과 배우별 평가 변환을 불변·방어 복사 형태의 `SceneSnapshot`으로 반환한다.
- `ISceneProjectionConsumer.Apply(SceneSnapshot)`은 Godot 타입이 없는 포트다. `SceneProjectionController`는 하나의 snapshot source와 서로 다른 top/world consumer를 주입받고, 문서 변경 event 1회당 snapshot을 한 번만 만들어 동일 인스턴스를 두 소비자에게 각각 한 번 전달한다. 같은 revision 이벤트는 중복 전달하지 않으며 Dispose 이후에는 전달하지 않는다.
- Main 장면은 `TopViewSurface`와 `WorldViewProjectionAdapter`를 실제 소비자로 조립한다. 초기 투영 뒤 `PROJECTION_SYNC_READY revision=1 top=1 world=1`, Move→Undo→Redo 뒤 `BASIC_EDITING_READY revision=4 selected=runtime-actor moved=1 undo=1 redo=1 top=4 world=4 actors=1`을 출력한다.
- `DocumentSession`이 소유한 `PlaybackClock`은 현재 시각과 재생 여부를 관리하고, `SceneProjectionController`는 `(revision, time)` 조합이 바뀔 때만 두 소비자에 같은 평가 snapshot을 적용한다. 시간 변경은 revision이나 Undo/Redo history가 아니다.
- `TimelineController`는 slider, Play/Pause·Stop 버튼과 표시 label을 playback에 연결하며 Main의 `Space` 입력도 같은 toggle API를 호출한다. `TransformTrackSurface` marker click, Add/Delete 버튼도 session CRUD API에 연결하고, Inspector는 selected keyframe의 Time/pose를 원자적으로 Apply한다.
- committed/preview 투영 조정자, Inspector와 탑뷰 선택 event 구독은 `_ExitTree`에서 모두 해제한다.

Domain과 Editor의 계약을 함께 검증할 때 저장소 루트(`D:\3D-render`)에서 다음 명령을 실행한다.

```powershell
$env:NUGET_PACKAGES = 'D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
```

Domain·Editor 테스트 실패 0, 구조 검사 PASS, `PROJECT_RUNTIME_READY`, `PROJECTION_SYNC_READY revision=1 top=1 world=1`, `BASIC_EDITING_*`, exact `TIMELINE_PLAYBACK_READY ...`, `TIMELINE_KEYFRAME_CRUD_READY ...`, `ACTION_LOCK_ON_TRACK_READY ...`, `GODOT_RUNTIME_VERIFICATION=PASS`가 모두 필요하다.

### Task 5 저장·가이드 가져오기·렌더링 기반

Task 5는 저작권 없는 합성 `gangqueen-topview-guide-v1` fixture를 `SceneDocument`로 가져오고, 문서를 버전형 JSON으로 안전하게 저장하며, Godot/FFmpeg를 직접 실행하지 않는 렌더 작업 큐를 제공한다. fixture는 `samples/guides/synthetic-topview-v1.scene.json`에 두고 `format`, `coordinate_system`, `backstab_rules`, `scene`, `evaluations`와 알 수 없는 원본 필드를 보존한다. 네 역할(`host`, `invader`, `phantom1`, `phantom2`)과 t=`0.25`, `0.9`, `1.4` 키프레임, `lock_on`/`target` 및 `current_index` 선택 힌트(문서 의미 데이터에는 미저장)를 검증한다. 가져오기 설정은 origin `(100,200)`, scale `0.1`, ground height `0`, FPS `30`이며 guide x/y를 world X/Z로 변환한다.

저장 포맷은 현재 `pvp-guide-scene/2`인 버전형 camelCase JSON이다. v2는 각 Lock-on frame의 `yawOffsetDegrees`와 `trackingMode`를 필수로 기록한다. serializer는 기존 `/1`을 읽을 때 빠진 offset을 `0`, mode를 `continuous`로 메모리 migration하고 다시 저장할 때 `/2`로 쓴다. `System.Text.Json` DTO로 indented UTF-8과 strict numbers를 사용하고 알 수 없는 문서 멤버는 거부한다. revision/event/current time과 actor/세 track selection·UI·Godot 상태는 저장하지 않는다. `SaveAtomicAsync`는 절대 경로, `.pvpscene.json` 확장자, 존재하는 부모를 확인한 뒤 같은 디렉터리의 고유 임시 파일에 flush하고 다시 Deserialize 검증 후 원자적으로 교체한다. 실패·취소 시 기존 파일 바이트를 보존하고 임시 파일을 정리하며, 교체 실패 시 검증된 임시 파일을 복구용으로 남긴다.

serializer는 필수 구조 멤버가 null이거나 중첩 배열·객체 항목이 null인 JSON도 경로를 포함한 구조 오류로 거부한다. importer는 중첩 객체의 알 수 없는 멤버를 warning으로 알리면서 raw source metadata에 보존하고, 원자 저장은 임시 파일 검증 직후 move 직전에 취소를 다시 확인해 기존 destination을 보호한다.

테스트 임시 데이터는 반드시 `D:\3D-render\cache\tests\<guid>` 아래에만 만들고, exact root를 검증한 뒤 정리한다. `RenderQueue`는 `D:\3D-render` 하위 출력 경로, 문서 ID/revision, 해상도·FPS, decimal `[start,end)`를 검증하고 `FrameCount=ceil((end-start)*fps)`, `GetTimeSeconds(n)=start+n/fps`를 사용한다. 기본 패턴은 `frame_%06d.png`, 시작 번호는 1이며 FFmpeg `.exe` 절대 경로와 방어적 복사한 인자 배열(`-n` 포함)을 보관한다. 셸 문자열이나 수동 quoting은 사용하지 않고 실제 Godot/FFmpeg 프로세스도 실행하지 않는다.

Task 5를 저장소 루트(`D:\3D-render`)에서 검증하는 정확한 명령은 다음과 같다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo
& .\scripts\Test-ProjectSkeleton.ps1
& .\scripts\Test-GodotRuntime.ps1
```

세 테스트 프로젝트와 두 구조·런타임 검사 스크립트가 모두 종료 코드 0이어야 하며, importer·roundtrip·atomic save·RenderQueue 검증 실패가 없어야 한다.

## Git 작업 방식

- 기능 브랜치에서 작업한다.
- 작업 단위가 끝나면 최신 검증을 실행한다.
- 확인된 경로만 명시적으로 스테이징한다.
- 커밋 후 현재 브랜치를 GitHub에 푸시한다.
- 사용자가 특정 기능의 정상 동작을 보고하면 어떤 시나리오가 정상인지 재확인한 뒤, 긍정 응답이 왔을 때만 `working/<기능>-YYYYMMDD-HHmm` 주석 태그를 만들고 푸시한다.

세부 규칙은 [AGENTS.md](AGENTS.md)와 [Git 및 릴리스 정책](docs/14-git-release-policy.md)을 따른다.

## 문서 색인

1. [프로젝트 개요](docs/01-project-overview.md)
2. [요구사항](docs/02-requirements.md)
3. [시스템 아키텍처](docs/03-system-architecture.md)
4. [데이터 아키텍처](docs/04-data-architecture.md)
5. [편집기 아키텍처](docs/05-editor-architecture.md)
6. [전투 시각화와 뒤잡 판정](docs/06-combat-visualization.md)
7. [렌더링 파이프라인](docs/07-rendering-pipeline.md)
8. [네트워크 아키텍처](docs/08-network-architecture.md)
9. [성능 전략](docs/09-performance-strategy.md)
10. [저장소와 디렉터리 구성](docs/10-storage-and-directory-layout.md)
11. [테스트와 품질](docs/11-testing-and-quality.md)
12. [오류 처리와 복구](docs/12-error-handling-and-recovery.md)
13. [개발 로드맵](docs/13-roadmap.md)
14. [Git 및 릴리스 정책](docs/14-git-release-policy.md)
15. [전체 설계 명세](docs/superpowers/specs/2026-08-27-3d-pvp-editor-architecture-design.md)
16. [구현 계획](docs/superpowers/plans/2026-08-27-project-bootstrap.md)

## 현재 상태

- GitHub 원격 저장소 연결 및 SSH 푸시 확인
- D 드라이브 중심 로컬 작업 구조 확정
- 게임 원본/추출 자산 및 도구 Git 제외
- DSR 플레이어 애니메이션 번들 존재와 일부 추출 확인
- 전체 아키텍처와 단계별 구현 계획 문서화
- Godot .NET 프로젝트 골격과 Domain/Application/Infrastructure 계층 구현
- 탑뷰 선택·이동·회전, 동일 미리보기의 3D 투영, 숫자 Inspector와 Undo/Redo 구현
- 타임라인 3A/3B: slider 스크럽, Play/Pause·Stop·Space, `(revision,time)` 동시 투영, transform marker 선택 및 Add/Time·pose Apply/Delete CRUD·Undo/Redo 구현
- 타임라인 Action/Lock-on foundation: v2 저장 migration/round-trip, step evaluation, marker·구간 lane, semantic Inspector CRUD, 공유 history, playback lock과 TopView/WorldView 교육 overlay 구현
- 다음 구현 단위: Lock-on 방향 계산과 Lock-on/자유 방향 이동 궤적

## 법적·운영 주의

이 프로젝트는 교육용 상황 재현 편집기이며 FromSoftware 또는 Bandai Namco의 공식 제품이 아니다. DARK SOULS 및 관련 자산의 권리는 각 권리자에게 있다. 사용자는 자신이 합법적으로 보유한 설치 파일만 로컬에서 사용해야 하며, 추출 자산을 저장소·릴리스·영상 원본 패키지에 재배포해서는 안 된다.
