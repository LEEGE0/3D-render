# 11. 테스트와 품질

## 품질 전략

정확한 위치·시간·판정이 핵심이므로 화면 수동 확인만으로 품질을 보장하지 않는다. 도메인 계산, 저장·가져오기와 렌더 시간을 자동 테스트하고, Godot UI와 최종 영상은 헤드리스 스모크 테스트와 기준 이미지·수동 검수를 결합한다.

## 테스트 피라미드

### 도메인 단위 테스트

가장 많고 빠른 테스트다.

- 각도 정규화와 최단 회전
- 위치·회전 보간
- 키프레임 시간 경계
- Lock-on `Continuous`/`Snap`/`KeyframeOnly`, offset wrap와 4방위 방향
- 위치 일치 epsilon의 안·경계·밖, 접선과 왼쪽 극한, target 누락 fallback
- 결정적 trajectory sample plan, anchor flags와 fingerprint
- 자유/Lock-on 방향이 동일 위치·시간 sample을 공유하는지
- evaluator `SegmentSteps`가 sample과 key 규모에 선형인지
- 선분-원 접촉
- 부채꼴 포함과 포함 비율
- 문서 불변 조건
- 명령 Execute/Undo/병합

Godot을 시작하지 않고 `dotnet test`로 실행할 수 있어야 한다.

### 인프라 통합 테스트

- V1 가이드 가져오기
- JSON 저장 왕복
- 버전 마이그레이션
- 원자적 저장 실패 처리
- 자동 저장과 복구 후보
- 자산 카탈로그 누락·대체
- FFmpeg 인자 구성과 종료 코드 처리

테스트는 임시 디렉터리를 사용하고 사용자 실제 DSR 설치·문서를 수정하지 않는다.

### Godot 장면 테스트

- 프로젝트 헤드리스 로드
- 메인 장면 인스턴스 생성
- 탑뷰와 3D가 같은 `SceneProjectionFrame`, snapshot과 trajectory 인스턴스를 받는지 확인
- 패널의 핵심 노드와 입력 연결 존재
- 자산 누락 시 플레이스홀더 사용
- 렌더 장면이 지정 시간으로 평가되는지 확인
- TopView shared path/free tick/Lock-on tick layer와 current/future 명도 확인
- World trajectory가 actor root와 분리된 고정 root를 사용하고 seek에서 mesh node를 재사용하는지 확인
- duration 0에서 sample UV와 shader uniform이 정확히 0인지 확인

### 엔드투엔드 시나리오

고정 샘플을 불러오고 편집·저장·다시 열기·렌더를 수행한다. 실제 게임 자산이 없는 기본 시나리오와 로컬 자산이 있는 선택 시나리오를 분리한다.

## 기준 샘플

사용자 가이드의 전체 원본은 개인 Desktop 경로에 의존하지 않게 저작권 문제가 없는 최소 축약 샘플을 `samples/guides`에 만든다. 다음 특징을 포함해야 한다.

- 네 역할
- 0/360 경계를 넘는 회전
- 락온 ON/OFF와 대상 보존
- 이동이 없는 키프레임
- 공격 시점과 평가 결과
- 한글 장면명·메모
- 0.5초 간격이 아닌 키프레임

실제 `뒤로빼기`와 `전략1` 파일은 로컬 회귀 비교에 사용할 수 있지만 개인 경로를 테스트에 하드코딩하지 않는다.

## 수치 허용 오차

- 좌표 저장 왕복: 가능한 한 정확한 double 왕복
- 위치 계산: 단위와 연산에 맞는 작은 절대·상대 허용 오차
- 각도: 정규화 후 원형 거리로 비교
- 포함 비율: 샘플 기반 구현이면 샘플 간격에 근거한 허용 오차
- 렌더 픽셀: GPU 차이가 가능한 효과는 픽셀 완전 일치 대신 지각 해시·마스크 비교

허용 오차를 테스트 통과를 위해 임의로 넓히지 않고 오차 근거를 주석과 문서에 기록한다.

## TDD 작업 순서

기능 또는 버그 수정은 다음 순서를 기본으로 한다.

1. 가장 작은 실패 테스트를 작성한다.
2. 의도한 이유로 실패하는지 실행 결과를 확인한다.
3. 최소 구현으로 통과시킨다.
4. 관련 전체 테스트를 실행한다.
5. 리팩터링 후 다시 실행한다.
6. Git diff와 요구사항을 검토한다.
7. 확인된 파일만 커밋하고 푸시한다.

회귀 버그는 수정 전 실패, 수정 적용 후 성공, 가능하면 수정 제거 시 다시 실패하는 red-green 근거를 남긴다.

## 정적 품질

- nullable reference type 활성화
- 경고를 가능한 한 오류로 처리
- 코드 포맷 고정
- 공개 API XML 문서 또는 의미 있는 이름
- 엔진 의존성이 Domain으로 역류하는지 프로젝트 참조 검사
- 비밀·대용량 자산·개인 경로 커밋 검사
- JSON 스키마와 샘플 유효성 검사

## 성능·안정성 테스트

- 4 actors/actor당 transform·Lock-on key 각각 100개의 8ms build p95 gate
- 16 actors/actor당 transform·Lock-on key 각각 1,000개의 기록 전용 진단
- wall-clock과 별도로 actor/sample/key/anchor/`SegmentSteps`의 선형 operation 상한
- 반복 Undo/Redo 후 상태 해시 일치
- 저장 중 예외를 주입했을 때 원본 보존
- 렌더 취소·재개와 디스크 부족 시뮬레이션
- 손상 JSON, 거대한 배열, 순환 또는 누락 참조 거부
- 30분 이상 편집 재생의 메모리 증가 추적

