using UnityEngine;

namespace Reinforce.Movement
{
    /// <summary>
    /// 플레이어 이동·회전 및 지면/경사 검사 메인 컨트롤러 (MVP 1단계 - 경사 등반 최적화)
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

        // 후순위 확장용 외부 속도 배율 (기본값 1.0f)
        public float ExternalSpeedMultiplier { get; set; } = 1.0f;

        // 수직 속도 누적값 (낙하 중력)
        private float verticalVelocity = 0f;

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
        }

        private void Start()
        {
            ApplyColliderSettings();
            UpdateGroundInfo();
        }

        private void Update()
        {
            CurrentInput = inputProvider.GetInput();
            RotateTowards(CurrentInput.LookDirection);
        }

        private void FixedUpdate()
        {
            UpdateGroundInfo();
            ProcessMovement();
            hasRecentSlopeHit = false;
        }

        public void ApplyColliderSettings()
        {
            if (CharController == null || settings == null) return;
            CharController.radius = settings.colliderRadius;
            CharController.stepOffset = settings.stepOffset;
            // 핵심: 75도 등반을 허용하기 위해 slopeLimit을 최대 등반각(75도)으로 동기화
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
                // 이동 중 경사면에 닿은 충돌 법선 활용
                IsGrounded = true;
                GroundNormal = hitSlopeNormal;
            }
            else if (CharController.isGrounded)
            {
                IsGrounded = true;
                GroundNormal = Vector3.up;
            }
            else
            {
                IsGrounded = false;
                GroundNormal = Vector3.up;
            }

            // 이동 중일 때 진입할 앞쪽 지면(0.3m 앞)을 미리 레이캐스트하여 사면 감지
            Vector3 worldMove = GetWorldMoveDirection(CurrentInput.MoveVector);
            if (worldMove.sqrMagnitude > 0.001f && IsGrounded)
            {
                Vector3 forwardOrigin = origin + worldMove.normalized * 0.3f;
                if (Physics.Raycast(forwardOrigin, Vector3.down, out RaycastHit forwardHit, distance + 0.4f, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    float forwardSlope = Vector3.Angle(Vector3.up, forwardHit.normal);
                    if (forwardSlope > 5f && forwardSlope <= settings.maxSlopeLimit)
                    {
                        // 전방 경사로 진입 시 법선을 전방 사면 법선으로 부드럽게 보정
                        GroundNormal = forwardHit.normal;
                    }
                }
            }

            CurrentSlopeAngle = Vector3.Angle(Vector3.up, GroundNormal);
        }

        /// <summary>
        /// 경사면 벡터 투영(Slope Vector Projection)을 반영한 이동 처리
        /// </summary>
        private void ProcessMovement()
        {
            Vector3 worldMove = GetWorldMoveDirection(CurrentInput.MoveVector);

            float baseSpeed = settings.walkSpeed;
            if (CurrentInput.IsSprint && !CurrentInput.IsCrouch)
            {
                baseSpeed = settings.sprintSpeed;
            }
            else if (CurrentInput.IsCrouch)
            {
                baseSpeed = settings.crouchSpeed;
            }

            // 1. 오르막/내리막 경사도 감속 배율 계산
            float slopeMultiplier = CalculateSlopeMultiplier(worldMove);
            float finalSpeed = baseSpeed * slopeMultiplier * ExternalSpeedMultiplier;

            Vector3 moveMotion = Vector3.zero;

            if (worldMove.sqrMagnitude > 0.001f)
            {
                if (IsGrounded && CurrentSlopeAngle <= settings.maxSlopeLimit)
                {
                    // 경사면 법선에 이동 벡터 투영 -> 경사면을 따라 위로 오르는 Y성분 자연 생성!
                    Vector3 slopeMoveDir = Vector3.ProjectOnPlane(worldMove, GroundNormal).normalized;
                    moveMotion = slopeMoveDir * finalSpeed;

                    // 완경사(15도 이하)나 평지일 때만 지면 밀착 보조력 적용
                    if (CurrentSlopeAngle <= 15.0f)
                    {
                        moveMotion.y -= settings.stickToGroundForce * 0.5f;
                    }
                }
                else
                {
                    // 공중 체공 또는 75도 초과 경사
                    moveMotion = worldMove * finalSpeed;
                }
            }

            // 2. 수직 중력 계산
            if (IsGrounded)
            {
                if (worldMove.sqrMagnitude <= 0.001f)
                {
                    // 정지 상태일 때 지면 밀착
                    verticalVelocity = -settings.stickToGroundForce;
                    moveMotion.y = verticalVelocity;
                }
                else
                {
                    verticalVelocity = moveMotion.y;
                }
            }
            else
            {
                // 공중 체공 시 중력 가속
                verticalVelocity -= settings.gravity * Time.fixedDeltaTime;
                verticalVelocity = Mathf.Max(verticalVelocity, -30f);
                moveMotion.y = verticalVelocity;
            }

            CharController.Move(moveMotion * Time.fixedDeltaTime);
        }

        /// <summary>
        /// 이동 중 경사면/벽 충돌 시 법선 수집
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            float hitAngle = Vector3.Angle(Vector3.up, hit.normal);
            if (hitAngle > 5f && hitAngle <= settings.maxSlopeLimit)
            {
                // 오르막 방향인지 확인
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
                // 개발서 명시 공식: cos(theta) * (1.0 - (theta/75)^2 * 0.6)
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
            verticalVelocity = 0f;
            ExternalSpeedMultiplier = 1.0f;
            CurrentInput = default;
        }

        private void OnGUI()
        {
            if (!showDebugHUD) return;

            const int width = 360;
            const int height = 210;
            GUI.Box(new Rect(10, 10, width, height), "[이동 모듈 디버그 HUD - 이용현]");

            GUILayout.BeginArea(new Rect(20, 35, width - 20, height - 40));
            GUILayout.Label($"위치: {transform.position:F2}");
            float hSpeed = new Vector3(CurrentVelocity.x, 0, CurrentVelocity.z).magnitude;
            GUILayout.Label($"수평 속도: {hSpeed:F2} m/s (설정: {(CurrentInput.IsSprint ? settings.sprintSpeed : (CurrentInput.IsCrouch ? settings.crouchSpeed : settings.walkSpeed))} m/s)");
            GUILayout.Label($"지면 접촉 (IsGrounded): {(IsGrounded ? "<color=lime>TRUE (지상)</color>" : "<color=red>FALSE (공중)</color>")}", new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
            GUILayout.Label($"지면 경사도 (Slope): {CurrentSlopeAngle:F1}° / 등반한계: {settings.maxSlopeLimit}°");
            GUILayout.Label($"이동 입력 (WASD): ({CurrentInput.MoveVector.x:F1}, {CurrentInput.MoveVector.y:F1})");
            GUILayout.Label($"자세/상태: {(CurrentInput.IsSprint ? "[전력 질주 (Shift)]" : (CurrentInput.IsCrouch ? "[앉기 (Ctrl)]" : "[보통 걷기]"))}");
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