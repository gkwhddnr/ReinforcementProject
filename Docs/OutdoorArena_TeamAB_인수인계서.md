# OutdoorArena Team A/B 인수인계서

2026-11-24 · Unity 6000.3.28f1

## 적용 대상과 설정

`Assets/하종욱/Landscape/Scenes/OutdoorArena.unity`의 `Outdoor Arena` 아래에 있는 원본 `Team A Combatant`와 `Team B Combatant`에 같은 설정을 사용한다. 평가 기록에는 맵 `outdoor-arena` v1과 스폰 배치 `outdoor-fixed-ab-01`을 함께 남긴다.

| Inspector 설정 위치 | 값 |
|---|---|
| `Player Movement Controller`의 `OutdoorArenaDuelMovementSettings` | 걷기 5 m/s, 질주 8.125 m/s, 앉기 2.5 m/s |
| `Player Combat Integration`의 `Shooting Settings` | 사거리 75 m, 명중 확률 25%, 몸통 피해 10, 머리 피해 20 |
| `Trial Vision`의 `Horizontal Fov` | 수평 시야각 75° |

이동속도는 평지 기준이다. 경사에 따른 감속은 기존 이동 컨트롤러가 처리한다. 시야 판정은 전방 각도와 가림을 검사하고, 발사 판정에는 무기 사거리와 총구에서 표적까지의 충돌 검사가 적용된다.

## 실행과 연동

1. 씬을 열고 Play Mode로 진입한다. `ArenaCombatSetup`은 두 원본과 스폰 준비 상태를 확인한 뒤 A/B 스폰과 팀·액터 ID를 등록하고, 이동 입력과 상대 목록을 연결한다.
2. 테스트 제어기에서 양쪽 `PlayerAgentInputBridge.SetActionsFromAgent`에 이동·시선·자세·발사 입력을 전달한다. 벡터를 사용하는 기존 호출부는 `SetActionsFromArrays`에 이동 X/Z, 시선 X/Z와 자세·발사 분기를 전달한다.
3. 매 경기의 에피소드 ID와 시드로 `PlayerCombatIntegration.ResetCombatEpisode`를 호출한다. `ShootingWeapon.ShotResolved`의 `ShotResult`에서 명중 부위, 적용 피해, 사격 원점과 충돌 위치를 읽는다. 장애물 유형은 `Contact.ObstacleId`를 `ArenaEnvironment.TryGetCoverById`에 전달해 조회한다.
4. 순찰 경유점과 300초 제한은 이전 비교 실험에서 테스트 제어기가 적용했다. 실제 학습의 정책·보상·종료 조건은 학습 시스템에서 연결한다.

## 초기화 및 버전 구분

- `ArenaCombatSetup`은 활성화 상태, 이동 준비, 입력 브리지와 두 스폰을 검증한다. 준비가 끝난 뒤 두 원본에 팀·액터 ID 1/2를 지정하고, 같은 팀은 상대 목록을 공유한다.
- 공개 씬은 대체 메시와 자체 스폰 배치를 사용한다. 공개 씬의 기존 Enemy 더미 8개는 비활성화하고 F8 테스트용 Player를 별도로 유지한다. 순찰 데이터는 로컬 원본 씬 기준이다.
- 기존 사격 설정에서 머리 피해를 지정하지 않은 경우에는 몸통 피해를 적용한다. OutdoorArena 두 원본에는 머리 피해 20을 명시했다.

[수치 데이터 테스트 기록](OutdoorArena_TeamAB_수치_테스트_기록.md)
