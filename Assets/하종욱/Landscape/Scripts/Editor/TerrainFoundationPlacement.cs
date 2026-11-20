using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ReinProject.Landscape
{
    internal static class TerrainFoundationPlacement
    {
        private const float SampleSpacing = .11f;
        private const float MaxControlSpacing = .55f;
        private const float GroundOverlap = .08f;
        private const float WallOverlap = .035f;
        private const string NeutralMaterial = "Assets/하종욱/LowPolyFPSLite/Materials/LowPolyFPSLite_BG2.mat";

        internal static bool TryGetWallFootprint(Transform target, out BoxCollider box)
        {
            box = target.GetComponent<BoxCollider>();
            if (box == null || !box.enabled || box.isTrigger || Vector3.Dot(target.up, Vector3.up) < .9999f)
                return false;

            var size = Vector3.Scale(box.size, target.lossyScale);
            return size.x > .05f && size.z > .05f &&
                Mathf.Max(size.x, size.z) >= 2.6f * Mathf.Min(size.x, size.z);
        }

        internal static bool TryFit(Transform target, BoxCollider box, List<TerrainCollider> terrains,
            Vector3 horizontalOffset, out Vector3 position, out Vector3[] sections, SandbagArc? arc = null)
        {
            return TryFit(target, box.center, box.size, terrains, horizontalOffset, out position, out sections, arc);
        }

        internal static bool TryFit(Transform target, Vector3 center, Vector3 dimensions, List<TerrainCollider> terrains,
            Vector3 horizontalOffset, out Vector3 position, out Vector3[] sections, SandbagArc? arc = null)
        {
            position = target.position + horizontalOffset;
            sections = null;
            var size = Vector3.Scale(dimensions, target.lossyScale);
            bool longX = arc.HasValue || size.x >= size.z;
            Vector3 along = longX ? Vector3.right * dimensions.x : Vector3.forward * dimensions.z;
            Vector3 across = longX ? Vector3.forward * dimensions.z : Vector3.left * dimensions.x;
            Vector3 baseCenter = center - Vector3.up * (dimensions.y * .5f);
            Vector3 Sample(float t, float side)
            {
                Vector3 point = baseCenter + along * (t - .5f) + across * (side * .5f);
                if (arc.HasValue)
                    point = arc.Value.Map(point);
                return target.TransformPoint(point) + horizontalOffset;
            }
            float length = (longX ? size.x : size.z) * (arc?.LengthMultiplier ?? 1f);
            int controls = Mathf.Clamp(Mathf.CeilToInt(length / MaxControlSpacing), 1, 110);
            int stepsPerControl = Mathf.Max(1, Mathf.CeilToInt(length / (controls * SampleSpacing)));
            int steps = controls * stepsPerControl;
            if (steps > 1024)
                return false;

            var left = new float[steps + 1];
            var right = new float[steps + 1];
            float highest = float.NegativeInfinity, lowest = float.PositiveInfinity;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                var a = Sample(t, -1f);
                var b = Sample(t, 1f);
                var spine = Sample(t, 0f);
                if (!TerrainPlacement.TryGetGroundHeight(a.x, a.z, terrains, out left[i]) ||
                    !TerrainPlacement.TryGetGroundHeight(b.x, b.z, terrains, out right[i]) ||
                    !TerrainPlacement.TryGetGroundHeight(spine.x, spine.z, terrains, out float middle))
                    return false;

                highest = Mathf.Max(highest, Mathf.Max(middle, Mathf.Max(left[i], right[i])));
                lowest = Mathf.Min(lowest, Mathf.Min(middle, Mathf.Min(left[i], right[i])));
            }

            position.y += highest - Sample(0f, 0f).y;
            if (highest - lowest < .035f)
                return true;

            sections = new Vector3[(steps + 1) * 4];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 a = Sample(t, -1f), b = Sample(t, 1f);
                float leftGround = Mathf.Min(left[i], EvaluateSpline(left, i, stepsPerControl, controls));
                float rightGround = Mathf.Min(right[i], EvaluateSpline(right, i, stepsPerControl, controls));
                int ring = i * 4;
                sections[ring] = new Vector3(a.x, leftGround - GroundOverlap, a.z);
                sections[ring + 1] = new Vector3(b.x, rightGround - GroundOverlap, b.z);
                sections[ring + 2] = new Vector3(a.x, highest + WallOverlap, a.z);
                sections[ring + 3] = new Vector3(b.x, highest + WallOverlap, b.z);
            }
            return true;
        }

        // Monotone cubic Hermite: flat tangents at reversals stop spline overshoot.
        private static float EvaluateSpline(float[] values, int index, int stride, int controls)
        {
            int cell = Mathf.Min(index / stride, controls - 1);
            int from = cell * stride, to = from + stride;
            float t = (float)(index - from) / stride;
            float delta = values[to] - values[from];
            float prev = cell == 0 ? delta : values[from] - values[from - stride];
            float next = cell + 1 == controls ? delta : values[to + stride] - values[to];
            float m0 = Tangent(prev, delta), m1 = Tangent(delta, next);
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * values[from] + (t3 - 2f * t2 + t) * m0
                + (-2f * t3 + 3f * t2) * values[to] + (t3 - t2) * m1;
        }

        private static float Tangent(float left, float right)
        {
            return left * right <= 0f ? 0f : 2f * left * right / (left + right);
        }

        internal static void Apply(Transform host, Vector3[] worldSections, string undoLabel, bool removeNested = false)
        {
            foreach (var old in host.GetComponentsInChildren<TerrainFoundation>(true))
                if (old != null && (removeNested || old.transform.parent == host))
                    Undo.DestroyObjectImmediate(old.gameObject);
            if (worldSections == null)
                return;

            var footing = new GameObject("Terrain Foundation");
            if (footing.scene != host.gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(footing, host.gameObject.scene);
            footing.transform.SetParent(host, false);
            footing.layer = host.gameObject.layer;
            footing.tag = host.gameObject.tag;
            var foundation = footing.AddComponent<TerrainFoundation>();
            var localSections = new Vector3[worldSections.Length];
            for (int i = 0; i < worldSections.Length; i++)
                localSections[i] = footing.transform.InverseTransformPoint(worldSections[i]);
            var shaderMaterial = AssetDatabase.LoadAssetAtPath<Material>(NeutralMaterial);
            if (shaderMaterial == null)
                shaderMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/하종욱/Landscape/PublicGeometry/Dry Concrete.mat");
            footing.GetComponent<MeshRenderer>().sharedMaterial = shaderMaterial;
            foundation.SetSections(localSections);
            TerrainFoundationAppearance.Apply(foundation);
            Undo.RegisterCreatedObjectUndo(footing, undoLabel);
        }
    }
}
