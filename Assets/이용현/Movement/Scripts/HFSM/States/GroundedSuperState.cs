using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class GroundedSuperState : BaseState
    {
        private IdleState idleState;
        private WalkState walkState;
        private SprintState sprintState;

        public GroundedSuperState(PlayerMovementController controller, BaseState parent) : base(controller, parent)
        {
            idleState = new IdleState(controller, this);
            walkState = new WalkState(controller, this);
            sprintState = new SprintState(controller, this);
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

            // 1. 공중 이탈 검사 -> AirborneState로 전이
            if (!controller.IsGrounded)
            {
                controller.ChangeRootState(controller.AirborneState);
                return;
            }

            // 2. 하위 이동 상태 전이 규칙
            MovementInputData input = controller.CurrentInput;
            bool hasMoveInput = input.MoveVector.sqrMagnitude > 0.01f;

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