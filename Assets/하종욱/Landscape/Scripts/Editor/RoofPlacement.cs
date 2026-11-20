using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReinProject.Landscape
{
    internal static class RoofPlacement
    {
        internal const string MenuPath = "GameObject/Landscape/Snap to Roof";
        private const string UndoLabel = "Snap to Roof";
        private const string BuildingTag = "ArenaBuilding";
        private const SelectionMode Mode = SelectionMode.TopLevel | SelectionMode.Editable;
        private const float EdgeTolerance = 0.0001f;
        private const float RoofCaptureDistance = 6f;

        [MenuItem(MenuPath, true)]
        private static bool CanSnap()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                PrefabStageUtility.GetCurrentPrefabStage() == null &&
                System.Array.Exists(Selection.GetTransforms(Mode), target => !EditorUtility.IsPersistent(target));
        }

        [MenuItem(MenuPath, false, 32)]
        private static void SnapSelection()
        {
            if (!CanSnap())
                return;

            Physics.SyncTransforms();
            var roofsByScene = new Dictionary<Scene, List<BoxCollider>>();
            var updates = new List<(Transform Target, Vector3 Position)>();
            int skipped = 0;
            foreach (Transform target in Selection.GetTransforms(Mode))
            {
                Scene scene = target.gameObject.scene;
                if (target.gameObject.CompareTag(BuildingTag))
                    continue;

                if (EditorUtility.IsPersistent(target) || !scene.isLoaded || !CanPlaceOnRoof(target))
                {
                    skipped++;
                    continue;
                }

                if (!roofsByScene.TryGetValue(scene, out var roofs))
                {
                    roofs = FindRoofs(scene);
                    roofsByScene.Add(scene, roofs);
                }

                if (TryGetClosestPlacement(target, roofs, out Vector3 position))
                {
                    if ((position - target.position).sqrMagnitude > EdgeTolerance * EdgeTolerance ||
                        target.GetComponentInChildren<TerrainFoundation>(true) != null)
                        updates.Add((target, position));
                }
                else
                {
                    skipped++;
                }
            }

            if (updates.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoLabel);

                foreach (var (target, position) in updates)
                {
                    Undo.RecordObject(target, UndoLabel);
                    target.position = position;
                    TerrainFoundationPlacement.Apply(target, null, UndoLabel, removeNested: true);
                    if (PrefabUtility.IsPartOfPrefabInstance(target))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                }

                Undo.CollapseUndoOperations(undoGroup);
            }

            if (skipped > 0)
                Debug.LogWarning($"Snap to Roof: skipped {skipped} selection(s) that do not fit on a nearby roof. Select the building together with the props to target its roof.");
        }

        internal static List<BoxCollider> FindRoofs(Scene scene)
        {
            var roofs = new List<BoxCollider>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>())
            {
                if (box.gameObject.activeInHierarchy && box.enabled && !box.isTrigger && box.gameObject.CompareTag(BuildingTag) &&
                    Vector3.Dot(box.transform.up, Vector3.up) > 0.999999f)
                    roofs.Add(box);
            }
            return roofs;
        }

        internal static BoxCollider FindRoofAt(Vector3 point, List<BoxCollider> roofs)
        {
            BoxCollider best = null;
            float bestHeight = float.NegativeInfinity;
            foreach (BoxCollider roof in roofs)
            {
                float height = GetRoofHeight(roof);
                if (height > bestHeight && Contains(roof, point))
                {
                    best = roof;
                    bestHeight = height;
                }
            }
            return best;
        }

        internal static BoxCollider GetSelectedRoof(List<BoxCollider> roofs)
        {
            BoxCollider selected = null;
            foreach (BoxCollider roof in roofs)
            {
                if (!Selection.Contains(roof.gameObject))
                    continue;
                if (selected != null)
                    return null;
                selected = roof;
            }
            return selected;
        }

        internal static BoxCollider FindRoofAlongRay(Ray ray, List<BoxCollider> roofs, out Vector3 point)
        {
            BoxCollider closest = null;
            float closestDistance = float.PositiveInfinity;
            point = default;
            foreach (BoxCollider roof in roofs)
            {
                if (!roof.Raycast(ray, out RaycastHit hit, closestDistance))
                    continue;

                closest = roof;
                closestDistance = hit.distance;
                point = hit.point;
                point.y = GetRoofHeight(roof);
            }
            return closest;
        }

        internal static bool TryGetPlacementAt(Transform target, BoxCollider roof, Vector3 horizontalOffset,
            out Vector3 position)
        {
            bool fits = TryFitOnRoof(new[] { target }, roof, horizontalOffset, out Vector3 offset);
            position = target.position + offset;
            return fits;
        }

        internal static bool TryFitOnRoof(IReadOnlyList<Transform> targets, BoxCollider roof, Vector3 horizontalOffset,
            out Vector3 offset)
        {
            offset = horizontalOffset;
            if (targets.Count == 0)
                return false;

            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            float lowest = float.PositiveInfinity;
            foreach (Transform target in targets)
            {
                if (!CanPlaceOnRoof(target) || roof.transform.IsChildOf(target))
                    return false;

                Bounds support = TerrainPlacement.GetSupportBounds(target);
                lowest = Mathf.Min(lowest, support.min.y);

                bool hasCollider = false;
                foreach (Collider collider in target.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger || collider.GetComponent<TerrainFoundation>() != null)
                        continue;

                    hasCollider = true;
                    // Use transformed box corners so rotation does not overstate the footprint.
                    if (collider is BoxCollider box)
                        AccumulateFootprint(roof, new Bounds(box.center, box.size), box.transform, horizontalOffset, ref min, ref max);
                    else
                        AccumulateFootprint(roof, collider.bounds, null, horizontalOffset, ref min, ref max);
                }

                if (!hasCollider)
                    AccumulateFootprint(roof, support, null, horizontalOffset, ref min, ref max);
            }

            Vector3 roofMin = roof.center - roof.size * 0.5f;
            Vector3 roofMax = roof.center + roof.size * 0.5f;
            var shiftMin = new Vector2(roofMin.x - min.x, roofMin.z - min.y);
            var shiftMax = new Vector2(roofMax.x - max.x, roofMax.z - max.y);
            if (shiftMin.x > shiftMax.x + EdgeTolerance || shiftMin.y > shiftMax.y + EdgeTolerance)
                return false;

            // Shift only as far inward as needed, preserving the whole group's layout.
            float x = Mathf.Clamp(0f, shiftMin.x, Mathf.Max(shiftMin.x, shiftMax.x));
            float z = Mathf.Clamp(0f, shiftMin.y, Mathf.Max(shiftMin.y, shiftMax.y));
            Vector3 correction = roof.transform.TransformVector(new Vector3(x, 0f, z));
            offset = horizontalOffset + new Vector3(correction.x, GetRoofHeight(roof) - lowest, correction.z);
            return true;
        }

        private static bool CanPlaceOnRoof(Transform target)
        {
            return target.gameObject.activeInHierarchy && !target.gameObject.CompareTag(BuildingTag) &&
                target.GetComponentInParent<TerrainFoundation>() == null &&
                target.GetComponentInChildren<TerrainCollider>(true) == null;
        }

        private static bool TryGetClosestPlacement(Transform target, List<BoxCollider> roofs, out Vector3 position)
        {
            BoxCollider selected = GetSelectedRoof(roofs);
            if (selected != null)
                return TryGetPlacementAt(target, selected, Vector3.zero, out position);

            position = target.position;
            float bestDistance = RoofCaptureDistance * RoofCaptureDistance;
            float bestVertical = float.PositiveInfinity;
            bool found = false;
            foreach (BoxCollider roof in roofs)
            {
                if (!TryGetPlacementAt(target, roof, Vector3.zero, out Vector3 candidate))
                    continue;

                Vector3 shift = candidate - target.position;
                float distance = shift.x * shift.x + shift.z * shift.z;
                float vertical = Mathf.Abs(shift.y);
                if (distance > bestDistance || (Mathf.Approximately(distance, bestDistance) && vertical >= bestVertical))
                    continue;

                position = candidate;
                bestDistance = distance;
                bestVertical = vertical;
                found = true;
            }
            return found;
        }

        internal static float GetRoofHeight(BoxCollider roof)
        {
            return roof.bounds.max.y;
        }

        private static void AccumulateFootprint(BoxCollider roof, Bounds bounds, Transform source,
            Vector3 horizontalOffset, ref Vector2 footprintMin, ref Vector2 footprintMax)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Matrix4x4 toRoof = roof.transform.worldToLocalMatrix * Matrix4x4.Translate(horizontalOffset);
            if (source != null)
                toRoof *= source.localToWorldMatrix;

            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                var corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 local = toRoof.MultiplyPoint3x4(corner);
                var projected = new Vector2(local.x, local.z);
                footprintMin = Vector2.Min(footprintMin, projected);
                footprintMax = Vector2.Max(footprintMax, projected);
            }
        }

        private static bool Contains(BoxCollider roof, Vector3 worldPoint)
        {
            Vector3 local = roof.transform.InverseTransformPoint(worldPoint) - roof.center;
            Vector3 half = roof.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x + EdgeTolerance && Mathf.Abs(local.z) <= half.z + EdgeTolerance;
        }
    }
}
