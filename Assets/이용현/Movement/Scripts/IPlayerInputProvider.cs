using UnityEngine;

namespace Reinforce.Movement
{
    public interface IPlayerInputProvider
    {
        MovementInputData GetInput();
    }
}