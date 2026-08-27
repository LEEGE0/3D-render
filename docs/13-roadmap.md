# 13. 개발 로드맵

## 진행 원칙

각 단계는 단독으로 검증 가능한 결과를 만들고 완료 후 커밋·푸시한다. 다음 단계는 이전 단계의 데이터 계약과 테스트를 사용한다. 실제 DSR 자산 연구가 지연돼도 플레이스홀더 기반 편집기 개발은 계속 진행할 수 있게 분리한다.

## 단계 0 — 저장소와 설계 기반

### 산출물

- GitHub 저장소와 기능 브랜치
- `.gitignore`, `AGENTS.md`, README와 상세 문서
- D 드라이브 디렉터리 정책
- DSR 애니메이션 자산 조사 기록

### 완료 기준

- 로컬 자산과 도구가 Git에서 제외된다.
- 커밋·푸시와 정상 버전 태그 절차가 문서화된다.
- 아키텍처, 데이터, 네트워크, 렌더와 품질 결정이 연결된 문서로 존재한다.

## 단계 1 — 개발 도구와 Godot 골격

### 작업

- Godot 4.7.2 Stable .NET, .NET SDK, FFmpeg 버전 고정
- 도구 체크섬 매니페스트
- Godot 프로젝트와 C# 솔루션
- Domain/Application/Infrastructure/Editor 프로젝트 참조
- 메인 창, 빈 탑뷰·3D·타임라인·속성 패널
- 헤드리스 로드와 기본 테스트

### 완료 기준

Windows 11에서 오프라인으로 메인 창이 열리고 헤드리스 검증과 `dotnet test`가 통과한다.

## 단계 2 — 장면 문서와 기본 편집

### 이번 마일스톤에서 완료

- [x] `SceneDocument`, 배우와 변환 키프레임 및 원자적 최초 키프레임 교체
- [x] 위치 선형 보간과 0/360 경계 최단 Yaw 보간
- [x] `DocumentSession` 선택·비영구 preview·명령 기반 Undo/Redo
- [x] 탑뷰 단일 배우 선택·빈 공간 해제·X/Z 이동·Yaw 방향 핸들 회전
- [x] 동일 committed snapshot과 preview를 사용하는 3D actor-ID 플레이스홀더
- [x] X/Y/Z/Yaw 숫자 preview와 Apply/Enter 확정
- [x] 불변 `ActorDisplayInfo`를 통한 표시 이름·역할 텍스트와 적대 역할 마름모 표식
- [x] stack 전환 후 `HistoryChanged` 기반 Inspector Undo/Redo 버튼 상태 동기화
- [x] 탑뷰 실제 입력·Escape·버튼·Inspector 범위 거부를 포함한 Godot 런타임 통합 검사
- [x] valid Inspector preview 뒤 invalid 입력의 committed 복원·invalid 값 보존 검사
- [x] Godot-free double transform mapper와 실제 actor node-name collision 통합 검사
- [x] 네 테스트 프로젝트, 구조 검사와 Godot 헤드리스 런타임 표식

### 단계 2 후속 정리

- 탑뷰 팬·줌·스냅과 겹친 actor 선택 보조
- 3D 직접 피킹과 축 기즈모
- 키보드 단축키와 패널 상태 저장

읽기 전용 시간 스크럽·재생은 단계 3A에서 완료했다. 현재 영구 편집은 원본 시간 구조를 조용히 바꾸지 않도록 각 배우의 시간상 최초 변환 키프레임만 대상으로 하며, 임의 시점 키프레임 생성·수정·삭제는 다음 구현 단위다.

### 완료 기준

탑뷰 이동·회전 또는 Inspector 확정 결과가 같은 `SceneDocument`와 3D 표현에 반영되고, 저장 없이 Move→Undo→Redo를 검증할 수 있다. 드래그 중 preview는 두 뷰에만 적용되며 문서 revision을 올리지 않는다.

## 단계 3 — 타임라인과 락온

### 완료된 단계 3A — 읽기 전용 시간 탐색과 재생

- [x] 문서 duration/FPS를 사용하는 Godot 독립 `PlaybackClock`과 seek/play/pause/toggle/stop/end clamp
- [x] `DocumentSession`의 playback 소유권, 시간·재생 상태 기반 편집 잠금과 시간 변경 시 active preview 취소
- [x] `(revision,time)` key로 동일 snapshot을 TopView/WorldView에 한 번씩 전달하고 같은 시각 play/pause 중복 투영 방지
- [x] `TimeSlider.ValueChanged`, Play/Pause·Stop button signal, 현재 시간·프레임·잠금 상태 표시
- [x] Main `Space` 입력과 Play/Pause button이 사용하는 동일 toggle 경로
- [x] 중간 시각 read-only TopView/Inspector guard와 Stop 뒤 최초 시각 편집 상태 복원
- [x] wait나 `_Process` 횟수 없이 hand-derived midpoint, preview cancellation, 문서/history 불변과 end auto-pause를 검증하는 exact runtime marker

