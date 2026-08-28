# 09. 성능 전략

## 기준 하드웨어

- Intel Core Ultra 5 225F, 10코어
- 시스템 메모리 약 15.6GB
- NVIDIA GeForce RTX 5060, VRAM 8GB
- Windows 11 Pro
- D 드라이브 여유 공간 약 1.1TB

목표는 이 사양에서 기본 네 캐릭터 교육 장면을 부드럽게 편집하고 안정적으로 고해상도 렌더하는 것이다. 최종 영상 렌더는 실시간보다 느려도 되지만 메모리 초과나 장시간 무응답이 없어야 한다.

## 성능 예산

초기 목표값이며 프로파일링 결과로 조정한다.

| 항목 | 목표 |
| --- | --- |
| 편집 뷰 FPS | 일반 장면 60FPS 목표, 최소 30FPS 유지 |
| 입력 반응 | 드래그·선택 시 50ms 이내 체감 반응 |
| 문서 평가 | 캐릭터 4명 기준 프레임당 2ms 이하 목표 |
| 자동 저장 | UI 정지 없이 백그라운드 직렬화 |
| 메모리 | 앱·자산 합계가 시스템 RAM의 안전 여유를 남김 |
| VRAM | 8GB 중 OS·드라이버 여유를 남기고 단계적 품질 저하 |

## CPU 전략

- 단일 snapshot 평가는 현재 시각의 transform과 Lock-on 방향을 계산한다.
- 전체 궤적 평가는 actor/target transform cursor와 Lock-on cursor를 앞으로만 전진시키며 sample마다 전체 key를 처음부터 다시 검색하지 않는다.
- `TrajectoryEvaluationDiagnostics`는 actor 수, sample 수, transform/Lock-on key 수, canonical anchor 시각과 evaluator `SegmentSteps`를 공개 immutable 값으로 기록한다.
- xUnit의 복잡도 계약은 wall-clock이 아니라 `samples + keys`에 비례하는 hand-derived operation 상한을 사용한다. 느린 PC에서도 절대 시간 때문에 단위 테스트가 실패하지 않는다.
- JSON 파싱, 체크섬, 자산 인덱싱과 렌더 준비를 작업 스레드로 보낸다.
- 메인 스레드로 전달하는 결과는 작은 불변 스냅샷 또는 배치 갱신이다.
- 병렬화 오버헤드가 더 큰 작은 계산에는 작업을 만들지 않는다.

### Projection cache

`SceneProjectionController`는 현재 source에 대해 궤적 cache 항목 하나만 유지한다. cache key는 `(MotionRevision, SamplingPolicyFingerprint)`다.

- seek 또는 정상 playback tick으로 `(revision,time)`의 time이 실제로 달라지면 현재 시각의 `SceneSnapshot`만 다시 평가하고 궤적 geometry는 cache hit로 재사용한다. 반면 같은 시각의 play/pause 상태 전환은 `PlaybackClock.Changed`를 발생시키더라도 `(revision,time)` short-circuit에서 끝나므로 projection과 snapshot 평가 자체를 하지 않는다.
- Action-only 편집: 문서 `Revision`만 증가하고 `MotionRevision`은 유지된다. `MovementTrajectorySet.WithRevision`이 얕은 immutable wrapper만 만들고 actor trajectory collection과 Editor geometry를 그대로 재사용한다.
- actor 추가, transform 또는 Lock-on 편집: `MotionRevision`이 증가하므로 궤적을 다시 만든다.
- `MotionRevision` 또는 sampling fingerprint가 달라지면 기존 한 항목을 새 계산 결과로 교체한다. 현재 controller에는 실행 중 source를 교체하는 API가 없으며, `Dispose`는 event 구독을 해제하고 cache를 명시적으로 비운다.
- projection 중 consumer에서 재진입 요청이 와도 중첩 실행하지 않고 최신 요청 하나로 합친 뒤 현재 `SceneProjectionFrame`을 TopView와 WorldView에 끝까지 전달한다.

현재 마일스톤은 motion 변경 시 대표 4-actor 전체 궤적을 다시 만드는 예외를 허용한다. Action-only reuse까지는 구현됐지만 영향 actor/변경 시간 구간만 다시 평가하는 증분 cache는 아직 없다. 대표 4 actors, actor당 transform/Lock-on key 각각 100개, 10초 fixture의 full rebuild p95가 8ms를 넘으면 이 예외는 즉시 완료 blocker가 된다.

## GPU 전략

- Forward+를 사용하되 조명 수를 제한한다.
- 교육용 기본 장면은 단순 재질, 하나의 주 조명과 약한 환경광으로 구성한다.
- 경로·부채꼴·선은 가능한 한 배치 가능한 단순 메시로 그린다.
- World trajectory는 actor별 `ImmediateMesh` 세 개와 `ShaderMaterial`을 한 번 만든다. `(MotionRevision, fingerprint)` geometry key가 바뀔 때만 surface를 다시 쓰며 playback tick에는 노드나 mesh를 만들지 않는다.
- 현재 시각 변화는 `current_time_normalized` shader uniform만 갱신한다. `UV.x`에 저장된 normalized sample time과 비교해 미래 vertex를 45% 명도로 표시한다.
- 미리보기 3D SubViewport의 해상도 배율을 50/75/100%로 조절한다.
- 그림자 해상도, MSAA, SSAO 등은 품질 프리셋으로 묶고 자동 저하를 명시적으로 알린다.

## 자산 메모리

