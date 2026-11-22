using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class IdleState : BaseState
    {
        public IdleState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            // 정지 상태에서도 지면 밀착력 유지
            Vector3 velocity = new Vector3(0f, -controller.Settings.stickToGroundForce, 0f);
            controller.CharController.Move(velocity * Time.fixedDeltaTime);
        }
    }
}