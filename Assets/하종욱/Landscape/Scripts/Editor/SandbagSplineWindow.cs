using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ReinProject.Landscape
{
    internal sealed class SandbagSplineWindow : EditorWindow
    {
        internal const string MenuPath = "GameObject/Landscape/Edit Sandbag Spline";
        private const string UndoLabel = "Edit Sandbag Spline";
        private const float DefaultAngle = 115f;

        [SerializeField] private Transform _target;
        [SerializeField] private float _angle;
        [SerializeField] private bool _fitTerrain;

        [MenuItem(MenuPath, true)]
        private static bool CanOpen()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                PrefabStageUtility.GetCurrentPrefabStage() == null &&
                FindBarrier(Selection.activeTransform) != null;
        }

        [MenuItem(MenuPath, false, 34)]
        private static void Open()
        {
            var window = GetWindow<SandbagSplineWindow>("Sandbag Spline");
            window._target = FindBarrier(Selection.activeTransform);
            window._angle = window._target.TryGetComponent(out SandbagSpline spline) ? spline.Angle : DefaultAngle;
            window._fitTerrain = window._target.GetComponentInChildren<TerrainFoundation>() != null;
            SceneView.RepaintAll();
        }

        private static Transform FindBarrier(Transform selected)
        {
            for (var current = selected; current != null; current = current.parent)
            {
                if (!EditorUtility.IsPersistent(current) && current.gameObject.scene.isLoaded &&
                    (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current.gameObject) == SandbagSplineBake.PrefabPath ||
                     SandbagSplineBake.HasPublicSources(current)) &&
                    current.GetComponent<BoxCollider>() != null)
                    return current;
            }
            return null;
        }

        private void OnEnable() => SceneView.duringSceneGui += DrawPreview;

        private void OnDisable() => SceneView.duringSceneGui -= DrawPreview;

        private void OnGUI()
        {
            if (_target == null)
            {
                EditorGUILayout.HelpBox("Select a SandbagBarrier instance and open Edit Sandbag Spline.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Barrier", _target.gameObject, typeof(GameObject), true);

            EditorGUI.BeginChangeCheck();
            _angle = EditorGUILayout.Slider("Arc angle", _angle, -SandbagArc.MaxAngle, SandbagArc.MaxAngle);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();

            _fitTerrain = EditorGUILayout.ToggleLeft("Fit to terrain on apply", _fitTerrain);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Apply Curve"))
                    Apply(_target, _angle, _fitTerrain);
                using (new EditorGUI.DisabledScope(_target.GetComponent<SandbagSpline>() == null))
                {
                    if (GUILayout.Button("Restore Straight"))
                    {
                        if (Apply(_target, 0f, _fitTerrain))
                            _angle = 0f;
                        SceneView.RepaintAll();
                    }
                }
            }
        }

        private void DrawPreview(SceneView view)
        {
            if (_target == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var box = _target.GetComponent<BoxCollider>();
            if (box == null)
                return;

            var arc = new SandbagArc(box, _angle);
            var front = new Vector3[37];
            var back = new Vector3[front.Length];
            for (int i = 0; i < front.Length; i++)
            {
                Vector3 point = box.center;
                point.x += box.size.x * (i / (float)(front.Length - 1) - 0.5f);
                point.y += box.size.y * .5f;
                var depth = Vector3.forward * box.size.z * .5f;
                front[i] = _target.TransformPoint(arc.Map(point + depth));
                back[i] = _target.TransformPoint(arc.Map(point - depth));
            }

            using (new Handles.DrawingScope(new Color(1f, .32f, .16f)))
            {
                Handles.DrawAAPolyLine(3f, front);
                Handles.DrawAAPolyLine(3f, back);
                Vector3 center = box.center + Vector3.up * (box.size.y * .5f);
                Vector3 handle = _target.TransformPoint(arc.Map(center));
                float size = HandleUtility.GetHandleSize(handle) * .08f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider(handle, _target.forward, size, Handles.SphereHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    float sagitta = _target.InverseTransformPoint(moved).z - center.z;
                    _angle = Mathf.Clamp(4f * Mathf.Atan2(sagitta, box.size.x * .5f) * Mathf.Rad2Deg,
                        -SandbagArc.MaxAngle, SandbagArc.MaxAngle);
                    Repaint();
                }
            }
        }

        internal static bool Apply(Transform target, float angle, bool fitTerrain)
        {
            if (FindBarrier(target) != target || EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            angle = Mathf.Abs(angle) < .5f ? 0f : Mathf.Clamp(angle, -SandbagArc.MaxAngle, SandbagArc.MaxAngle);
            var existing = target.GetComponent<SandbagSpline>();
            var box = target.GetComponent<BoxCollider>();
            if (box.size.x <= .05f || box.size.x < box.size.z * 2f)
                return false;
            if (existing == null && angle == 0f)
                return true;

            Physics.SyncTransforms();
            var arc = new SandbagArc(box, angle);
            Vector3 position = target.position;
            Vector3[] foundation = null;
            if (fitTerrain)
            {
                var terrains = TerrainPlacement.FindTerrains(target.gameObject.scene);
                if (!TerrainFoundationPlacement.TryFit(target, box, terrains, Vector3.zero, out position, out foundation,
                    angle == 0f ? null : arc))
                {
                    Debug.LogWarning("Sandbag Spline: the barrier footprint must fit on the terrain to ground it.", target);
                    return false;
                }
            }

            MeshRenderer[] sources = existing != null ? existing.Sources : SandbagSplineBake.FindSources(target);
            if (sources.Length == 0)
                return false;

            Mesh mesh = null;
            Material[] materials = null;
            if (angle != 0f)
                mesh = SandbagSplineBake.Bake(target, box, angle, sources, out materials);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                if (existing != null && existing.Output != null)
                    Undo.DestroyObjectImmediate(existing.Output);

                foreach (MeshRenderer renderer in sources)
                {
                    Undo.RecordObject(renderer, UndoLabel);
                    renderer.enabled = angle == 0f;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }

                Undo.RecordObject(box, UndoLabel);
                box.enabled = angle == 0f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(box);

                if (angle == 0f)
                {
                    Undo.DestroyObjectImmediate(existing);
                }
                else
                {
                    var output = CreateOutput(target, box, angle, arc, mesh, materials);
                    if (existing == null)
                        existing = Undo.AddComponent<SandbagSpline>(target.gameObject);
                    Undo.RecordObject(existing, UndoLabel);
                    existing.SetBake(angle, output, sources);
                }

                Undo.RecordObject(target, UndoLabel);
                target.position = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                TerrainFoundationPlacement.Apply(target, foundation, UndoLabel);
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }

            SceneView.RepaintAll();
            return true;
        }

        private static GameObject CreateOutput(Transform target, BoxCollider source, float angle, SandbagArc arc,
            Mesh mesh, Material[] materials)
        {
            var output = new GameObject("Sandbag Arc");
            output.layer = target.gameObject.layer;
            output.tag = "ArenaSandbag";
            output.AddComponent<MeshFilter>().sharedMesh = mesh;
            output.AddComponent<MeshRenderer>().sharedMaterials = materials;

            int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(angle) / 9f));
            for (int i = 0; i < count; i++)
            {
                Vector3 center = source.center;
                center.x += source.size.x * ((i + .5f) / count - .5f);
                var section = new GameObject($"Arc Collider {i + 1}");
                section.layer = output.layer;
                section.tag = output.tag;
                section.transform.SetParent(output.transform, false);
                Vector3 mapped = arc.Map(center, out Quaternion rotation, out float stretch);
                section.transform.SetLocalPositionAndRotation(mapped, rotation);
                var collider = section.AddComponent<BoxCollider>();
                collider.sharedMaterial = source.sharedMaterial;
                collider.size = new Vector3(source.size.x * stretch / count + .045f,
                    source.size.y, source.size.z + SandbagArc.ColliderDepthPadding);
            }

            if (output.scene != target.gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(output, target.gameObject.scene);
            output.transform.SetParent(target, false);
            Undo.RegisterCreatedObjectUndo(output, UndoLabel);
            return output;
        }
    }
}