- 모든 DSR 애니메이션을 시작 시 로드하지 않는다.
- 현재 장면이 참조한 모델·동작과 가까운 다음 동작만 지연 로드한다.
- 썸네일과 분석 인덱스는 실제 HKX/텍스처보다 별도 작은 캐시로 유지한다.
- 동일 자산 참조는 인스턴스 간 공유한다.
- 큰 텍스처는 교육용 장면에 맞춰 변환 캐시 해상도를 제한한다.
- 캐시 상한과 LRU 제거 정책을 설정하고 사용 중인 자산은 제거하지 않는다.

## 두 뷰 최적화

탑뷰는 3D 장면을 위에서 한 번 더 렌더하는 방식보다 2D 전용 표현을 사용한다. 이 방식은 판정 도형과 텍스트가 선명하고 GPU 비용이 낮다. `SceneProjectionController`가 원자적인 `SceneProjectionFrame` 하나를 만들고 TopView와 WorldView에 같은 frame, snapshot과 trajectory 인스턴스를 순서대로 전달한다.

```text
SceneProjectionController
→ SceneProjectionFrame
   ├─ SceneSnapshot: 현재 위치·작성 Yaw·resolved facing·semantic 상태
   └─ MovementTrajectorySet: 전체 shared path와 free/Lock-on facing samples
      ├─ TopView: immutable 2D geometry + selection/time presentation
      └─ WorldView: world-fixed reusable mesh + time uniform
```

TopView도 selection과 현재/미래 명도를 geometry에 굳히지 않는다. 선택 또는 현재 시각만 바뀌면 동일 geometry 참조 위에서 presentation만 다시 만든다. WorldView는 동일 geometry key일 때 actor별 geometry dictionary와 mesh node를 재사용한다.

## 렌더 성능

- 렌더는 실시간 FPS를 목표로 하지 않고 안정성과 재현성을 우선한다.
- 이미지 시퀀스를 순차적으로 기록해 메모리에 전체 프레임을 보관하지 않는다.
- 디스크 쓰기 큐는 메모리 상한을 두고 렌더가 생산자를 과도하게 앞서지 않게 한다.
- 렌더 전에 예상 프레임 수와 대략적 디스크 사용량을 표시한다.
- 인코딩은 렌더 후 별도 단계로 실행해 GPU·CPU 경쟁을 줄인다.

## 프로파일링 절차

1. 기준 샘플 장면과 고부하 합성 장면을 고정한다.
2. Godot 프로파일러로 프레임 시간, 드로콜, 메모리와 GPU 시간을 기록한다.
3. .NET 프로파일러로 할당, GC와 CPU 상위 함수를 확인한다.
4. 변경 전 기준과 변경 후 결과를 같은 설정으로 비교한다.
5. 10% 미만의 변동은 반복 측정하고 잡음과 구분한다.
6. 병목이 증명된 함수만 최적화한다.

C++ GDExtension은 대량 기하 교차나 궤적 생성이 실제 병목이고 C# 최적화·배치로 해결되지 않을 때만 검토한다.

## 성능 회귀 테스트

- 4 actors/actor당 transform·Lock-on key 각각 100개, 10초 구간의 궤적 생성 benchmark
- 16 actors/actor당 transform·Lock-on key 각각 1,000개의 대규모 진단 benchmark
- 두 뷰 갱신 시 프레임 시간 상위 백분위
- 문서 저장·로드 시간과 할당량
- 1080p 60FPS 10초 렌더의 프레임 누락 여부

실행 명령은 다음과 같다.

```powershell
& .\scripts\Measure-TrajectoryPerformance.ps1
```

스크립트는 PowerShell 7과 `D:\3D-render\tools\nuget-packages`를 사용한다. test assembly를 먼저 build한 뒤 `--no-build --no-restore` diagnostic probe를 실행한다. test process 안에서 warm-up 후 실제 `SceneDocument.CreateMovementTrajectories(plan)`과 `CreateSnapshot(time)` 호출만 `Stopwatch`로 감싸므로 build, restore와 test runner 시작 시간은 p95에 포함되지 않는다.

2026-08-28 fresh 실행에서 얻은 진단값은 다음과 같다.

| Fixture | Build p95 | Snapshot p95 | Actors | Samples | Keys | Segment steps | 판정 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 4×100, 10초 | 1.8741ms | 0.0113ms | 4 | 1,588 | 800 | 3,968 | 8ms gate PASS |
| 16×1,000, 10초 | 27.0564ms | 0.3174ms | 16 | 20,752 | 32,000 | 73,472 | 기록 전용 |

대표 marker 형식은 다음과 같다.

```text
TRAJECTORY_PERFORMANCE_RESULT fixture=4x100 build_p95_ms=1.874100 snapshot_p95_ms=0.011300 actors=4 samples=1588 keys=800 segment_steps=3968 ...
TRAJECTORY_PERFORMANCE_GATE=PASS build_p95_ms=1.874100 limit_ms=8.00
```

이 수치는 현재 장비에서 한 번 fresh 실행한 진단값이며 다른 장비의 절대 성능을 보장하지 않는다. 4×100의 8ms gate만 현재 완료 기준으로 사용하고, 16×1,000은 wall-clock으로 xUnit을 실패시키지 않고 수치와 선형 operation count만 기록한다.

대규모 16 actors/1,000 keys 지원을 완료로 선언하기 전에는 영향 actor와 변경 시간 구간만 무효화하는 증분 trajectory cache를 구현해야 한다. 현재 full motion-revision rebuild 예외를 장기 구조로 간주하지 않는다.
