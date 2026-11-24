using UnityEngine;
using Reinforce.Movement.HFSM;

namespace Reinforce.Movement
{
    /// <summary>
    /// 플레이어 이동·회전 및 계층형 상태 기계(HFSM) 메인 컨트롤러 (급경사 슬라이딩 완벽 지원)
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovementController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private MovementSettingsSO settings;

        [Header("Ground Detection")]
        [SerializeField] private LayerMask groundLayer = ~0;
        [SerializeField] private float groundCheckDistance = 0.25f;

        [Header("Debug HUD")]
        [SerializeField] private bool showDebugHUD = true;
        [SerializeField] private bool enableDemoControls = true;

        // 컴포넌트 & 인터페이스
        public MovementSettingsSO Settings => settings;
        public CharacterController CharController { get; private set; }
        public MovementInputData CurrentInput { get; private set; }
        public IPlayerInputProvider InputProvider => inputProvider;
        private IPlayerInputProvider inputProvider;

        public void SetInputProvider(IPlayerInputProvider provider)
        {
            if (provider != null)
            {
                inputProvider = provider;
            }
        }

        // 지면 및 물리 상태 프로퍼티
        public bool IsGrounded { get; private set; }
        public bool IsTouchingSteepSlope { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float CurrentSlopeAngle { get; private set; } = 0f;
        public Vector3 CurrentVelocity => CharController != null ? CharController.velocity : Vector3.zero;
        public bool IsCrouching => CharController != null && settings != null && Mathf.Approximately(CharController.height, settings.crouchingHeight);
        public bool IsDead => currentRootState is DeadState;

        // 조원별 전달 요청서 규격 프로퍼티 (김현석 / 이승재 연동용)
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 Velocity => CurrentVelocity;
        public float MaximumSpeed => settings != null ? settings.sprintSpeed : 6.5f;
        public PostureState Posture => IsCrouching ? PostureState.Crouching : PostureState.Standing;
        public bool IsReady => CharController != null && settings != null;
        public event System.Action<PostureState> OnPostureChanged;

        // 후순위 확장용 외부 속도 배율 (기본값 1.0f)
        public float ExternalSpeedMultiplier { get; set; } = 1.0f;

        // HFSM 루트 상태들
        private BaseState currentRootState;
        public GroundedSuperState GroundedState { get; private set; }
        public AirborneState AirborneState { get; private set; }
        public SlopeSlideState SlopeSlideState { get; private set; }
        public DeadState DeadState { get; private set; }

        public string CurrentStateHierarchyName => currentRootState != null ? currentRootState.GetFullHierarchyName() : "None";

        // 진입부/측면 사면 충돌 감지용 임시 법선
        private Vector3 hitSlopeNormal = Vector3.up;
        private bool hasRecentSlopeHit = false;

        private void Awake()
        {
            CharController = GetComponent<CharacterController>();

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MovementSettingsSO>();
            }

            ApplyColliderSettings();

            inputProvider = GetComponent<IPlayerInputProvider>();
            if (inputProvider == null)
            {
                inputProvider = gameObject.AddComponent<PlayerKeyboardInput>();
            }

            // HFSM 루트 상태 초기화
            GroundedState = new GroundedSuperState(this, null);
            AirborneState = new AirborneState(this, null);
            SlopeSlideState = new SlopeSlideState(this, null);
            DeadState = new DeadState(this, null);
            currentRootState = GroundedState;
        }

        private void Start()
        {
            ApplyColliderSettings();
            UpdateGroundInfo();
            currentRootState.Enter();

#if UNITY_EDITOR
            if (enableDemoControls)
            {
                EnsureCrouchObstacleExists();
                EnsureCombatManagerExists();
            }
#endif
        }

#if UNITY_EDITOR
        private void EnsureCrouchObstacleExists()
        {
            if (FindFirstObjectByType<CrouchTestObstacle>() == null)
            {
                GameObject tunnel = new GameObject("CrouchTestObstacle_Tunnel");
                tunnel.transform.position = new Vector3(0f, 0f, -5f);
                tunnel.AddComponent<CrouchTestObstacle>();
            }
        }

