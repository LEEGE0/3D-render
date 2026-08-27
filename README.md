# 3D Render — DARK SOULS REMASTERED PvP 교육 영상 편집기

Windows 11에서 오프라인으로 실행되는 DARK SOULS REMASTERED PvP 상황 재현·교육 영상 제작 프로그램이다. 일반적인 3D 제작 도구보다 전투 참여자의 위치, 방향, 거리, 타이밍, 락온, 공격과 뒤잡 관계를 빠르고 정확하게 설명하는 데 초점을 둔다.

현재 저장소는 아키텍처·데이터 계약·개발 정책을 확정하고 실제 게임 애니메이션 자산의 존재와 변환 가능성을 조사한 기반 단계다. 애플리케이션 코드는 후속 마일스톤에서 Godot 프로젝트 골격부터 추가한다.

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
├─ src\                      애플리케이션 코드(후속 단계)
├─ tests\                    자동화 테스트(후속 단계)
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
3. Godot 4.7.2 Stable .NET과 호환 .NET SDK를 `D:\3D-render\tools` 아래에 준비한다.
4. FFmpeg 정식 빌드의 버전과 체크섬을 기록하고 `tools/`에 둔다.
5. 실제 게임 자산이 필요하면 Steam 설치 경로를 읽기 전용 입력으로 선택한다.
6. `local-assets/`, `tools/`, `cache/`, `exports/`가 Git에서 제외되는지 확인한다.

아직 실행 가능한 Godot 프로젝트가 없으므로 현재 단계에서 빌드 명령을 제공하지 않는다. 프로젝트 골격이 추가되면 고정된 도구 버전과 정확한 빌드·테스트·내보내기 명령을 이 문서에 추가한다.

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
- 다음 구현 단위: Godot .NET 프로젝트 골격과 자동 검증 환경

## 법적·운영 주의

이 프로젝트는 교육용 상황 재현 편집기이며 FromSoftware 또는 Bandai Namco의 공식 제품이 아니다. DARK SOULS 및 관련 자산의 권리는 각 권리자에게 있다. 사용자는 자신이 합법적으로 보유한 설치 파일만 로컬에서 사용해야 하며, 추출 자산을 저장소·릴리스·영상 원본 패키지에 재배포해서는 안 된다.
