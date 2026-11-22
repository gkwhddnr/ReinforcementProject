# 플레이어 이동·회전·경사 이동 MVP 개발서

담당 이용현 · 작성일 2026년 11월 17일 (11월 16일 랜드스케이프 회의 반영) · Unity 6.3 LTS

---

## 1. 개발 결과물과 아키텍처 개요

PPO 1 vs 1 고정 소형 엄폐물 아레나(250 × 250 × 50 Unity 단위) 및 향후 MA-POCA 3 vs 3 환경에서 캐릭터의 **전후좌우 이동, 수평 회전, 경사면 등반/미끄러짐, 엄폐용 콜라이더 크기 조절(앉기), 경기 재설정 초기화**를 처리하는 단일 캐릭터 컨트롤러 모듈을 제공한다.

### 1.1 물리 엔진 접근 방식: Kinematic CharacterController 채택
강화학습 시뮬레이션은 `Time.timeScale`을 수십 배로 높여 고속으로 실행된다. `Rigidbody` 기반의 완전 물리를 사용할 경우, 고속 배속 환경에서 Simplex Noise 지형 메쉬의 폴리곤 틈새 튕김(Jittering)이나 엄폐물 충돌체 관통(Tunneling) 현상이 발생한다.  
따라서 **`CharacterController` 기반에 정밀한 커스텀 지면 검사(`SphereCast`) + 벡터 투영(`Vector Projection`)을 결합한 Kinematic 제어 방식**을 채택한다.

### 1.2 전체 아키텍처 다이어그램

```text
                  ┌─────────────────────────┐
                  │   IPlayerInputProvider  │ (Keyboard / ML-Agents Bridge)
                  └────────────┬────────────┘
                               │ MovementInputData
                               ▼
                  ┌─────────────────────────┐
                  │ PlayerMovementController│ ◄──── ArenaReset / Spawn
                  └────────────┬────────────┘       (랜드스케이프 연동)
                               │ Update / FixedUpdate
                               ▼
                  ┌─────────────────────────┐
                  │      Movement HFSM      │
                  │ ┌─────────────────────┐ │
                  │ │ GroundedSuperState  │ │
                  │ │ - Idle, Walk, Sprint│ │
                  │ │ - Crouch, SlopeSlide│ │
                  │ └─────────────────────┘ │
                  │ ┌─────────────────────┐ │
                  │ │    AirborneState    │ │
                  │ └─────────────────────┘ │
                  │ ┌─────────────────────┐ │
                  │ │      DeadState      │ │
                  │ └─────────────────────┘ │
                  └────────────┬────────────┘
                               │ Move(Vector3 motion)
                               ▼
                  ┌─────────────────────────┐
                  │   CharacterController   │
                  └─────────────────────────┘
```

---

## 2. 파일과 책임

| 파일명 | 책임 및 역할 | 구현 우선순위 |
| :--- | :--- | :---: |
| `MovementData.cs` | 입력 구조체(`MovementInputData`), 이동 물리 설정 SO(`MovementSettingsSO`) 정의 | **MVP 필수** |
| `IPlayerInputProvider.cs` | 입력 인터페이스. 수동 키보드 입력과 ML-Agents 정책 입력 추상화 | **MVP 필수** |
| `PlayerKeyboardInput.cs` | 게임 베이스 수동 검증용 키보드(WASD/Shift/Ctrl) 및 마우스 회전 입력 | **MVP 필수** |
| `PlayerAgentInputBridge.cs` | ML-Agents `Agent.OnActionReceived`에서 행동 벡터를 받아 주입하는 브릿지 | **MVP 필수** |
| `PlayerMovementController.cs` | 메인 컨트롤러. 지면 검사, 경사 연산, 머리 위 장애물 검사, 경기 리셋(`ResetState`) 제공 | **MVP 필수** |
| `HFSM/IState.cs` | 계층형 상태 기본 인터페이스 (`Enter`, `Update`, `FixedUpdate`, `Exit`) | **MVP 필수** |
| `HFSM/BaseState.cs` | 계층형 FSM 기본 클래스 (부모/자식 상태 전이 트리 관리) | **MVP 필수** |
| `HFSM/States/GroundedSuperState.cs` | 지상 상위 상태. 지면 밀착, $75^\circ$ 경사 검사, 하위 이동 상태 전이 관리 | **MVP 필수** |
| `HFSM/States/AirborneState.cs` | 공중 낙하(중력 $2g$) 및 공중 수평 관성 이동 처리 | **MVP 필수** |
| `HFSM/States/WalkState.cs` | 지면 투영 이동 벡터 및 오르막/내리막 경사 배율 계산 | **MVP 필수** |
| `HFSM/States/SprintState.cs` | 고속 전력 질주 이동 처리 | **MVP 필수** |
| `HFSM/States/CrouchState.cs` | 앉기 상태. 콜라이더 높이 축소($1.2\text{m}$), 머리 위 여유 공간 확인 후 기립 복귀 | **MVP 필수** |
| `HFSM/States/SlopeSlideState.cs` | $75^\circ$ 초과 급경사 및 수직 장애물 측면 강제 하향 슬라이딩 | **MVP 필수** |
| `HFSM/States/DeadState.cs` | 사망 시 이동/회전 차단 및 충돌 비활성화 상태 | **MVP 필수** |
| `IMovementZoneModifier.cs` | (확장용) 추후 이동 제약 구역 배율 조회를 위한 인터페이스 훅 | **후순위** |

