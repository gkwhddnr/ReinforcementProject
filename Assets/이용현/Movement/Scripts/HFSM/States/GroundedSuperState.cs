using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class GroundedSuperState : BaseState
    {
        private IdleState idleState;
        private WalkState walkState;
        private SprintState sprintState;
        private CrouchState crouchState;
        private SlopeSlideState slopeSlideState;

        public GroundedSuperState(PlayerMovementController controller, BaseState parent) : base(controller, parent)
        {
            idleState = new IdleState(controller, this);
            walkState = new WalkState(controller, this);
            sprintState = new SprintState(controller, this);
            crouchState = new CrouchState(controller, this);
            slopeSlideState = new SlopeSlideState(controller, this);
        }

        public override void Enter()
        {
            base.Enter();
            // 기본 상태는 Idle
            SetSubState(idleState);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            // 1. 75도 초과 급경사 사면 접촉 시 즉시 루트 SlopeSlideState로 전이
            if (controller.IsTouchingSteepSlope || controller.CurrentSlopeAngle > controller.Settings.maxSlopeLimit)
            {
                controller.ChangeRootState(controller.SlopeSlideState);
                return;
            }

            // 2. 공중 이탈 검사 -> AirborneState로 전이
            if (!controller.IsGrounded)
            {
                controller.ChangeRootState(controller.AirborneState);
                return;
            }

            // 3. 앉기 및 기립 공간 검사 기반 하위 상태 전이 규칙
            MovementInputData input = controller.CurrentInput;
            bool hasMoveInput = input.MoveVector.sqrMagnitude > 0.01f;
            bool wantCrouch = input.IsCrouch;
            bool canStand = controller.CanStandUp();

            // 앉기 입력이 유지 중이거나, 머리 위 장애물로 인해 일어설 수 없으면 Crouch 상태 유지
            if (wantCrouch || !canStand)
            {
                if (currentSubState != crouchState)
                    SetSubState(crouchState);
                return;
            }

            if (hasMoveInput)
            {
                if (input.IsSprint)
                {
                    if (currentSubState != sprintState)
                        SetSubState(sprintState);
                }
                else
                {
                    if (currentSubState != walkState)
                        SetSubState(walkState);
                }
            }
            else
            {
                if (currentSubState != idleState)
                    SetSubState(idleState);
            }
        }
    }
}