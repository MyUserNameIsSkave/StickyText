using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Kept intentionally minimal — editing happens in <see cref="StickyTextSettingsWindow"/>
    /// (Tools ▸ StickyText), not here. This asset still holds the actual data.
    /// </summary>
    [CustomEditor(typeof(StickyTextSettings))]
    public class StickyTextSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit these settings from the StickyText window (Tools ▸ StickyText).",
                MessageType.Info);
            if (GUILayout.Button("Open StickyText Settings"))
                StickyTextSettingsWindow.Open();
        }
    }
}
