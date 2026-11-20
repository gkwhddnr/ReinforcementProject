using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class AirborneState : BaseState
    {
        private float verticalVelocity = 0f;

        public AirborneState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void Enter()
        {
            base.Enter();
            // 체공 진입 시 현재 하향 속도 유지 또는 0에서 시작
            verticalVelocity = Mathf.Min(controller.CurrentVelocity.y, 0f);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            // 1. 착지 검사 -> GroundedState로 즉시 복귀
            if (controller.IsGrounded)
            {
                controller.ChangeRootState(controller.GroundedState);
                return;
            }

            // 2. 공중 중력 가속도 (2g = 19.6 m/s^2)
            verticalVelocity -= controller.Settings.gravity * Time.fixedDeltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, -30f); // 종단 속도 제한

            // 3. 공중 수평 관성/제어 이동
            MovementInputData input = controller.CurrentInput;
            Vector3 worldMove = controller.GetWorldMoveDirection(input.MoveVector);
            float airSpeed = controller.Settings.walkSpeed * 0.8f;

            Vector3 motion = worldMove * airSpeed;
            motion.y = verticalVelocity;

            controller.CharController.Move(motion * Time.fixedDeltaTime);
        }
    }
}