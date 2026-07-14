using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Draws the <see cref="TextLabel"/> inspector in an explicit order (Tag, Text, Margin,
    /// Text/Background Color, Editor Only) instead of raw declaration order, and replaces the
    /// raw "Tag" string field with the same coloured tag picker used in the Management window's
    /// label list.
    /// </summary>
    [CustomEditor(typeof(TextLabel))]
    class TextLabelEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "A StickyText label. Manage, search, and tag every label in the open scene(s) " +
                "from Tools ▸ StickyText.", MessageType.Info);
            EditorGUILayout.Space(6);

            serializedObject.Update();

            var label = (TextLabel)target;

            var tagRect = EditorGUILayout.GetControlRect();
            var tagFieldRect = EditorGUI.PrefixLabel(tagRect, new GUIContent("Tag"));
            StickyTextTagGUI.DrawPicker(tagFieldRect, label.Tag, tag =>
            {
                Undo.RecordObject(label, "Set Tag");
                label.Tag = tag?.name ?? "";
                label.TextColorOverride = tag?.textColor ?? StickyTextSettings.Instance.textColor;
                label.BackgroundColorOverride = tag?.color ?? StickyTextSettings.Instance.backgroundColor;
                label.EditorOnly = tag?.editorOnly ?? StickyTextSettings.Instance.editorOnly;
            });
            EditorGUILayout.Space(8);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Text"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                StickyTextLabelNaming.Apply(label);
            }
            EditorGUILayout.Space(8);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Margin"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_ForceUppercase"), new GUIContent("Force Uppercase"));
            EditorGUILayout.Space(8);

            // Custom labels (not "Text Color Override"/"Background Color Override", the fields'
            // own nicified names) while still keeping each field's [Tooltip] intact.
            var textColorProp = serializedObject.FindProperty("m_TextColorOverride");
            EditorGUILayout.PropertyField(textColorProp, new GUIContent("Text Color", textColorProp.tooltip));
            var backgroundColorProp = serializedObject.FindProperty("m_BackgroundColorOverride");
            EditorGUILayout.PropertyField(backgroundColorProp, new GUIContent("Background Color", backgroundColorProp.tooltip));
            EditorGUILayout.Space(8);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_EditorOnly"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
