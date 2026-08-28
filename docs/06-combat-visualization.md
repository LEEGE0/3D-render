# 06. 전투 시각화와 뒤잡 판정

## 목적과 정확성 표시

전투 시각화는 실제 게임 판정을 단정하는 기능이 아니라 위치·방향·시간 관계를 교육용으로 설명하는 기능이다. 규칙 값을 사용할 때는 raw fixture metadata, 설계 예시 기본값, 실제 측정값을 서로 구분하고 각 규칙 세트에 출처, 버전, 단위, 정확성 수준과 메모를 기록한다.

현재 구현된 범위는 작성된 transform 키프레임을 보간한 이동 경로와 자유 방향, Lock-on에서 파생된 방향을 TopView와 3D WorldView에 함께 표시하는 단계다. 두 뷰의 전투 오버레이에서 Action은 현재 `행동: {actionKey}` 텍스트로만 표시한다. 공격 이펙트 렌더링, 공격·접촉 판정, 뒤잡 성공 계산, 실제 DSR 애니메이션과 root motion은 모두 후속 구현 범위이며 현재 궤적 생성이나 production combat 계산에 포함되지 않는다.

## fixture raw metadata와 설계 예시의 경계

`samples/guides/synthetic-topview-v1.scene.json`의 `backstab_rules`에는 `rear_arc_degrees: 96`, `cast_length_pixels: 24`, `minimum_inside_ratio: 0.55`, `requires_contact: true`가 들어 있다. 이 값은 저작권 없는 importer fixture의 raw metadata다. `TopviewGuideV1Importer`는 원본 JSON 전체를 `ImportMetadata.RawSourcePayload`에 보존하고 `backstab_rules`를 해석하지 않는다는 warning을 반환한다. 따라서 96°/24px/0.55는 현재 `SceneDocument`의 전투 규칙으로 변환되지 않으며 판정, 성공 계산, 공격 이펙트 또는 뒤잡 영역 렌더링에 사용되지 않는다.

아래 후속 설계의 36px/110°/0.60은 fixture 값과 별개인 **설계 기본값 예시**일 뿐이다. 36px은 예시의 `castLength`와 `sectorRadius`, 110°는 예시의 후방 부채꼴 총각, 0.60은 예시의 `insideThreshold`에 사용한다. 이 값들도 현재 production combat 계산으로 구현되어 있지 않으며, 실제 구현 전에 규칙 출처와 측정 근거를 확정해야 한다.

## 배우 기하 모델

후속 판정 구현에서 각 배우는 다음 수평면 기하를 갖도록 설계한다.

- 중심점 `P = (x, z)`
- 방향 단위 벡터 `F`
- 오른쪽 단위 벡터 `R`
- 충돌 반경 `collisionRadius`
- 교육용 뒤잡 반경 `backstabSectorRadius`
- 모델 표시 크기와 독립적인 판정 크기

가이드 각도는 오른쪽 0°, 아래 90°이므로 내부 X/Z 평면에서 다음과 같이 계산할 수 있다.

```text
radians = degrees * π / 180
F = (cos(radians), sin(radians))
R = (-F.z, F.x)
```

실제 Godot 모델의 전방축이 -Z라면 표현 계층에서 모델 회전 오프셋을 적용하고 도메인 방향 정의는 바꾸지 않는다.

## 공격 이펙트

후속 공격 시각화에서는 행동이 공격 상태로 전환되는 시점에 공격자 앞에 빨간 칼 두 개가 X자로 교차하는 표시를 만든다. 이 표시는 히트박스 자체가 아니라 시청자가 공격 시점을 인지하기 위한 교육 오버레이다. 현재 production renderer는 이 공격 이펙트를 만들지 않는다.

표시 속성은 다음과 같다.

- 시작 시간과 지속 시간
- 화면/월드 크기
- 색, 투명도와 페이드
- 최종 영상 포함 여부
- 공격 종류에 따른 아이콘 변형
- 깊이에 가려질지 항상 위에 보일지 선택

