using UnityEngine;
using Reinforce.Movement.HFSM;

namespace Reinforce.Movement
{
    /// <summary>
    /// 플레이어 이동·회전 및 계층형 상태 기계(HFSM) 메인 컨트롤러
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

        // 컴포넌트 & 인터페이스
        public MovementSettingsSO Settings => settings;
        public CharacterController CharController { get; private set; }
        public MovementInputData CurrentInput { get; private set; }
        private IPlayerInputProvider inputProvider;

        // 지면 및 물리 상태 프로퍼티
        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public float CurrentSlopeAngle { get; private set; } = 0f;
        public Vector3 CurrentVelocity => CharController != null ? CharController.velocity : Vector3.zero;
        public bool IsCrouching => settings != null && Mathf.Approximately(CharController.height, settings.crouchingHeight);
        public bool IsDead => currentRootState is DeadState;

        // 후순위 확장용 외부 속도 배율 (기본값 1.0f)
        public float ExternalSpeedMultiplier { get; set; } = 1.0f;

        // HFSM 루트 상태들
        private BaseState currentRootState;
        public GroundedSuperState GroundedState { get; private set; }
        public AirborneState AirborneState { get; private set; }
        public DeadState DeadState { get; private set; }

        public string CurrentStateHierarchyName => currentRootState != null ? currentRootState.GetFullHierarchyName() : "None";

        // 진입부 사면 감지용 임시 법선
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

            // HFSM 초기화
            GroundedState = new GroundedSuperState(this, null);
            AirborneState = new AirborneState(this, null);
            DeadState = new DeadState(this, null);
            currentRootState = GroundedState;
        }

        private void Start()
        {
            ApplyColliderSettings();
            UpdateGroundInfo();
            currentRootState.Enter();
        }

        private void Update()
        {
            if (IsDead) return;

            CurrentInput = inputProvider.GetInput();
            RotateTowards(CurrentInput.LookDirection);
            currentRootState.Update();

            // 사망 테스트용 K 키 입력 지원 (수동 검증)
            if (Input.GetKeyDown(KeyCode.K))
            {
                Die();
            }
        }

        private void FixedUpdate()
        {
            UpdateGroundInfo();
            currentRootState.FixedUpdate();
            hasRecentSlopeHit = false;
        }

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
        /// 지면 상태 정밀 검사 (SphereCast + 전방 사면 검사)
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

            Vector3 worldMove = GetWorldMoveDirection(CurrentInput.MoveVector);
            if (worldMove.sqrMagnitude > 0.001f && IsGrounded)
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
            if (hitAngle > 5f && hitAngle <= settings.maxSlopeLimit)
            {
                Vector3 worldMove = GetWorldMoveDirection(CurrentInput.MoveVector);
                if (worldMove.sqrMagnitude > 0.001f)
                {
                    Vector3 slopeHorizontal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                    if (Vector3.Dot(worldMove, slopeHorizontal) < 0f)
                    {
                        hitSlopeNormal = hit.normal;
                        hasRecentSlopeHit = true;
                    }
                }
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
            SetColliderHeight(settings.standingHeight);
            CharController.enabled = true;

            IsGrounded = true;
            GroundNormal = Vector3.up;
            CurrentSlopeAngle = 0f;
            ExternalSpeedMultiplier = 1.0f;
            CurrentInput = default;

            Revive();
        }

        private void OnGUI()
        {
            if (!showDebugHUD) return;

            const int width = 380;
            const int height = 230;
            GUI.Box(new Rect(10, 10, width, height), "[이동 HFSM 디버그 HUD - 이용현]");

            GUILayout.BeginArea(new Rect(20, 35, width - 20, height - 40));
            
            // HFSM 현재 상태 표시 (핵심)
            string stateColor = IsDead ? "red" : (currentRootState is AirborneState ? "yellow" : "cyan");
            GUILayout.Label($"HFSM 상태: <color={stateColor}><b>{CurrentStateHierarchyName}</b></color>", new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.Label($"위치: {transform.position:F2}");
            float hSpeed = new Vector3(CurrentVelocity.x, 0, CurrentVelocity.z).magnitude;
            GUILayout.Label($"수평 속도: {hSpeed:F2} m/s (설정: {(CurrentInput.IsSprint ? settings.sprintSpeed : settings.walkSpeed)} m/s)");
            GUILayout.Label($"지면 접촉 (IsGrounded): {(IsGrounded ? "<color=lime>TRUE (지상)</color>" : "<color=red>FALSE (공중)</color>")}", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
            GUILayout.Label($"지면 경사도 (Slope): {CurrentSlopeAngle:F1}° / 등반한계: {settings.maxSlopeLimit}°");
            GUILayout.Label($"이동 입력: ({CurrentInput.MoveVector.x:F1}, {CurrentInput.MoveVector.y:F1}) | 사망 테스트: [K 키]");
            GUILayout.EndArea();
        }

        private void OnDrawGizmosSelected()
        {
            if (CharController == null) return;

            float radius = CharController.radius * 0.95f;
            float bottomSphereOffsetY = CharController.center.y - (CharController.height * 0.5f - CharController.radius);
            Vector3 origin = transform.position + new Vector3(0f, bottomSphereOffsetY + 0.05f, 0f);
            Vector3 end = origin + Vector3.down * (0.05f + groundCheckDistance);

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin, radius);
            Gizmos.DrawWireSphere(end, radius);
            Gizmos.DrawLine(origin, end);

            if (IsGrounded)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(transform.position, GroundNormal * 1.5f);
            }
        }
    }
}