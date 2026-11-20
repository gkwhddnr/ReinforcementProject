using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class SprintState : BaseState
    {
        public SprintState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            MovementInputData input = controller.CurrentInput;
            Vector3 worldMove = controller.GetWorldMoveDirection(input.MoveVector);

            if (worldMove.sqrMagnitude > 0.001f)
            {
                Vector3 slopeDir = Vector3.ProjectOnPlane(worldMove, controller.GroundNormal).normalized;
                float slopeMultiplier = controller.CalculateSlopeMultiplier(worldMove);
                float finalSpeed = controller.Settings.sprintSpeed * slopeMultiplier * controller.ExternalSpeedMultiplier;

                Vector3 velocity = slopeDir * finalSpeed;

                if (controller.CurrentSlopeAngle <= 15.0f)
                {
                    velocity.y -= controller.Settings.stickToGroundForce * 0.5f;
                }

                controller.CharController.Move(velocity * Time.fixedDeltaTime);
            }
        }
    }
}