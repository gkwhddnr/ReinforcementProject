# OutdoorArena 데이터 취합 1차 검토

2026-11-28 · 김현석 · 검토 대상: 상태 조회와 실제 사격 사건 수집

## 병합과 작업 기준

기존 학습 뼈대 `3c50e01`을 최신 develop `231697e`에 충돌 없이 병합했다. 병합 커밋은 `53ce9ea`이며 이후 취합 작업은 `feature/learning`에서 진행한다. 학습 뼈대의 관측 10개·행동 [5,3,2], 보상·경기 제어 규칙은 이번 취합에서 변경하지 않았다.

## 이번에 연결한 흐름

```text
ArenaSpawnManager.CombatantA / CombatantB
  → PlayerCombatIntegration의 실제 프로퍼티 조회
  → PlayerState 스냅샷 2개

ShootingWeapon.ShotResolved
  → ShotResult의 값 복사
  → ShotSample 기록
```

`OutdoorArenaTelemetry`는 관찰용 컴포넌트다. 입력 명령·피해·스폰·리셋·보상·ML-Agents 결정을 실행하지 않는다. 기존 장면·프리팹·조원 코드는 수정하지 않았다.

## 코드 위치

| 파일 | 역할 |
| --- | --- |
| `Training/Scripts/CombatTelemetry.cs` | PlayerState, ShotSample과 경기별 사건 버퍼 |
| `Integration/OutdoorArenaTelemetry.cs` | 실제 전투원 조회·사격 이벤트 구독과 해제 |
| `Integration/Editor/OutdoorArenaTelemetryMenu.cs` | Play 중 검토용 취합 컴포넌트를 연결하는 메뉴 |
| `Training/Tests/Editor/CombatTelemetryTests.cs` | 사건 식별·중복·이전 경기·값 복사 검사 |
| `Training/Tests/Editor/OutdoorArenaTelemetryTests.cs` | 실제 아레나·통합 프리팹을 통한 연결 검사 |

이동·통합 코드는 기본 Assembly-CSharp에 있다. 이 코드를 참조하는 연결부도 김현석의 `Integration` 폴더에 배치하고 별도 asmdef를 만들지 않았다. 상태·사건 자료형은 기존 Reinforce.Training 어셈블리에 두고 Reinforcement.Shooting 참조를 추가했다. 다른 담당자 폴더의 어셈블리나 코드는 바꾸지 않았다.

## PlayerState에서 확인할 값

| 취합 필드 | 원래 제공값 |
| --- | --- |
| ActorId, TeamId | Actor의 실제 식별값 |
| Position, Rotation, Velocity, MaximumSpeed | 이동 시스템 프로퍼티 |
| IsCrouching | 실제 Posture가 Crouching인지 |
| Health, MaximumHealth, Alive | 기존 PlayerShotActor의 체력·생존 |
| HasTarget, TargetId, TargetAimPoint | 기존 오토에임의 선택 표적. 표적 없음은 -1 / 0 |
| DetectionRange, EffectiveRange, MuzzlePosition | 기존 전투 통합 프로퍼티 |
| CanFire, CooldownRemaining | 기존 무기 상태 |
| CurrentAccuracy, TargetLockDuration | 조회 시점의 동적 명중률·조준 유지시간 |
| ArenaEpisodeId | 스폰 매니저가 현재 보고한 경기 ID |
| CaptureFrame, CaptureFixedTime | 이 취합본을 읽은 Unity 프레임·고정 시간 |
| SourceTargetUpdateTick | 기존 TargetUpdateTick의 원래 값 |

같은 조회 함수 호출에서 값들을 복사한다. 원본 컴포넌트들의 Update/FixedUpdate 시점까지 통일한 것은 아니다. CaptureFixedTime은 경기 시작 기준 시각이 아니며 원본의 비공개 경기 시계로 대체하지 않는다. SourceTargetUpdateTick도 이미 제공된 값을 그대로 기록한다. 정책에 전달할 관측 배열은 별도로 구성해야 한다.

필수 컴포넌트가 없거나 비활성 상태이면 HasFirstState / HasSecondState가 false가 된다. 누락된 체력·속도를 정상 0값으로 채워 연결 성공으로 표시하지 않는다. MaximumSpeed는 현재 구현상 질주 속도 기준이다. 사망은 스냅샷의 Alive로 확인한다.

## ShotSample에서 확인할 값

- 식별: EpisodeId, ShooterId, Sequence. 이 세 값으로 같은 사건을 구분한다.
- 재현: Seed, Time, TeamId.
- 선택과 실제 결과: TargetId와 HitActorId를 별도로 기록한다. 실제 피격 대상이 없으면 HitActorId=-1이다.
- 피해: AppliedDamage와 Killed를 이미 확정된 DamageReceipt에서 복사한다. 피해를 다시 적용하지 않는다.
- 물리: ContactKind, Region, ObstacleId, 원점·방향·충돌점·거리.
- 진단: MountBlocked와 AimedAtTarget. AimedAtTarget을 실제 명중 횟수로 계산하지 않는다.