성능 xUnit은 장비 속도로 실패하지 않는다. `TrajectoryPerformanceContractTests`는 실제 production result에서 만든 immutable diagnostics로 additive operation 상한을 검증한다. wall-clock gate는 별도 PowerShell 7 스크립트가 담당한다.

```powershell
& .\scripts\Measure-TrajectoryPerformance.ps1
```

대표 4×100 fixture가 8ms를 넘으면 스크립트가 `TRAJECTORY_PERFORMANCE_GATE=FAIL`을 출력하고 nonzero로 끝난다. 16×1,000 fixture는 대규모 증분 cache 설계 판단을 위한 기록이며 절대 시간 gate가 아니다.

## Lock-on 방향·궤적 회귀 범위

이번 마일스톤의 자동 검증은 다음 경계를 명시적으로 포함한다.

- schema는 계속 `pvp-guide-scene/2`이며 facing, trajectory, `MotionRevision`, cache key와 current time 같은 파생 상태를 저장하지 않는다.
- duration 0 문서는 time `0` sample 하나만 만들고 `PlaybackClock`은 재생 상태나 변경 event를 잘못 만들지 않는다.
- target 누락은 finite authored facing으로 fallback하고 TopView/World semantic target 표시를 안전하게 숨긴다.
- Action-only revision은 trajectory actor collection과 Editor geometry를 재사용한다.
- transform/Lock-on motion revision은 trajectory를 정확히 한 번 다시 만든다.
- seek/playback current-time 변경은 cached trajectory를 재사용한다. World는 shader uniform만 바꾸고 mesh surface와 node를 다시 만들지 않는다.
- projection consumer 재진입은 pending 최신 요청으로 직렬화하며 TopView와 WorldView frame 순서를 뒤집지 않는다.
- preview는 committed trajectory를 다시 만들지 않고 actor body와 semantic lock line만 임시 authored transform으로 표시한다.
- 4방위 Domain Yaw와 Godot local `+X` 전방, optional model visual offset의 경계를 순수 테스트한다.

## 현재 자동 테스트 수

2026-08-28 같은 worktree에서 각 테스트 프로젝트를 직렬로 fresh 실행한 결과다.

| 프로젝트 | 통과 | 실패 | 건너뜀 |
| --- | ---: | ---: | ---: |
| `PvpGuide.Domain.Tests` | 86 | 0 | 0 |
| `PvpGuide.Application.Tests` | 129 | 0 | 0 |
| `PvpGuide.Infrastructure.Tests` | 43 | 0 | 0 |
| `PvpGuide.Editor.Tests` | 109 | 0 | 0 |
| 합계 | 367 | 0 | 0 |

테스트 수는 이 시점의 실제 결과이며 기능 추가와 함께 바뀔 수 있다. 문서의 숫자를 고정된 영구 목표로 사용하지 않고, 완료 커밋 직전에 다시 실행해 갱신한다.

## 수동 검수 체크리스트

- 탑뷰·3D 위치와 방향이 같은가
- 한글 텍스트와 경로가 깨지지 않는가
- 색맹 모드에서도 역할과 결과를 구분할 수 있는가
- 드래그, 숫자 입력과 Undo/Redo가 자연스러운가
- 락온 표시와 실제 방향이 일치하는가
- 공격·뒤잡 표시가 모델에 가려지거나 과도하게 번쩍이지 않는가
- 최종 영상의 첫·마지막 프레임과 FPS가 설정과 맞는가

## 완료 전 검증

완료·성공·정상이라고 말하기 전에 해당 주장을 증명하는 최신 명령과 결과를 확인한다. 문서 작업도 예외가 아니다. 최소한 변경 파일, `git diff --check`, 링크 대상, 제외 규칙, 미완성 표식과 staged diff를 확인한다.

Lock-on 방향·궤적 마일스톤의 표준 검증 명령은 다음과 같다. 모든 `dotnet` 명령은 D drive NuGet cache를 명시한다.

```powershell
$env:NUGET_PACKAGES='D:\3D-render\tools\nuget-packages'
dotnet test .\tests\PvpGuide.Domain.Tests\PvpGuide.Domain.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Application.Tests\PvpGuide.Application.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Infrastructure.Tests\PvpGuide.Infrastructure.Tests.csproj -c Debug --nologo
dotnet test .\tests\PvpGuide.Editor.Tests\PvpGuide.Editor.Tests.csproj -c Debug --nologo

pwsh -NoProfile -File .\scripts\Test-ProjectSkeleton.ps1
pwsh -NoProfile -File .\scripts\Measure-TrajectoryPerformance.ps1
pwsh -NoProfile -File .\scripts\Test-GodotRuntime.ps1
```

Skeleton 성공 표식은 다음과 같다.

```text
PROJECT_SKELETON_VERIFICATION=PASS
```

성능 성공 표식은 대표 fixture의 machine-readable result와 8ms gate를 모두 포함한다.

```text
TRAJECTORY_PERFORMANCE_RESULT fixture=4x100 build_p95_ms=1.874100 snapshot_p95_ms=0.011300 actors=4 samples=1588 keys=800 segment_steps=3968 ...
TRAJECTORY_PERFORMANCE_GATE=PASS build_p95_ms=1.874100 limit_ms=8.00
```

Godot runtime에서는 기존 표식을 보존하면서 아래 한 줄 전체를 exact output으로 요구한다.

```text
LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1
```

각 항목은 순서대로 세 tracking mode, 위치 일치 fallback, target 누락, TopView/WorldView shared frame, trajectory 표시, cache 재사용과 Godot node 재사용을 뜻한다. runtime script는 이 줄을 부분 문자열이 아니라 한 줄 전체로 확인한다.
