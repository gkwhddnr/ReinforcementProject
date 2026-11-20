using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReinProject.Landscape
{
    internal static class TerrainFoundationAppearance
    {
        internal static bool Apply(TerrainFoundation foundation)
        {
            var renderer = foundation.GetComponent<MeshRenderer>();
            var mesh = foundation.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null || foundation.transform.parent == null)
                return false;

            Vector3 seam = mesh.bounds.center;
            seam.y = mesh.bounds.max.y;
            seam = foundation.transform.TransformPoint(seam);
            if (!FindSurface(foundation.transform.parent, seam, out Material material, out Vector2 uv))
                return false;

            renderer.sharedMaterial = material;
            foundation.SetSurfaceUv(uv);
            return true;
        }

        private static bool FindSurface(Transform host, Vector3 seam, out Material material, out Vector2 uv)
        {
            material = null;
            uv = new Vector2(.5f, .5f);
            float closest = float.PositiveInfinity;
            foreach (var renderer in host.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.GetComponentInParent<TerrainFoundation>() != null)
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                var materials = renderer.sharedMaterials;
                if (filter == null || filter.sharedMesh == null || materials.Length == 0)
                    continue;

                Mesh mesh = filter.sharedMesh;
                using var meshData = MeshUtility.AcquireReadOnlyMeshData(mesh);
                var data = meshData[0];
                if (data.vertexCount == 0)
                    continue;
                using var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                using var uv0 = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
                data.GetVertices(positions);
                bool hasUv = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
                if (hasUv)
                    data.GetUVs(0, uv0);

                var localToWorld = renderer.localToWorldMatrix;
                for (int submesh = 0; submesh < data.subMeshCount; submesh++)
                {
                    var property = data.GetSubMesh(submesh);
                    Material sourceMaterial = materials[Mathf.Min(submesh, materials.Length - 1)];
                    if (sourceMaterial == null || property.topology != MeshTopology.Triangles || property.indexCount < 3)
                        continue;

                    using var indices = new NativeArray<int>(property.indexCount, Allocator.Temp);
                    data.GetIndices(indices, submesh, true);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                        Vector3 pa = localToWorld.MultiplyPoint3x4(positions[a]);
                        Vector3 pb = localToWorld.MultiplyPoint3x4(positions[b]);
                        Vector3 pc = localToWorld.MultiplyPoint3x4(positions[c]);
                        Vector3 normal = Vector3.Cross(pb - pa, pc - pa);
                        if (normal.sqrMagnitude < 0.00000001f)
                            continue;

                        Vector3 delta = (pa + pb + pc) / 3f - seam;
                        // Choose a low, visible face near the seam. Sample inside its UV island.
                        float score = delta.sqrMagnitude + 5f * delta.y * delta.y
                            + .5f * Mathf.Abs(normal.normalized.y);
                        if (score >= closest)
                            continue;

                        closest = score;
                        material = sourceMaterial;
                        uv = hasUv ? (uv0[a] + uv0[b] + uv0[c]) / 3f : new Vector2(.5f, .5f);
                    }
                }
            }
            return material != null;
        }
    }
}
