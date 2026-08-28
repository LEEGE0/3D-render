# 02. 요구사항

## 기능 요구사항

### 장면과 프로젝트

- 새 장면을 만들고 이름과 설명을 편집할 수 있어야 한다.
- 장면을 버전이 있는 JSON 형식으로 저장하고 다시 열 수 있어야 한다.
- 현재 저장 schema는 `pvp-guide-scene/2`를 유지한다. `/2`가 이미 Lock-on 재평가에 필요한 `yawOffsetDegrees`와 `trackingMode`를 저장하며, facing·trajectory·MotionRevision·cache key·current time은 문서 의미가 아닌 파생 런타임 상태이므로 저장하거나 schema 변경의 이유로 삼지 않는다.
- 자동 저장과 복구 사본을 제공하되 사용자가 저장한 파일을 무조건 덮어쓰지 않아야 한다.
- 최근 문서 목록은 로컬에만 저장한다.
- 문서가 변경됐는지 표시하고 종료 전에 저장 여부를 묻는다.

### 데이터 가져오기

- `gangqueen-topview-guide-v1` 형식의 `scene.json`을 지원한다.
- `host`, `invader`, `phantom1`, `phantom2`를 기본 역할로 가져오되 임의 배우 ID도 허용한다.
- 원본의 X, Y, 방향각, 락온, 대상, 행동, 메모와 평가 결과를 손실 없이 가져온다.
- 탑뷰 단위에서 3D 미터로 변환하는 배율과 원점을 가져오기 설정에 기록한다.
- 알 수 없는 필드는 경고와 함께 보존 가능한 확장 데이터로 저장한다.
- 잘못된 키프레임 하나 때문에 원본 파일을 수정하지 않는다.

### 탑뷰와 3D 편집

- 두 뷰를 동시에 표시하고 분할 비율을 조절할 수 있어야 한다.
- 배우 선택, 이동, 회전, 복제와 삭제를 지원한다.
- 탑뷰는 X/Z 평면으로 표시하고 기존 가이드의 아래쪽 양수 Y를 3D의 +Z로 매핑한다.
- 3D의 높이 Y는 별도 값으로 두며 탑뷰 입력의 기본 높이는 0이다.
- 그리드 스냅과 자유 이동을 전환할 수 있어야 한다.
- 어느 뷰에서 편집해도 동일한 명령 기록과 Undo/Redo를 사용한다.

### 시간축과 키프레임

- 현재 시간, 재생 범위, FPS, 전체 길이를 편집할 수 있어야 한다.
- 위치, 방향, 행동, 락온, 카메라와 오버레이를 트랙으로 표시한다.
- 키프레임 추가·이동·복제·삭제를 지원한다.
- 타임라인 확대·스크롤과 프레임/키프레임/구간 스냅은 우선순위 검토를 마친 별도 후속 요구사항이며 현재 완료 범위로 표시하지 않는다.
- 같은 트랙과 시간에 충돌하는 키프레임의 처리 규칙을 명확히 한다.
- 위치는 기본 선형 보간, 방향은 최단 각도 보간을 제공한다.
- 단계형 상태인 행동·락온 대상은 명시된 전환 시점까지 이전 값을 유지한다.

### 락온

- 배우별로 락온 ON/OFF와 대상 ID를 저장한다.
- 락온 ON이고 대상이 존재하면 수평면에서 대상을 향하는 방향을 계산한다.
- 락온 대상이 삭제되거나 같은 배우를 가리키면 문서 검증 오류로 표시한다.
- 사용자가 락온 자동 방향을 일시적으로 굽는 연출이 필요할 경우 별도의 방향 오프셋을 둘 수 있다.
- `Snap`은 활성 Lock-on 키프레임 시각에 계산한 target 방향과 offset을 다음 Lock-on 키프레임까지 유지해야 한다.
- `Continuous`는 현재 actor/target 위치에서 방향을 계속 평가해야 한다. X/Z 상대 거리 `1e-6` 이하의 위치 일치에서는 같은 구간의 이전 유효 방향을 유지하고, 이전 방향도 없으면 authored Yaw로 fallback해야 한다.
- `KeyframeOnly`는 target 자동 방향을 적용하지 않고 Transform track의 authored Yaw를 유지해야 한다.
- 비정상·외부 평가 입력에서 target이 없더라도 NaN이나 임의 방향을 만들지 않고 authored Yaw와 명시적 missing-target 진단으로 fallback해야 한다. 정상 문서 mutation은 활성 Lock-on의 self/missing target을 계속 거부한다.

### 행동과 애니메이션

- 최소 행동은 `idle`, `move`, `attack`, `roll`, `backstab_attacker`, `backstab_victim`, `custom`이다.
- 행동 키는 의미 기반 이름을 사용하고 실제 HKX ID는 자산 카탈로그에서 매핑한다.
- 실제 자산이 없거나 매핑에 실패하면 플레이스홀더 애니메이션으로 대체한다.
- 뒤잡 공격자·피격자 애니메이션은 같은 동기화 그룹과 기준 시점을 가져야 한다.

### 전투 시각화

