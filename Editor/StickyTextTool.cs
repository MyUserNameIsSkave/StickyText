using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Scene view tool: drag a rectangle in the viewport to project a plane onto the
    /// level geometry. The plane is placed facing the camera at the depth where the
    /// selection hits geometry, and sized so that — as long as the viewport does not
    /// move — its on-screen footprint matches the selected rectangle exactly.
    /// </summary>
    [EditorTool("StickyText")]
    public class StickyTextTool : EditorTool
    {
        static StickyTextSettings Settings => StickyTextSettings.Instance;

        enum PlacementMode
        {
            FaceCamera,     // plane faces the camera; matches the screen selection exactly
            AlignToSurface, // plane lies on the surface hit at the start of the drag
            FixedDistance,  // plane faces the camera at a fixed, user-set distance
        }

        // Base mode, cycled via the "StickyText/Toggle Mode" shortcut ([M] by default);
        // only ever FaceCamera or AlignToSurface.
        PlacementMode m_Mode = PlacementMode.FaceCamera;
        GUIStyle m_HintStyle;

        // True for as long as Ctrl stays held (see OnToolGUI/SetFixedDistanceHeld).
        static bool s_FixedDistanceHeld;

        // True while Fixed Distance was picked manually from the overlay's tabs — acts exactly
        // like holding Ctrl, but stays on until another tab is picked instead.
        static bool s_FixedDistanceForcedByOverlay;

        static StickyTextTool s_ActiveInstance;

        /// <summary>The mode actually in effect this frame: Fixed Distance while the clutch key
        /// is held (or forced from the overlay), overriding the base mode for as long as it
        /// stays down/forced — including mid-drag.</summary>
        PlacementMode EffectiveMode =>
            s_FixedDistanceHeld || s_FixedDistanceForcedByOverlay ? PlacementMode.FixedDistance : m_Mode;

        /// <summary>True only while Ctrl is physically held (ignores the overlay-forced
        /// override). Used by <see cref="StickyTextOverlay"/> to distinguish a momentary
        /// Ctrl preview from its own sticky tab selection, so the two never fight over what to
        /// display.</summary>
        internal bool IsFixedDistanceHeldByCtrl => s_FixedDistanceHeld;

        /// <summary>True while Fixed Distance is stickily forced on from the overlay's tabs (as
        /// opposed to just being previewed via a held Ctrl). Used by
        /// <see cref="StickyTextOverlay"/> to notice when that override was cleared
        /// elsewhere (toggle-mode key, or releasing Ctrl) and resync its tab selection.</summary>
        internal static bool IsFixedDistanceForcedByOverlay => s_FixedDistanceForcedByOverlay;

        /// <summary>Forces Fixed Distance on/off from the overlay's tabs, independent of Ctrl.</summary>
        internal static void SetFixedDistanceForcedByOverlay(bool forced)
        {
            if (s_FixedDistanceForcedByOverlay == forced)
                return;
            s_FixedDistanceForcedByOverlay = forced;
            if (TryGetActiveInstance(out var tool))
                tool.CancelDragOnModeChange();
            SceneView.RepaintAll();
        }

        /// <summary>Cancels the placement drag in progress, if any — switching mode (or the
        /// Fixed Distance override) mid-drag discards the current selection instead of
        /// reinterpreting it under the new mode, matching right-click/Escape.</summary>
        void CancelDragOnModeChange()
        {
            if (!m_IsDragging)
                return;
            m_IsDragging = false;
            m_HasProjection = false;
            m_InteractionOwner = null;
            GUIUtility.hotControl = 0;
        }

        public override void OnActivated() => s_ActiveInstance = this;

        public override void OnWillBeDeactivated()
        {
            if (s_ActiveInstance == this)
                s_ActiveInstance = null;
        }

        /// <summary>Used by the shortcut methods, which only have static context.</summary>
        internal static bool TryGetActiveInstance(out StickyTextTool tool)
        {
            tool = s_ActiveInstance;
            return tool != null;
        }

        internal void ToggleMode()
        {
            if (s_FixedDistanceForcedByOverlay)
            {
                // Exiting a sticky Fixed Distance selection just reveals the current base mode
                // — one action, one effect, instead of also toggling it in the same press.
                SetFixedDistanceForcedByOverlay(false);
                return;
            }
            SetBaseMode(m_Mode != PlacementMode.AlignToSurface);
        }

        /// <summary>True while the base mode (as opposed to the Ctrl-held Fixed Distance
        /// override) is Align to Surface. Used by <see cref="StickyTextOverlay"/>.</summary>
        internal bool BaseModeIsAlignToSurface => m_Mode == PlacementMode.AlignToSurface;

        /// <summary>Sets the base mode directly — used by <see cref="StickyTextOverlay"/>'s
        /// mode tabs, which don't have their own <see cref="PlacementMode"/> reference.</summary>
        internal void SetBaseMode(bool alignToSurface)
        {
            var newMode = alignToSurface ? PlacementMode.AlignToSurface : PlacementMode.FaceCamera;
            if (m_Mode == newMode)
                return;
            m_Mode = newMode;
            CancelDragOnModeChange();
            SceneView.RepaintAll();
        }

        /// <summary>Called from <see cref="OnToolGUI"/> whenever Ctrl is pressed/released.</summary>
        static void SetFixedDistanceHeld(bool held)
        {
            if (s_FixedDistanceHeld == held)
                return;
            s_FixedDistanceHeld = held;
            if (TryGetActiveInstance(out var tool))
            {
                if (!held)
                {
                    // Releasing Ctrl mid right-click-drag cancels the distance adjustment
                    // instead of leaving it stuck adjusting a now-hidden setting.
                    if (tool.m_AdjustingDistance)
                    {
                        tool.m_AdjustingDistance = false;
                        tool.m_InteractionOwner = null;
                        GUIUtility.hotControl = 0;
                        EditorGUIUtility.SetWantsMouseJumping(0);
                    }

                    // Releasing Ctrl also exits a sticky Fixed Distance selection made from the
                    // overlay — a quick tap of Ctrl doubles as a "get me out of here" gesture.
                    s_FixedDistanceForcedByOverlay = false;
                }
                tool.CancelDragOnModeChange();
            }
            SceneView.RepaintAll();
        }

        GUIContent m_ToolbarIcon;
        bool m_IsDragging;
        Vector2 m_DragStart;
        Vector2 m_DragCurrent;

        // Right-click drag, while Fixed Distance is active, adjusts fixedPlaneDistance.
        bool m_AdjustingDistance;

        // Which Scene view currently owns an in-progress drag/adjustment, if any. With multiple
        // Scene view windows open, OnToolGUI runs once per window each frame for this same
        // shared tool instance — without this, an idle second window's call could react to (and
        // stomp) state a different, actually-in-use window's call just set on the same frame
        // (e.g. reading Ctrl as released there immediately after the focused window read it as
        // held, cancelling an adjustment one frame after it started).
        SceneView m_InteractionOwner;

        // Projection is recomputed only on mouse events (it triggers scene picking, which
        // must never run during a Repaint) and cached here for the preview to draw.
        bool m_HasProjection;
        Vector3 m_ProjCenter;
        Quaternion m_ProjRotation;
        Vector2 m_ProjSize;
        Vector3[] m_ProjCorners;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (m_ToolbarIcon == null)
                {
                    var icon = EditorGUIUtility.IconContent("d_Text Icon").image
                               ?? EditorGUIUtility.IconContent("GameObject Icon").image;
                    m_ToolbarIcon = new GUIContent(
                        icon,
                        "StickyText — drag a rectangle in the Scene view to project a plane onto geometry.");
                }
                return m_ToolbarIcon;
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView) || sceneView.camera == null)
                return;

            // StickyText is a level-authoring tool, not meant to run during Play Mode: Unity's
            // static batching combines level geometry into shared meshes at play time, which
            // breaks our own mesh-vs-transform raycasting (HandleUtility.PickGameObject still
            // resolves the right GameObject, but the combined mesh no longer matches the
            // original transform, so ray/triangle intersections come out completely wrong).
            if (Application.isPlaying)
            {
                DrawPlayModeDisabledHint(sceneView);
                return;
            }

            // Only ever process events for the Scene view that owns the current drag/adjustment
            // (if any), or — when idle — the one that's actually focused. Every other open Scene
            // view's call this frame is ignored outright, instead of letting it read/react to
            // its own (irrelevant) focus and modifier-key state and stomp shared tool state.
            bool ownsInteraction = m_InteractionOwner == sceneView;
            bool otherWindowInteracting = m_InteractionOwner != null && !ownsInteraction;
            bool idleAndUnfocused = m_InteractionOwner == null && EditorWindow.focusedWindow != sceneView;
            if (otherWindowInteracting || idleAndUnfocused)
                return;

            var e = Event.current;

            // Fixed Distance is a temporary override of the base mode, active only while Ctrl is
            // held. Hardcoded for now — Unity's Shortcut Manager UI doesn't accept binding a
            // bare modifier key (no companion key) as a clutch shortcut, so this isn't currently
            // rebindable. Checked every event, so it also reacts mid-drag. Only tracked while
            // this Scene view actually has focus, so holding Ctrl for an unrelated reason
            // elsewhere (another window, another app) never engages it here; losing focus while
            // held releases it immediately instead of leaving it stuck on.
            //
            // Exception: while actively right-click-dragging to adjust the fixed distance,
            // don't re-derive this from a per-event snapshot of e.control/focus — the OS-level
            // cursor warp used for infinite mouse jumping (SetWantsMouseJumping) can make a
            // single MouseDrag event report a stale/false modifier state or a momentary focus
            // flicker, which would otherwise cancel the drag after only one frame. Once the
            // drag is running, only a real KeyUp of the physical Control key ends it.
            if (m_AdjustingDistance)
            {
                if (e.type == EventType.KeyUp &&
                    (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl))
                    SetFixedDistanceHeld(false);
            }
            else
            {
                bool sceneViewFocused = EditorWindow.focusedWindow == sceneView;
                bool ctrlEffective = sceneViewFocused && e.control;
                if (ctrlEffective != s_FixedDistanceHeld)
                    SetFixedDistanceHeld(ctrlEffective);
            }

            // A fresh click always means "start placing somewhere new": commit/close any
            // pending edit right away, on this same event, instead of letting it swallow the
            // mouse-down and fall through to Unity's default marquee-select. Typing itself is
            // handled globally by TextLabelInlineEditor (works no matter which tool is active),
            // so this tool only needs to hand off to it, never draw the field itself.
            if (e.type == EventType.MouseDown && e.button == 0 && TextLabelInlineEditor.IsEditing)
                TextLabelInlineEditor.EndEditing();

            if (TextLabelInlineEditor.IsEditing)
                return;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            // Keep the tool as the active control so the default scene selection /
            // marquee does not steal our drag.
            HandleUtility.AddDefaultControl(controlId);

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (e.button == 0)
                    {
                        // Clicking directly on an existing label selects it instead of starting
                        // a new placement drag; TextLabelInlineEditor picks up the selection
                        // change on the next event and starts typing into it, same as from the
                        // Hierarchy. (Any pending edit was already committed above.)
                        var hitLabel = PickTextLabel(e.mousePosition);
                        if (hitLabel != null)
                        {
                            Selection.activeGameObject = hitLabel.gameObject;
                            e.Use();
                            break;
                        }

                        m_IsDragging = true;
                        m_DragStart = m_DragCurrent = e.mousePosition;
                        m_HasProjection = false;
                        m_InteractionOwner = sceneView;
                        GUIUtility.hotControl = controlId;
                        e.Use();
                    }
                    else if (e.button == 1 && m_IsDragging)
                    {
                        // Right-click while placing cancels the current selection/drag in
                        // progress, same as pressing Escape.
                        m_IsDragging = false;
                        m_HasProjection = false;
                        m_InteractionOwner = null;
                        GUIUtility.hotControl = 0;
                        sceneView.Repaint();
                        e.Use();
                    }
                    // Gated on Ctrl specifically (not just Fixed Distance being in effect): when
                    // Fixed Distance is only sticky from the overlay, plain right-click-drag
                    // should still orbit the camera as normal. Holding Ctrl on top of that
                    // switches right-click-drag to adjusting the distance instead; releasing it
                    // hands right-click back to the camera without touching the sticky mode.
                    else if (e.button == 1 && s_FixedDistanceHeld)
                    {
                        // Right-click drag, while Ctrl is held, adjusts the plane distance
                        // instead of placing anything — left/right moves it closer/away. Mouse
                        // jumping lets the cursor wrap invisibly at the screen edges instead of
                        // clamping there, so the drag never runs out of room; the reported delta
                        // stays continuous either way.
                        m_AdjustingDistance = true;
                        m_InteractionOwner = sceneView;
                        GUIUtility.hotControl = controlId;
                        EditorGUIUtility.SetWantsMouseJumping(1);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (m_IsDragging)
                    {
                        m_DragCurrent = e.mousePosition;

                        // Face Camera/Fixed Distance only ever preview the flat 2D drag
                        // rectangle (see DrawPlacementPreview) — never the projected quad — so
                        // there's nothing for the (potentially expensive, depthSamples-driven)
                        // projection to actually improve here. Only Align to Surface's preview
                        // draws the real projected/tilted quad, so only it needs re-projecting on
                        // every drag event; the other two get it once, for real, on MouseUp
                        // (see below) right before placing.
                        if (EffectiveMode == PlacementMode.AlignToSurface)
                            // picking is safe here (not a Repaint); expensive fallback disabled
                            // (see UpdateProjection) since this runs every drag event.
                            UpdateProjection(sceneView, allowExpensiveFallback: false);
                        sceneView.Repaint();
                        e.Use();
                    }
                    else if (m_AdjustingDistance)
                    {
                        float newDistance =
                            Settings.fixedPlaneDistance + e.delta.x * Settings.fixedPlaneDistanceDragSensitivity;
                        Settings.fixedPlaneDistance = Mathf.Max(0.01f, newDistance);
                        EditorUtility.SetDirty(Settings);
                        sceneView.Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (m_IsDragging && e.button == 0)
                    {
                        m_IsDragging = false;
                        m_InteractionOwner = null;
                        GUIUtility.hotControl = 0;
                        UpdateProjection(sceneView);
                        TryPlace();
                        m_HasProjection = false;
                        sceneView.Repaint();
                        e.Use();
                    }
                    else if (m_AdjustingDistance && e.button == 1)
                    {
                        m_AdjustingDistance = false;
                        m_InteractionOwner = null;
                        GUIUtility.hotControl = 0;
                        EditorGUIUtility.SetWantsMouseJumping(0);
                        sceneView.Repaint();
                        e.Use();
                    }
                    break;

                case EventType.KeyDown:
                    // Mode toggling and the Fixed Distance clutch are handled by the
                    // "StickyText/Toggle Mode" and "StickyText/Fixed Distance (Hold)"
                    // shortcuts (see StickyTextShortcuts.cs) — rebindable via Edit > Shortcuts.
                    if (e.keyCode == KeyCode.Escape && m_IsDragging)
                    {
                        m_IsDragging = false;
                        m_HasProjection = false;
                        m_InteractionOwner = null;
                        GUIUtility.hotControl = 0;
                        sceneView.Repaint();
                        e.Use();
                    }
                    break;
            }

            if (m_IsDragging)
                DrawPreview(sceneView);

            // Shown for as long as the Fixed Distance clutch key is held (even without
            // dragging) so the user can see exactly where the plane sits before/while placing.
            if (EffectiveMode == PlacementMode.FixedDistance)
                DrawFixedPlaneGizmo(sceneView);

            DrawModeHint(sceneView);
        }

        // --- Projection ----------------------------------------------------------

        Rect CurrentGuiRect()
        {
            return Rect.MinMaxRect(
                Mathf.Min(m_DragStart.x, m_DragCurrent.x),
                Mathf.Min(m_DragStart.y, m_DragCurrent.y),
                Mathf.Max(m_DragStart.x, m_DragCurrent.x),
                Mathf.Max(m_DragStart.y, m_DragCurrent.y));
        }

        /// <summary>Recomputes the projection and caches it. Must only be called on mouse
        /// events (never during Repaint) because it triggers scene picking.
        /// <paramref name="allowExpensiveFallback"/> gates the depthSamples-driven grid sample
        /// used when Align to Surface's cheap single-point fit misses geometry — pass false for
        /// drag-time preview calls (see MouseDrag) so a drag that starts over empty space can't
        /// re-run the full grid every single event for the rest of the drag; only the final
        /// MouseUp call needs (and gets) the expensive, more-accurate fallback.</summary>
        void UpdateProjection(SceneView sceneView, bool allowExpensiveFallback = true)
        {
            m_HasProjection = ComputeProjection(sceneView, allowExpensiveFallback, out m_ProjCenter,
                out m_ProjRotation, out m_ProjSize, out m_ProjCorners);
        }

        /// <summary>
        /// Projects the current selection rectangle onto the scene. Returns false if the
        /// drag is too small. Outputs the world-space quad (centre, camera-facing rotation,
        /// size in world units, and the four world corners for previewing).
        /// </summary>
        bool ComputeProjection(SceneView sceneView, bool allowExpensiveFallback, out Vector3 center,
            out Quaternion rotation, out Vector2 size, out Vector3[] corners)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector2.zero;
            corners = null;

            var rect = CurrentGuiRect();
            if (rect.width < Settings.minDragPixels || rect.height < Settings.minDragPixels)
                return false;

            var cam = sceneView.camera;

            var mode = EffectiveMode;

            // Already-placed text labels are never treated as level geometry to project onto —
            // otherwise placing a new sign near/behind an existing one could snap to its
            // background plane instead of the actual wall.
            var ignore = GetPlacedLabelRenderers();

            // Surface-aligned mode: the quad lies on the surface hit at the start of the drag.
            // Falls through to camera-facing if the starting point missed geometry.
            if (mode == PlacementMode.AlignToSurface &&
                TryComputeSurfaceAligned(cam, ignore, out center, out rotation, out size, out corners))
                return true;

            // The starting point missed geometry — normally falls back to the grid-sampled
            // camera-facing depth below, but that's the expensive (depthSamples-driven) path;
            // skip it during a drag preview so a drag that starts over empty space doesn't pay
            // that cost on every single MouseDrag event, only once for real on MouseUp.
            if (mode == PlacementMode.AlignToSurface && !allowExpensiveFallback)
                return false;

            Vector3 planePoint;
            if (mode == PlacementMode.FixedDistance)
            {
                // No geometry sampling at all: always the same distance from the camera.
                planePoint = cam.transform.position + cam.transform.forward * Settings.fixedPlaneDistance;
            }
            else
            {
                // Sample a grid across the selection and keep the depth of the geometry that is
                // *closest* to the camera, so the plane stops at the first contact and never
                // pokes through a protrusion. Depth is measured along the camera's view axis.
                // If nothing is hit at all (pointing at open sky), the text floats at the same
                // fixed distance as Fixed Distance mode — regardless of which mode is active.
                float nearestDepth = SampleNearestDepth(rect, cam, ignore);
                planePoint = nearestDepth > 0f
                    ? cam.transform.position + cam.transform.forward * (nearestDepth - Settings.faceCameraOffset)
                    : cam.transform.position + cam.transform.forward * Settings.fixedPlaneDistance;
            }

            BuildCameraFacingQuad(cam, rect, planePoint, out center, out rotation, out size, out corners);
            return true;
        }

        /// <summary>
        /// Collects the renderer GameObjects (Text + Background children) of every placed
        /// <see cref="TextLabel"/> in the scene, to exclude from placement raycasts.
        /// </summary>
        static GameObject[] GetPlacedLabelRenderers()
        {
            var labels = Object.FindObjectsByType<TextLabel>(FindObjectsSortMode.None);
            if (labels.Length == 0)
                return null;

            var result = new List<GameObject>(labels.Length * 2);
            foreach (var label in labels)
                foreach (var r in label.GetComponentsInChildren<MeshRenderer>())
                    result.Add(r.gameObject);
            return result.ToArray();
        }

        /// <summary>
        /// Builds a quad on the plane through <paramref name="planePoint"/>, perpendicular to
        /// the camera's view direction. In camera space this is a constant-depth plane, so an
        /// axis-aligned screen rectangle projects onto it as an axis-aligned rectangle — a
        /// perfect match with the on-screen selection.
        /// </summary>
        static void BuildCameraFacingQuad(Camera cam, Rect rect, Vector3 planePoint,
            out Vector3 center, out Quaternion rotation, out Vector2 size, out Vector3[] corners)
        {
            var plane = new Plane(-cam.transform.forward, planePoint);

            // GUI y grows downward: yMin is the top of the screen, yMax the bottom.
            var bl = ProjectCorner(new Vector2(rect.xMin, rect.yMax), plane);
            var br = ProjectCorner(new Vector2(rect.xMax, rect.yMax), plane);
            var tr = ProjectCorner(new Vector2(rect.xMax, rect.yMin), plane);
            var tl = ProjectCorner(new Vector2(rect.xMin, rect.yMin), plane);

            corners = new[] { bl, br, tr, tl };
            center = (bl + br + tr + tl) * 0.25f;
            rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
            size = new Vector2(Vector3.Distance(bl, br), Vector3.Distance(bl, tl));
        }

        static readonly Vector3[] s_PrincipalAxes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        /// <summary>
        /// Chooses the quad's "up" direction within the surface plane. Starts from world up,
        /// and — on near-horizontal surfaces (floors/ceilings) only — optionally snaps it to
        /// the nearest world principal axis. The detection offset merely biases which axis is
        /// picked; it is never baked into the final orientation.
        /// </summary>
        static Vector3 ComputeSurfaceUp(Vector3 n, Camera cam)
        {
            bool nearHorizontal = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.99f;

            Vector3 upRef = nearHorizontal ? cam.transform.up : Vector3.up;
            Vector3 baseUp = Vector3.ProjectOnPlane(upRef, n);
            if (baseUp.sqrMagnitude < 1e-6f)
                baseUp = Vector3.ProjectOnPlane(cam.transform.up, n);
            baseUp.Normalize();

            if (!nearHorizontal || !Settings.alignFloorCeilingToWorldAxes)
                return baseUp;

            // Bias the reference used for detection (does not rotate the placement itself).
            Vector3 detectUp = Mathf.Abs(Settings.snapDetectionOffset) > 1e-4f
                ? (Quaternion.AngleAxis(Settings.snapDetectionOffset, n) * baseUp)
                : baseUp;

            Vector3 result = baseUp;
            float bestAngle = float.MaxValue;
            foreach (var axis in s_PrincipalAxes)
            {
                Vector3 proj = Vector3.ProjectOnPlane(axis, n);
                if (proj.sqrMagnitude < 1e-6f) // axis parallel to the normal
                    continue;
                proj.Normalize();

                float angle = Vector3.Angle(detectUp, proj);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    if (angle <= Settings.axisSnapMargin)
                        result = proj; // snap to the pure world axis, no offset baked in
                }
            }

            return result.normalized;
        }

        /// <summary>
        /// Builds a quad lying on the surface that was hit at the start of the drag. The two
        /// drag corners are projected onto that surface plane and interpreted as opposite
        /// corners of a rectangle expressed in the surface's tangent frame — so on screen it
        /// appears sheared by perspective, but on the surface it is a true rectangle.
        /// </summary>
        bool TryComputeSurfaceAligned(Camera cam, GameObject[] ignore, out Vector3 center,
            out Quaternion rotation, out Vector2 size, out Vector3[] corners)
        {
            center = Vector3.zero;
            rotation = Quaternion.identity;
            size = Vector2.zero;
            corners = null;

            if (!TryRaycastSurface(m_DragStart, out var p0, out var nHit, ignore))
                return false;

            // Derive the flat face normal from three real hit points (start + two neighbours),
            // so it reflects the true surface slope regardless of smoothed vertex normals or
            // the mesh's Read/Write flag. Falls back to the raw hit normal near face edges.
            const float px = 8f; // pixel spacing of the neighbour rays used to measure the normal
            Vector3 n = nHit;
            if (TryRaycastSurface(m_DragStart + new Vector2(px, 0f), out var pRight, out _, ignore) &&
                TryRaycastSurface(m_DragStart + new Vector2(0f, px), out var pDown, out _, ignore))
            {
                Vector3 cross = Vector3.Cross(pRight - p0, pDown - p0);
                if (cross.sqrMagnitude > 1e-10f)
                    n = cross;
            }

            if (n.sqrMagnitude < 1e-6f)
                return false;
            n.Normalize();
            if (Vector3.Dot(n, cam.transform.position - p0) < 0f) // orient toward the camera
                n = -n;

            // Tangent frame on the surface (front face -Z of the Quad points out along +n).
            Vector3 surfaceUp = ComputeSurfaceUp(n, cam);
            rotation = Quaternion.LookRotation(-n, surfaceUp);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            // Opposite corner: use the real geometry under the cursor rather than intersecting
            // the infinite surface plane. On a grazing ramp the plane intersection shoots far
            // away and stretches the quad wildly; a mesh hit stays bounded and sensible. Its
            // out-of-plane component is discarded by projecting onto the tangent axes.
            Vector3 pEnd;
            if (TryRaycastSurface(m_DragCurrent, out var endHit, out _, ignore))
            {
                pEnd = endHit;
            }
            else
            {
                var plane = new Plane(n, p0);
                var endRay = HandleUtility.GUIPointToWorldRay(m_DragCurrent);
                if (!plane.Raycast(endRay, out float enter))
                    return false;
                pEnd = endRay.GetPoint(enter);
            }

            float w = Vector3.Dot(pEnd - p0, right);
            float h = Vector3.Dot(pEnd - p0, up);

            // Push the whole quad slightly off the surface, along its normal, to avoid z-fighting.
            Vector3 origin = p0 + n * Settings.surfaceOffset;

            Vector3 c0 = origin;
            Vector3 c1 = origin + right * w;
            Vector3 c2 = origin + right * w + up * h;
            Vector3 c3 = origin + up * h;

            corners = new[] { c0, c1, c2, c3 };
            center = origin + (right * w + up * h) * 0.5f;
            size = new Vector2(Mathf.Abs(w), Mathf.Abs(h));
            return size.x > 1e-4f && size.y > 1e-4f;
        }

        // Editor-only intersection against actual mesh triangles (internal API).
        static readonly MethodInfo k_IntersectRayMesh = typeof(HandleUtility).GetMethod(
            "IntersectRayMesh",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(Ray), typeof(Mesh), typeof(Matrix4x4), typeof(RaycastHit).MakeByRefType() },
            null);

        /// <summary>
        /// Finds the surface under the given GUI point, via either mesh picking or a collider
        /// raycast depending on <see cref="StickyTextSettings.depthTestMethod"/>.
        /// <paramref name="ignore"/> only applies to the Mesh method — the Collider method never
        /// needs it, since placed labels' background quads have their collider destroyed on
        /// creation (see CreateBackground) and the text itself never has one.
        /// </summary>
        static bool TryRaycastSurface(Vector2 guiPoint, out Vector3 point, out Vector3 normal,
            GameObject[] ignore = null)
        {
            return Settings.depthTestMethod == DepthTestMethod.Collider
                ? TryRaycastCollider(guiPoint, out point, out normal)
                : TryRaycastMesh(guiPoint, out point, out normal, ignore);
        }

        /// <summary>Raycasts against level colliders — cheap (no rendering involved), but blind
        /// to any geometry that has no collider.</summary>
        static bool TryRaycastCollider(Vector2 guiPoint, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.zero;

            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (!Physics.Raycast(ray, out var hit))
                return false;

            point = hit.point;
            normal = hit.normal;
            return true;
        }

        /// <summary>
        /// Raycasts against the rendered mesh under the given GUI point — no colliders
        /// involved. Uses the scene picking system to isolate the object, then intersects
        /// its mesh triangles for a precise hit. The normal is the raw (possibly smoothed)
        /// hit normal; callers that need the flat face normal derive it from several hits.
        /// <paramref name="ignore"/> is excluded from picking — used to skip already-placed
        /// text labels so new placements never treat one as level geometry.
        /// </summary>
        static bool TryRaycastMesh(Vector2 guiPoint, out Vector3 point, out Vector3 normal,
            GameObject[] ignore = null)
        {
            point = Vector3.zero;
            normal = Vector3.zero;

            var go = HandleUtility.PickGameObject(guiPoint, false, ignore);
            if (go == null)
                return false;

            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            float best = float.MaxValue;
            bool found = false;

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;

                var args = new object[] { ray, mesh, mf.transform.localToWorldMatrix, null };
                if ((bool)k_IntersectRayMesh.Invoke(null, args))
                {
                    var hit = (RaycastHit)args[3];
                    if (hit.distance < best)
                    {
                        best = hit.distance;
                        point = hit.point;
                        normal = hit.normal;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Casts a grid of rays across the selection rectangle and returns the depth (along
        /// the camera's view axis) of the geometry closest to the camera, or -1 if nothing
        /// was hit anywhere in the rectangle.
        /// </summary>
        static float SampleNearestDepth(Rect rect, Camera cam, GameObject[] ignore)
        {
            Vector3 camPos = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            int res = Mathf.Max(1, Settings.depthSamples);
            float nearest = float.MaxValue;

            for (int iy = 0; iy < res; iy++)
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float u = res == 1 ? 0.5f : ix / (float)(res - 1);
                    float v = res == 1 ? 0.5f : iy / (float)(res - 1);
                    var gp = new Vector2(
                        Mathf.Lerp(rect.xMin, rect.xMax, u),
                        Mathf.Lerp(rect.yMin, rect.yMax, v));

                    if (TryRaycastSurface(gp, out var hit, out _, ignore))
                    {
                        float depth = Vector3.Dot(hit - camPos, forward);
                        if (depth > 0f && depth < nearest)
                            nearest = depth;
                    }
                }
            }

            return nearest < float.MaxValue ? nearest : -1f;
        }

        static Vector3 ProjectCorner(Vector2 guiPoint, Plane plane)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : ray.origin;
        }

        /// <summary>
        /// Finds the <see cref="TextLabel"/> under the cursor via an analytic ray-vs-rectangle
        /// test against each label's own transform and <see cref="TextLabel.Size"/> — not
        /// Unity's render-based <see cref="HandleUtility.PickGameObject"/>. The background
        /// plane's transparent material (ZWrite off, for correct alpha blending) can lose the
        /// depth test in Unity's picking pass against opaque geometry behind it, causing clicks
        /// on a clearly visible sign to select the wall instead; this sidesteps that entirely.
        /// </summary>
        static TextLabel PickTextLabel(Vector2 guiPoint)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            TextLabel best = null;
            float bestDistance = float.MaxValue;

            foreach (var label in Object.FindObjectsByType<TextLabel>(FindObjectsSortMode.None))
            {
                var t = label.transform;
                // The quad's visible front face normal (world space) is -t.forward, matching
                // the LookRotation conventions used everywhere else in this tool.
                var plane = new Plane(-t.forward, t.position);
                if (!plane.Raycast(ray, out float enter) || enter < 0f || enter >= bestDistance)
                    continue;

                Vector3 local = t.InverseTransformPoint(ray.GetPoint(enter));
                Vector2 size = label.Size;
                if (Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f)
                {
                    bestDistance = enter;
                    best = label;
                }
            }

            return best;
        }

        // --- Placement -----------------------------------------------------------

        void TryPlace()
        {
            if (!m_HasProjection)
                return;

            // Parent holds the wrapper + transform; the child holds the actual TMP text.
            var parent = new GameObject("StickyText");
            parent.transform.SetPositionAndRotation(m_ProjCenter, m_ProjRotation);
            if (Settings.defaultParent != null)
                parent.transform.SetParent(Settings.defaultParent, worldPositionStays: true);

            var child = new GameObject("Text", typeof(RectTransform), typeof(TextMeshPro));
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            // A dedicated instance rather than whatever TMP's own default font material happens
            // to be (see ResolveTextMaterial) - shared across every label, unlike the background
            // (which needs its own colour per instance), since TMP's own vertex colour already
            // handles per-label tinting.
            Material textMat = ResolveTextMaterial();
            if (textMat != null)
                child.GetComponent<TextMeshPro>().fontSharedMaterial = textMat;

            // The overlay's current tag (if any) supplies both colours, the same
            // fallback-to-Settings rule used everywhere a tag is resolved.
            var (backgroundColor, _) = StickyTextTagGUI.Resolve(Settings.currentTag);
            var currentTag = StickyTextTagRegistry.Instance.Find(Settings.currentTag);
            Color textColor = currentTag?.textColor ?? Settings.textColor;

            // The background plane is always shown now, so it always doubles as the zone
            // indicator — no separate corner gizmo needed.
            var backgroundRenderer = CreateBackground(parent.transform, m_ProjSize, backgroundColor);

            var label = parent.AddComponent<TextLabel>();
            label.Size = m_ProjSize;
            label.TextColorOverride = textColor;
            label.Margin = Settings.textMargin;
            label.MinFontSize = Settings.minFontSize;
            label.MaxFontSize = Settings.maxFontSize;
            label.ForceUppercase = Settings.forceUppercase;
            label.EditorOnly = Settings.currentEditorOnly;
            label.Tag = Settings.currentTag;
            label.SetBackground(backgroundRenderer, backgroundColor);
            label.Text = string.Empty; // start empty so the placeholder shows
            StickyTextLabelNaming.Apply(label);

            // Register the fully built hierarchy as one undo step.
            Undo.RegisterCreatedObjectUndo(parent, "Place Text");

            Selection.activeGameObject = parent;
            TextLabelInlineEditor.BeginEditing(label);
        }

        static MeshRenderer CreateBackground(Transform parent, Vector2 size, Color color)
        {
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            if (bg.TryGetComponent<Collider>(out var col))
                Object.DestroyImmediate(col);

            var t = bg.transform;
            t.SetParent(parent, false);
            t.localPosition = Vector3.zero; // on the placement plane; the text sits in front
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(size.x, size.y, 1f);

            // Instance the shared base material so each plane keeps its own colour — renderQueue,
            // Cull, and every other property (incl. sorting priority) carry over from the
            // resolved base material via this copy constructor, nothing to reapply here.
            var mat = new Material(ResolveBackgroundMaterial()) { name = "Text Background" };
            ApplyBackgroundColor(mat, color);

            var renderer = bg.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            return renderer;
        }

        // Shared by TextLabel.ApplyBackground (Runtime, can't reference this Editor-only method
        // directly, so keep the exact same property list in sync there) - _BaseColor (HDRP/Lit,
        // URP/Lit), _Color (Built-in/legacy), and _UnlitColor (HDRP/Unlit specifically - it does
        // NOT use _BaseColor despite being HDRP, which is why the auto-created HDRP background
        // material's colour never actually followed the label before this).
        internal static void ApplyBackgroundColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", color);
        }

        /// <summary>
        /// Returns the base background material: the one set in the settings, or a transparent
        /// Unlit material asset auto-created next to the settings file — HDRP/Unlit if HDRP is
        /// the active pipeline, Universal Render Pipeline/Unlit (or Sprites/Default as a last
        /// resort under the Built-in pipeline) otherwise. Detected by type name rather than a
        /// direct reference so this package never has to hard-depend on the HDRP package.
        /// </summary>
        static Material ResolveBackgroundMaterial()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            bool isHdrp = pipeline != null && pipeline.GetType().Name.Contains("HDRenderPipelineAsset");
            string suffix = isHdrp ? "HDRP" : "URP";

            Material mat = Settings.backgroundMaterial;
            bool isNew = mat == null;
            string path = null;

            if (isNew)
            {
                // Next to the settings asset (as its backgroundMaterial tooltip promises),
                // wherever the user keeps that — falling back to the default folder if its path
                // can't be resolved for some reason.
                var settingsPath = AssetDatabase.GetAssetPath(Settings);
                string folder;
                if (string.IsNullOrEmpty(settingsPath))
                {
                    StickyTextSettings.EnsureDefaultFolder();
                    folder = StickyTextSettings.k_DefaultFolder;
                }
                else
                {
                    folder = settingsPath.Substring(0, settingsPath.LastIndexOf('/'));
                }
                path = $"{folder}/TextBackground_{suffix}.mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                isNew = mat == null;
                if (isNew)
                {
                    var shader = isHdrp
                        ? Shader.Find("HDRP/Unlit")
                        : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                    mat = new Material(shader) { name = $"TextBackground_{suffix}" };
                }
            }

            // Re-applied every time (cheap, only saved if something actually changed) — so an
            // asset generated by an older version of this tool, or one already assigned in
            // Settings from before these checks existed, still gets brought in line with what
            // this tool currently expects (ZWrite off so overlapping signs blend instead of
            // depth-testing each other away, Cull for a background that doesn't disappear viewed
            // from behind, and the sorting priority that keeps it behind the text).
            bool zWriteOk = !mat.HasProperty("_ZWrite") || mat.GetFloat("_ZWrite") == 0f;
            bool cullOk = (!mat.HasProperty("_Cull") || mat.GetFloat("_Cull") == (float)CullMode.Off)
                && (!mat.HasProperty("_CullMode") || mat.GetFloat("_CullMode") == (float)CullMode.Off)
                // HDRP's own material validation (runs whenever the Inspector's ShaderGUI touches
                // this asset) recomputes _CullMode from _DoubleSidedEnable and silently resets it
                // back to single-sided if this is still 0 - so a material whose _CullMode merely
                // *currently* reads Off isn't actually stable unless this is set too.
                && (!mat.HasProperty("_DoubleSidedEnable") || mat.GetFloat("_DoubleSidedEnable") == 1f);
            bool queueOk = mat.renderQueue == (int)RenderQueue.Transparent + k_BackgroundSortingPriority;
            if (isNew || !zWriteOk || !cullOk || !queueOk)
            {
                if (isHdrp) ConfigureTransparentHdrp(mat);
                else ConfigureTransparentUrp(mat);
                if (isNew && path != null) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            Settings.backgroundMaterial = mat;
            EditorUtility.SetDirty(Settings);
            return mat;
        }

        /// <summary>
        /// Returns the material every placed label's TMP uses: the one set in Settings, or a
        /// dedicated instance auto-created next to the settings file the first time this runs,
        /// copied from whatever font asset TMP currently defaults to (TMP Settings' Default Font
        /// Asset) — a StickyText-owned copy, so retuning it later never affects any other TMP
        /// text in the project sharing that same default font material. Returns null (TMP falls
        /// back to its own default) if TMP has no default font asset configured at all.
        /// </summary>
        static Material ResolveTextMaterial()
        {
            Material mat = Settings.textMaterial;
            bool isNew = mat == null;
            string path = null;

            if (isNew)
            {
                var defaultFont = TMP_Settings.defaultFontAsset;
                if (defaultFont == null || defaultFont.material == null)
                    return null;

                var settingsPath = AssetDatabase.GetAssetPath(Settings);
                string folder;
                if (string.IsNullOrEmpty(settingsPath))
                {
                    StickyTextSettings.EnsureDefaultFolder();
                    folder = StickyTextSettings.k_DefaultFolder;
                }
                else
                {
                    folder = settingsPath.Substring(0, settingsPath.LastIndexOf('/'));
                }

                path = $"{folder}/TextMaterial.mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                isNew = mat == null;
                if (isNew)
                {
                    mat = new Material(defaultFont.material) { name = "StickyText Material" };
                    // Off by default - an outline copied from whatever the project's default font
                    // material happens to have configured would show on every placed label, not
                    // just ones that actually want one.
                    if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
                    mat.DisableKeyword("OUTLINE_ON");
                }
            }

            // Re-applied every time (cheap, only saved if actually different) - one queue above
            // every background (k_BackgroundSortingPriority + 1), so a label's own text always
            // draws after its own background deterministically instead of tying on distance.
            // Trade-off: distance-based back-to-front sorting only happens WITHIN a single render
            // queue, never across two different ones - so a nearer label's translucent background
            // won't correctly darken a farther label's text the way two backgrounds already blend
            // with each other.
            int wantedQueue = (int)RenderQueue.Transparent + k_BackgroundSortingPriority + 1;
            if (isNew || mat.renderQueue != wantedQueue)
            {
                mat.renderQueue = wantedQueue;
                if (isNew && path != null) AssetDatabase.CreateAsset(mat, path);
                else EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            Settings.textMaterial = mat;
            EditorUtility.SetDirty(Settings);
            return mat;
        }

        // Sorting priority applied to every generated background material, both pipelines.
        const int k_BackgroundSortingPriority = -1;

        static void ConfigureTransparentUrp(Material mat)
        {
            // Standard URP Unlit transparent (alpha-blended) surface setup. ZWrite is OFF, the
            // normal choice for alpha-blended materials - keeping it ON (an earlier version of
            // this did, so the Fixed Distance gizmo's Handles.zTest could occlude/be occluded by
            // existing signs, see DrawFixedPlaneGizmo) let two overlapping sign backgrounds write
            // conflicting depth and fail each other's depth test outright instead of blending -
            // one would just vanish behind the other rather than showing through it. Level
            // geometry (walls, etc.) is opaque and already writes its own depth regardless, so the
            // gizmo's occlusion against the actual level is unaffected - only sign-vs-preview-
            // gizmo occlusion is given up here, in exchange for signs correctly overlapping.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            // Render Face: Both — a background plane shouldn't disappear when viewed from behind.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            // "Priority" in the Inspector's Advanced Options — URP's ShaderGUI recomputes
            // renderQueue from this on every validation pass, so setting renderQueue alone
            // (below) wouldn't stick.
            if (mat.HasProperty("_QueueOffset")) mat.SetFloat("_QueueOffset", k_BackgroundSortingPriority);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + k_BackgroundSortingPriority;
        }

        // Best-effort HDRP/Unlit setup — property names match current (Unity 6) HDRP shaders but
        // are internal and not guaranteed stable across versions; every write is HasProperty-
        // guarded so a renamed/missing property is silently skipped rather than breaking.
        static void ConfigureTransparentHdrp(Material mat)
        {
            if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f); // Transparent
            if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f); // Alpha
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            // ZWrite OFF - see ConfigureTransparentUrp's comment: keeping it on let two
            // overlapping sign backgrounds fail each other's depth test and vanish instead of
            // blending, for the sake of an edge case (signs occluding the Fixed Distance preview
            // gizmo) far less common than signs actually overlapping each other.
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            // Render Face: Both. HDRP treats _CullMode/_CullModeForward as DERIVED from
            // _DoubleSidedEnable (its material validation - triggered whenever the Inspector's
            // ShaderGUI runs - recomputes both from this and the _DOUBLESIDED_ON keyword), so
            // setting the cull properties alone without this got silently reset back to
            // single-sided the next time that validation ran.
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
            mat.EnableKeyword("_DOUBLESIDED_ON");
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", (float)CullMode.Off);
            if (mat.HasProperty("_CullModeForward")) mat.SetFloat("_CullModeForward", (float)CullMode.Off);
            // "Sorting Priority" in the Inspector.
            if (mat.HasProperty("_TransparentSortPriority"))
                mat.SetFloat("_TransparentSortPriority", k_BackgroundSortingPriority);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + k_BackgroundSortingPriority;
        }

        // --- Preview -------------------------------------------------------------

        void DrawPreview(SceneView sceneView)
        {
            // In Face Camera and Fixed Distance modes the quad projects back exactly onto the
            // screen selection, so a thin screen-space rectangle is the cleanest, faithful
            // preview (no bulky 3D outline overlapping it).
            var mode = EffectiveMode;
            if (mode == PlacementMode.FaceCamera || mode == PlacementMode.FixedDistance)
            {
                DrawFlatRectPreview();
                return;
            }

            // Surface mode: draw the tilted world quad (the raw screen rectangle is omitted
            // so it doesn't clutter the surface-aligned preview) whenever the cheap single-point
            // fit has one. While it doesn't — e.g. the drag started over empty space, and the
            // expensive grid fallback is deliberately skipped mid-drag (see ComputeProjection) —
            // fall back to the same flat rectangle as Face Camera/Fixed Distance instead of
            // showing nothing at all; the accurate tilted quad reappears the moment the fit
            // succeeds again (including on release, which always gets the accurate fallback).
            if (m_HasProjection && m_ProjCorners != null)
            {
                Handles.DrawSolidRectangleWithOutline(
                    m_ProjCorners,
                    Settings.surfaceFill,
                    Settings.surfaceOutline);
            }
            else
            {
                DrawFlatRectPreview();
            }
        }

        void DrawFlatRectPreview()
        {
            Handles.BeginGUI();
            var rect = CurrentGuiRect();
            var border = Settings.faceCameraOutline;
            EditorGUI.DrawRect(rect, Settings.faceCameraFill);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), border);
            Handles.EndGUI();
        }

        /// <summary>Human-readable current binding for a shortcut id (e.g. "M", "Alt"), so the
        /// hint always reflects whatever the user rebound it to via Edit ▸ Shortcuts.</summary>
        static string ShortcutLabel(string shortcutId)
        {
            string s = ShortcutManager.instance.GetShortcutBinding(shortcutId).ToString();
            return string.IsNullOrEmpty(s) ? "unbound" : s;
        }

        void DrawPlayModeDisabledHint(SceneView sceneView)
        {
            if (m_HintStyle == null)
            {
                m_HintStyle = new GUIStyle(EditorStyles.miniLabel);
                m_HintStyle.normal.textColor = Color.white;
                m_HintStyle.padding = new RectOffset(6, 6, 2, 2);
            }

            var content = new GUIContent("StickyText is disabled in Play Mode.");

            Handles.BeginGUI();
            var size = m_HintStyle.CalcSize(content);
            var r = new Rect(8f, sceneView.position.height - 46f, size.x, size.y);
            EditorGUI.DrawRect(r, new Color(0.4f, 0.1f, 0.1f, 0.7f));
            GUI.Label(r, content, m_HintStyle);
            Handles.EndGUI();
        }

        void DrawModeHint(SceneView sceneView)
        {
            if (m_HintStyle == null)
            {
                m_HintStyle = new GUIStyle(EditorStyles.miniLabel);
                m_HintStyle.normal.textColor = Color.white;
                m_HintStyle.padding = new RectOffset(6, 6, 2, 2);
            }

            string modeName = EffectiveMode switch
            {
                PlacementMode.FaceCamera => "Face Camera",
                PlacementMode.AlignToSurface => "Align to Surface",
                _ => $"Fixed Distance ({Settings.fixedPlaneDistance:0.##}m)",
            };
            string hint =
                $"Placement: {modeName}   —   [{ShortcutLabel("StickyText/Toggle Mode")}] toggle mode" +
                "   —   [Ctrl] hold for Fixed Distance"; // hardcoded, not a rebindable shortcut
            if (EffectiveMode == PlacementMode.FixedDistance)
                hint += "   —   [Right-click drag] adjust distance";
            var content = new GUIContent(hint);

            Handles.BeginGUI();
            var size = m_HintStyle.CalcSize(content);
            var r = new Rect(8f, sceneView.position.height - 46f, size.x, size.y);
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(r, content, m_HintStyle);
            Handles.EndGUI();
        }

        /// <summary>
        /// Draws the Fixed Distance plane as a bordered, gridded rectangle spanning the full
        /// Scene view frustum at that distance — shown continuously (not just while dragging)
        /// so the user can see exactly where text will land before starting a drag.
        /// </summary>
        void DrawFixedPlaneGizmo(SceneView sceneView)
        {
            var cam = sceneView.camera;
            if (cam == null)
                return;

            float d = Settings.fixedPlaneDistance;
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, d));
            Vector3 br = cam.ViewportToWorldPoint(new Vector3(1f, 0f, d));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, d));
            Vector3 tl = cam.ViewportToWorldPoint(new Vector3(0f, 1f, d));

            // Depth-test against the scene so the plane is occluded by nearer geometry (and
            // hides parts of itself embedded inside walls) instead of drawing flat on top of
            // everything — gives an actual sense of where the plane sits in the level.
            var prevZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;

            Handles.DrawSolidRectangleWithOutline(
                new[] { bl, br, tr, tl }, Settings.fixedPlaneFill, Settings.fixedPlaneGrid);

            // Fixed world-space cell size, anchored on the centre — not a fixed division count
            // uniformly spread edge-to-edge. That way the grid visibly gets denser as the plane
            // moves farther away (a real sense of distance/scale), while lines sit at fixed
            // cellSize increments from the middle: growing/shrinking the view only adds or
            // removes lines at the edges, instead of the whole grid reflowing/shifting.
            const int maxStepsPerSide = 100; // safety cap for very large distances/small cells
            float cellSize = Mathf.Max(0.01f, Settings.fixedPlaneGridCellSize);
            float width = Vector3.Distance(bl, br);
            float height = Vector3.Distance(bl, tl);

            Color prev = Handles.color;
            Handles.color = Settings.fixedPlaneGrid;
            DrawCenteredGridLines(bl, br, tl, tr, width, cellSize, maxStepsPerSide);  // vertical
            DrawCenteredGridLines(bl, tl, br, tr, height, cellSize, maxStepsPerSide); // horizontal
            Handles.color = prev;

            Handles.zTest = prevZTest;
        }

        /// <summary>
        /// Draws grid lines along one axis of a rectangle, at fixed <paramref name="cellSize"/>
        /// world-space increments outward from the centre (plus a centre line). Each line runs
        /// from a point on the <paramref name="aStart"/>-<paramref name="aEnd"/> edge to the
        /// corresponding point on the <paramref name="bStart"/>-<paramref name="bEnd"/> edge.
        /// </summary>
        static void DrawCenteredGridLines(Vector3 aStart, Vector3 aEnd, Vector3 bStart, Vector3 bEnd,
            float axisLength, float cellSize, int maxStepsPerSide)
        {
            if (axisLength <= 0f || cellSize <= 0f)
                return;

            float step = cellSize / axisLength;
            Handles.DrawLine(Vector3.Lerp(aStart, aEnd, 0.5f), Vector3.Lerp(bStart, bEnd, 0.5f));

            int steps = Mathf.Clamp(Mathf.FloorToInt(0.5f / step), 0, maxStepsPerSide);
            for (int i = 1; i <= steps; i++)
            {
                float offset = i * step;
                Handles.DrawLine(Vector3.Lerp(aStart, aEnd, 0.5f + offset), Vector3.Lerp(bStart, bEnd, 0.5f + offset));
                Handles.DrawLine(Vector3.Lerp(aStart, aEnd, 0.5f - offset), Vector3.Lerp(bStart, bEnd, 0.5f - offset));
            }
        }
    }
}