## 뒤잡시전

공격자 중심 또는 설정된 판정 원점에서 정면으로 길이 `castLength`인 선분을 만든다.

```text
C0 = attacker.position + originOffset
C1 = C0 + attacker.forward * castLength
```

후속 설계 기본값 예시의 `castLength`는 36px이다. 이는 synthetic fixture의 raw `cast_length_pixels: 24`를 해석한 값이 아니다. 3D에서는 변환 배율을 적용한 미터 단위를 사용하고, 전체 선분과 판정에 사용하는 유효 부분을 서로 다른 색·굵기로 그리도록 설계한다.

“전체 선분 길이의 60%”는 성공 임계값과 혼동될 수 있으므로 데이터에는 다음을 분리한다.

- `castLength`: 전체 선분 길이
- `insideThreshold`: 대상 뒤잡각 안에 포함돼야 하는 선분 비율, 설계 기본값 예시 0.60
- `effectiveLengthRatio`: 실제로 앞쪽 60%만 검사하는 규칙이 확인될 경우 사용할 별도 값

synthetic V1 fixture의 raw `minimum_inside_ratio`는 0.55지만 importer가 이를 해석하지 않으므로 현재 성공식은 존재하지 않는다. 후속 구현에서는 확정된 규칙 세트에 따라 이 필드들을 명시적으로 매핑한 뒤 판정식에 사용한다.

## 뒤잡각

대상의 전방 `Ftarget` 반대인 후방 벡터 `B = -Ftarget`을 중심으로 좌우 `sectorHalfAngle` 범위의 부채꼴을 만든다.

- 설계 기본값 예시 반각: 55°
- 설계 기본값 예시 총각: 110°
- 설계 기본값 예시 반경: 36px
- 중심: 대상의 판정 중심

점 `Q`가 부채꼴 내부인지 판정하려면 다음을 모두 만족해야 한다.

1. `distance(Q, target.position) <= sectorRadius`
2. `angle(B, normalize(Q - target.position)) <= sectorHalfAngle`

중심점에서 방향 벡터를 정규화할 수 없는 경우는 내부로 취급하거나 별도 경계 정책을 테스트로 고정한다.

## 포함 비율

선분과 부채꼴의 정확한 교차 길이를 해석적으로 구할 수 있지만 후속 초기 구현은 결정적인 균등 샘플링으로 시작할 수 있다.

```text
N = max(32, ceil(castLength / sampleSpacing))
insideCount = 0
for i in 0..N:
    t = i / N
    Q = lerp(C0, C1, t)
    if IsInsideSector(Q): insideCount++
insideRatio = insideCount / (N + 1)
```

검증용 샘플 결과와 충분히 일치하는지 확인한 뒤, 경계 정확도나 성능 문제가 있으면 선분-원 및 각 경계 교차점을 사용하는 해석 방식으로 교체한다. 샘플 수는 렌더 프레임과 무관한 고정 규칙으로 정해 결과가 PC 성능에 따라 달라지지 않게 한다.

## 접촉 판정

후속 판정 구현에서는 뒤잡시전 선분과 대상 충돌원의 최소 거리를 계산한다.

```text
t = clamp(dot(Ptarget - C0, C1 - C0) / lengthSquared(C1 - C0), 0, 1)
closest = C0 + (C1 - C0) * t
contact = distance(closest, Ptarget) <= contactRadius
```

선분 길이가 0이면 시작점과 대상 중심 거리만 검사한다. NaN을 생성하지 않는다.

## 성공식과 결과 모델

후속 설계 기본값 예시의 성공식은 다음과 같다. synthetic V1 fixture의 현재 production 해석식이 아니다.

```text
success = insideRatio >= 0.60 && contact
```

후속 평가 결과 모델은 최소한 다음 값을 가진다.

- 공격자 ID와 대상 ID
- 평가 시간
- 규칙 세트 ID와 버전
- 성공 여부
- 포함 비율
- 접촉 여부
- 공격자·대상 위치와 방향 스냅샷
- 실패 이유: 각도 부족, 거리 부족, 접촉 없음, 입력 무효

