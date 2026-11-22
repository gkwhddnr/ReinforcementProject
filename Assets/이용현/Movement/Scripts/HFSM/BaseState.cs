using UnityEngine;

namespace Reinforce.Movement.HFSM
{
    public abstract class BaseState : IState
    {
        protected PlayerMovementController controller;
        protected BaseState parentState;
        protected BaseState currentSubState;

        public virtual string StateName => GetType().Name;
        public BaseState CurrentSubState => currentSubState;

        public BaseState(PlayerMovementController controller, BaseState parentState = null)
        {
            this.controller = controller;
            this.parentState = parentState;
        }

        public virtual void Enter()
        {
            currentSubState?.Enter();
        }

        public virtual void Update()
        {
            currentSubState?.Update();
        }

        public virtual void FixedUpdate()
        {
            currentSubState?.FixedUpdate();
        }

        public virtual void Exit()
        {
            currentSubState?.Exit();
        }

        protected void SetSubState(BaseState newSubState)
        {
            if (currentSubState == newSubState) return;
            currentSubState?.Exit();
            currentSubState = newSubState;
            currentSubState?.Enter();
        }

        public string GetFullHierarchyName()
        {
            if (currentSubState != null)
            {
                return $"{StateName} -> {currentSubState.GetFullHierarchyName()}";
            }
            return StateName;
        }
    }
}