using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Unity Shortcut Manager bindings for <see cref="StickyTextTool"/> — rebindable via
    /// Edit ▸ Shortcuts ▸ "StickyText", instead of hardcoded keys.
    /// </summary>
    static class StickyTextShortcuts
    {
        /// <summary>Switches the Scene view to the StickyText tool. No default key (avoids
        /// guessing at a binding that might already be in use) — assign one under Edit ▸
        /// Shortcuts ▸ StickyText ▸ Activate Tool.</summary>
        [Shortcut("StickyText/Activate Tool")]
        static void ActivateTool()
        {
            ToolManager.SetActiveTool<StickyTextTool>();
        }

        /// <summary>Cycles the base placement mode (Face Camera / Align to Surface). Only acts
        /// while the tool is active and no label is currently being typed into, so it never
        /// steals an "m" keystroke out of a sign's text.</summary>
        [Shortcut("StickyText/Toggle Mode", typeof(SceneView), KeyCode.M)]
        static void ToggleMode()
        {
            if (TextLabelInlineEditor.IsEditing)
                return;
            if (ToolManager.activeToolType == typeof(StickyTextTool) &&
                StickyTextTool.TryGetActiveInstance(out var tool))
                tool.ToggleMode();
        }

        // Fixed Distance mode's "hold" key is hardcoded to Alt directly in
        // StickyTextTool.OnToolGUI (via Event.alt), not a Shortcut Manager binding: the
        // Shortcuts window doesn't accept a bare modifier key (no companion key) as a clutch
        // shortcut.
    }
}
