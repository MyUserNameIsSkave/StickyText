using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Scene view overlay panel exposing <see cref="StickyTextSettings"/> without hunting
    /// for the asset in the Project window. Drag it, dock it, or collapse it like any other
    /// Scene view overlay (right-click the overlay toolbar to show/hide it).
    /// </summary>
    [Overlay(typeof(SceneView), k_Id, "StickyText", defaultDisplay = true)]
    public class StickyTextOverlay : IMGUIOverlay
    {
        const string k_Id = "StickyText.Overlay";
        const float k_Width = 230f;

        // Reserved height for the tab content area, sized for the tallest tab (Align to
        // Surface: Offset + Floor/Ceiling Snap + Snap Margin + Snap Offset, one 4px gap), so
        // switching tabs never resizes the overlay.
        const float k_TabContentHeight = 88f;

        enum Tab { FaceCamera, AlignToSurface, FixedDistance }
        Tab m_Tab;

        bool m_TextFoldout = true;

        static StickyTextSettings Settings => StickyTextSettings.Instance;

        public override void OnCreated()
        {
            ToolManager.activeToolChanged += UpdateVisibility;
            UpdateVisibility();
        }

        public override void OnWillBeDestroyed()
        {
            ToolManager.activeToolChanged -= UpdateVisibility;
        }

        /// <summary>Fully hides the overlay whenever the StickyText tool isn't the active
        /// Scene view tool, and shows it again once it is — avoids the collapsed-icon look
        /// varying with wherever the overlay happens to be docked/floating.</summary>
        void UpdateVisibility()
        {
            displayed = ToolManager.activeToolType == typeof(StickyTextTool);
        }

        public override void OnGUI()
        {
            var s = Settings;
            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 110f;

            EditorGUILayout.BeginVertical(GUILayout.Width(k_Width));
            EditorGUI.BeginChangeCheck();

            DrawTextSection(s);
            EditorGUILayout.Space(8);
            DrawModeTabs(s);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(s);

            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        void DrawTextSection(StickyTextSettings s)
        {
            // One dark box contains both the header and the content, so the whole section reads
            // as a single panel. Drawn manually (not EditorStyles.helpBox, which renders lighter
            // than the surrounding overlay) so it recedes instead of standing out; the foldout
            // itself uses the plain arrow+label style, with no background of its own, so it
            // doesn't look like a second, narrower bar on top of the box.
            var boxRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(boxRect, new Color(0f, 0f, 0f, 0f));
            GUILayout.Space(2);

            m_TextFoldout = EditorGUILayout.Foldout(m_TextFoldout, "Global Settings", true);
            if (m_TextFoldout)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(22);
                EditorGUILayout.BeginVertical();

                // Tag applied to every newly placed label — same coloured picker as the
                // Management window's label list and the TextLabel inspector. Also the only
                // control over the background's colour now (no separate Background
                // toggle/colour here — the plane is always shown, tinted by this tag, or by
                // DEFAULT's colour (edited in the Tags manager) when none is picked).
                var tagRect = EditorGUILayout.GetControlRect();
                var tagFieldRect = EditorGUI.PrefixLabel(tagRect, new GUIContent("Tag"));
                StickyTextTagGUI.DrawPicker(tagFieldRect, s.currentTag, tag =>
                {
                    s.currentTag = tag?.name ?? "";
                    // Reclaim currentEditorOnly from the picked tag (or from DEFAULT's own
                    // editorOnly) — never touch editorOnly/the tag's own stored value here, or
                    // picking a real tag would permanently clobber DEFAULT's value, making it
                    // impossible to go back to DEFAULT's actual setting afterward.
                    s.currentEditorOnly = tag?.editorOnly ?? s.editorOnly;
                });

                // Manually toggling this after picking a tag overrides it for future placements
                // until the tag is (re)picked, which reclaims it — same override-until-next-pick
                // behaviour as a label's own Editor Only after assigning it a tag.
                s.currentEditorOnly = EditorGUILayout.Toggle("Editor Only", s.currentEditorOnly);

                // Not tag-driven (unlike Tag/Editor Only above) — just the default applied to
                // every newly placed label, same as the Settings window's own copy of this field.
                s.forceUppercase = EditorGUILayout.Toggle("Force Uppercase", s.forceUppercase);

                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }

        void DrawModeTabs(StickyTextSettings s)
        {
            StickyTextTool.TryGetActiveInstance(out var active);

            // m_Tab is the sticky tab the user picked here; it is never rewritten just because
            // Ctrl is held, so a momentary Ctrl press/release can never disturb it. Keep it in
            // sync with the tool's persistent base mode only while it isn't stickily set to
            // Fixed Distance — except when that override was itself just cleared elsewhere (the
            // toggle-mode key, or releasing Ctrl), in which case resync immediately instead of
            // staying stuck showing a tab that's no longer actually in effect.
            if (active != null &&
                (m_Tab != Tab.FixedDistance || !StickyTextTool.IsFixedDistanceForcedByOverlay))
                m_Tab = active.BaseModeIsAlignToSurface ? Tab.AlignToSurface : Tab.FaceCamera;

            // While Ctrl is physically held, preview Fixed Distance without touching m_Tab —
            // releasing Ctrl simply goes back to showing whatever was stickily selected.
            bool ctrlPreview = active != null && active.IsFixedDistanceHeldByCtrl;
            var displayTab = ctrlPreview ? Tab.FixedDistance : m_Tab;

            var labels = new[] { "Camera", "Surface", "Fixed" };
            var clicked = (Tab)GUILayout.Toolbar((int)displayTab, labels);

            if (clicked != displayTab)
            {
                if (active == null)
                {
                    ToolManager.SetActiveTool<StickyTextTool>();
                    StickyTextTool.TryGetActiveInstance(out active);
                }

                // Picking Fixed Distance here forces it on, exactly as if Ctrl were held, until
                // another tab is picked instead. Picking a base-mode tab always drops that
                // override, even if Ctrl happens to still be held.
                m_Tab = clicked;
                StickyTextTool.SetFixedDistanceForcedByOverlay(clicked == Tab.FixedDistance);
                if (clicked != Tab.FixedDistance)
                    active?.SetBaseMode(clicked == Tab.AlignToSurface);
                displayTab = clicked;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(k_TabContentHeight));
            GUILayout.Space(22);
            EditorGUILayout.BeginVertical();
            switch (displayTab)
            {
                case Tab.FaceCamera: DrawFaceCameraTab(s); break;
                case Tab.AlignToSurface: DrawAlignToSurfaceTab(s); break;
                case Tab.FixedDistance: DrawFixedDistanceTab(s); break;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        static void DrawFaceCameraTab(StickyTextSettings s)
        {
            s.faceCameraOffset = DrawSlider("Offset", s.faceCameraOffset, 0f, 5f);
        }

        static void DrawAlignToSurfaceTab(StickyTextSettings s)
        {
            s.surfaceOffset = DrawSlider("Offset", s.surfaceOffset, 0f, 0.5f);
            EditorGUILayout.Space(4);

            s.alignFloorCeilingToWorldAxes =
                EditorGUILayout.Toggle("Floor/Ceiling Snap", s.alignFloorCeilingToWorldAxes);
            if (s.alignFloorCeilingToWorldAxes)
            {
                s.axisSnapMargin = DrawSlider("Snap Margin", s.axisSnapMargin, 0f, 45f);
                s.snapDetectionOffset = DrawSlider("Snap Offset", s.snapDetectionOffset, -180f, 180f);
            }
        }

        static void DrawFixedDistanceTab(StickyTextSettings s)
        {
            s.fixedPlaneDistance = EditorGUILayout.FloatField("Distance", s.fixedPlaneDistance);
            s.fixedPlaneDistance = Mathf.Max(0.01f, s.fixedPlaneDistance);
            s.fixedPlaneDistanceDragSensitivity =
                DrawSlider("Drag Sensitivity", s.fixedPlaneDistanceDragSensitivity, 0.001f, 0.2f);
        }

        // Hand-rolled replacement for EditorGUILayout.Slider: some installed Inspector-patching
        // extensions (VInspector/VTabs/Better Inspector) intercept EditorGUI's own slider
        // drawing and were leaving it rendering as a bare number field with no track/handle in
        // this overlay. GUI.HorizontalSlider is a different (UnityEngine, not UnityEditor) API
        // that those patches don't touch, so it keeps working regardless.
        static float DrawSlider(string label, float value, float min, float max)
        {
            var rect = EditorGUILayout.GetControlRect();

            const float fieldWidth = 40f;
            const float fieldGap = 4f;

            var labelWidth = EditorGUIUtility.labelWidth;
            var labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            var fieldRect = new Rect(rect.xMax - fieldWidth, rect.y, fieldWidth, rect.height);
            var sliderRect = new Rect(
                labelRect.xMax, rect.y, fieldRect.x - fieldGap - labelRect.xMax, rect.height);

            GUI.Label(labelRect, label);
            value = GUI.HorizontalSlider(sliderRect, value, min, max);
            value = EditorGUI.FloatField(fieldRect, value);
            return Mathf.Clamp(value, min, max);
        }
    }
}
