# 10. 저장소와 디렉터리 구성

## 기본 원칙

사용자 요구에 따라 프로젝트 관련 파일과 프로그램은 D 드라이브에서 관리한다. 소스 저장소의 표준 위치는 `D:\3D-render`다. Steam 게임 원본은 기존 C 드라이브 설치 위치에 남겨 두고 읽기 전용 입력으로 취급한다. 필요한 분석 파일은 D 드라이브로 복사한다.

## 권장 구조

```text
D:\3D-render\
├─ .git\
├─ .gitignore
├─ AGENTS.md
├─ README.md
├─ docs\
│  ├─ research\
│  └─ superpowers\
├─ src\
│  ├─ PvpGuide.Domain\
│  ├─ PvpGuide.Application\
│  ├─ PvpGuide.Infrastructure\
│  └─ PvpGuide.Editor\
├─ tests\
│  ├─ PvpGuide.Domain.Tests\
│  ├─ PvpGuide.Infrastructure.Tests\
│  └─ PvpGuide.Editor.Tests\
├─ samples\
│  └─ guides\                 저작권 문제가 없는 최소 회귀 샘플
├─ scripts\                   검증·빌드·패키징 스크립트
├─ local-assets\              Git 제외
│  ├─ raw\dsr\chr\
│  ├─ extracted\witchy\
│  ├─ converted\
│  ├─ catalog\
│  └─ project.json
├─ tools\                     Git 제외
│  ├─ godot\
│  ├─ ffmpeg\
│  ├─ blender\
│  ├─ WitchyBND-3.0.1.0\
│  ├─ downloads\
│  └─ src\
├─ cache\                     Git 제외
│  ├─ thumbnails\
│  ├─ trajectories\
│  └─ asset-index\
└─ exports\                   Git 제외
   ├─ renders\
   └─ packages\
```

## Git 추적 대상

- 소스 코드와 테스트
- 프로젝트 설정과 사람이 관리하는 작은 설정
- 저작권 문제가 없는 샘플
- 문서와 빌드·검증 스크립트
- 외부 의존성 버전·체크섬 매니페스트

## Git 제외 대상

- 게임 원본 및 추출·변환 자산
- Godot, Blender, FFmpeg, WitchyBND 실행 파일
- NuGet/Godot/IDE 생성 캐시
- 렌더 프레임, 영상과 배포 패키지
- 개인 로컬 설정과 최근 문서 목록
- 비밀 정보와 인증 자료

## 사용자 데이터 위치

앱이 설치된 후 사용자 문서와 설정의 기본 위치도 D 드라이브를 선택할 수 있게 한다. 포터블 모드와 설치 모드를 구분한다.

### 포터블 모드

실행 파일 옆의 `userdata\`를 사용한다. USB나 D 드라이브 폴더 단위 백업이 쉽지만 프로그램 폴더 권한과 업데이트 덮어쓰기에 주의한다.

### 설치 모드

사용자가 지정한 `D:\3D-render-data` 같은 루트를 설정에 저장한다. Windows AppData에는 경로 선택을 위한 작은 설정만 둘 수 있으나 대용량 캐시·자산·렌더는 D 드라이브를 사용한다.

현재 WitchyBND는 자체 구현상 `%APPDATA%\WitchyBND\appsettings.user.json`을 생성한다. 이는 작은 도구 설정 예외이며 게임 자산·추출물·프로그램 본체는 D 드라이브에 있다. 프로젝트에서 제작할 프로그램은 처음부터 데이터 루트를 사용자 지정할 수 있게 설계한다.

## 경로 규칙

- 내부 저장에는 가능하면 프로젝트 파일 기준 상대 경로 또는 자산 카탈로그 ID를 사용한다.
- 절대 경로는 로컬 설정이나 가져오기 출처 메타데이터로 제한한다.
- 경로 비교 전 `GetFullPath`로 정규화하고 의도한 루트 아래인지 확인한다.
- Windows 예약 이름, 금지 문자, 최대 경로와 대소문자 차이를 처리한다.
- 파일 작업에 와일드카드 문자열을 사용하지 않고 검증된 실제 경로를 사용한다.
- 재귀 삭제·이동 전 대상의 해석된 절대 경로가 예상 루트 안인지 확인한다.

## 저장 파일과 자동 저장

```text
<사용자 프로젝트>\example.pvpscene.json
<데이터 루트>\autosave\<document-id>\<timestamp>.autosave.json
<데이터 루트>\recovery\<session-id>\recovery-manifest.json
```

자동 저장은 최근 N개 또는 기간 기준 보존 정책을 사용한다. 정리 전에 현재 사용자 문서와 무관한 정확한 자동 저장 루트인지 확인한다. 복구본을 열 때는 원본을 덮어쓰지 않고 “복구됨” 새 문서로 연다.

## 도구 매니페스트

추후 `tools/manifest.json` 또는 추적 가능한 `config/tool-versions.json`에 다음을 기록한다.

- 도구 이름과 버전
- 공식 배포 URL
- SHA-256
- 지원 운영체제와 아키텍처
- 설치 상대 경로
- 라이선스 링크
- 앱 배포에 포함되는지 사용자 별도 설치인지

실행 시 파일 체크섬을 매번 계산할 필요는 없지만 최초 설치와 진단 화면에서 검증할 수 있게 한다.
