using UnityEngine;

namespace ReinProject.Landscape
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SandbagSpline : MonoBehaviour
    {
        [SerializeField, HideInInspector] private float _angle;
        [SerializeField, HideInInspector] private GameObject _output;
        [SerializeField, HideInInspector] private MeshRenderer[] _sources;

        public float Angle => _angle;
        public GameObject Output => _output;
        public MeshRenderer[] Sources => _sources;

#if UNITY_EDITOR
        public void SetBake(float angle, GameObject output, MeshRenderer[] sources)
        {
            _angle = angle;
            _output = output;
            _sources = sources;
        }
#endif
    }
}
