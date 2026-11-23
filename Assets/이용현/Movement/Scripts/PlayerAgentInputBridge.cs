using UnityEngine;

namespace Reinforce.Movement
{
    /// <summary>
    /// ML-Agents Agent와 PlayerMovementController를 연결하는 입력 브릿지
    /// Agent.OnActionReceived에서 행동 벡터를 받아 MovementInputData로 변환 및 주입합니다.
    /// IPlayerInputProvider를 구현하므로 PlayerMovementController의 수정 없이 키보드 입력과 1:1 교체 가능합니다.
    /// </summary>
    public class PlayerAgentInputBridge : MonoBehaviour, IPlayerInputProvider
    {
        private MovementInputData currentAgentInput;

        [Header("Manual / Debug Simulation")]
        [Tooltip("체크 시 가상 에이전트 행동 벡터를 자동 주입하여 키보드 없이 자율 주행을 테스트합니다.")]
        [SerializeField] private bool simulateAgentActions = false;
        [SerializeField] private Vector2 simulatedMoveVector = new Vector2(0f, 1f);
        [SerializeField] private bool simulatedSprint = false;
        [SerializeField] private bool simulatedCrouch = false;
        [SerializeField] private bool simulatedFire = false;

        private void Update()
        {
            if (simulateAgentActions)
            {
                SetActionsFromAgent(simulatedMoveVector.x, simulatedMoveVector.y, simulatedSprint, simulatedCrouch, simulatedFire, transform.forward);
            }
        }

        /// <summary>
        /// ML-Agents Agent.OnActionReceived(ActionBuffers actions) 또는 수동 호출에서 전달받은 값 주입
        /// </summary>
        /// <param name="moveX">수평 이동 입력 (-1.0 ~ 1.0)</param>
        /// <param name="moveZ">전후 이동 입력 (-1.0 ~ 1.0)</param>
        /// <param name="sprint">전력 질주 여부</param>
        /// <param name="crouch">앉기 여부</param>
        /// <param name="fire">발사 유지 여부</param>
        /// <param name="targetLook">목표 시선/조준 월드 방향 벡터</param>
        public void SetActionsFromAgent(float moveX, float moveZ, bool sprint, bool crouch, bool fire, Vector3 targetLook)
        {
            currentAgentInput = new MovementInputData
            {
                MoveVector = Vector2.ClampMagnitude(new Vector2(moveX, moveZ), 1.0f),
                LookDirection = targetLook.sqrMagnitude > 0.001f ? targetLook.normalized : transform.forward,
                IsSprint = sprint,
                IsCrouch = crouch,
                IsFire = fire
            };
        }

        public void SetActionsFromAgent(float moveX, float moveZ, bool sprint, bool crouch, Vector3 targetLook)
        {
            SetActionsFromAgent(moveX, moveZ, sprint, crouch, false, targetLook);
        }

        /// <summary>
        /// 연속/이산 행동 배열을 직접 주입하는 오버로드 (ML-Agents 표준 규격)
        /// continuousActions[0]: moveX (-1 ~ 1)
        /// continuousActions[1]: moveZ (-1 ~ 1)
        /// continuousActions[2]: targetLook.x (-1 ~ 1)
        /// continuousActions[3]: targetLook.z (-1 ~ 1)
        /// discreteActions[0]: 0=Walk, 1=Sprint, 2=Crouch
        /// discreteActions[1]: 0=Hold, 1=Fire
        /// </summary>
        public void SetActionsFromArrays(float[] continuousActions, int[] discreteActions)
        {
            float moveX = continuousActions != null && continuousActions.Length > 0 ? continuousActions[0] : 0f;
            float moveZ = continuousActions != null && continuousActions.Length > 1 ? continuousActions[1] : 0f;

            Vector3 lookDir = transform.forward;
            if (continuousActions != null && continuousActions.Length >= 4)
            {
                Vector3 inputLook = new Vector3(continuousActions[2], 0f, continuousActions[3]);
                if (inputLook.sqrMagnitude > 0.001f)
                {
                    lookDir = inputLook.normalized;
                }
            }

            bool sprint = false;
            bool crouch = false;
            bool fire = false;

            if (discreteActions != null && discreteActions.Length > 0)
            {
                int posture = discreteActions[0];
                if (posture == 1) sprint = true;
                else if (posture == 2) crouch = true;
            }

            if (discreteActions != null && discreteActions.Length > 1)
            {
                fire = discreteActions[1] == 1;
            }

            SetActionsFromAgent(moveX, moveZ, sprint, crouch, fire, lookDir);
        }

        public MovementInputData GetInput() => currentAgentInput;

        public void ClearInput()
        {
            currentAgentInput = default;
        }
    }
}
