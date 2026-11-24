using Unity.AI.Navigation;
using UnityEngine;

namespace ReinProject.Landscape
{
    [RequireComponent(typeof(NavMeshLink))]
    public sealed class ArenaLadder : MonoBehaviour
    {
        [SerializeField] private Vector3 _upperClimbOffset;

        public Vector3 ClimbBottom => transform.position;
        public Vector3 ClimbTop => transform.TransformPoint(_upperClimbOffset);
    }
}
