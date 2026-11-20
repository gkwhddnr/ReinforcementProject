using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReinProject.Landscape
{
    internal static class TerrainClipboardPlacement
    {
        internal const string MenuPath = "GameObject/Landscape/Paste and Snap to Terrain";
        internal const string RoofMenuPath = "GameObject/Landscape/Paste and Snap to Roof";

        [MenuItem(MenuPath, false, 31)]
        private static void PasteAtSceneViewCenter()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                TryPasteAt(view.pivot);
        }

        [MenuItem(RoofMenuPath, false, 33)]
        private static void PasteOnRoofAtSceneViewCenter()
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null)
                TryPasteAt(view.pivot, onRoof: true, roofRay: view.camera.ViewportPointToRay(new Vector3(.5f, .5f)));
        }

        [MenuItem(MenuPath, true)]
        [MenuItem(RoofMenuPath, true)]
        private static bool CanPasteAtSceneViewCenter()
        {
            return CanEditScene() && SceneView.lastActiveSceneView != null;
        }

        internal static bool TryPasteAt(Vector3 point)
        {
            return TryPasteAt(point, onRoof: false);
        }

        internal static bool TryPasteOnRoofAt(Vector3 point)
        {
            return TryPasteAt(point, onRoof: true);
        }

        private static bool TryPasteAt(Vector3 point, bool onRoof, Ray? roofRay = null)
        {
            if (!CanEditScene())
                return false;

            string undoLabel = onRoof ? "Paste and Snap to Roof" : "Paste and Snap to Terrain";

            Physics.SyncTransforms();

            List<TerrainCollider> terrains = null;
            BoxCollider roof = null;
            Scene destination;
            Collider surface;
            if (onRoof)
            {
                if (!TryGetDestination(RoofPlacement.FindRoofs, out destination, out List<BoxCollider> roofs))
                {
                    Debug.LogWarning($"{undoLabel}: select an object in the intended scene with a building roof.");
                    return false;
                }

                roof = roofRay.HasValue ? RoofPlacement.GetSelectedRoof(roofs) : null;
                if (roof != null)
                {
                    point = roof.transform.TransformPoint(roof.center);
                }
                else if (roofRay.HasValue)
                {
                    roof = RoofPlacement.FindRoofAlongRay(roofRay.Value, roofs, out Vector3 hitPoint);
                    if (roof != null)
                        point = hitPoint;
                }

                if (roof == null)
                    roof = RoofPlacement.FindRoofAt(point, roofs);
                surface = roof;
            }
            else
            {
                if (!TryGetDestination(TerrainPlacement.FindTerrains, out destination, out terrains))
                {
                    Debug.LogWarning($"{undoLabel}: select an object in the intended terrain scene.");
                    return false;
                }

                surface = FindTerrainAt(point, terrains);
            }

            if (surface == null)
            {
                Debug.LogWarning(onRoof
                    ? $"{undoLabel}: select a building or point the Scene view center at it."
                    : $"{undoLabel}: move the Scene view center onto the terrain.");
                return false;
            }

            Transform placementRoot = surface.transform.root;
            Scene previousScene = SceneManager.GetActiveScene();
            Object[] previousSelection = Selection.objects;
            var existingObjects = GetSceneObjectIds();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);
            bool placed = false;

            try
            {
                if (previousScene != destination && !SceneManager.SetActiveScene(destination))
                    return false;

                // Unity's hierarchy clipboard keeps prefab connections and references between pasted objects.
                Selection.objects = System.Array.Empty<Object>();
                Unsupported.PasteGameObjectsFromPasteboard();
                var pasted = new List<Transform>();
                foreach (Transform item in Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable))
                {
                    if (item.gameObject.scene.isLoaded && !existingObjects.Contains(item.GetInstanceID()))
                        pasted.Add(item);
                }
                if (pasted.Count == 0)
                {
                    Debug.LogWarning($"{undoLabel}: copy scene objects with Ctrl+C first.");
                    return false;
                }

                Vector3 center = Vector3.zero;
                foreach (Transform item in pasted)
                    center += onRoof ? item.position : TerrainPlacement.GetPlacementFrame(item).position;
                center /= pasted.Count;
                var offset = new Vector3(point.x - center.x, 0f, point.z - center.z);
                var positions = new Vector3[pasted.Count];
                var foundations = new Vector3[pasted.Count][];

                Physics.SyncTransforms();
                if (onRoof)
                {
                    if (!RoofPlacement.TryFitOnRoof(pasted, roof, offset, out Vector3 fit))
                    {
                        Debug.LogWarning($"{undoLabel}: the copied selection does not fit on this roof.");
                        return false;
                    }
                    for (int i = 0; i < pasted.Count; i++)
                        positions[i] = pasted[i].position + fit;
                }
                else
                {
                    for (int i = 0; i < pasted.Count; i++)
                    {
                        Transform item = pasted[i];
                        if (item.GetComponentInChildren<TerrainCollider>(true) != null ||
                            !TerrainPlacement.TryGetPlacementAt(item, terrains, offset, true, out positions[i], out foundations[i]))
                        {
                            Debug.LogWarning($"{undoLabel}: '{item.name}' cannot fit on the terrain at this location.");
                            return false;
                        }
                    }
                }

                for (int i = 0; i < pasted.Count; i++)
                {
                    Transform item = pasted[i];
                    if (item.gameObject.scene != destination)
                    {
                        if (item.parent != null)
                            Undo.SetTransformParent(item, null, undoLabel);
                        Undo.MoveGameObjectToScene(item.gameObject, destination, undoLabel);
                    }
                    if (!item.IsChildOf(placementRoot))
                        Undo.SetTransformParent(item, placementRoot, undoLabel);

                    if (!onRoof)
                    {
                        TerrainPlacement.ApplyPlacement(item, positions[i], foundations[i], undoLabel);
                        continue;
                    }

                    Undo.RecordObject(item, undoLabel);
                    item.position = positions[i];
                    TerrainFoundationPlacement.Apply(item, null, undoLabel, removeNested: true);
                    if (PrefabUtility.IsPartOfPrefabInstance(item))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(item);
                }

                EditorSceneManager.MarkSceneDirty(destination);
                Undo.SetCurrentGroupName(undoLabel);
                Undo.CollapseUndoOperations(undoGroup);
                placed = true;
                return true;
            }
            finally
            {
                if (!placed)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    Selection.objects = previousSelection;
                }

                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
            }
        }

        private static bool CanEditScene()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        private static TerrainCollider FindTerrainAt(Vector3 point, List<TerrainCollider> terrains)
        {
            TerrainCollider surface = null;
            float surfaceHeight = float.NegativeInfinity;
            foreach (var terrain in terrains)
            {
                if (TerrainPlacement.TryHitTerrain(terrain, point.x, point.z, out float height) && height > surfaceHeight)
                {
                    surface = terrain;
                    surfaceHeight = height;
                }
            }
            return surface;
        }

        private static bool TryGetDestination<T>(System.Func<Scene, List<T>> findSurfaces,
            out Scene scene, out List<T> surfaces) where T : Collider
        {
            var selected = Selection.activeTransform;
            if (selected != null && !EditorUtility.IsPersistent(selected) &&
                TryGetSurfaces(selected.gameObject.scene, findSurfaces, out surfaces))
            {
                scene = selected.gameObject.scene;
                return true;
            }

            scene = SceneManager.GetActiveScene();
            if (TryGetSurfaces(scene, findSurfaces, out surfaces))
                return true;

            scene = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene candidate = SceneManager.GetSceneAt(i);
                if (!TryGetSurfaces(candidate, findSurfaces, out var candidateSurfaces))
                    continue;
                if (scene.IsValid())
                    return false;
                scene = candidate;
                surfaces = candidateSurfaces;
            }
            return scene.IsValid();
        }

        private static bool TryGetSurfaces<T>(Scene scene, System.Func<Scene, List<T>> findSurfaces,
            out List<T> surfaces) where T : Collider
        {
            surfaces = scene.IsValid() && scene.isLoaded ? findSurfaces(scene) : null;
            return surfaces != null && surfaces.Count > 0;
        }

        private static HashSet<int> GetSceneObjectIds()
        {
            var ids = new HashSet<int>();
            var descendants = new List<Transform>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, descendants);
                    foreach (Transform item in descendants)
                        ids.Add(item.GetInstanceID());
                }
            }
            return ids;
        }
    }
}