---

## 3. 설정과 데이터 (`MovementData.cs`)

### 3.1 `MovementInputData` 구조체
입력 장치(키보드 또는 ML-Agents 에이전트)와 컨트롤러 간의 데이터 전송 객체(DTO)다.

```csharp
using UnityEngine;

public struct MovementInputData
{
    public Vector2 MoveVector;     // x: 좌우(-1~1), y: 전후(-1~1)
    public Vector3 LookDirection;  // 월드 공간 목표 전방 방향 벡터 (수평 회전용)
    public bool IsSprint;          // 전력 질주 여부
    public bool IsCrouch;          // 앉기 여부
}
```

### 3.2 `MovementSettingsSO` ScriptableObject
랜드스케이프의 `ArenaValidator` 스폰·통로 검증 기준과 동기화된 물리 설정 에셋이다.

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "MovementSettings", menuName = "Reinforce/Movement Settings")]
public class MovementSettingsSO : ScriptableObject
{
    [Header("Speeds (m/s)")]
    public float walkSpeed = 4.0f;
    public float sprintSpeed = 6.5f;
    public float crouchSpeed = 2.0f;
    public float rotationSpeed = 360.0f;       // 초당 360도 회전

    [Header("Slope & Physics")]
    public float maxSlopeLimit = 75.0f;        // 최대 등반 가능 경사각
    public float slideSpeed = 8.0f;            // 75도 초과 사면 슬라이딩 속도
    public float gravity = 19.6f;              // 2g 중력 가속도
    public float stickToGroundForce = 5.0f;    // 내리막 지면 밀착 하향 속도

    [Header("Collision Dimensions (ArenaValidator 동기화)")]
    public float standingHeight = 2.0f;        // 기본 서기 키 (H)
    public float crouchingHeight = 1.2f;       // 앉기 키 (H_crouch)
    public float colliderRadius = 0.5f;        // 캡슐 반지름 (r)
    public float stepOffset = 0.3f;            // 단차 극복 한계
}
```

---

## 4. 경사 이동 & 지면 접촉 알고리즘

### 4.1 경사면 벡터 투영 (Slope Vector Projection)
캐릭터의 평면 이동 의도 벡터 $\vec{V}_{input}$를 지면 법선 $\vec{N}$에 투영하여, 지면을 파고들거나 공중으로 뜨지 않고 사면을 따라 부드럽게 흐르는 속도 벡터 $\vec{V}_{projected}$를 계산한다.

$$\vec{V}_{projected} = \text{Vector3.ProjectOnPlane}(\vec{V}_{input}, \vec{N}).\text{normalized} \times \text{FinalSpeed}$$

### 4.2 오르막/내리막 판별 및 지면 밀착 (Stick-to-Ground)
- **오르막/내리막 판별**: 이동 의도 방향($\vec{V}_{input}$)과 지면 법선의 수평 투영 벡터($\vec{N}_{xz}$)의 내적(Dot Product)을 통해 판별한다 ($\text{dot} < 0$ 이면 오르막).
- **Stick-to-Ground**: 완경사 및 내리막길을 고속(`Sprint`)으로 이동할 때 관성 때문에 캐릭터가 공중으로 튀어 올라 `AirborneState`로 오인되는 현상을 방지한다. 지면 접촉 중일 때 아래 방향으로 일정한 밀착 속도($-5.0\text{ m/s}$)를 주입한다.

### 4.3 $75^\circ$ 초과 급경사 처리 (Slope Slide)
경사도가 $75^\circ$를 초과하면 캐릭터는 등반할 수 없다. 지면 법선 $\vec{N}$을 기준으로 경사면 아래 방향의 접선 벡터를 산출하여 강제 하향 이동시킨다.

$$\vec{F}_{slide} = \left(\text{Vector3.down} - \vec{N} \times (\text{Vector3.down} \cdot \vec{N})\right).\text{normalized} \times \text{slideSpeed}$$

---

## 5. HFSM 코어 및 주요 상태 구현

### 5.1 지상 상위 상태 (`HFSM/States/GroundedSuperState.cs`)

```csharp
using UnityEngine;

