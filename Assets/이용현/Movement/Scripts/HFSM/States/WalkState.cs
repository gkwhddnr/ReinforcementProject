using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class WalkState : BaseState
    {
        public WalkState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            MovementInputData input = controller.CurrentInput;
            Vector3 worldMove = controller.GetWorldMoveDirection(input.MoveVector);

            if (worldMove.sqrMagnitude > 0.001f)
            {
                // 경사면 투영 벡터 계산
                Vector3 slopeDir = Vector3.ProjectOnPlane(worldMove, controller.GroundNormal).normalized;
                float slopeMultiplier = controller.CalculateSlopeMultiplier(worldMove);
                float finalSpeed = controller.Settings.walkSpeed * slopeMultiplier * controller.ExternalSpeedMultiplier;

                Vector3 velocity = slopeDir * finalSpeed;

                // 완경사/평지 지면 밀착력 보조
                if (controller.CurrentSlopeAngle <= 15.0f)
                {
                    velocity.y -= controller.Settings.stickToGroundForce * 0.5f;
                }

                controller.CharController.Move(velocity * Time.fixedDeltaTime);
            }
        }
    }
}