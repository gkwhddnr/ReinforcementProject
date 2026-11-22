using UnityEditor;
using UnityEngine;

namespace ReinProject.Landscape
{
    [CustomPropertyDrawer(typeof(ArenaCoverEntry))]
    internal sealed class ArenaCoverEntryDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float lineStep = lineHeight + EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(position.x, position.y, position.width, lineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                SerializedProperty colliders = property.FindPropertyRelative("_colliders");
                var firstCollider = colliders.arraySize > 0
                    ? colliders.GetArrayElementAtIndex(0).objectReferenceValue as Collider
                    : null;
                string layerName = firstCollider ? LayerMask.LayerToName(firstCollider.gameObject.layer) : "—";
                string tag = firstCollider ? firstCollider.tag : "—";

                using (new EditorGUI.IndentLevelScope())
                {
                    row.y += lineStep;
                    EditorGUI.PropertyField(row, property.FindPropertyRelative("_id"));
                    row.y += lineStep;
                    EditorGUI.LabelField(row, "Layer", layerName);
                    row.y += lineStep;
                    EditorGUI.LabelField(row, "Tag", tag);
                    row.y += lineStep;
                    row.height = EditorGUI.GetPropertyHeight(colliders, true);
                    EditorGUI.PropertyField(row, colliders, true);
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
                return lineHeight;

            float lineStep = lineHeight + EditorGUIUtility.standardVerticalSpacing;
            return 4 * lineStep + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("_colliders"), true);
        }
    }
}
