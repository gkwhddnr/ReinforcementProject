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

                // 내리막 및 평지 지면 밀착 (Stick-to-ground)
                Vector3 slopeHorizontal = Vector3.ProjectOnPlane(controller.GroundNormal, Vector3.up).normalized;
                float dot = Vector3.Dot(worldMove, slopeHorizontal);

                if (dot >= 0f)
                {
                    // 평지 또는 내리막: 지면 이탈(체공 오인) 방지 하향 밀착 속도 적용
                    velocity.y -= controller.Settings.stickToGroundForce;
                }

                controller.CharController.Move(velocity * Time.fixedDeltaTime);
            }
        }
    }
}