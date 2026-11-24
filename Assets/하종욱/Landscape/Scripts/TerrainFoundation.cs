#if UNITY_EDITOR
using System;
using UnityEditor;
#endif
using UnityEngine;

namespace ReinProject.Landscape
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class TerrainFoundation : MonoBehaviour
    {
        [SerializeField, HideInInspector] private Vector3[] _sections;
        [SerializeField, HideInInspector] private Vector2 _surfaceUv;
        private Mesh _generatedMesh;

#if UNITY_EDITOR
        private Vector3[] _builtSections;
        private Vector2 _builtSurfaceUv;
#endif

        public bool HasGeometry => _sections != null && _sections.Length >= 8;

        public void SetSections(Vector3[] localSections)
        {
            _sections = localSections;
            Rebuild();
        }

        private void OnEnable()
        {
            Rebuild();
#if UNITY_EDITOR
            if (!Application.IsPlaying(gameObject))
                Undo.undoRedoPerformed += RebuildAfterUndoRedo;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= RebuildAfterUndoRedo;
#endif
            ReleaseMesh();
        }

#if UNITY_EDITOR
        private void RebuildAfterUndoRedo()
        {
            if (!Application.IsPlaying(gameObject) &&
                (!_builtSections.AsSpan().SequenceEqual(_sections) || !_builtSurfaceUv.Equals(_surfaceUv)))
                Rebuild();
        }
#endif

        private void Rebuild()
        {
            ReleaseMesh();
#if UNITY_EDITOR
            _builtSections = HasGeometry ? (Vector3[])_sections.Clone() : null;
            _builtSurfaceUv = _surfaceUv;
#endif
            if (!HasGeometry)
                return;

            int rings = _sections.Length / 4;
            int[] indices = new int[(rings - 1) * 24 + 12];
            int next = 0;
            for (int i = 0; i < rings - 1; i++)
            {
                int a = i * 4;
                int b = a + 4;
                AddQuad(indices, ref next, a, a + 2, b + 2, b);
                AddQuad(indices, ref next, a + 1, b + 1, b + 3, a + 3);
                AddQuad(indices, ref next, a + 2, a + 3, b + 3, b + 2);
                AddQuad(indices, ref next, a, b, b + 1, a + 1);
            }
            AddQuad(indices, ref next, 0, 1, 3, 2);
            int end = _sections.Length - 4;
            AddQuad(indices, ref next, end, end + 2, end + 3, end + 1);

            _generatedMesh = new Mesh { name = "Terrain Foundation Mesh", hideFlags = HideFlags.HideAndDontSave };
            _generatedMesh.vertices = _sections;
            _generatedMesh.triangles = indices;
            _generatedMesh.RecalculateNormals();
            ApplySurfaceUv();
            _generatedMesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _generatedMesh;
            GetComponent<MeshCollider>().sharedMesh = _generatedMesh;
        }

        public void SetSurfaceUv(Vector2 surfaceUv)
        {
            _surfaceUv = surfaceUv;
#if UNITY_EDITOR
            _builtSurfaceUv = surfaceUv;
#endif
            if (_generatedMesh != null)
                ApplySurfaceUv();
        }

        private void ApplySurfaceUv()
        {
            int count = _generatedMesh.vertexCount;
            var uv = new Vector2[count];
            var tangents = new Vector4[count];
            var normals = _generatedMesh.normals;
            Vector3 lengthAxis = (_sections[6] - _sections[2]).normalized;
            Vector3 widthAxis = (_sections[3] - _sections[2]).normalized;
            for (int i = 0; i < count; i++)
            {
                uv[i] = _surfaceUv;
                Vector3 tangent = Vector3.ProjectOnPlane(lengthAxis, normals[i]);
                if (tangent.sqrMagnitude < .001f)
                    tangent = Vector3.ProjectOnPlane(widthAxis, normals[i]);
                tangent.Normalize();
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
            }
            _generatedMesh.uv = uv;
            _generatedMesh.tangents = tangents;
        }

        private static void AddQuad(int[] buffer, ref int cursor, int a, int b, int c, int d)
        {
            buffer[cursor++] = a;
            buffer[cursor++] = b;
            buffer[cursor++] = c;
            buffer[cursor++] = a;
            buffer[cursor++] = c;
            buffer[cursor++] = d;
        }

        private void ReleaseMesh()
        {
            if (_generatedMesh == null)
                return;

            var filter = GetComponent<MeshFilter>();
            var collider = GetComponent<MeshCollider>();
            if (filter != null && filter.sharedMesh == _generatedMesh)
                filter.sharedMesh = null;
            if (collider != null && collider.sharedMesh == _generatedMesh)
                collider.sharedMesh = null;

            if (Application.IsPlaying(gameObject))
                Destroy(_generatedMesh);
            else
                DestroyImmediate(_generatedMesh);
            _generatedMesh = null;
        }
    }
}
