using UnityEngine;
using Reinforce.Movement;
using Reinforce.Integration;

namespace ReinProject.Landscape
{
    /// <summary>
    /// OutdoorArena에서 Team A와 Team B 간 1v1 자동 순찰 및 교전 테스트를 위한 봇 컴포넌트
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCombatIntegration))]
    [RequireComponent(typeof(PlayerAgentInputBridge))]
    public class ArenaDuelBot : MonoBehaviour
    {
        [Header("상대방 Combatant")]
        [SerializeField] private PlayerCombatIntegration _opponent;

        [Header("수색/교전 속도 설정")]
        [SerializeField] private float _searchSpeed = 1.0f;
        [SerializeField] private float _combatSpeed = 0.3f;
        [SerializeField] private float _minStopDistance = 15.0f;

        private PlayerCombatIntegration _combat;
        private PlayerAgentInputBridge _bridge;

        public void SetOpponent(PlayerCombatIntegration opponent)
        {
            _opponent = opponent;
        }

        private void Awake()
        {
            _combat = GetComponent<PlayerCombatIntegration>();
            _bridge = GetComponent<PlayerAgentInputBridge>();
        }

        private void Update()
        {
            // 자신이 사망했거나 움직일 준비가 안 되었으면 정지
            if (_combat.Actor == null || !_combat.Actor.Alive || !_combat.IsMovementReady)
            {
                _bridge.SetActionsFromAgent(0f, 0f, false, false, false, transform.forward);
                return;
            }

            // 상대방이 이미 사망했으면 승리 자세 유지 및 정지
            if (_opponent != null && _opponent.Actor != null && !_opponent.Actor.Alive)
            {
                _bridge.SetActionsFromAgent(0f, 0f, false, false, false, transform.forward);
                return;
            }

            // 1. 적을 시야에 포착한 경우 (HasTarget == true)
            if (_combat.HasTarget)
            {
                Vector3 toTarget = _combat.TargetAimPoint - transform.position;
                float distance = toTarget.magnitude;
                toTarget.y = 0f;
                Vector3 lookDir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;

                // 목표 거리에 가까우면 멈추고 사격, 멀면 천천히 전진
                float speed = distance > _minStopDistance ? _combatSpeed : 0f;
                Vector2 moveInput = CalculateMoveInput(lookDir * speed);

                _bridge.SetActionsFromAgent(
                    moveX: moveInput.x,
                    moveZ: moveInput.y,
                    sprint: false,
                    crouch: false,
                    fire: _combat.CanFire,
                    targetLook: lookDir
                );
            }
            // 2. 적을 아직 못 본 경우 -> 상대방 위치(또는 맵 중앙)를 향해 수색 전진
            else
            {
                Vector3 targetPos = _opponent != null ? _opponent.transform.position : Vector3.zero;
                Vector3 toDest = targetPos - transform.position;
                toDest.y = 0f;
                Vector3 moveDir = toDest.sqrMagnitude > 0.001f ? toDest.normalized : transform.forward;

                Vector2 moveInput = CalculateMoveInput(moveDir * _searchSpeed);

                _bridge.SetActionsFromAgent(
                    moveX: moveInput.x,
                    moveZ: moveInput.y,
                    sprint: false,
                    crouch: false,
                    fire: false,
                    targetLook: moveDir
                );
            }
        }

        private Vector2 CalculateMoveInput(Vector3 worldMove)
        {
            if (worldMove.sqrMagnitude < 0.001f)
                return Vector2.zero;

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
                float x = Vector3.Dot(worldMove, right);
                float z = Vector3.Dot(worldMove, forward);
                return Vector2.ClampMagnitude(new Vector2(x, z), 1.0f);
            }

            return Vector2.ClampMagnitude(new Vector2(worldMove.x, worldMove.z), 1.0f);
        }
    }
}
