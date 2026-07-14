using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Lets a <see cref="TextLabel"/> be typed into directly, right after placement, or when
    /// selected (from the Hierarchy or by clicking it) while the StickyText tool is active
    /// — never as a side effect of selecting one with another tool, to avoid accidentally
    /// hijacking the keyboard. Capture happens via an off-screen invisible field (so no
    /// on-screen input box is ever shown).
    /// </summary>
    [InitializeOnLoad]
    static class TextLabelInlineEditor
    {
        const string k_EditControlName = "TextLabelEditor";

        static StickyTextSettings Settings => StickyTextSettings.Instance;

        static TextLabel s_Editing;
        static bool s_FocusPending;
        static int s_SelectAllGuardFrames; // extra frames, past focus confirmation, spent undoing Unity's auto select-all
        static bool s_HasEdited; // whether any change has happened since BeginEditing — see the Delete-key special case below
        static Vector3[] s_EditCorners; // zone indicator drawn until the text is validated
        static GameObject s_LastAutoEditTarget; // last selection considered for auto-edit, to fire once

        public static bool IsEditing => s_Editing != null;

        static TextLabelInlineEditor()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        /// <summary>
        /// Starts typing into <paramref name="label"/> right away. Pass the freshly projected
        /// corners to keep showing a placement zone gizmo until validated (a brand new
        /// placement); leave null for an already-existing object, which has its own visual.
        /// </summary>
        public static void BeginEditing(TextLabel label, Vector3[] zoneGizmo = null)
        {
            s_Editing = label;
            s_EditCorners = zoneGizmo;
            s_LastAutoEditTarget = label != null ? label.gameObject : null;
            s_FocusPending = true;
            s_SelectAllGuardFrames = 8;
            s_HasEdited = false;
        }

        /// <summary>Commits/closes the current edit session (if any) and deselects.</summary>
        public static void EndEditing()
        {
            if (s_Editing == null)
                return;
            StopEditingWithoutDeselecting();
            Selection.activeGameObject = null; // deselect once the text is finished
        }

        /// <summary>Clears the local editing state without touching the selection — used
        /// before an Undo/Redo, which manages selection on its own.</summary>
        static void StopEditingWithoutDeselecting()
        {
            s_Editing = null;
            s_EditCorners = null;
            s_FocusPending = false;
            s_SelectAllGuardFrames = 0;
            s_HasEdited = false;
            s_LastAutoEditTarget = null;
            EditorGUIUtility.editingTextField = false;
            GUIUtility.keyboardControl = 0;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            var e = Event.current;

            if (s_Editing != null && HandleTextEditing(sceneView, e))
                return;

            // Selecting an existing label (from the Hierarchy, or by clicking it with any
            // tool) starts typing straight away — but never in direct response to a mouse
            // action, so clicking/dragging elsewhere is free to mean something else (e.g. the
            // StickyText tool's own drag-to-place).
            TryAutoBeginEditing(sceneView, e);
        }

        static void TryAutoBeginEditing(SceneView sceneView, Event e)
        {
            var active = Selection.activeGameObject;
            if (active == s_LastAutoEditTarget)
                return;
            s_LastAutoEditTarget = active;

            if (s_Editing != null)
                return;

            // Only auto-start typing when the selection happened while the StickyText tool
            // is active — selecting a label with Move/Rotate/etc. should just select it, not
            // risk hijacking the keyboard for text entry.
            if (ToolManager.activeToolType != typeof(StickyTextTool))
                return;

            if (active != null && active.TryGetComponent<TextLabel>(out var label))
            {
                // Selecting from elsewhere (e.g. the Hierarchy) leaves keyboard focus on that
                // window; without moving it to the Scene view, keystrokes never reach us at all.
                sceneView.Focus();
                BeginEditing(label);
                sceneView.Repaint();
            }
        }

        /// <summary>
        /// Shows the zone indicator for the label being typed into and captures keystrokes via
        /// an off-screen, invisible text field (which keeps keyboard focus so Scene shortcuts
        /// stay suppressed). Returns true when a commit/cancel key was consumed.
        /// </summary>
        static bool HandleTextEditing(SceneView sceneView, Event e)
        {
            if (s_Editing == null) // may have been deleted
            {
                EndEditing();
                return false;
            }

            // Right-click means "I'm navigating the camera now" (Unity's own orbit/fly controls
            // take it from here — the event is deliberately left unused so they still see it).
            // Fully deselect instead of just ending the edit session, so held keys during a
            // right-click flythrough (WASD) always reach the camera, never a leftover invisible
            // field still holding keyboard focus.
            if (e.type == EventType.MouseDown && e.button == 1)
            {
                EndEditing();
                return false;
            }

            // The selection changed away from the label being edited without going through
            // Enter/Escape (e.g. the user picked something else, or deselected, from the
            // Hierarchy, or via a different tool). Release the edit session instead of holding
            // onto it forever.
            if (Selection.activeGameObject != s_Editing.gameObject)
            {
                StopEditingWithoutDeselecting(); // selection already changed; don't stomp it
                return false;
            }

            // Zone indicator, shown until the text is validated.
            if (s_EditCorners != null)
                Handles.DrawSolidRectangleWithOutline(
                    s_EditCorners, Settings.surfaceFill, Settings.surfaceOutline);

            // Finish on Enter/Escape before the field can swallow or revert them.
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                 e.keyCode == KeyCode.Escape))
            {
                EndEditing();
                e.Use();
                sceneView.Repaint();
                return true;
            }

            // Ctrl/Cmd+Z (+Shift for redo) would otherwise be swallowed by the focused text
            // field as an in-field undo. Forward it to Unity's global Undo/Redo instead, so
            // the placement itself (or the last edit) can still be undone right away.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command))
            {
                bool redo = e.shift;
                StopEditingWithoutDeselecting(); // clear our own state first, regardless of outcome below
                try
                {
                    if (redo) Undo.PerformRedo(); else Undo.PerformUndo();
                }
                catch (System.Exception ex)
                {
                    // Undo/Redo can trigger SceneGUI callbacks belonging to other systems (e.g.
                    // URP's Adaptive Probe Volumes) that may throw on their own; don't let that
                    // leave this tool's state stuck or propagate past this handler.
                    Debug.LogException(ex);
                }
                e.Use();
                sceneView.Repaint();
                return true;
            }

            // The Delete key is normally a global "delete selected GameObject" shortcut that
            // bypasses the focused field entirely.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                if (string.IsNullOrEmpty(s_Editing.Text))
                {
                    // Empty label: delete the object instead (e.g. an accidental/abandoned
                    // placement).
                    var go = s_Editing.gameObject;
                    EndEditing(); // clears our state and deselects before the object is gone
                    Undo.DestroyObjectImmediate(go);
                    e.Use();
                    sceneView.Repaint();
                    return true;
                }

                if (!s_HasEdited)
                {
                    // First action right after selecting a non-empty label: clear it in one
                    // press, matching the old "everything pre-selected" behaviour — without
                    // permanently re-enabling Unity's select-all, which would also make plain
                    // typing replace instead of append.
                    Undo.RecordObject(s_Editing, "Edit Text Label");
                    s_Editing.Text = string.Empty;
                    StickyTextLabelNaming.Apply(s_Editing);
                    s_HasEdited = true;
                    e.Use();
                    sceneView.Repaint();
                    return true;
                }
                // Otherwise leave the event alone: the field's own Delete handling applies
                // (forward-delete at the cursor), same as any normal text field.
            }

            // Invisible off-screen field: captures characters and holds focus, no window shown.
            Handles.BeginGUI();
            GUI.SetNextControlName(k_EditControlName);
            string newText = GUI.TextField(new Rect(-100f, -100f, 1f, 1f), s_Editing.Text);
            if (s_FocusPending)
            {
                // The Scene view may not have OS/window focus yet (e.g. selection came from the
                // Hierarchy) — keep re-requesting both window and control focus every event
                // until the control actually reports itself focused, not just after one Repaint.
                if (EditorWindow.focusedWindow != sceneView)
                    sceneView.Focus();
                GUI.FocusControl(k_EditControlName);
                EditorGUIUtility.editingTextField = true;

                if (GUI.GetNameOfFocusedControl() == k_EditControlName)
                    s_FocusPending = false;
                else
                    sceneView.Repaint(); // keep pumping events until focus is confirmed
            }

            // A text field selects all its existing content once it detects it just gained
            // keyboard focus (Unity's normal behaviour), which would make the first keystroke
            // replace everything instead of appending. That internal detection can land a
            // frame or two AFTER GetNameOfFocusedControl above already confirms the name match
            // (it happens inside GUI.TextField's own processing, one call later), so keep
            // collapsing the selection to a cursor at the end for a few extra frames past
            // confirmation, not just until confirmation.
            if (s_SelectAllGuardFrames > 0 && GUI.GetNameOfFocusedControl() == k_EditControlName)
            {
                if (GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) is TextEditor editor)
                    editor.MoveTextEnd();
                s_SelectAllGuardFrames--;
                sceneView.Repaint();
            }
            Handles.EndGUI();

            if (newText != s_Editing.Text)
            {
                Undo.RecordObject(s_Editing, "Edit Text Label");
                s_Editing.Text = newText;
                StickyTextLabelNaming.Apply(s_Editing);
                s_HasEdited = true;
                sceneView.Repaint();
            }

            return false;
        }
    }
}