- 공격 시 빨간 칼 두 개가 교차하는 X 표시 또는 동등한 3D 표시를 제공한다.
- 배우 정면 방향과 락온 선을 선택적으로 표시한다.
- 키프레임 사이 이동 경로와 시간 마커를 표시한다.
- 자유 방향과 Lock-on 방향 궤적은 같은 Transform 위치 경로와 같은 sample time을 공유해야 한다. 두 위치 경로를 따로 만들어 움직임이 달라진 것처럼 표시하지 않고 authored/free Yaw 표식과 Lock-on-resolved Yaw 표식만 분리한다.
- TopView와 WorldView는 동일한 projection frame과 동일한 ordered trajectory sample을 사용해야 한다. 반복 seek와 actor preview는 world-fixed 궤적 geometry/node/resource identity를 불필요하게 다시 만들지 않아야 한다.
- 뒤잡시전 선분, 유효 60% 구간, 대상 후방 부채꼴과 충돌원을 표시한다.
- 성공 여부뿐 아니라 포함 비율과 접촉 여부를 설명 패널에 표시한다.
- 교육용 규칙값은 문서 단위로 저장하고 기본값으로 되돌릴 수 있어야 한다.

### 카메라와 렌더링

- 탑뷰, 자유 3D, 대상 추적과 고정 프리셋 카메라를 지원한다.
- 미리보기 해상도와 최종 렌더 해상도를 분리한다.
- 렌더 시 현재 UI 프레임률이 아니라 지정 FPS의 결정적 시간 샘플을 사용한다.
- 이미지 시퀀스를 기본 산출물로 만들고 FFmpeg를 통해 MP4 등으로 인코딩한다.
- 인코딩 실패 시 이미지 시퀀스를 보존해 재시도할 수 있어야 한다.

## 비기능 요구사항

### 플랫폼

- Windows 11 x64에서만 공식 지원한다.
- 인터넷 연결 없이 신규·기존 문서 편집과 영상 렌더링이 가능해야 한다.
- 관련 프로그램과 대용량 데이터를 D 드라이브에서 관리한다.

### 성능

- 기준 PC에서 캐릭터 4명, 궤적·판정 오버레이와 두 뷰가 있는 편집 장면을 60FPS 목표로 조작한다.
- 대표 Domain fixture는 `4 actors × actor당 100 transform + 100 Lock-on key`, duration 10초다. production trajectory build를 warm-up 뒤 여러 번 측정한 p95의 임시 완료 gate는 `8ms`이며 fresh 기준값은 `1.8741ms`다.
- `16 actors × actor당 1,000 transform + 1,000 Lock-on key` fresh build p95 `27.0564ms`는 현재 장비의 기록용 진단값이며 xUnit 또는 완료 wall-clock gate가 아니다.
- 일반 편집 중 VRAM 8GB와 시스템 RAM 약 16GB를 넘지 않도록 자산 스트리밍·LOD·캐시를 제한한다.
- 3D 미리보기는 품질을 단계적으로 낮출 수 있어야 하며 최종 렌더 품질과 분리한다.
- 도메인 계산은 렌더 프레임과 분리한다. 현재 cache는 Action-only revision에서 trajectory payload를 재사용하고 Transform/Lock-on motion 변경에서 전체 trajectory를 다시 만든다.
- 대규모 actor/key 규모를 제품 완료 범위로 올리기 전 actor별·변경 구간별 증분 trajectory cache를 구현하고 같은 deterministic operation-count 계약과 실제 p95 측정으로 검증해야 한다.

### 안정성

- 저장은 임시 파일에 완전히 쓴 뒤 검증하고 원자적으로 교체한다.
- 가져오기·자산 변환·렌더링은 취소할 수 있어야 하며 부분 산출물을 구분한다.
- 예외로 프로그램 전체가 종료되지 않게 작업 단위 경계를 둔다.
- 사용자 문서에 손상을 줄 가능성이 있는 자동 복구는 원본 대신 새 복구 파일을 만든다.

### 접근성과 사용성

- 주요 기능은 마우스와 키보드 모두로 접근할 수 있어야 한다.
- 색만으로 역할·성공·오류를 구분하지 않고 모양·아이콘·텍스트를 함께 사용한다.
- 각도·거리·시간은 숫자 입력과 화면 조작을 모두 제공한다.
- 위험한 작업에는 대상 경로와 결과를 명확히 보여준다.

### 보안과 개인정보

- 계정, 로그인, 원격 분석, 광고 SDK를 포함하지 않는다.
- 게임 설치 경로와 최근 문서 경로는 로컬 설정에만 저장한다.
- 외부 프로세스 실행은 번들되거나 사용자가 지정한 허용 도구와 고정 인자 구조로 제한한다.
- 프로젝트 파일 안의 상대 경로를 해석할 때 작업 루트 밖으로 탈출하지 못하게 검증한다.

## 수용 기준

각 요구사항은 자동 테스트, 헤드리스 장면 로드, 고정 샘플 비교 또는 수동 시각 검수 중 하나 이상의 근거를 가져야 한다. “화면에 보인다”만으로 데이터 정확성을 판단하지 않으며, 같은 시간의 계산값과 렌더 결과를 함께 검증한다.

현재 Lock-on motion 수용 근거는 Domain/Application/Infrastructure/Editor 자동 테스트, production API 성능 probe와 exact Godot runtime marker를 함께 사용한다. runtime marker는 다음 문자열과 정확히 일치해야 한다.

```text
LOCK_ON_MOTION_READY snap=1 continuous=1 keyframe_only=1 coincidence=1 missing_target=1 shared_frame=1 trajectories=1 cache_reuse=1 nodes_reused=1
```

## 현재 비범위와 후속

- 현재 trajectory는 Transform 보간 위치를 사용하며 DSR animation의 root motion을 계산하거나 합성하지 않는다.
- 실제 DSR HKX animation 재생·매핑, root motion, 영상 렌더 실행과 인코딩은 후속 마일스톤이다.
- 게임패드 조작은 현재 요구 범위에 포함하지 않는다.
- 타임라인 확대·스크롤·스냅은 우선순위만 검토했으며 구현 완료로 간주하지 않는다.