표시할 때 `성공/실패`만 보여주지 않고 수치와 실패 이유를 함께 보여준다.

## 락온 방향

공격자 위치 `A`, 대상 위치 `T`에서 수평 방향은 다음과 같다.

```text
D = T - A
yaw = atan2(D.z, D.x) * 180 / π
```

Domain Yaw의 기준은 `+X=0°`, `+Z=90°`, `-X=180°`, `-Z=270°`다. `yawOffsetDegrees`는 target 방향에 더한 후 항상 유한한 `[0, 360)` 범위로 정규화한다. 작성 transform의 Yaw는 보존하고, 파생 결과는 `SceneSnapshot.ActorFacings`와 궤적 sample의 `LockOnFacing`에만 둔다.

추적 모드는 다음과 같이 구분한다.

- `Continuous`: actor와 target의 보간 위치를 sample 시각마다 평가해 계속 target을 바라본다.
- `Snap`: Lock-on source keyframe 시각의 방향을 고정하고 이후 이동에도 유지한다.
- `KeyframeOnly`: target이나 offset과 무관하게 작성된 transform Yaw를 사용한다.
- Lock-on 비활성 또는 첫 Lock-on 이전: 작성 Yaw를 사용한다.

수평 거리가 `1e-6` 이하인 위치 일치 구간은 방향을 새로 만들 수 없다. 같은 Lock-on 구간에 직전 유효 상대 방향이 있으면 그 방향을 유지하고, 시작부터 일치했으면 작성 Yaw로 fallback한다. 경계 진입은 piecewise-linear 선분과 epsilon 원의 교점을 사용해 왼쪽 극한을 결정하므로 sample 호출 순서에 의존하지 않는다. target actor가 누락된 비정상 seam에서도 예외나 NaN을 내지 않고 `TargetUnavailableFallback`과 작성 Yaw를 사용하며, semantic lock line과 target marker는 안전하게 숨긴다.

## 이동 경로

위치는 자유 방향과 Lock-on 방향 때문에 달라지지 않는다. 한 actor에는 동일한 위치·시간 sample을 사용하는 공유 경로 하나만 만들고, 자유/Lock-on 차이는 그 경로 위 방향 tick의 Yaw로만 표시한다. 서로 다른 것처럼 보이게 하려고 두 경로를 평행 offset하지 않는다.

sample plan은 누적 `t += step` 대신 정수 `k / rate`로 균일 시각을 만들고, 정확한 문서 시작·끝과 actor transform/Lock-on, 활성 target transform anchor 시각을 합친다. 방향 tick은 공용 `TrajectoryTickSelectionPolicy`를 사용한다.

- 균일 rate가 5Hz 이하이면 모든 균일 sample을 사용한다.
- 5Hz를 넘으면 `n/5` 목표 시각에서 가장 가까운 정확한 균일 sample을 고른다.
- 거리가 정확히 같으면 더 이른 sample을 고른다.
- transform/Lock-on/활성 target transform anchor는 0.2초보다 가까워도 항상 합친다.
- actor transform 또는 활성 target transform anchor는 원, actor Lock-on anchor는 마름모로 표시하며 같은 시각의 flags를 잃지 않는다.

### TopView 표시

TopView의 draw 순서는 아래에서 위로 고정한다.

1. 공유 위치 경로
2. 자유 방향 tick
3. Lock-on 방향 tick
4. 현재 semantic lock line
5. actor body
6. target marker
7. action/Lock-on text

공유 경로는 `#6ea8fe`, 자유 방향은 `#55aaff`, Lock-on 방향은 `#ffd166`을 사용한다. 공유 경로는 2px 선이며, 자유 방향 tick 끝은 화살촉, Lock-on 방향 tick 끝은 굵은 막대로 구분한다. transform anchor는 자유 tick 원, Lock-on anchor는 Lock-on tick 마름모로 그린다.

