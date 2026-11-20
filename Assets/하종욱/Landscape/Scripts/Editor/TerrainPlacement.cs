using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReinProject.Landscape
{
    internal static class TerrainPlacement
    {
        private const string MenuPath = "GameObject/Landscape/Snap to Terrain";
        private const string UndoLabel = "Snap to Terrain";
        private const SelectionMode Mode = SelectionMode.TopLevel | SelectionMode.Editable;

        [MenuItem(MenuPath, true)]
        private static bool CanSnap()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && PrefabStageUtility.GetCurrentPrefabStage() == null
                && System.Array.Exists(Selection.GetTransforms(Mode), target => !EditorUtility.IsPersistent(target));
        }

        [MenuItem(MenuPath, false, 30)]
        private static void SnapSelection()
        {
            if (!CanSnap())
                return;

            Physics.SyncTransforms();
            var terrainsByScene = new Dictionary<Scene, List<TerrainCollider>>();
            var updates = new List<(Transform Target, Vector3 Position, Vector3[] Foundation)>();
            int skipped = 0;

            var targets = new HashSet<Transform>();
            foreach (Transform selected in Selection.GetTransforms(Mode))
            {
                Transform target = GetSnapTarget(selected);
                if (!targets.Add(target))
                    continue;

                Scene scene = target.gameObject.scene;
                if (EditorUtility.IsPersistent(target) || !scene.isLoaded || target.GetComponentInChildren<TerrainCollider>(true) != null || target.GetComponentInParent<TerrainFoundation>() != null)
                {
                    skipped++;
                    continue;
                }

                if (!terrainsByScene.TryGetValue(scene, out var terrains))
                {
                    terrains = FindTerrains(scene);
                    terrainsByScene.Add(scene, terrains);
                }

                if (!TryGetPlacementAt(target, terrains, Vector3.zero, false, out Vector3 position, out Vector3[] foundation))
                {
                    skipped++;
                    continue;
                }

                if ((position - target.position).sqrMagnitude > 0.00000001f || foundation != null ||
                    target.GetComponentInChildren<TerrainFoundation>() != null)
                    updates.Add((target, position, foundation));
            }

            if (updates.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoLabel);

                var editedScenes = new HashSet<Scene>();
                foreach (var update in updates)
                {
                    Transform target = update.Target;
                    ApplyPlacement(target, update.Position, update.Foundation, UndoLabel);
                    editedScenes.Add(target.gameObject.scene);
                }

                foreach (Scene scene in editedScenes)
                    EditorSceneManager.MarkSceneDirty(scene);
                Undo.CollapseUndoOperations(undoGroup);
            }

            if (skipped > 0)
                Debug.LogWarning($"Snap to Terrain: skipped {skipped} selection(s) without a terrain surface in their scene, or containing terrain.");
        }

        private static Transform GetSnapTarget(Transform selected)
        {
            var spline = selected.GetComponentInParent<SandbagSpline>();
            if (spline != null && spline.Output != null &&
                (selected.IsChildOf(spline.Output.transform) || selected.TryGetComponent(out TerrainFoundation _)))
                return spline.transform;

            for (Transform ancestor = selected; ancestor != null; ancestor = ancestor.parent)
            {
                if (SandbagSplineBake.IsBakedArc(ancestor))
                    return ancestor;
            }

            return selected;
        }

        internal static Transform GetPlacementFrame(Transform target)
        {
            if (target.TryGetComponent(out SandbagSpline spline) && spline.Output != null &&
                spline.Output.transform.parent == target)
                return spline.Output.transform;

            return target;
        }

        internal static void ApplyPlacement(Transform target, Vector3 position, Vector3[] foundation, string undoLabel)
        {
            Transform frame = GetPlacementFrame(target);
            if (frame == target && SandbagSplineBake.IsBakedArc(target))
            {
                var owner = target.GetComponentInParent<SandbagSpline>();
                if (owner != null && owner.Output != target.gameObject)
                    Undo.SetTransformParent(target, owner.transform.parent, undoLabel);
            }

            Undo.RecordObject(target, undoLabel);
            if (frame != target && frame.localPosition != Vector3.zero)
            {
                // Transfer the generated arc's offset to its root without changing the arc's world XZ.
                Undo.RecordObject(frame, undoLabel);
                frame.localPosition = Vector3.zero;
                if (PrefabUtility.IsPartOfPrefabInstance(frame))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(frame);
            }

            target.position = position;
            TerrainFoundationPlacement.Apply(target, foundation, undoLabel);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        internal static List<TerrainCollider> FindTerrains(Scene scene)
        {
            var terrains = new List<TerrainCollider>();
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TerrainCollider terrain in root.GetComponentsInChildren<TerrainCollider>())
            {
                if (terrain.enabled && !terrain.isTrigger && terrain.terrainData != null)
                    terrains.Add(terrain);
            }
            return terrains;
        }

        internal static bool TryGetPlacementAt(Transform target, List<TerrainCollider> terrains,
            Vector3 horizontalOffset, bool requireFullFootprint, out Vector3 position, out Vector3[] foundation)
        {
            position = target.position + horizontalOffset;
            foundation = null;
            if (TreeTerrainPlacement.IsTree(target))
                return TreeTerrainPlacement.TryFit(target, terrains, horizontalOffset, requireFullFootprint, out position);

            if (target.TryGetComponent(out SandbagSpline spline) && target.TryGetComponent(out BoxCollider sourceBox))
            {
                return TerrainFoundationPlacement.TryFit(GetPlacementFrame(target), sourceBox, terrains, horizontalOffset,
                    out position, out foundation, new SandbagArc(sourceBox, spline.Angle));
            }

            if (SandbagArc.TryReadFootprint(target, out Vector3 center, out Vector3 dimensions, out float angle))
            {
                return TerrainFoundationPlacement.TryFit(target, center, dimensions, terrains, horizontalOffset,
                    out position, out foundation, new SandbagArc(center, dimensions.x, angle));
            }

            if (TerrainFoundationPlacement.TryGetWallFootprint(target, out BoxCollider box))
                return TerrainFoundationPlacement.TryFit(target, box, terrains, horizontalOffset, out position, out foundation);

            Bounds bounds = GetSupportBounds(target);
            bounds.center += horizontalOffset;
            if (!TryGetGroundHeight(bounds.center.x, bounds.center.z, terrains, out float height))
                return false;

            // Check the footprint so upright objects do not sink in on the uphill side.
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float z in new[] { bounds.min.z, bounds.max.z })
            {
                if (TryGetGroundHeight(x, z, terrains, out float sample))
                    height = Mathf.Max(height, sample);
                else if (requireFullFootprint)
                    return false;
            }

            position.y += height - bounds.min.y;
            return true;
        }

        internal static Bounds GetSupportBounds(Transform target)
        {
            var bounds = new Bounds(target.position, Vector3.zero);
            bool found = false;
            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && !collider.isTrigger && collider.GetComponent<TerrainFoundation>() == null)
                    IncludeBounds(ref bounds, ref found, collider.bounds);
            }

            if (!found)
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.enabled && renderer.GetComponent<TerrainFoundation>() == null)
                        IncludeBounds(ref bounds, ref found, renderer.bounds);
                }
            }
            return bounds;
        }

        private static void IncludeBounds(ref Bounds bounds, ref bool found, Bounds other)
        {
            if (found)
                bounds.Encapsulate(other);
            else
                bounds = other;
            found = true;
        }

        internal static bool TryGetGroundHeight(float x, float z, List<TerrainCollider> terrains, out float height)
        {
            height = float.NegativeInfinity;
            foreach (TerrainCollider terrain in terrains)
            {
                if (TryHitTerrain(terrain, x, z, out float sample))
                    height = Mathf.Max(height, sample);
            }
            return !float.IsNegativeInfinity(height);
        }

        internal static bool TryHitTerrain(TerrainCollider terrain, float x, float z, out float height)
        {
            height = float.NegativeInfinity;
            Bounds bounds = terrain.bounds;
            if (x < bounds.min.x || x > bounds.max.x || z < bounds.min.z || z > bounds.max.z)
                return false;

            var ray = new Ray(new Vector3(x, bounds.max.y + 1f, z), Vector3.down);
            if (!terrain.Raycast(ray, out RaycastHit hit, bounds.size.y + 2f))
                return false;

            height = hit.point.y;
            return true;
        }
    }
}
