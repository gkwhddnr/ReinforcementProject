using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    /// <summary>
    /// 앉기 상태 (콜라이더 축소, 2.0 m/s 저속 이동, 기립 공간 부족 시 유지)
    /// </summary>
    public class CrouchState : BaseState
    {
        public CrouchState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void Enter()
        {
            base.Enter();
            // 콜라이더 높이를 앉기 키(1.2m)로 축소 및 비주얼 조정
            controller.SetCrouchVisual(true);
        }

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
                float finalSpeed = controller.Settings.crouchSpeed * slopeMultiplier * controller.ExternalSpeedMultiplier;

                Vector3 velocity = slopeDir * finalSpeed;

                // 내리막 및 평지 지면 밀착
                Vector3 slopeHorizontal = Vector3.ProjectOnPlane(controller.GroundNormal, Vector3.up).normalized;
                float dot = Vector3.Dot(worldMove, slopeHorizontal);

                if (dot >= 0f)
                {
                    velocity.y -= controller.Settings.stickToGroundForce;
                }

                controller.CharController.Move(velocity * Time.fixedDeltaTime);
            }
            else
            {
                // 앉은 상태 정지 시 지면 밀착 유지
                Vector3 velocity = new Vector3(0f, -controller.Settings.stickToGroundForce, 0f);
                controller.CharController.Move(velocity * Time.fixedDeltaTime);
            }
        }

        public override void Exit()
        {
            base.Exit();
            // 기립 시 콜라이더 높이를 서기 키(2.0m)로 복원 및 비주얼 복구
            controller.SetCrouchVisual(false);
        }
    }
}
