using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReinProject.Landscape
{
    internal static class SandbagSplineBake
    {
        internal const string PrefabPath = "Assets/하종욱/Sandbag/Prefabs/SandbagBarrier.prefab";
        private const string SandbagPath = "Assets/하종욱/Sandbag";
        private const string ModelPath = SandbagPath + "/Models/SandbagBarrier.fbx";
        private const string PublicModelPath = SandbagPath + "/Public/Sandbag Segment.asset";
        private const string GeneratedPath = SandbagPath + "/Generated";

        internal static bool IsBakedArc(Transform target)
        {
            return target.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null &&
                AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(GeneratedPath + "/Sandbag Arc ", System.StringComparison.Ordinal);
        }

        internal static bool HasPublicSources(Transform target)
        {
            if (!target.CompareTag("ArenaSandbag"))
                return false;

            foreach (var filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (AssetDatabase.GetAssetPath(filter.sharedMesh) == PublicModelPath)
                    return true;
            }
            return false;
        }

        private static bool IsSourceMesh(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            return path == ModelPath || path == PublicModelPath;
        }

        internal static MeshRenderer[] FindSources(Transform target)
        {
            var sources = new List<MeshRenderer>();
            foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.enabled && renderer.TryGetComponent(out MeshFilter filter) &&
                    IsSourceMesh(filter.sharedMesh))
                    sources.Add(renderer);
            }

            return sources.ToArray();
        }

        internal static Mesh Bake(Transform target, BoxCollider box, float angle, MeshRenderer[] sources,
            out Material[] materials)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var materialList = new List<Material>();
            var triangles = new List<List<int>>();
            var arc = new SandbagArc(box, angle);
            var key = new StringBuilder("SandbagArc-v2;");
            Append(key, angle);
            for (int axis = 0; axis < 3; axis++)
            {
                Append(key, box.center[axis]);
                Append(key, box.size[axis]);
            }

            bool isPublic = true;
            foreach (var renderer in sources)
            {
                Mesh source = renderer.GetComponent<MeshFilter>().sharedMesh;
                isPublic &= AssetDatabase.GetAssetPath(source) == PublicModelPath;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id);
                key.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source)));
                key.Append(guid).Append(':').Append(id).Append(';');
                Matrix4x4 toRoot = target.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < 16; i++)
                    Append(key, toRoot[i]);

                int baseVertex = vertices.Count;
                using var read = MeshUtility.AcquireReadOnlyMeshData(source);
                var data = read[0];
                using var sourceVertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                using var sourceNormals = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                using var sourceUv = new NativeArray<Vector2>(data.vertexCount, Allocator.Temp);
                data.GetVertices(sourceVertices);
                data.GetNormals(sourceNormals);
                data.GetUVs(0, sourceUv);
                Matrix4x4 normalToRoot = toRoot.inverse.transpose;
                for (int i = 0; i < data.vertexCount; i++)
                {
                    vertices.Add(arc.Map(toRoot.MultiplyPoint3x4(sourceVertices[i]), out var rotation, out float stretch));
                    Vector3 normal = normalToRoot.MultiplyVector(sourceNormals[i]);
                    normal.x /= stretch;
                    normals.Add((rotation * normal).normalized);
                    uv.Add(sourceUv[i]);
                }

                Material[] sourceMaterials = renderer.sharedMaterials;
                bool mirrored = toRoot.determinant < 0f;
                for (int i = 0; i < data.subMeshCount; i++)
                {
                    var submesh = data.GetSubMesh(i);
                    var material = sourceMaterials[Mathf.Min(i, sourceMaterials.Length - 1)];
                    int slot = materialList.IndexOf(material);
                    if (slot < 0)
                    {
                        slot = materialList.Count;
                        materialList.Add(material);
                        triangles.Add(new List<int>());
                    }

                    key.Append(slot).Append(',');
                    using var indices = new NativeArray<int>(submesh.indexCount, Allocator.Temp);
                    data.GetIndices(indices, i, true);
                    var destination = triangles[slot];
                    for (int j = 0; j < indices.Length; j += 3)
                    {
                        destination.Add(baseVertex + indices[j]);
                        destination.Add(baseVertex + indices[j + (mirrored ? 2 : 1)]);
                        destination.Add(baseVertex + indices[j + (mirrored ? 1 : 2)]);
                    }
                }
            }

            materials = materialList.ToArray();
            string prefix = isPublic ? "Sandbag Arc public " : "Sandbag Arc ";
            string path = $"{GeneratedPath}/{prefix}{Hash128.Compute(key.ToString())}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(GeneratedPath))
                AssetDatabase.CreateFolder(SandbagPath, "Generated");

            var mesh = new Mesh { name = "Sandbag Arc", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = triangles.Count;
            for (int i = 0; i < triangles.Count; i++)
                mesh.SetTriangles(triangles[i], i, false);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void Append(StringBuilder key, float value)
        {
            key.Append(value.ToString("F5", CultureInfo.InvariantCulture)).Append(';');
        }
    }
}
