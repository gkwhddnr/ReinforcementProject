using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace ReinProject.Landscape
{
    internal static class TreeTerrainPlacement
    {
        private const float RootInset = .03f;
        private const float RootEdgeSampleSpacing = .16f;

        internal static bool IsTree(Transform target)
        {
            var rootMesh = target.GetComponent<MeshFilter>();
            if (rootMesh != null && rootMesh.sharedMesh != null)
                return target.gameObject.tag == "ArenaTree" ||
                    rootMesh.sharedMesh.name.StartsWith("SM_FreeTree_", StringComparison.OrdinalIgnoreCase);

            return target.gameObject.tag == "ArenaTree" && PrefabUtility.IsAnyPrefabInstanceRoot(target.gameObject);
        }

        internal static bool TryFit(Transform tree, List<TerrainCollider> terrains, Vector3 horizontalOffset,
            bool requireFullFootprint, out Vector3 position)
        {
            position = tree.position + horizontalOffset;
            var ring = FindRootRing(tree);
            if (ring.Count < 3)
                return false;

            Vector3 center = Vector3.zero;
            foreach (Vector3 point in ring)
                center += point;
            center /= ring.Count;
            ring.Sort((a, b) => Mathf.Atan2(a.z - center.z, a.x - center.x)
                .CompareTo(Mathf.Atan2(b.z - center.z, b.x - center.x)));

            float shift = float.PositiveInfinity;
            var localToWorld = tree.localToWorldMatrix;
            if (!Sample(localToWorld.MultiplyPoint3x4(center) + horizontalOffset, terrains, ref shift))
                return false;

            for (int i = 0; i < ring.Count; i++)
            {
                var a = localToWorld.MultiplyPoint3x4(ring[i]) + horizontalOffset;
                var b = localToWorld.MultiplyPoint3x4(ring[(i + 1) % ring.Count]) + horizontalOffset;
                float length = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                int steps = Mathf.Clamp(Mathf.CeilToInt(length / RootEdgeSampleSpacing), 1, 32);
                for (int j = 0; j < steps; j++)
                {
                    if (!Sample(Vector3.Lerp(a, b, (float)j / steps), terrains, ref shift) && requireFullFootprint)
                        return false;
                }
            }

            position.y += shift - RootInset;
            return true;
        }

        private static List<Vector3> FindRootRing(Transform tree)
        {
            var vertices = new List<Vector3>();
            Matrix4x4 worldToRoot = tree.worldToLocalMatrix;
            float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
            foreach (var filter in tree.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                if (renderer == null || !renderer.enabled || mesh == null ||
                    filter.GetComponentInParent<TerrainFoundation>() != null)
                    continue;

                using var read = MeshUtility.AcquireReadOnlyMeshData(mesh);
                var data = read[0];
                using var points = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                data.GetVertices(points);
                Matrix4x4 toRoot = worldToRoot * filter.transform.localToWorldMatrix;
                foreach (var point in points)
                {
                    Vector3 vertex = toRoot.MultiplyPoint3x4(point);
                    vertices.Add(vertex);
                    lowest = Mathf.Min(lowest, vertex.y);
                    highest = Mathf.Max(highest, vertex.y);
                }
            }

            var ring = new List<Vector3>();
            var seen = new HashSet<Vector3>();
            float tolerance = Mathf.Clamp((highest - lowest) * .005f, .002f, .012f);
            foreach (var vertex in vertices)
                if (vertex.y <= lowest + tolerance && seen.Add(vertex))
                    ring.Add(vertex);

            return ring;
        }

        private static bool Sample(Vector3 rootPoint, List<TerrainCollider> terrains, ref float shift)
        {
            if (!TerrainPlacement.TryGetGroundHeight(rootPoint.x, rootPoint.z, terrains, out float ground))
                return false;

            // Lower the visible roots enough to touch on the downhill side as well.
            shift = Mathf.Min(shift, ground - rootPoint.y);
            return true;
        }
    }
}