        private void EnsureCombatManagerExists()
        {
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name != null && activeScene.name.Contains("Combat"))
            {
                if (FindFirstObjectByType<Reinforce.Integration.CombatIntegrationManager>() == null)
                {
                    GameObject mgr = new GameObject("CombatIntegrationManager");
                    mgr.AddComponent<Reinforce.Integration.CombatIntegrationManager>();
                }
            }
        }
#endif

        private void Update()
        {
            if (IsDead) return;

            CurrentInput = inputProvider.GetInput();
            RotateTowards(CurrentInput.LookDirection);
            currentRootState.Update();

            if (!enableDemoControls)
                return;

            if (Input.GetKeyDown(KeyCode.K))
            {
                Die();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetForEpisode(new Vector3(0f, 1f, 0f), Quaternion.identity);
            }
        }

        private void FixedUpdate()
        {
            UpdateGroundInfo();

            // 75도 초과 급경사 접촉 시 즉시 SlopeSlideState로 전이
            if (IsTouchingSteepSlope && !(currentRootState is SlopeSlideState))
            {
                ChangeRootState(SlopeSlideState);
            }

            currentRootState.FixedUpdate();
            hasRecentSlopeHit = false;
        }

        private readonly Collider[] standUpCheckBuffer = new Collider[8];

        public void ApplyColliderSettings()
        {
            if (CharController == null || settings == null) return;
            CharController.radius = settings.colliderRadius;
            CharController.stepOffset = settings.stepOffset;
            CharController.slopeLimit = Mathf.Max(settings.maxSlopeLimit, 75.0f);
            SetColliderHeight(settings.standingHeight);
        }

        public void SetColliderHeight(float height)
        {
            if (CharController == null) return;
            CharController.height = height;
            CharController.center = new Vector3(0f, height * 0.5f, 0f);
        }

        /// <summary>
        /// 앉기/서기 상태에 따라 CharacterController 높이 및 시각적 모델(Visual/Visor) 크기/위치 동기화
        /// </summary>
        public void SetCrouchVisual(bool isCrouch)
        {
            if (settings == null) return;
            float targetHeight = isCrouch ? settings.crouchingHeight : settings.standingHeight;
            SetColliderHeight(targetHeight);

            Transform visual = transform.Find("Visual");
            if (visual != null)
            {
                float visualScaleY = targetHeight / settings.standingHeight;
                visual.localScale = new Vector3(1f, visualScaleY, 1f);
                visual.localPosition = new Vector3(0f, targetHeight * 0.5f, 0f);
            }

            Transform visor = transform.Find("Visor");
            if (visor != null)
            {
                float visorY = isCrouch ? 0.9f : 1.5f;
                visor.localPosition = new Vector3(0f, visorY, 0.4f);
            }

            // 사격 모듈 연동용 Eye 및 Mount 오프셋 동기화
            Transform eye = transform.Find("Eye");
            if (eye != null)
            {
                eye.localPosition = new Vector3(0f, targetHeight - 0.18f, 0f);
            }

            Transform mount = transform.Find("Mount");
            if (mount != null)
            {
                mount.localPosition = new Vector3(0f, targetHeight * 0.62f, 0f);
            }

            OnPostureChanged?.Invoke(Posture);
        }

        /// <summary>
        /// 앉기 상태에서 서기(2.0m)로 복귀할 때 머리 위 장애물 충돌 여부 검사
        /// </summary>
        public bool CanStandUp()
        {
            if (!IsCrouching) return true;

            float radius = settings.colliderRadius * 0.95f;
            // 머리 위 공간만 정밀 검사하기 위해 앉은 키 높이부터 서는 키 높이까지 캡슐 검사 (바닥 오탐지 원천 방지)
            Vector3 lower = transform.position + Vector3.up * settings.crouchingHeight;
            Vector3 upper = transform.position + Vector3.up * (settings.standingHeight - radius);

            if (upper.y < lower.y)
            {
                upper = lower;
            }

            int hitCount = Physics.OverlapCapsuleNonAlloc(lower, upper, radius, standUpCheckBuffer, groundLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = standUpCheckBuffer[i];
                if (col != null && col != CharController && col.transform != transform && !col.transform.IsChildOf(transform))
                {
                    return false; // 머리 위에 장애물이 있어 일어설 수 없음
                }
            }

            return true;
        }

        public void ChangeRootState(BaseState newState)
        {
            if (currentRootState == newState || newState == null) return;
            currentRootState?.Exit();
            currentRootState = newState;
            currentRootState?.Enter();
        }

        public void Die()
        {
            ChangeRootState(DeadState);
        }

        public void Revive()
        {
            ChangeRootState(GroundedState);
        }

        /// <summary>
        /// 지면 및 급경사 상태 정밀 검사
        /// </summary>
        private void UpdateGroundInfo()
        {
            if (CharController == null)
            {
                CharController = GetComponent<CharacterController>();
                if (CharController == null) return;
            }

            float radius = CharController.radius * 0.95f;
            float bottomSphereOffsetY = CharController.center.y - (CharController.height * 0.5f - CharController.radius);
            Vector3 origin = transform.position + new Vector3(0f, bottomSphereOffsetY + 0.05f, 0f);
            float distance = 0.05f + groundCheckDistance;

            bool sphereHit = Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance, groundLayer, QueryTriggerInteraction.Ignore);

            if (sphereHit)
            {
                IsGrounded = true;
                GroundNormal = hit.normal;
            }
            else if (hasRecentSlopeHit)
            {
                IsGrounded = true;
                GroundNormal = hitSlopeNormal;
            }
            else
            {
                IsGrounded = false;
                GroundNormal = Vector3.up;
            }

            // 캡슐 하단 주변 급경사(75도 초과) 접촉 정밀 감지 (MeshCollider/TerrainCollider 호환 안전 Raycast 방식)
            bool foundSteepSlope = false;
            float checkDist = radius + 0.25f;
            Vector3[] checkDirs = { transform.forward, -transform.forward, transform.right, -transform.right };

            for (int i = 0; i < checkDirs.Length; i++)
            {
                if (Physics.Raycast(origin, checkDirs[i], out RaycastHit sideHit, checkDist, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    float angle = Vector3.Angle(Vector3.up, sideHit.normal);
                    if (angle > settings.maxSlopeLimit && angle < 89.5f)
                    {
                        foundSteepSlope = true;
                        hitSlopeNormal = sideHit.normal;
                        break;
                    }
                }
            }

            if (foundSteepSlope || (hasRecentSlopeHit && Vector3.Angle(Vector3.up, hitSlopeNormal) > settings.maxSlopeLimit))
            {
                IsTouchingSteepSlope = true;
                GroundNormal = hitSlopeNormal;
            }
            else
            {
                IsTouchingSteepSlope = false;
            }

            // 완경사 전방 사면 감지 보조
            Vector3 worldMove = GetWorldMoveDirection(CurrentInput.MoveVector);
            if (worldMove.sqrMagnitude > 0.001f && IsGrounded && !IsTouchingSteepSlope)
            {
                Vector3 forwardOrigin = origin + worldMove.normalized * 0.3f;
                if (Physics.Raycast(forwardOrigin, Vector3.down, out RaycastHit forwardHit, distance + 0.4f, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    float forwardSlope = Vector3.Angle(Vector3.up, forwardHit.normal);
                    if (forwardSlope > 5f && forwardSlope <= settings.maxSlopeLimit)
                    {
                        GroundNormal = forwardHit.normal;
                    }
                }
            }

            CurrentSlopeAngle = Vector3.Angle(Vector3.up, GroundNormal);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            float hitAngle = Vector3.Angle(Vector3.up, hit.normal);
            if (hitAngle > 5f && hitAngle < 89.5f)
            {
                hitSlopeNormal = hit.normal;
                hasRecentSlopeHit = true;
            }
        }

        public Vector3 GetWorldMoveDirection(Vector2 moveVector)
        {
            if (moveVector.sqrMagnitude < 0.001f) return Vector3.zero;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
                return (right * moveVector.x + forward * moveVector.y).normalized;
            }

            return new Vector3(moveVector.x, 0f, moveVector.y).normalized;
        }

        public float CalculateSlopeMultiplier(Vector3 moveDirection)
        {
            if (CurrentSlopeAngle <= 15.0f || moveDirection.sqrMagnitude < 0.001f) return 1.0f;

            Vector3 slopeHorizontal = Vector3.ProjectOnPlane(GroundNormal, Vector3.up).normalized;
            float dot = Vector3.Dot(moveDirection, slopeHorizontal);

            if (dot < 0f) // 오르막 진행
            {
                float t = CurrentSlopeAngle / settings.maxSlopeLimit;
                return Mathf.Clamp(Mathf.Cos(CurrentSlopeAngle * Mathf.Deg2Rad) * (1.0f - (t * t * 0.6f)), 0.15f, 1.0f);
            }
            else // 내리막 진행
            {
                return 1.05f;
            }
        }

        public void RotateTowards(Vector3 lookDirection)
        {
            Vector3 flatDir = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
            if (flatDir.sqrMagnitude < 0.001f) return;

            Quaternion targetRot = Quaternion.LookRotation(flatDir.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, settings.rotationSpeed * Time.deltaTime);
        }

        public void ResetForEpisode(Vector3 spawnPosition, Quaternion spawnRotation)
        {
            CharController.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            SetCrouchVisual(false); // 서기 자세(2.0m) 및 비주얼 완벽 복구
            CharController.enabled = true;

            IsGrounded = true;
            IsTouchingSteepSlope = false;
            GroundNormal = Vector3.up;
            CurrentSlopeAngle = 0f;
            ExternalSpeedMultiplier = 1.0f;
            CurrentInput = default;

            if (inputProvider is PlayerAgentInputBridge agentBridge)
            {
                agentBridge.ClearInput();
            }

            Revive();
        }

        #region ML-Agents Observations (6-Dimensions)

        /// <summary>
        /// 1. 정규화된 3D 속도 벡터 (3차원) : CharController.velocity / settings.sprintSpeed
        /// </summary>
        public Vector3 NormalizedVelocity
        {
            get
            {
                if (CharController == null || settings == null || settings.sprintSpeed <= 0f) return Vector3.zero;
                return CharController.velocity / settings.sprintSpeed;
            }
        }

        /// <summary>
        /// 2. 정규화된 현재 경사도 (1차원) : CurrentSlopeAngle / settings.maxSlopeLimit
        /// </summary>
        public float NormalizedSlopeAngle
        {
            get
            {
                if (settings == null || settings.maxSlopeLimit <= 0f) return 0f;
                return Mathf.Clamp01(CurrentSlopeAngle / settings.maxSlopeLimit);
            }
        }

        /// <summary>
        /// 3. 지면 접촉 여부 관측치 (1차원) : IsGrounded ? 1.0f : 0.0f
        /// </summary>
        public float GroundedObservation => IsGrounded ? 1.0f : 0.0f;

        /// <summary>
        /// 4. 앉기 자세 여부 관측치 (1차원) : IsCrouching ? 1.0f : 0.0f
        /// </summary>
        public float CrouchingObservation => IsCrouching ? 1.0f : 0.0f;

        /// <summary>
        /// 이동·자세 6차원 관측 데이터를 float 배열로 반환합니다.
        /// [0~2]: NormalizedVelocity (x, y, z)
        /// [3]: NormalizedSlopeAngle
        /// [4]: GroundedObservation
        /// [5]: CrouchingObservation
        /// </summary>
        public float[] GetMovementObservations()
        {
            Vector3 vel = NormalizedVelocity;
            return new float[]
            {
                vel.x,
                vel.y,
                vel.z,
                NormalizedSlopeAngle,
                GroundedObservation,
                CrouchingObservation
            };
        }

        /// <summary>
        /// ML-Agents VectorSensor 또는 버퍼에 6차원 관측치를 직접 추가합니다.
        /// </summary>
        public void CollectMovementObservations(System.Collections.Generic.IList<float> buffer)
        {
            if (buffer == null) return;
            Vector3 vel = NormalizedVelocity;
            buffer.Add(vel.x);
            buffer.Add(vel.y);
            buffer.Add(vel.z);
            buffer.Add(NormalizedSlopeAngle);
            buffer.Add(GroundedObservation);
            buffer.Add(CrouchingObservation);
        }

        #endregion

        private void OnGUI()
        {
            if (!showDebugHUD) return;

            const int width = 450;
            const int height = 290;
            GUI.Box(new Rect(10, 10, width, height), "[이동 HFSM 디버그 HUD - 이용현]");

            GUILayout.BeginArea(new Rect(20, 35, width - 20, height - 40));
            
            // HFSM 현재 상태 표시 (SlopeSlideState는 주황색으로 강조)
            string stateColor = IsDead ? "red" : (currentRootState is SlopeSlideState ? "orange" : (currentRootState is AirborneState ? "yellow" : "cyan"));
            GUILayout.Label($"HFSM 상태: <color={stateColor}><b>{CurrentStateHierarchyName}</b></color>", new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.Label($"위치: {transform.position:F2}");
            float hSpeed = new Vector3(CurrentVelocity.x, 0, CurrentVelocity.z).magnitude;
            float targetSpeed = IsCrouching ? settings.crouchSpeed : (CurrentInput.IsSprint ? settings.sprintSpeed : settings.walkSpeed);
            GUILayout.Label($"수평 속도: {hSpeed:F2} m/s (목표: {targetSpeed:F1} m/s)");
            GUILayout.Label($"자세: {(IsCrouching ? "<color=cyan>앉기 (Crouching, 1.2m)</color>" : "<color=lime>서기 (Standing, 2.0m)</color>")} | 기립 가능: {(CanStandUp() ? "<color=lime>YES</color>" : "<color=red>NO (머리 위 장애물)</color>")}", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
            GUILayout.Label($"지면 접촉: {(IsGrounded ? "<color=lime>TRUE (지상)</color>" : (IsTouchingSteepSlope ? "<color=orange>절벽 접촉</color>" : "<color=red>FALSE (공중)</color>"))}", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
            GUILayout.Label($"지면 경사도 (Slope): {CurrentSlopeAngle:F1}° / 등반한계: {settings.maxSlopeLimit}°");

            // 6차원 관측 데이터 표시
            Vector3 nVel = NormalizedVelocity;
            GUILayout.Label($"ML-Agents 관측치(6D): Vel=({nVel.x:F2}, {nVel.y:F2}, {nVel.z:F2}) | Slp={NormalizedSlopeAngle:F2} | Grd={GroundedObservation:F0} | Crch={CrouchingObservation:F0}", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Italic });
            GUILayout.Label($"입력: 이동({CurrentInput.MoveVector.x:F1}, {CurrentInput.MoveVector.y:F1}) | [R]: 리셋 | [K]: 사망");
            GUILayout.EndArea();
        }

        private void OnDrawGizmosSelected()
        {
            if (CharController == null) return;

            float radius = CharController.radius * 0.95f;
            float bottomSphereOffsetY = CharController.center.y - (CharController.height * 0.5f - CharController.radius);
            Vector3 origin = transform.position + new Vector3(0f, bottomSphereOffsetY + 0.05f, 0f);
            Vector3 end = origin + Vector3.down * (0.05f + groundCheckDistance);

            Gizmos.color = IsGrounded ? Color.green : (IsTouchingSteepSlope ? Color.yellow : Color.red);
            Gizmos.DrawWireSphere(origin, radius);
            Gizmos.DrawWireSphere(end, radius);
            Gizmos.DrawLine(origin, end);

            if (IsGrounded || IsTouchingSteepSlope)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, GroundNormal * 1.5f);
            }

            // 앉은 상태일 때 서기 공간 검사 캡슐 시각화 (기립 가능: 청록, 불가능: 빨강)
            if (IsCrouching && settings != null)
            {
                Vector3 lower = transform.position + Vector3.up * settings.crouchingHeight;
                Vector3 upper = transform.position + Vector3.up * (settings.standingHeight - radius);
                if (upper.y < lower.y) upper = lower;
                Gizmos.color = CanStandUp() ? Color.cyan : Color.red;
                Gizmos.DrawWireSphere(lower, radius);
                Gizmos.DrawWireSphere(upper, radius);
                Gizmos.DrawLine(lower + Vector3.forward * radius, upper + Vector3.forward * radius);
                Gizmos.DrawLine(lower - Vector3.forward * radius, upper - Vector3.forward * radius);
                Gizmos.DrawLine(lower + Vector3.right * radius, upper + Vector3.right * radius);
                Gizmos.DrawLine(lower - Vector3.right * radius, upper - Vector3.right * radius);
            }
        }
    }
}
