using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    /// <summary>
    /// 75도 초과 등반 불가 급경사/절벽 접촉 시 강제 하향 슬라이딩 상태
    /// </summary>
    public class SlopeSlideState : BaseState
    {
        private float verticalVelocity = 0f;

        public SlopeSlideState(PlayerMovementController controller, BaseState parent = null) : base(controller, parent) { }

        public override void Enter()
        {
            base.Enter();
            verticalVelocity = 0f;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            // 1. 완경사(75도 이하)나 평지에 닿으면 즉시 GroundedState로 복귀
            if (controller.IsGrounded && controller.CurrentSlopeAngle <= controller.Settings.maxSlopeLimit)
            {
                controller.ChangeRootState(controller.GroundedState);
                return;
            }

            // 2. 절벽/사면에서 완전히 떨어져 허공에 뜨면 AirborneState로 전이
            if (!controller.IsGrounded && !controller.IsTouchingSteepSlope)
            {
                controller.ChangeRootState(controller.AirborneState);
                return;
            }

            // 3. 75도 초과 사면을 타고 아래로 미끄러지는 이동 처리
            Vector3 normal = controller.GroundNormal;
            Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, normal).normalized;

            Vector3 velocity = slideDir * controller.Settings.slideSpeed;

            verticalVelocity -= controller.Settings.gravity * Time.fixedDeltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, -30f);
            velocity.y += verticalVelocity;

            controller.CharController.Move(velocity * Time.fixedDeltaTime);
            controller.RotateTowards(slideDir);
        }

        public override void Exit()
        {
            base.Exit();
            verticalVelocity = 0f;
        }
    }
}
