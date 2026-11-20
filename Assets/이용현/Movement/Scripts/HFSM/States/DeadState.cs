using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public class DeadState : BaseState
    {
        public DeadState(PlayerMovementController controller, BaseState parent) : base(controller, parent) { }

        public override void Enter()
        {
            base.Enter();
            // 사망 시 정지
        }

        public override void FixedUpdate()
        {
            // 사망 상태에서는 모든 이동 및 입력 처리 완전 차단
        }
    }
}