public class GroundedSuperState : BaseState
{
    public GroundedSuperState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

    public override void Enter()
    {
        base.Enter();
        SetSubState(new IdleState(controller, this));
    }

    public override void FixedUpdate()
    {
        base.FixedUpdate();

        // 1. 공중 이탈 검사
        if (!controller.IsGrounded)
        {
            controller.ChangeRootState(controller.AirborneState);
            return;
        }

        // 2. 등반 한계 경사(75도) 초과 검사
        if (controller.CurrentSlopeAngle > controller.Settings.maxSlopeLimit)
        {
            if (!(currentSubState is SlopeSlideState))
                SetSubState(new SlopeSlideState(controller, this));
            return;
        }

        // 3. 지상 하위 상태 전이 규칙
        MovementInputData input = controller.CurrentInput;
        bool hasMoveInput = input.MoveVector.sqrMagnitude > 0.01f;

        if (currentSubState is SlopeSlideState)
        {
            SetSubState(new IdleState(controller, this));
        }
        else if (input.IsCrouch || !controller.CanStandUp())
        {
            // 앉기 입력 중이거나, 머리 위 장애물로 인해 일어설 수 없으면 Crouch 유지
            if (!(currentSubState is CrouchState))
                SetSubState(new CrouchState(controller, this));
        }
        else if (hasMoveInput)
        {
            if (input.IsSprint && !(currentSubState is SprintState))
                SetSubState(new SprintState(controller, this));
            else if (!input.IsSprint && !(currentSubState is WalkState))
                SetSubState(new WalkState(controller, this));
        }
        else
        {
            if (!(currentSubState is IdleState))
                SetSubState(new IdleState(controller, this));
        }
    }
}
```

### 5.2 걷기 상태 (`HFSM/States/WalkState.cs`)
MVP 단계에서는 랜드스케이프의 이동 제약 구역(`MovementZoneMap`)이 후순위이므로 경사 배율(`slopeMultiplier`)만 즉시 적용하고, 추후 확장을 위해 `ExternalSpeedMultiplier`(기본값 `1.0f`) 훅을 유지한다.

```csharp
using UnityEngine;

public class WalkState : BaseState
{
    public WalkState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

