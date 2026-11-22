using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReinProject.Landscape
{
    internal sealed class SandbagTerrainFit
    {
        private const string Folder = "Assets/하종욱/Sandbag/Generated";
        private const string RawPrefix = Folder + "/Sandbag Arc ";
        private const string LegacyPrefix = RawPrefix + "posture";
        private const string FittedPrefix = RawPrefix + "terrain ";
        private const int GuidLength = 32;
        private const float CoverHeight = 1.36f;
        private const float GroundOverlap = .04f;

        private readonly MeshFilter _filter;
        private readonly Mesh _source;
        private readonly Vector3[] _vertices;
        private readonly Section[] _sections;

        private SandbagTerrainFit(MeshFilter filter, Mesh source, Vector3[] vertices, Section[] sections)
        {
            _filter = filter;
            _source = source;
            _vertices = vertices;
            _sections = sections;
        }

        internal static bool HasProfile(Transform frame)
        {
            if (!frame.TryGetComponent(out MeshFilter filter))
                return false;

            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            return path.StartsWith(LegacyPrefix, System.StringComparison.Ordinal) ||
                path.StartsWith(FittedPrefix, System.StringComparison.Ordinal);
        }

        internal static bool TryCreate(Transform frame, List<TerrainCollider> terrains, Vector3 horizontalOffset,
            out Vector3 position, out SandbagTerrainFit fit)
        {
            position = frame.position + horizontalOffset;
            fit = null;
            var filter = frame.GetComponent<MeshFilter>();
            Mesh source = FindSource(filter.sharedMesh, frame);
            if (source == null || source.bounds.size.y < .01f ||
                !TerrainPlacement.TryGetGroundHeight(position.x, position.z, terrains, out float originHeight))
                return false;

            position.y = originHeight;
            Vector3 shift = position - frame.position;
            var boxes = new List<BoxCollider>();
            foreach (Transform child in frame)
            {
                if (child.TryGetComponent(out BoxCollider box) && box.enabled && !box.isTrigger)
                    boxes.Add(box);
            }
            if (boxes.Count == 0)
                return false;

            var profile = new Vector3[boxes.Count + 2];
            var sections = new Section[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                BoxCollider box = boxes[i];
                Transform section = box.transform;
                Vector3 center = section.position + shift;
                Vector3 halfWidth = section.TransformVector(Vector3.right * (box.size.x * .5f));
                Vector3 halfDepth = section.TransformVector(Vector3.forward * (box.size.z * .5f));
                float vertical = Mathf.Abs(section.TransformVector(Vector3.up).y);
                if (vertical < .01f || !TrySetGround(ref center, terrains))
                    return false;

                profile[i + 1] = center;
                float bottom = center.y;
                for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 point = center + halfWidth * x + halfDepth * z;
                    if (!TrySetGround(ref point, terrains))
                        return false;
                    bottom = Mathf.Min(bottom, point.y);
                }

                if (i == 0)
                    profile[0] = center - halfWidth;
                if (i == boxes.Count - 1)
                    profile[profile.Length - 1] = center + halfWidth;

                float top = center.y + CoverHeight;
                bottom -= GroundOverlap;
                center.y = (top + bottom) * .5f;
                Vector3 size = box.size;
                size.y = Mathf.Max(.01f, (top - bottom - 2f * (Mathf.Abs(halfWidth.y) + Mathf.Abs(halfDepth.y))) / vertical);
                sections[i] = new Section(box, section.InverseTransformPoint(center - shift), size);
            }
            if (!TrySetGround(ref profile[0], terrains) || !TrySetGround(ref profile[profile.Length - 1], terrains))
                return false;

            Vector3[] vertices = source.vertices;
            Bounds sourceBounds = source.bounds;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 local = vertices[i];
                Vector3 world = position + frame.TransformVector(local);
                float top = GetProfileHeight(world, profile) + CoverHeight;
                if (!TrySetGround(ref world, terrains))
                    return false;

                float rise = Mathf.InverseLerp(sourceBounds.min.y, sourceBounds.max.y, local.y);
                world.y = Mathf.Lerp(world.y - GroundOverlap, Mathf.Max(top, world.y), rise);
                vertices[i] = frame.InverseTransformVector(world - position);
            }

            fit = new SandbagTerrainFit(filter, source, vertices, sections);
            return true;
        }

        internal void Apply(string undoLabel)
        {
            string key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_source));
            var hash = Hash128.Compute("Sandbag terrain v1");
            hash.Append(_vertices);
            string path = $"{FittedPrefix}{key}_{hash}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = Object.Instantiate(_source);
                mesh.name = "Sandbag Arc terrain";
                mesh.vertices = _vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                    mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, path);
            }

            if (_filter.sharedMesh != mesh)
            {
                Undo.RecordObject(_filter, undoLabel);
                _filter.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(_filter);
            }

            foreach (Section section in _sections)
            {
                if (section.Box.center == section.Center && section.Box.size == section.Size)
                    continue;

                Undo.RecordObject(section.Box, undoLabel);
                section.Box.center = section.Center;
                section.Box.size = section.Size;
                PrefabUtility.RecordPrefabInstancePropertyModifications(section.Box);
            }
        }

        private static Mesh FindSource(Mesh current, Transform frame)
        {
            if (!current || !current.isReadable)
                return null;

            string currentPath = AssetDatabase.GetAssetPath(current);
            if (currentPath.StartsWith(FittedPrefix, System.StringComparison.Ordinal))
            {
                string guid = currentPath.Substring(FittedPrefix.Length, GuidLength);
                return AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
            }

            // Legacy posture meshes were baked without a source link. Their original world XZ
            // coordinates and vertex order identify the reusable ungraded spline mesh.
            Vector3[] fitted = current.vertices;
            Mesh best = null;
            float bestError = float.PositiveInfinity;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Length != RawPrefix.Length + GuidLength + ".asset".Length ||
                    !path.StartsWith(RawPrefix, System.StringComparison.Ordinal))
                    continue;

                Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (source == null || !source.isReadable || source.vertexCount != fitted.Length ||
                    source.subMeshCount != current.subMeshCount)
                    continue;

                Vector3[] vertices = source.vertices;
                float error = 0f;
                for (int i = 0; i < fitted.Length; i += 31)
                {
                    Vector3 delta = frame.TransformVector(fitted[i] - vertices[i]);
                    error += delta.x * delta.x + delta.z * delta.z;
                }
                if (error < bestError)
                {
                    bestError = error;
                    best = source;
                }
            }

            return best;
        }

        private static bool TrySetGround(ref Vector3 point, List<TerrainCollider> terrains)
        {
            if (!TerrainPlacement.TryGetGroundHeight(point.x, point.z, terrains, out float height))
                return false;

            point.y = height;
            return true;
        }

        private static float GetProfileHeight(Vector3 point, Vector3[] profile)
        {
            float closest = float.PositiveInfinity;
            float height = profile[0].y;
            var xz = new Vector2(point.x, point.z);
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector3 start = profile[i], end = profile[i + 1];
                var a = new Vector2(start.x, start.z);
                var edge = new Vector2(end.x - start.x, end.z - start.z);
                float t = edge.sqrMagnitude > .000001f
                    ? Mathf.Clamp01(Vector2.Dot(xz - a, edge) / edge.sqrMagnitude)
                    : 0f;
                float distance = (xz - (a + edge * t)).sqrMagnitude;
                if (distance < closest)
                {
                    closest = distance;
                    height = Mathf.Lerp(start.y, end.y, t);
                }
            }
            return height;
        }

        private readonly struct Section
        {
            internal readonly BoxCollider Box;
            internal readonly Vector3 Center;
            internal readonly Vector3 Size;

            internal Section(BoxCollider box, Vector3 center, Vector3 size)
            {
                Box = box;
                Center = center;
                Size = size;
            }
        }
    }
}
