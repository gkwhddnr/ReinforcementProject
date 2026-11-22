using UnityEngine;

namespace Reinforcement.Shooting
{
    [DisallowMultipleComponent]
    public sealed class ShotObstacle : MonoBehaviour
    {
        [SerializeField] private string obstacleId;

        public string ObstacleId
        {
            get => obstacleId;
            set => obstacleId = value;
        }
    }
}