Unity Actor/Collider 참조는 보관하지 않고 값만 복사한다. 버퍼는 현재 경기의 사건만 유지한다. 새 경기 ID가 관찰되면 기존 사건을 비우며, 다른 경기 ID의 늦은 사건과 같은 발사자·Sequence의 중복 사건은 받지 않는다. 서로 다른 발사자의 같은 Sequence는 각각 기록한다. 이는 파일 저장·지표 계산·보상 지급을 구현한 것이 아니다.

컴포넌트가 연결된 이후의 사건만 수집한다. 부착 전 사격은 복원하지 않으므로, Play 중 수동 부착으로 얻은 자료를 전체 경기의 평가 결과로 사용하면 안 된다. 정식 경기 취합에서는 경기 시작 전에 연결하고 종료 시 결과를 보관해야 한다.

## 직접 확인하는 방법

1. `Assets/하종욱/Landscape/Scenes/OutdoorArena.unity`를 열고 Play에 진입한다.
2. `Tools > Reinforcement > Attach OutdoorArena Telemetry`를 실행한다.
3. 선택된 Outdoor Arena 오브젝트의 OutdoorArenaTelemetry 컴포넌트를 확인한다.
4. Has First State / Has Second State가 true인지, First State / Second State의 ID·체력·위치·자세가 실제 전투원과 같은지 확인한다.
5. 기존 봇이 실제 교전하면 Recorded Shots와 Last Shot의 선택 표적·실제 명중·피해를 확인한다.
6. 기존 R키 리셋을 사용한 경우 Arena Episode Id가 바뀌고 사건 버퍼가 새 경기로 전환되는지 확인한다.

메뉴는 Play 중에만 연결한다. Stop하면 추가한 컴포넌트도 사라진다. 장면·프리팹을 저장할 필요가 없다. 봇·맵·무기 설정은 원래 장면의 설정을 따른다. 실제 아레나에서 교전이 발생하지 않으면 Shots=0이며, 이 값만으로 사건 연결 실패로 단정하지 않는다.

## 다음 검토에서 결정할 부분

- 경기 ID·시뮬레이션 시각·종료·리셋의 단일 소유자와 첫 스폰 시드 전달
- 카메라에 종속되지 않는 이동 입력, 테스트 봇·수동 입력과의 제어권 분리
- 엄폐 거리·앉기·질주·동적 사격 상태를 포함한 최종 관측·행동 스키마
- 피해·탈락과 MatchEpisodeController 연결, 보상 공식과 최종 평가 집계
- Python ReLU 모델·PPO 실행과 ONNX 추론

현재 스폰 매니저가 같은 ID로 다시 리셋하면 사건 Sequence 재사용을 구별할 수 없다. 다음 경기에는 새 ID를 발급하도록 경기 관리 통합에서 맞춰야 한다. 자동 경기 종료·새 ID 발급·원본 시간 제어는 이번 관찰용 컴포넌트가 수행하지 않는다.

## 실행한 검증과 제한

Unity 6000.3.28f1에서 컴파일을 확인했고, 최종 코드로 실행한 전체 EditMode 검사 결과는 41개 중 25개 통과·16개 준비 단계 실패다.

| 검사 | 결과 | 확인 범위 |
| --- | --- | --- |
| LearningShellTests | 15/15 통과 | 기존 관측·행동·종료 정산·Agent 진입점 |
| CombatTelemetryTests | 4/4 통과 | 발사자별 순번·중복·경기 교체·값 복사·잘못된 값 |
| OutdoorArenaTelemetryTests | 2/2 통과 | 실제 OutdoorArena의 두 상태 조회, 통합 프리팹의 실제 발사·양수 피해 수집, 비활성화 시 구독 해제 |
| DynamicAccuracyTests | 4/4 통과 | 기존 동적 명중률 검사 |
| ShootingMvpTests | 0/16, SetUp 실패 | 아래 준비 문제로 사격 동작에 대한 해당 검사는 수행되지 않음 |

기본 배치 실행에서는 미저장 임시 씬에 Additive 씬을 만들려다 ShootingMvpTests가 실패했다. 검사 시작 시 임시 씬을 저장하도록 준비한 별도 실행에서는 `RecordingShotActor`를 Editor 스크립트로 판단해 AddComponent가 거부됐고, ShootingMvpTests.cs:79에서 null 참조로 실패했다. 사격 검사만 단독 실행해도 같은 문제가 발생했다. 해당 담당자의 테스트·어셈블리는 수정하지 않았으며 전체 검사 통과로 보고하지 않는다. 임시 검사 준비 스크립트와 씬은 제거하고 커밋하지 않는다.

실행 결과 XML은 로컬 `/private/tmp/reinforcement-integration-editmode.xml`, 저장된 씬 조건의 결과는 `/private/tmp/reinforcement-integration-saved-scene.xml`, 사격 단독 실행 결과는 `/private/tmp/reinforcement-shooting-editmode.xml`에 있다. 이 경로의 파일은 임시 검증 자료이며 저장소 산출물이 아니다. 학습 쪽만 재검사하려면 Test Runner에서 `Reinforce.Training.Tests`를 선택한다.

Python 트레이너, 실제 학습 수렴·성능, 플레이어 빌드, GUI Inspector 수동 검토와 R키 리셋 조작은 실행하지 않았다. 경기 전체 기록과 평가 지표 집계는 아직 연결하지 않았으므로 이 취합본을 완성된 학습·평가 시스템으로 사용하지 않는다.