3A는 기존 transform keyframe을 평가하는 read-only playback foundation이다. track/keyframe 편집 UI, 행동·락온 track, 이동 궤적, 실제 DSR animation playback, render 실행과 gamepad 조작까지 완료했다는 의미가 아니다.

### 다음 구현 단위 — 임의 시점 변환 키프레임 CRUD

- 선택 actor와 현재 paused time을 기준으로 transform keyframe 생성
- keyframe ID/time/position/yaw 조회와 선택 상태
- 선택 keyframe의 transform/time 수정 및 충돌 검증
- keyframe 삭제와 최소 한 개 transform 유지 정책
- 생성·수정·삭제 각각의 원자적 command, Undo/Redo와 revision/event 계약
- 같은 시각 중복, 문서 범위 밖 시간, 재생 중 편집과 stale preimage 거부
- track marker/Inspector/두 view의 선택·평가 동기화와 결정적 런타임 검증

이 CRUD가 들어오기 전에는 slider가 가리키는 임의 시각에서 Inspector 값을 확정하거나 키프레임을 자동 생성하지 않는다.

### 단계 3 후속 작업

- 행동·락온 트랙과 구간 편집
- 최단 회전과 단계 상태 평가
- 락온 대상과 연속 방향 계산
- 이동 궤적
- 타임라인 확대·스크롤·프레임/키프레임/구간 스냅

### 완료 기준

단계 3 전체 완료 기준은 네 캐릭터 장면을 60FPS 목표로 재생하고, transform CRUD를 Undo/Redo할 수 있으며, 락온 이동과 자유 방향 이동이 예상대로 다르게 동작하는 것이다. 현재 3A는 이 기준 중 읽기 전용 시간 평가와 재생 기반만 완료했다.

## 단계 4 — 가이드 가져오기와 저장

### 작업

- `gangqueen-topview-guide-v1` 가져오기
- 좌표 배율·원점 설정
- 내부 버전형 JSON
- 원자적 저장·다시 열기
- 자동 저장·복구
- 축약 회귀 샘플

### 완료 기준

`전략1`과 `뒤로빼기`를 불러와 역할·시간·좌표·방향·락온·행동을 보존하고 저장 왕복 테스트를 통과한다.

## 단계 5 — 전투 시각화

### 작업

- 공격 X 표시
- 방향·락온 선
- 뒤잡시전·유효 구간·뒤잡각
- 접촉·포함 비율·성공식
- 규칙 세트와 정확성 수준
- 원본 평가값 비교 테스트

### 완료 기준

고정 샘플의 평가값을 허용 오차 안에서 재현하고 실패 이유를 수치와 함께 표시한다.

## 단계 6 — 렌더와 영상 출력

### 작업

- 카메라 트랙과 프리셋
- 결정적 프레임 시간
- Movie Maker 이미지 시퀀스
- FFmpeg 인코딩
- 진행·취소·재시도
- 렌더 보고서

### 완료 기준

동일 장면을 1080p 지정 FPS로 반복 렌더해 프레임 수와 의미 상태가 일치하고 인코딩 실패 후 재시도가 가능하다.

## 단계 7 — DSR 애니메이션 자산 파이프라인

### 병렬 연구 트랙

- TAE 제어문자에 강한 읽기 전용 인덱서
- HKX/스켈레톤 시각 확인 도구 검증
- 이동·대기·회피·무기 공격 ID 카탈로그
- 공격자·피격자 뒤잡 쌍과 루트 정렬
- 표준 형식 변환·Godot 가져오기
- 라이선스·배포 제외 검증

### 완료 기준

최소 한 세트의 이동, 공격과 뒤잡 동기화 애니메이션을 로컬 카탈로그로 연결하며 자산이 없는 환경에서는 같은 문서가 플레이스홀더로 열린다.

## 단계 8 — 사용성·성능·배포

### 작업

- 단축키와 패널 저장
- 성능 프로파일링과 품질 프리셋
- 접근성 표시
- 깨끗한 Windows 11 오프라인 배포 테스트
- 샘플 프로젝트와 사용자 가이드
- 릴리스 패키지와 체크섬

### 완료 기준

기준 PC와 깨끗한 Windows 11 환경에서 설치·실행·편집·렌더를 완료하고, 릴리스에 게임 자산·개인 경로·비밀 정보가 없다.

## 정상 버전 마일스톤

사용자가 특정 기능이 잘 된다고 보고하면 기능과 재현 시나리오를 다시 확인한다. 사용자의 긍정 응답 후 현재 커밋에 `working/<기능>-YYYYMMDD-HHmm` 주석 태그를 생성해 알려진 정상 시점을 고정한다. 주요 후보는 다음과 같다.

- `working/guide-v1-import-*`
- `working/dual-view-sync-*`
- `working/lock-on-playback-*`
- `working/backstab-visualization-*`
- `working/video-render-*`
- `working/dsr-animation-mapping-*`