현재 시각 이하 sample의 기본 명도는 `1.0`, 미래 sample은 `0.45`다. 선택 actor는 선택 명도 `1.0`, 선택되지 않은 actor는 `0.35`를 추가로 곱한다. 선택 변경과 transform preview는 committed trajectory geometry를 다시 만들지 않으며, preview는 actor body와 semantic lock line만 임시 authored transform으로 바꾼다.

### WorldView 표시

3D 궤적은 움직이고 회전하는 actor root 아래가 아니라 `_actorsRoot/TrajectoryOverlayRoot` 아래의 actor별 고정 container에 둔다. 따라서 playback으로 actor가 이동·회전해도 이미 만든 world-space 경로 vertex는 움직이지 않는다. actor가 사라지면 body root와 대응 trajectory container를 각각 정리한다.

- 공유 경로: `SharedTrajectory`, `#6ea8fe`, `ImmediateMesh` line strip
- 자유 방향: `FreeFacingTicks`, `#55aaff`, `ImmediateMesh` line pairs
- Lock-on 방향: `LockOnFacingTicks`, `#ffd166`, `ImmediateMesh` line pairs

World geometry는 Domain X/Z를 그대로 사용하고 모든 path/tick vertex의 Y에 작은 고정 높이 `0.025`를 더해 z-fighting을 피한다. 방향 tick 길이는 `0.35`다. Domain Yaw는 `WorldTransformMapper.ToRotationYRadians`를 단일 변환 경계로 사용해 local `+X` 전방과 맞춘다. 모델 전방축 보정이 필요하면 actor root의 world Yaw가 아니라 `VisualRoot` local offset으로만 적용한다.

각 vertex의 `UV.x`에는 `time / duration`을 넣는다. `TrajectoryTimeFade.gdshader`는 `current_time_normalized` uniform과 비교해 미래를 `0.45` 명도로 만든다. seek/playback tick에서는 mesh를 다시 쓰지 않고 uniform만 갱신한다. duration이 0이면 유일 sample의 UV와 uniform은 모두 정확히 `0`이며 미래로 흐려지지 않는다.

궤적 sample 정책은 문서 의미나 판정 결과를 바꾸지 않는다. 현재 구현은 transform 키프레임 사이의 authored 위치 보간을 시각화한다. 실제 HKX clip의 root motion을 궤적에 적용하는 기능, 경로 전체/꼬리/숨김 사용자 설정과 최종 영상용 표시 제어는 후속 범위다.

## 실제 애니메이션 연계

후속 실제 애니메이션 연계에서는 공격 키프레임의 의미 행동과 HKX 클립을 `AnimationMapping`으로 연결한다. 뒤잡은 다음 세트를 하나로 검증한다.

- 공격자 클립
- 피격자 클립
- 두 루트의 시작 상대 위치와 방향
- 동기화 기준 이벤트
- 타격·피해·제어 이벤트 시간
- 루트 모션 적용 또는 억제 정책

TAE/HKX ID만 보고 의미를 확정하지 않고 뷰어 재생과 실제 게임 비교로 확인한다. 확인 전에는 카탈로그 상태를 `unverified`로 표시한다.

## 후속 공격·뒤잡 구현 테스트 기준

- importer가 raw metadata를 해석하도록 확장할 때 synthetic fixture의 96°/24px/0.55와 `requires_contact: true`를 명시적으로 매핑하고, 단순 raw payload 보존과 production 규칙 적용을 각각 검증한다.
- 36px/110°/0.60 설계 예시를 구현할 경우 fixture 규칙과 섞이지 않는 별도 규칙 세트로 검증한다.
- 0°, 55°, 55° 초과, 180° 방향 경계를 검사한다.
- 선분이 원을 관통, 접선, 미접촉하는 경우를 검사한다.
- 0 길이 선분, 같은 위치 락온, 매우 큰 좌표와 잘못된 반경을 검사한다.
- 좌표 변환 전후의 판정이 동일한 비율 스케일에서 일관되는지 검사한다.