    public override void FixedUpdate()
    {
        base.FixedUpdate();

        MovementInputData input = controller.CurrentInput;
        Vector3 moveDir = controller.GetWorldMoveDirection(input.MoveVector);

        if (moveDir.sqrMagnitude > 0.001f)
        {
            // 경사면 투영 벡터 계산
            Vector3 slopeDir = Vector3.ProjectOnPlane(moveDir, controller.GroundNormal).normalized;

            // 오르막/내리막 경사도 배율 계산
            float slopeMultiplier = controller.CalculateSlopeMultiplier(moveDir);

            // 최종 속도 적용 (후순위 확장용 ExternalSpeedMultiplier 기본 1.0f)
            float finalSpeed = controller.Settings.walkSpeed * slopeMultiplier * controller.ExternalSpeedMultiplier;
            Vector3 velocity = slopeDir * finalSpeed;

            // 지면 밀착(Stick-to-ground) 하향 벡터 추가
            velocity.y -= controller.Settings.stickToGroundForce;

            controller.CharController.Move(velocity * Time.fixedDeltaTime);
        }

        // 이동 여부와 관계없이 목표 시선 방향으로 회전 처리 (오토에임/시야각 대응)
        controller.RotateTowards(input.LookDirection);
    }
}
```

---

## 6. 메인 컨트롤러 및 경기 리셋 (`PlayerMovementController.cs`)

랜드스케이프의 `ArenaValidator` 규격 동기화, 앉기 해제 시 머리 위 장애물 검사(`CanStandUp`), 그리고 `ArenaReset` 시 상태 초기화(`ResetForEpisode`)를 포함한다.

```csharp
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementController : MonoBehaviour
{
    [SerializeField] private MovementSettingsSO settings;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask obstacleLayer; // 엄폐물·벽 레이어
    [SerializeField] private float groundCheckDistance = 0.3f;

    public MovementSettingsSO Settings => settings;
    public CharacterController CharController { get; private set; }
    public MovementInputData CurrentInput { get; private set; }

    // 지면 & 경사 정보
    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public float CurrentSlopeAngle { get; private set; } = 0f;
    public bool IsCrouching => Mathf.Approximately(CharController.height, settings.crouchingHeight);

    // 후순위 확장용 외부 속도 배율 훅 (MVP 기본값 1.0f)
    public float ExternalSpeedMultiplier { get; set; } = 1.0f;

    private IPlayerInputProvider inputProvider;

    // HFSM Root States
    private BaseState currentRootState;
    public GroundedSuperState GroundedState { get; private set; }
    public AirborneState AirborneState { get; private set; }
    public DeadState DeadState { get; private set; }

    private void Awake()
    {
        CharController = GetComponent<CharacterController>();
        ApplyColliderSettings();

        inputProvider = GetComponent<IPlayerInputProvider>();
        if (inputProvider == null)
        {
            inputProvider = gameObject.AddComponent<PlayerKeyboardInput>();
        }

        GroundedState = new GroundedSuperState(this, null);
        AirborneState = new AirborneState(this, null);
        DeadState = new DeadState(this, null);
        currentRootState = GroundedState;
    }

    private void Start()
    {
        currentRootState.Enter();
    }

    private void Update()
    {
        CurrentInput = inputProvider.GetInput();
        currentRootState.Update();
    }

    private void FixedUpdate()
    {
        UpdateGroundInfo();
        currentRootState.FixedUpdate();
    }

    private void ApplyColliderSettings()
    {
        CharController.radius = settings.colliderRadius;
        CharController.stepOffset = settings.stepOffset;
        SetColliderHeight(settings.standingHeight);
    }

    public void ChangeRootState(BaseState newState)
    {
        if (currentRootState == newState) return;
        currentRootState.Exit();
        currentRootState = newState;
        currentRootState.Enter();
    }

    /// <summary>
    /// 랜드스케이프 ArenaReset 호출 시 스폰 위치 워프 및 이동 상태 완전 초기화
    /// </summary>
    public void ResetForEpisode(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        CharController.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        SetColliderHeight(settings.standingHeight);
        CharController.enabled = true;

        IsGrounded = true;
        GroundNormal = Vector3.up;
        CurrentSlopeAngle = 0f;
        ExternalSpeedMultiplier = 1.0f;
        CurrentInput = default;

        ChangeRootState(GroundedState);
    }

    private void UpdateGroundInfo()
    {
        Vector3 origin = transform.position + Vector3.up * (CharController.radius + 0.05f);
        float radius = CharController.radius * 0.9f;
        float distance = CharController.radius + groundCheckDistance;

        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance, groundLayer, QueryTriggerInteraction.Ignore))
        {
            IsGrounded = true;
            GroundNormal = hit.normal;
            CurrentSlopeAngle = Vector3.Angle(Vector3.up, GroundNormal);
        }
        else
        {
            IsGrounded = false;
            GroundNormal = Vector3.up;
            CurrentSlopeAngle = 0f;
        }
    }

    /// <summary>
    /// 앉기 상태에서 서기(2.0m)로 복귀할 때 머리 위 장애물 충돌 여부 검사
    /// </summary>
    public bool CanStandUp()
    {
        if (!IsCrouching) return true;

        float radius = settings.colliderRadius * 0.95f;
        Vector3 lower = transform.position + Vector3.up * radius;
        Vector3 upper = transform.position + Vector3.up * (settings.standingHeight - radius);

        return !Physics.CheckCapsule(lower, upper, radius, obstacleLayer, QueryTriggerInteraction.Ignore);
    }

    public Vector3 GetWorldMoveDirection(Vector2 moveVector)
    {
        // AI 에이전트 입력 시 월드 기준 또는 캐릭터 로컬 기준 통일
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        return (right * moveVector.x + forward * moveVector.y).normalized;
    }

    public float CalculateSlopeMultiplier(Vector3 moveDirection)
    {
        if (CurrentSlopeAngle <= 15.0f) return 1.0f;

        Vector3 slopeHorizontal = Vector3.ProjectOnPlane(GroundNormal, Vector3.up).normalized;
        float dot = Vector3.Dot(moveDirection, slopeHorizontal);

        if (dot < 0f) // 오르막
        {
            float t = CurrentSlopeAngle / settings.maxSlopeLimit;
            return Mathf.Clamp(Mathf.Cos(CurrentSlopeAngle * Mathf.Deg2Rad) * (1.0f - (t * t * 0.6f)), 0.15f, 1.0f);
        }
        else // 내리막
        {
            return 1.05f;
        }
    }

    public void RotateTowards(Vector3 lookDirection)
    {
        Vector3 flatDir = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
        if (flatDir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, settings.rotationSpeed * Time.fixedDeltaTime);
    }

    public void SetColliderHeight(float height)
    {
        CharController.height = height;
        CharController.center = new Vector3(0f, height * 0.5f, 0f);
    }
}
```

---

## 7. 강화학습 (Unity ML-Agents) 연동 인터페이스

게임 베이스(수동 조작) 완료 후 코드 수정 없이 PPO(1 vs 1) 및 MA-POCA(3 vs 3) 에이전트 브릿지로 교체할 수 있도록 설계한다.

```csharp
public class PlayerAgentInputBridge : MonoBehaviour, IPlayerInputProvider
{
    private MovementInputData currentAgentInput;

