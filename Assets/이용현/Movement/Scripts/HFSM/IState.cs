namespace Reinforce.Movement.HFSM
{
    public interface IState
    {
        void Enter();
        void Update();
        void FixedUpdate();
        void Exit();
        string StateName { get; }
    }
}