    // ML-Agents의 Agent.OnActionReceived(ActionBuffers actions)에서 호출
    public void SetActionsFromAgent(float moveX, float moveZ, bool sprint, bool crouch, Vector3 targetLook)
    {
        currentAgentInput = new MovementInputData
        {
            MoveVector = Vector2.ClampMagnitude(new Vector2(moveX, moveZ), 1.0f),
            LookDirection = targetLook,
            IsSprint = sprint,
            IsCrouch = crouch
        };
    }

    public MovementInputData GetInput() => currentAgentInput;
}
```

### ML-Agents 관측치(Observations) 제공 규격
학습 담당(김현석)에게 전달할 이동·자세 관측 벡터(총 6차원):
1. `CharController.velocity / settings.sprintSpeed` (정규화된 3D 속도 벡터, 3차원)
2. `CurrentSlopeAngle / settings.maxSlopeLimit` (정규화된 현재 경사도, 1차원)
3. `IsGrounded ? 1.0f : 0.0f` (지면 접촉 여부, 1차원)
4. `IsCrouching ? 1.0f : 0.0f` (앉기 자세 여부, 1차원)

---

## 8. 구현 범위 및 후순위 기술 명세

### 8.1 MVP 구현 범위 (1차 완료 대상)
- `CharacterController` 기반 8방향 이동(`Walk`, `Sprint`) 및 수평 회전(`RotateTowards`).
- `SphereCast` 지면 감지, 경사면 벡터 투영, 내리막 `Stick-to-Ground`, $15^\circ \sim 75^\circ$ 오르막 감속 공식.
- $75^\circ$ 초과 사면 및 수직 엄폐물 측면 강제 슬라이딩(`SlopeSlideState`).
- 앉기(`CrouchState`) 콜라이더 높이 축소($2.0\text{m} \rightarrow 1.2\text{m}$) 및 `CanStandUp()` 천장 충돌 검증.
- `IPlayerInputProvider` 추상화 및 `ResetForEpisode()` 경기 재설정 연동.

### 8.2 후순위 기능 (Post-MVP 확장 대상)
- **이동 제약 구역(`IMovementZoneModifier`)**: 랜드스케이프 후속 검토 완료 시 `ExternalSpeedMultiplier` 프로퍼티를 통해 늪/흙/웅덩이 배율 주입.
- **피킹(Peeking) 콜라이더 오프셋**: 엄폐물 좌우 피킹 채택 시 `CharacterController.center`의 X축 오프셋 보간 및 벽 관통 방지 레이캐스트 추가.
- **가감속 관성 및 스태미나**: 이동 속도 변화에 대한 `SmoothDamp` 보간 및 전력 질주 스태미나 자원 관리.
- **조준·사격 연동**: 사격 시스템의 `Aiming` 상태 수신 시 이동 속도 감쇄 및 현재 속도에 따른 탄퍼짐(Spread) 데이터 송신.

---

## 9. 구현 순서와 일정 (11월 23일 ~ 29일 목표)

1. **11월 18일**: `MovementSettingsSO`, `CharacterController` 기본 이동·회전, `SphereCast` 지면 감지 및 `PlayerKeyboardInput` 구현.
2. **11월 18일**: HFSM 코어 프레임워크 및 `GroundedSuperState`, `AirborneState`, `WalkState`, `SprintState`, `CrouchState`(`CanStandUp` 포함), `DeadState` 구현.
3. **11월 19일**: 경사면 벡터 투영 알고리즘, $15^\circ \sim 75^\circ$ 사면 감속 및 $75^\circ$ 초과 `SlopeSlideState` 구현.
4. **11월 20일**: 랜드스케이프 고정 아레나(250 × 250) 스폰·통로 주행 테스트, `ResetForEpisode` 리셋 검증 및 `PlayerAgentInputBridge` 연동 완료.