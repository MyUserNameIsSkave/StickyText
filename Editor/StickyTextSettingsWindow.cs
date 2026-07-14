using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Dedicated window (Tools ▸ StickyText). Two top-level pages: Management (currently just a
    /// live list of every placed label's text; sorting/filtering/focusing comes later) and
    /// Settings (the intended way to edit
    /// <see cref="StickyTextSettings"/> from now on, replacing its old tabbed Inspector).
    /// The Scene view Overlay is unaffected: it keeps its own quick-access copy of a subset of
    /// the Settings page for use while actively placing text.
    /// </summary>
    public class StickyTextSettingsWindow : EditorWindow
    {
        enum Page { Management, Settings }

        readonly TabbedGUI m_Gui = new();
        SerializedObject m_SerializedObject;
        Vector2 m_Scroll;
        GUIStyle m_TitleStyle;
        GUIStyle m_PageTabStyle;
        Page m_Page = Page.Settings; // Management has no content yet — don't land on an empty page

        [MenuItem("Tools/StickyText")]
        public static void Open()
        {
            var window = GetWindow<StickyTextSettingsWindow>();
            // Same icon as the StickyText tool's own Scene view toolbar button.
            var icon = EditorGUIUtility.IconContent("d_Text Icon").image
                       ?? EditorGUIUtility.IconContent("GameObject Icon").image;
            window.titleContent = new GUIContent("StickyText", icon);
            // Narrower than this and long field labels (e.g. "Align Floor Ceiling To World
            // Axes") start squeezing their control/value off to the side or truncating.
            window.minSize = new Vector2(340f, 240f);
        }

        void OnEnable()
        {
            Refresh();
            // Keeps the Management list's row highlight in sync when the selection changes from
            // elsewhere (Hierarchy, Scene view) — Repaint isn't automatic on selection change.
            Selection.selectionChanged += Repaint;
        }

        void OnDisable() => Selection.selectionChanged -= Repaint;

        void Refresh()
        {
            var settings = StickyTextSettings.Instance;
            m_SerializedObject = new SerializedObject(settings);
            m_Gui.Rebuild(m_SerializedObject, settings.GetType());
        }

        void OnGUI()
        {
            // The settings asset can be deleted/recreated from under the window (e.g. via
            // source control); re-resolve it rather than throwing on a stale reference.
            if (m_SerializedObject == null || m_SerializedObject.targetObject == null)
                Refresh();

            m_TitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
            };

            // Same text formatting as the fold box headers (TabbedGUI.FoldoutTitleStyle) —
            // bold, slightly larger — so the page tabs read as the same kind of heading.
            // GUI.skin.button's own left/right margin would otherwise add on top of the
            // GUILayout.Space(8) margin below, pushing the toolbar a few px past it on each side.
            m_PageTabStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, GUI.skin.button.margin.top, GUI.skin.button.margin.bottom),
            };

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("StickyText", m_TitleStyle);
            EditorGUILayout.Space(10);

            // Same outer side margin as the fold boxes/note below, so the tabs line up with them.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            // ExpandWidth forces it to stretch to exactly fill the space between the two
            // Space(8) calls — without it, Toolbar sizes itself to its buttons' natural width
            // and left-aligns within the group, reading as "shifted left" against the box below.
            m_Page = (Page)GUILayout.Toolbar((int)m_Page, new[] { "Management", "Settings" }, m_PageTabStyle,
                GUILayout.ExpandWidth(true));
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);

            switch (m_Page)
            {
                case Page.Management: DrawManagementPage(); break;
                case Page.Settings: DrawSettingsPage(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        const float k_MinColumnWidth = 30f;
        const float k_MaxColumnWidth = 300f;

        // Label list column widths — user-resizable by dragging the header dividers.
        const float k_LabelRemoveWidth = 20f;
        float m_LabelTagColumnWidth = 70f;
        float m_LabelEditorOnlyColumnWidth = 100f; // wide enough for "Editor Only ▼"

        // Same purpose as m_TagTrailingSpace — the last divider (before Remove) trades against
        // this instead of growing Editor Only unchecked.
        float m_LabelTrailingSpace = 8f;
        const float k_LabelTrailingSpaceMin = 4f;

        // Tag manager column widths (distinct from the label list's columns above). Also
        // user-resizable by dragging the header dividers.
        const float k_TagRemoveWidth = 20f;
        float m_TagTextColorColumnWidth = 60f;
        float m_TagColorColumnWidth = 60f;
        float m_TagEditorOnlyColumnWidth = 70f;

        // Slack between Editor Only and the row's right edge, not a real column — the last
        // divider trades against this instead of directly growing Editor Only, so growing it
        // never eats into Name's leftover-space calculation (which would shift Name/Color left).
        // Now that nameWidth (see LayOutTagColumns) is solved from the row's real width, draining
        // this to 0 can't push Remove past the row's actual right edge — the floor below is
        // just a small cosmetic gap, not a safety margin.
        float m_TagTrailingSpace = 8f;
        const float k_TagTrailingSpaceMin = 4f;

        // Cached across repaints (not rebuilt every OnGUI call like the label list, which never
        // needs dragging) — a drag gesture spans several repaints, and ReorderableList tracks
        // which index is being dragged against a specific list/instance. Recreating either one
        // mid-drag loses that state, which is why the handle showed but didn't actually drag.
        ReorderableList m_TagList;
        readonly List<StickyTextTag> m_DisplayTags = new();

        // DEFAULT's row Y-range from the last repaint — a MouseDown anywhere in that band (any
        // X, not just over specific controls) is swallowed before ReorderableList's own
        // hit-testing this frame ever sees it, so it can't be selected or start dragging (and
        // therefore never shows the drag preview) in the first place. X is deliberately ignored:
        // disabled controls (GUI.enabled = false) on that row don't consume clicks themselves,
        // so a click landing on one of them would otherwise fall through to the row's own
        // selection/drag hit-test regardless of which control it visually landed on.
        float m_DefaultRowY, m_DefaultRowHeight;

        // Real row x/width, captured from DrawDefaultTagRow each repaint (it always runs,
        // regardless of how many real tags exist) — draggable:true reserves a drag-handle gutter
        // on the left of each row, so the actual content rect is narrower/shifted right
        // compared to GUILayoutUtility.GetLastRect()'s overall list rect (which spans the full
        // box width, gutter included). Using the latter for the continuous dividers put them at
        // the wrong x, overlapping the content instead of sitting in the gaps between columns.
        float m_TagRowX, m_TagRowWidth;

        // Same purpose as m_TagRowX/m_TagRowWidth above, for the label list — captured from its
        // header (rather than an element row, which might not exist if the list is empty) since
        // it always runs.
        float m_LabelRowX, m_LabelRowWidth;

        // Arbitrary fixed IDs for the draggable dividers — this window only ever has these
        // drag interactions, so simple constants are enough (no GetControlID bookkeeping).
        const int k_LabelDivider1DragId = 379001; // Content ↔ Tag
        const int k_LabelDivider2DragId = 379002; // Tag ↔ Editor Only
        const int k_LabelDivider3DragId = 379007; // Editor Only ↔ trailing space
        const int k_TagDivider1DragId = 379003; // Name ↔ Text Color
        const int k_TagDivider2DragId = 379004; // Text Color ↔ Background
        const int k_TagDivider3DragId = 379005; // Background ↔ Editor Only
        const int k_TagDivider4DragId = 379006; // Editor Only ↔ trailing space

        enum SortColumn { None, Text, Tag, EditorOnly }

        string m_SearchFilter = "";
        SortColumn m_SortColumn;
        bool m_SortDescending;
        bool m_TagsFoldout = true;
        bool m_ListFoldout = true;

        void DrawManagementPage()
        {
            // Same outer side margin as the fold boxes below, wrapping the note too so its
            // edges line up with them instead of sitting flush against the window.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            EditorGUILayout.HelpBox(
                "Browse, search, and tag every StickyText label placed in the open scene(s).",
                MessageType.Info);
            EditorGUILayout.Space(6);

            DrawTagManager();
            DrawLabelListBox();

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }

        void DrawLabelListBox()
        {
            // One box contains both the foldout header and its content (when expanded) — same
            // recipe as the Settings page's fold boxes (see TabbedGUI.DrawAsFoldouts): Space(2)
            // after the header, indented content, Space(4) before closing the box, Space(6)
            // after it, every time, whether expanded or not.
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            m_ListFoldout = EditorGUILayout.Foldout(m_ListFoldout, "Labels", true, TabbedGUI.FoldoutTitleStyle);
            EditorGUILayout.Space(2);
            if (m_ListFoldout)
            {
                EditorGUI.indentLevel++;

                // A little breathing room between the list and the fold box's own edges — the
                // ReorderableList otherwise stretches flush to the box's right edge (indent only
                // affects the left side).
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                EditorGUILayout.BeginVertical();

                IEnumerable<TextLabel> labels = UnityEngine.Object.FindObjectsByType<TextLabel>(FindObjectsSortMode.None)
                    .Where(l => l != null);

                if (!string.IsNullOrEmpty(m_SearchFilter))
                    labels = labels.Where(l => (l.Text ?? "").IndexOf(m_SearchFilter, StringComparison.OrdinalIgnoreCase) >= 0);

                labels = m_SortColumn switch
                {
                    SortColumn.Text => labels.OrderBy(l => l.Text, StringComparer.OrdinalIgnoreCase),
                    SortColumn.Tag => labels.OrderBy(l => l.Tag, StringComparer.OrdinalIgnoreCase),
                    SortColumn.EditorOnly => labels.OrderBy(l => l.EditorOnly),
                    _ => labels,
                };
                var list = labels.ToList();
                if (m_SortDescending)
                    list.Reverse();

                // Matches the Tags list's own gap between its foldout title and its column header.
                EditorGUILayout.Space(8);

                // Width-matched to the Content column using the same formula LayOutColumns uses,
                // but from position.width (a stable field) instead of a value captured from the
                // list's own rendering — that earlier approach caused repeated sizing bugs (a
                // too-small/mispositioned field, then a field that collapsed to icon-only while
                // being typed into — a Layout/Repaint desync from depending on a value written
                // elsewhere, later, in the same OnGUI pass). This won't be quite as pixel-perfect
                // (position.width only approximates the row's real usable width — it doesn't know
                // about indentation or a visible scrollbar) but it's stable regardless of focus.
                float approxTextColumnWidth = Mathf.Max(k_MinColumnWidth, position.width - m_LabelTagColumnWidth
                    - m_LabelEditorOnlyColumnWidth - k_LabelRemoveWidth - m_LabelTrailingSpace
                    - k_LabelFixedOverhead - 40f);
                m_SearchFilter = EditorGUILayout.TextField(m_SearchFilter, EditorStyles.toolbarSearchField,
                    GUILayout.Width(approxTextColumnWidth));
                EditorGUILayout.Space(2);

                // A plain UnityEditorInternal.ReorderableList (dragging/add/remove all disabled
                // — there's nothing meaningful to reorder or add here) instead of hand-rolled
                // rows: its built-in dark boxed background, row banding, and selection
                // highlight are exactly the native "serialized array" look, and it handles
                // row-click-to-select and Toggle-inside-a-row interaction correctly without
                // extra plumbing.
                var reorderable = new ReorderableList(list, typeof(TextLabel), false, true, false, false)
                {
                    headerHeight = EditorGUIUtility.singleLineHeight, // was +4f — that extra height showed as a gap between "Content" and the first row
                    elementHeight = EditorGUIUtility.singleLineHeight, // matches the Tags list exactly — the ~2px gap comes from the list's own inherent row padding, not from any extra height added here
                    footerHeight = 0f, // no add/remove buttons here — default footer height would just reserve dead space below the last row
                    drawHeaderCallback = DrawLabelListHeader,
                    drawElementCallback = (rect, index, _, _) => DrawLabelListElement(rect, list[index]),
                    onSelectCallback = l => Selection.activeGameObject = ((TextLabel)l.list[l.index]).gameObject,
                    index = list.FindIndex(l => l.gameObject == Selection.activeGameObject),
                };
                reorderable.DoLayoutList();

                // Each row only drew its own short divider segment, so the lines looked broken
                // at every row boundary instead of one continuous line down the whole list — this
                // draws one full-height line per divider on top, spanning header to last row.
                DrawContinuousDividers(GUILayoutUtility.GetLastRect());

                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        static readonly GUIContent s_RemoveTagIcon = new("✕", "Remove tag"); // ✕ — a Windows-close-button-style cross, bolder and clearer at this size than a plain "x"
        static readonly GUIContent s_DeleteLabelIcon = new("✕", "Delete label");
        static GUIStyle s_RemoveTagButtonStyle;

        static GUIStyle RemoveTagButtonStyle =>
            s_RemoveTagButtonStyle ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };

        static GUIStyle s_BorderlessTextFieldStyle;

        // EditorStyles.textField draws a lighter border/background around a hovered or focused
        // field — reads as an unwanted "highlight outline" on these always-editable row fields
        // (Content/Name), which are meant to look like plain row text until clicked into. Using
        // the normal-state background for every visual state removes that border entirely.
        static GUIStyle BorderlessTextFieldStyle
        {
            get
            {
                if (s_BorderlessTextFieldStyle == null)
                {
                    s_BorderlessTextFieldStyle = new GUIStyle(EditorStyles.textField);
                    var normalBackground = s_BorderlessTextFieldStyle.normal.background;
                    s_BorderlessTextFieldStyle.hover.background = normalBackground;
                    s_BorderlessTextFieldStyle.active.background = normalBackground;
                    s_BorderlessTextFieldStyle.focused.background = normalBackground;
                    s_BorderlessTextFieldStyle.onNormal.background = normalBackground;
                    s_BorderlessTextFieldStyle.onHover.background = normalBackground;
                    s_BorderlessTextFieldStyle.onActive.background = normalBackground;
                    s_BorderlessTextFieldStyle.onFocused.background = normalBackground;
                }
                return s_BorderlessTextFieldStyle;
            }
        }

        // A fixed-width rect at the left edge of the column, rather than handing EditorGUI.Toggle
        // the whole (wider) column rect — same visual result by default (Toggle without a label
        // draws left-aligned regardless), but pinning it explicitly keeps this independent of the
        // column's own width if that ever changes.
        const float k_CheckboxWidth = 18f;

        static Rect LeftAlignedCheckboxRect(Rect column) =>
            new(column.x, column.y, k_CheckboxWidth, column.height);

        // A single Rect sliced manually into columns for the tag manager, shared by its header,
        // the DEFAULT row, every real tag row, and the new-tag row — using the same rect for
        // all of them (via the same ReorderableList, DEFAULT included as a non-real first
        // element) guarantees they all line up pixel-for-pixel, the same way the label list's
        // header/rows share LayOutColumns.
        struct TagColumns
        {
            public Rect Name, Divider1, TextColor, Divider2, Background, Divider3, EditorOnly, Divider4, Remove;
        }

        // Sum of every fixed gap and 1px divider line between Name and Remove (see the x
        // accumulation below): 4+1+4 + 4+1+4 + 4+1+4 + 4+1+4. Used to solve nameWidth from the
        // row's real width so the row's content can never extend past its actual right edge.
        const float k_TagFixedOverhead = 36f;

        // Was 6f to widen the drag-handle gutter and match the Labels list's left-side offset —
        // now 0 since DrawTagManager's own outer margin does that instead (and also shifts the
        // handle icon along with it, which this alone couldn't). Kept (rather than removed
        // outright) since nameWidth still needs to subtract whatever this is to avoid overflowing
        // row.xMax, if it's ever raised above 0 again.
        const float k_TagContentOffset = 0f;

        // Divider lines render this many pixels further right than their raw column-boundary
        // math would place them — a compensating offset for a sub-pixel misalignment against
        // the ReorderableList's own row/header chrome. Only the divider lines (and their drag
        // hit rects) are shifted; column content positions aren't. Headers pass 0 (they're
        // already aligned); the new-tag input row needs a bigger offset than the list rows,
        // since GetControlRect positions it slightly differently.
        TagColumns LayOutTagColumns(Rect row, float dividerOffset = 0f)
        {
            // Solved from the row's own real width (not the window's) so Name always takes
            // exactly what's left after every other column, the dividers, and the trailing
            // slack — the row's content can never run past row.xMax no matter how the other
            // columns are dragged. Using the window's width instead (an earlier version did)
            // only approximates the row's actual usable width — it doesn't know about
            // indentation or a visible scrollbar — so it could silently under-reserve space and
            // let Remove get dragged out past the box's real edge.
            float nameWidth = Mathf.Max(k_MinColumnWidth, row.width - m_TagTextColorColumnWidth
                - m_TagColorColumnWidth - m_TagEditorOnlyColumnWidth - k_TagRemoveWidth
                - m_TagTrailingSpace - k_TagFixedOverhead - k_TagContentOffset);

            // Fields are drawn at a fixed, thinner height and vertically centred in the row's
            // (taller) slot, instead of stretching to fill it — that's what actually makes them
            // look thinner, with visible breathing room above/below, rather than just shrinking
            // the row itself.
            float fieldHeight = EditorGUIUtility.singleLineHeight;
            float fieldY = row.y + (row.height - fieldHeight) * 0.5f;

            float x = row.x + k_TagContentOffset;
            var c = new TagColumns();
            c.Name = new Rect(x, fieldY, nameWidth, fieldHeight);
            x += nameWidth + 4f;
            c.Divider1 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.TextColor = new Rect(x, fieldY, m_TagTextColorColumnWidth, fieldHeight);
            x += m_TagTextColorColumnWidth + 4f;
            c.Divider2 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.Background = new Rect(x, fieldY, m_TagColorColumnWidth, fieldHeight);
            x += m_TagColorColumnWidth + 4f;
            c.Divider3 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.EditorOnly = new Rect(x, fieldY, m_TagEditorOnlyColumnWidth, fieldHeight);
            x += m_TagEditorOnlyColumnWidth + 4f;
            c.Divider4 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.Remove = new Rect(x, fieldY, k_TagRemoveWidth, fieldHeight);
            return c;
        }

        // Drawn once on top of the whole list (header through the last tag row, stopping short
        // of the footer — the "New Tag" button spans all columns, so lines through it would just
        // cut across it), instead of each row drawing its own short segment — those broke
        // visually at every row boundary (the list's own inherent ~2px inter-row padding) rather
        // than reading as one continuous line.
        void DrawContinuousTagDividers(Rect listRect)
        {
            if (Event.current.type != EventType.Repaint || m_TagRowWidth <= 0f)
                return;
            float height = listRect.height - m_TagList.footerHeight;
            var c = LayOutTagColumns(new Rect(m_TagRowX, listRect.y, m_TagRowWidth, height), 1f);
            var color = new Color(1f, 1f, 1f, 0.15f);
            EditorGUI.DrawRect(new Rect(c.Divider1.x - 1f, listRect.y, 1f, height), color);
            EditorGUI.DrawRect(new Rect(c.Divider2.x - 1f, listRect.y, 1f, height), color);
            EditorGUI.DrawRect(new Rect(c.Divider3.x - 1f, listRect.y, 1f, height), color);
            EditorGUI.DrawRect(new Rect(c.Divider4.x - 1f, listRect.y, 1f, height), color);
        }

        /// <summary>Divider1-3 each trade width with their immediate neighbour (Name↔Text
        /// Color, Text Color↔Background, Background↔Editor Only) — predictable, spreadsheet-
        /// style resizing. Divider4 (before Remove) trades with the trailing slack
        /// (<see cref="m_TagTrailingSpace"/>) instead of growing Editor Only unchecked — Editor
        /// Only feeds into Name's leftover-space formula, so growing it without taking the width
        /// from somewhere else shrinks Name and shifts every column after it left, which read as
        /// "dragging this pushes everything else left" — the same bug an earlier Name-trading
        /// version of this divider had. Only wired up from the header row.</summary>
        void DrawTagColumnResizeHandles(TagColumns c)
        {
            DrawResizeHandle(c.Divider1, k_TagDivider1DragId, delta =>
            {
                m_TagTextColorColumnWidth = Mathf.Clamp(m_TagTextColorColumnWidth - delta, k_MinColumnWidth, k_MaxColumnWidth);
            });
            DrawResizeHandle(c.Divider2, k_TagDivider2DragId, delta =>
            {
                float clamped = Mathf.Clamp(delta,
                    -(m_TagTextColorColumnWidth - k_MinColumnWidth),
                    m_TagColorColumnWidth - k_MinColumnWidth);
                m_TagTextColorColumnWidth += clamped;
                m_TagColorColumnWidth -= clamped;
            });
            DrawResizeHandle(c.Divider3, k_TagDivider3DragId, delta =>
            {
                float clamped = Mathf.Clamp(delta,
                    -(m_TagColorColumnWidth - k_MinColumnWidth),
                    m_TagEditorOnlyColumnWidth - k_MinColumnWidth);
                m_TagColorColumnWidth += clamped;
                m_TagEditorOnlyColumnWidth -= clamped;
            });
            DrawResizeHandle(c.Divider4, k_TagDivider4DragId, delta =>
            {
                float clamped = Mathf.Clamp(delta,
                    -(m_TagEditorOnlyColumnWidth - k_MinColumnWidth),
                    m_TagTrailingSpace - k_TagTrailingSpaceMin);
                m_TagEditorOnlyColumnWidth += clamped;
                m_TagTrailingSpace -= clamped;
            });
        }

        static void DrawResizeHandle(Rect dividerRect, int dragId, Action<float> applyDeltaX)
        {
            // The visible divider line is only 1px wide — much too thin to reliably grab —so
            // the actual hit area (and resize cursor) extends a few pixels either side of it.
            var hitRect = new Rect(dividerRect.x - 3f, dividerRect.y, dividerRect.width + 6f, dividerRect.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when hitRect.Contains(e.mousePosition):
                    GUIUtility.hotControl = dragId;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == dragId:
                    applyDeltaX(e.delta.x);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == dragId:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        void DrawTagManager()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            m_TagsFoldout = EditorGUILayout.Foldout(m_TagsFoldout, "Tags", true, TabbedGUI.FoldoutTitleStyle);

            // Top/bottom padding applies whether expanded or not — matches TabbedGUI.DrawAsFoldouts.
            // Matches the Labels list's own gap between its search field and its column header.
            EditorGUILayout.Space(8);
            if (m_TagsFoldout)
            {
                EditorGUI.indentLevel++;

                // A little breathing room between the list and the fold box's own edges — the
                // ReorderableList otherwise stretches flush to the box's right edge (indent only
                // affects the left side). The extra +6px (vs. the Labels list's plain 4px) also
                // shifts the drag handle icon along with it — unlike k_TagContentOffset below,
                // which only affects where our own content starts, this affects the row rect
                // ReorderableList itself computes, which is what the handle icon is positioned
                // relative to.
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical();

                // Refreshed in place every repaint (not replaced) — a null placeholder in front
                // stands for the always-present, non-removable "DEFAULT" row. Keeping it inside
                // the same ReorderableList as the real tags guarantees pixel-identical column
                // alignment between the header, DEFAULT, and every tag row, and keeps it visually
                // part of the same boxed list rather than a separate strip above it.
                m_DisplayTags.Clear();
                m_DisplayTags.Add(null);
                m_DisplayTags.AddRange(StickyTextTagRegistry.Instance.tags);

                // Swallows a MouseDown across DEFAULT's whole row band (see field comment) before
                // ReorderableList's own hit-testing this frame ever sees it.
                if (Event.current.type == EventType.MouseDown && m_DefaultRowHeight > 0f)
                {
                    float y = Event.current.mousePosition.y;
                    if (y >= m_DefaultRowY && y <= m_DefaultRowY + m_DefaultRowHeight)
                        Event.current.Use();
                }

                // The list/ReorderableList instances themselves are cached (see field comments)
                // rather than rebuilt here — dragging a row spans several repaints, and
                // recreating either one mid-drag loses ReorderableList's internal drag state.
                // registry is re-fetched inside each callback (not captured here) so the
                // callbacks never go stale if the asset is ever deleted/recreated.
                //
                // The add button lives in the list's own footer (drawFooterCallback) rather than
                // a separately laid-out button below it — no custom spacing to keep in sync with
                // the list's own box padding — but drawn ourselves (not the native
                // displayAddButton) since that one renders as a small icon-only button, too
                // small to hit comfortably.
                m_TagList ??= new ReorderableList(m_DisplayTags, typeof(StickyTextTag), true, true, false, false)
                {
                    headerHeight = EditorGUIUtility.singleLineHeight, // was +4f — that extra height showed as a gap between the header and DEFAULT
                    elementHeight = EditorGUIUtility.singleLineHeight,
                    footerHeight = EditorGUIUtility.singleLineHeight + 9f, // 3px gap above the button + 6px for the button itself
                    drawHeaderCallback = DrawTagListHeader,
                    drawElementCallback = (rect, index, _, _) =>
                    {
                        if (index == 0)
                            DrawDefaultTagRow(rect);
                        else
                            DrawTagRow(rect, StickyTextTagRegistry.Instance, index - 1);
                    },
                    // Belt-and-braces alongside the MouseDown block above: suppresses the visual
                    // selection highlight for DEFAULT in case a click ever slips through anyway.
                    drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index == 0 && !isActive)
                            return;
                        ReorderableList.defaultBehaviours.DrawElementBackground(rect, index, isActive, isFocused, true);
                    },
                    // Second line of defence, not the primary mechanism (the MouseDown block
                    // above should stop a drag from ever starting on DEFAULT) — ignores any
                    // reorder that would move it or drop something onto its slot.
                    onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                    {
                        if (oldIndex == 0 || newIndex == 0)
                            return;

                        var registry = StickyTextTagRegistry.Instance;
                        var movedTag = (StickyTextTag)list.list[newIndex];
                        Undo.RecordObject(registry, "Reorder Tags");
                        registry.tags.Remove(movedTag);
                        registry.tags.Insert(newIndex - 1, movedTag);
                        EditorUtility.SetDirty(registry);
                    },
                    drawFooterCallback = rect =>
                    {
                        rect.yMin += 3f; // gap above the button
                        if (GUI.Button(rect, "New Tag"))
                        {
                            var registry = StickyTextTagRegistry.Instance;
                            var settings = StickyTextSettings.Instance;
                            Undo.RecordObject(registry, "Add Tag");
                            // Starts from DEFAULT's current colours rather than plain white, so a
                            // new tag looks like a deliberate variation instead of a blank slate.
                            registry.tags.Add(new StickyTextTag
                            {
                                textColor = settings.textColor,
                                color = settings.backgroundColor,
                            });
                            EditorUtility.SetDirty(registry);
                        }
                    },
                };
                m_TagList.DoLayoutList();

                // Each row only drew its own short divider segment, so the lines looked broken at
                // every row boundary instead of one continuous line down the whole list — this
                // draws one full-height line per divider on top, spanning header to the footer.
                DrawContinuousTagDividers(GUILayoutUtility.GetLastRect());

                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        void DrawTagListHeader(Rect rect)
        {
            var c = LayOutTagColumns(rect);
            GUI.Label(c.Name, "Name", EditorStyles.boldLabel);
            GUI.Label(c.TextColor, "Text", EditorStyles.boldLabel);
            GUI.Label(c.Background, "Background", EditorStyles.boldLabel);
            GUI.Label(c.EditorOnly, "Editor Only", EditorStyles.boldLabel);
            DrawTagColumnResizeHandles(c); // only draggable from the header row
        }

        // Not a real entry in the registry — its Name and Remove button are locked (it's not a
        // renameable or removable row), but Text/Background/Editor Only edit the live Settings
        // values directly (the same fields the Overlay and Settings page expose) and propagate
        // to every untagged label exactly like DrawTagRow does for a real tag.
        void DrawDefaultTagRow(Rect rect)
        {
            // Cached for next frame's MouseDown block (see DrawTagManager).
            m_DefaultRowY = rect.y;
            m_DefaultRowHeight = rect.height;

            // Cached for the continuous dividers (see m_TagRowX's field comment).
            m_TagRowX = rect.x;
            m_TagRowWidth = rect.width;

            var c = LayOutTagColumns(rect, 1f);

            GUI.enabled = false;
            EditorGUI.TextField(c.Name, "DEFAULT", BorderlessTextFieldStyle);
            GUI.enabled = true;

            var settings = StickyTextSettings.Instance;
            EditorGUI.BeginChangeCheck();
            Color newTextColor = EditorGUI.ColorField(c.TextColor, settings.textColor);
            Color newColor = EditorGUI.ColorField(c.Background, settings.backgroundColor);
            bool newEditorOnly = EditorGUI.Toggle(LeftAlignedCheckboxRect(c.EditorOnly), settings.editorOnly);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Edit Default Tag");

                // Same propagation as DrawTagRow for a real tag — untagged labels are the ones
                // using DEFAULT, matched by the settings' value as it was BEFORE this edit.
                var untaggedLabels = UnityEngine.Object.FindObjectsByType<TextLabel>(FindObjectsSortMode.None)
                    .Where(l => l != null && string.IsNullOrEmpty(l.Tag)).ToList();

                if (newEditorOnly != settings.editorOnly)
                    foreach (var label in untaggedLabels)
                        if (label.EditorOnly == settings.editorOnly)
                        {
                            Undo.RecordObject(label, "Change Editor Only");
                            label.EditorOnly = newEditorOnly;
                        }

                if (newTextColor != settings.textColor)
                    foreach (var label in untaggedLabels)
                        if (label.TextColorOverride == settings.textColor)
                        {
                            Undo.RecordObject(label, "Change Text Color");
                            label.TextColorOverride = newTextColor;
                        }
                if (newColor != settings.backgroundColor)
                    foreach (var label in untaggedLabels)
                        if (label.BackgroundColorOverride == settings.backgroundColor)
                        {
                            Undo.RecordObject(label, "Change Background Color");
                            label.BackgroundColorOverride = newColor;
                        }

                settings.textColor = newTextColor;
                settings.backgroundColor = newColor;
                settings.editorOnly = newEditorOnly;
                EditorUtility.SetDirty(settings);
            }

            GUI.enabled = false;
            GUI.Button(c.Remove, s_RemoveTagIcon, RemoveTagButtonStyle); // DEFAULT can't be removed — shown greyed out, not hidden, so the column still reads as a real row
            GUI.enabled = true;

            DrawTagRowOverlays(rect, c.Name, settings.backgroundColor);
        }

        void DrawTagRow(Rect rect, StickyTextTagRegistry registry, int tagIndex)
        {
            var tag = registry.tags[tagIndex];
            var c = LayOutTagColumns(rect, 1f);

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUI.TextField(c.Name, tag.name, BorderlessTextFieldStyle);
            Color newTextColor = EditorGUI.ColorField(c.TextColor, tag.textColor);
            Color newColor = EditorGUI.ColorField(c.Background, tag.color);
            bool newEditorOnly = EditorGUI.Toggle(LeftAlignedCheckboxRect(c.EditorOnly), tag.editorOnly);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(registry, "Edit Tag");

                // Match by the tag's name/value as they were BEFORE this edit — captured once,
                // since every check below needs it.
                var taggedLabels = UnityEngine.Object.FindObjectsByType<TextLabel>(FindObjectsSortMode.None)
                    .Where(l => l != null && l.Tag == tag.name).ToList();

                // Existing labels reference this tag by name — renaming it here would orphan
                // them, so update their stored name to match instead of just relabeling it.
                if (newName != tag.name)
                    foreach (var label in taggedLabels)
                    {
                        Undo.RecordObject(label, "Rename Tag");
                        label.Tag = newName;
                    }

                // Only follow the tag's new value for labels currently matching its OLD value —
                // a label whose Editor Only was already manually overridden away from the tag's
                // value is left alone, not silently reverted.
                if (newEditorOnly != tag.editorOnly)
                    foreach (var label in taggedLabels)
                        if (label.EditorOnly == tag.editorOnly)
                        {
                            Undo.RecordObject(label, "Change Editor Only");
                            label.EditorOnly = newEditorOnly;
                        }

                // Same override-preserving match as Editor Only above — a label whose colour was
                // manually overridden away from the tag's is left alone, not silently reverted.
                if (newTextColor != tag.textColor)
                    foreach (var label in taggedLabels)
                        if (label.TextColorOverride == tag.textColor)
                        {
                            Undo.RecordObject(label, "Change Text Color");
                            label.TextColorOverride = newTextColor;
                        }
                if (newColor != tag.color)
                    foreach (var label in taggedLabels)
                        if (label.BackgroundColorOverride == tag.color)
                        {
                            Undo.RecordObject(label, "Change Background Color");
                            label.BackgroundColorOverride = newColor;
                        }

                tag.name = newName;
                tag.textColor = newTextColor;
                tag.color = newColor;
                tag.editorOnly = newEditorOnly;
                EditorUtility.SetDirty(registry);
            }

            if (GUI.Button(c.Remove, s_RemoveTagIcon, RemoveTagButtonStyle))
            {
                Undo.RecordObject(registry, "Remove Tag");
                registry.tags.RemoveAt(tagIndex);
                EditorUtility.SetDirty(registry);
                GUIUtility.ExitGUI(); // list just mutated mid-layout; stop this pass cleanly
            }

            DrawTagRowOverlays(rect, c.Name, tag.color);
        }

        // Same overlay pair as the label list's rows: a hint of the row's own background colour
        // behind its Name field, and a whole-row hover highlight — both drawn on top of
        // everything else in the row (the fields' own opaque backgrounds would otherwise hide a
        // tint drawn underneath them).
        static void DrawTagRowOverlays(Rect rowRect, Rect nameRect, Color tint)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            EditorGUI.DrawRect(nameRect, new Color(tint.r, tint.g, tint.b, 0.08f));
            if (rowRect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));
        }

        static void DrawTagCell(Rect rect, TextLabel label)
        {
            StickyTextTagGUI.DrawPicker(rect, label.Tag, tag =>
            {
                Undo.RecordObject(label, "Set Tag");
                label.Tag = tag?.name ?? "";
                // Assigning a tag adopts its colours and Editor Only immediately, not just on
                // the next time the tag itself is edited (see DrawTagRow's propagation).
                label.TextColorOverride = tag?.textColor ?? StickyTextSettings.Instance.textColor;
                label.BackgroundColorOverride = tag?.color ?? StickyTextSettings.Instance.backgroundColor;
                label.EditorOnly = tag?.editorOnly ?? StickyTextSettings.Instance.editorOnly;
            });
        }

        // A single Rect sliced manually into columns, shared by the label list's header and
        // each element.
        struct Columns
        {
            public Rect Text, Divider1, Tag, Divider2, EditorOnly, Divider3, Remove;
        }

        // Sum of every fixed gap and 1px divider line between Content and Remove (see the x
        // accumulation below): 4+1+4 + 4+1+4 + 4+1+4. Used to solve the Content column's width
        // from the row's real width, the same reasoning as LayOutTagColumns.
        const float k_LabelFixedOverhead = 27f;

        // Divider lines render this many pixels further right than their raw column-boundary
        // math would place them — a compensating offset for a sub-pixel misalignment against
        // the ReorderableList's own row/header chrome. Only the divider lines (and their drag
        // hit rects) are shifted; column content positions aren't. Headers pass 0 (they're
        // already aligned).
        Columns LayOutColumns(Rect row, float dividerOffset = 0f)
        {
            // Solved from the row's own real width — see LayOutTagColumns for why this replaced
            // an earlier version that approximated it from the window's width instead.
            float textColumnWidth = Mathf.Max(k_MinColumnWidth, row.width - m_LabelTagColumnWidth
                - m_LabelEditorOnlyColumnWidth - k_LabelRemoveWidth - m_LabelTrailingSpace - k_LabelFixedOverhead);

            // Fields are drawn at a fixed, thinner height and vertically centred in the row's
            // (taller) slot, instead of stretching to fill it — same recipe as
            // LayOutTagColumns, so both lists get the same breathing room around their rows.
            // Dividers still span the row's full height.
            float fieldHeight = EditorGUIUtility.singleLineHeight;
            float fieldY = row.y + (row.height - fieldHeight) * 0.5f;

            float x = row.x;
            var c = new Columns();
            c.Text = new Rect(x, fieldY, textColumnWidth, fieldHeight);
            x += textColumnWidth + 4f;
            c.Divider1 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.Tag = new Rect(x, fieldY, m_LabelTagColumnWidth, fieldHeight);
            x += m_LabelTagColumnWidth + 4f;
            c.Divider2 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.EditorOnly = new Rect(x, fieldY, m_LabelEditorOnlyColumnWidth, fieldHeight);
            x += m_LabelEditorOnlyColumnWidth + 4f;
            c.Divider3 = new Rect(x + dividerOffset, row.y, 1f, row.height);
            x += 1f + 4f;
            c.Remove = new Rect(x, fieldY, k_LabelRemoveWidth, fieldHeight);
            return c;
        }

        // Drawn once on top of the whole list (header through last row), instead of each row
        // drawing its own short segment — those broke visually at every row boundary (the list's
        // own inherent ~2px inter-row padding) rather than reading as one continuous line.
        void DrawContinuousDividers(Rect listRect)
        {
            if (Event.current.type != EventType.Repaint || m_LabelRowWidth <= 0f)
                return;
            var c = LayOutColumns(new Rect(m_LabelRowX, listRect.y, m_LabelRowWidth, listRect.height), 1f);
            var color = new Color(1f, 1f, 1f, 0.15f);
            EditorGUI.DrawRect(new Rect(c.Divider1.x - 1f, listRect.y, 1f, listRect.height), color);
            EditorGUI.DrawRect(new Rect(c.Divider2.x - 1f, listRect.y, 1f, listRect.height), color);
            EditorGUI.DrawRect(new Rect(c.Divider3.x - 1f, listRect.y, 1f, listRect.height), color);
        }

        /// <summary>Same neighbour-only resizing as the tag manager's columns — see
        /// DrawTagColumnResizeHandles for why trading with Content instead (for a wider range)
        /// was dropped: it shrinks Content, which shifts every column after it. Divider3 (before
        /// Remove) trades with the trailing slack instead of growing Editor Only unchecked, for
        /// the same reason as the tag manager's last divider.</summary>
        void DrawLabelColumnResizeHandles(Columns c)
        {
            DrawResizeHandle(c.Divider1, k_LabelDivider1DragId, delta =>
            {
                m_LabelTagColumnWidth = Mathf.Clamp(m_LabelTagColumnWidth - delta, k_MinColumnWidth, k_MaxColumnWidth);
            });
            DrawResizeHandle(c.Divider2, k_LabelDivider2DragId, delta =>
            {
                float clamped = Mathf.Clamp(delta,
                    -(m_LabelTagColumnWidth - k_MinColumnWidth),
                    m_LabelEditorOnlyColumnWidth - k_MinColumnWidth);
                m_LabelTagColumnWidth += clamped;
                m_LabelEditorOnlyColumnWidth -= clamped;
            });
            DrawResizeHandle(c.Divider3, k_LabelDivider3DragId, delta =>
            {
                float clamped = Mathf.Clamp(delta,
                    -(m_LabelEditorOnlyColumnWidth - k_MinColumnWidth),
                    m_LabelTrailingSpace - k_LabelTrailingSpaceMin);
                m_LabelEditorOnlyColumnWidth += clamped;
                m_LabelTrailingSpace -= clamped;
            });
        }

        void DrawLabelListHeader(Rect rect)
        {
            // Fallback for the continuous dividers when the list is empty (no element row to
            // capture the more accurate rect from — see DrawLabelListElement).
            m_LabelRowX = rect.x;
            m_LabelRowWidth = rect.width;

            var c = LayOutColumns(rect);
            DrawSortableHeader(c.Text, "Content", SortColumn.Text);
            DrawSortableHeader(c.Tag, "Tag", SortColumn.Tag);
            DrawSortableHeader(c.EditorOnly, "Editor Only", SortColumn.EditorOnly);
            DrawLabelColumnResizeHandles(c); // only draggable from the header row
        }

        void DrawSortableHeader(Rect rect, string label, SortColumn column)
        {
            string arrow = m_SortColumn == column ? (m_SortDescending ? " ▼" : " ▲") : "";
            if (GUI.Button(rect, label + arrow, EditorStyles.label))
            {
                if (m_SortColumn == column)
                    m_SortDescending = !m_SortDescending;
                else
                {
                    m_SortColumn = column;
                    m_SortDescending = false;
                }
            }
        }

        const float k_SelectButtonWidth = 20f;

        void DrawLabelListElement(Rect rect, TextLabel label)
        {
            // Cached for the continuous dividers (see m_LabelRowX's field comment) — captured
            // from an element row rather than the header, since the header's rect turned out to
            // be 1px narrower/offset from the actual element rows' (ReorderableList insets
            // elements slightly for their own row-banding/selection highlight).
            m_LabelRowX = rect.x;
            m_LabelRowWidth = rect.width;

            var c = LayOutColumns(rect, 1f);

            // Same always-editable-field approach as the tag manager's Name column. The text
            // field consumes clicks across the whole column (the row can no longer be selected
            // just by clicking its text), so a dedicated select button sits to its left instead.
            var selectRect = new Rect(c.Text.x, c.Text.y, k_SelectButtonWidth, c.Text.height);
            var textRect = new Rect(c.Text.x + k_SelectButtonWidth + 2f, c.Text.y,
                c.Text.width - k_SelectButtonWidth - 2f, c.Text.height);

            if (GUI.Button(selectRect, "◉"))
            {
                Selection.activeGameObject = label.gameObject;

                // Face the label head-on (matching the angle it was originally placed at)
                // instead of just framing its bounds from whatever angle the Scene camera
                // currently happens to be at — labels can end up at any orientation (surface-
                // aligned, fixed-distance, etc.). The text was placed pushed toward -Z of this
                // rotation (see StickyTextTool.TryPlace), so matching the rotation exactly
                // reproduces the original placement viewpoint.
                if (SceneView.lastActiveSceneView != null)
                {
                    float size = Mathf.Max(0.5f, label.Size.magnitude * 0.5f);
                    SceneView.lastActiveSceneView.LookAt(label.transform.position, label.transform.rotation, size);
                }
            }

            EditorGUI.BeginChangeCheck();
            string newText = EditorGUI.TextField(textRect, label.Text, BorderlessTextFieldStyle);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(label, "Edit Label Text");
                label.Text = newText;
                StickyTextLabelNaming.Apply(label);
            }

            DrawTagCell(c.Tag, label);

            EditorGUI.BeginChangeCheck();
            bool editorOnly = EditorGUI.Toggle(LeftAlignedCheckboxRect(c.EditorOnly), label.EditorOnly);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(label, "Change Editor Only");
                label.EditorOnly = editorOnly;
            }

            if (GUI.Button(c.Remove, s_DeleteLabelIcon, RemoveTagButtonStyle))
            {
                Undo.DestroyObjectImmediate(label.gameObject);
                GUIUtility.ExitGUI(); // the underlying object just got destroyed mid-layout; stop this pass cleanly
            }

            // Both drawn as overlays, on top of everything above — the fields' own opaque
            // backgrounds would otherwise hide a tint drawn underneath them.
            if (Event.current.type == EventType.Repaint)
            {
                // A hint of the label's tag colour behind the Content field only (not the select
                // button to its left), same colour as the Tag cell's own tint but faint enough
                // not to fight with the field's text.
                var (tint, _) = StickyTextTagGUI.Resolve(label.Tag);
                EditorGUI.DrawRect(textRect, new Color(tint.r, tint.g, tint.b, 0.08f));

                // Whole-row hover highlight.
                if (rect.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.025f));
            }
        }

        void DrawSettingsPage()
        {
            // Same outer side margin as the fold boxes below, wrapping the note too so its
            // edges line up with them instead of sitting flush against the window.
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            EditorGUILayout.HelpBox(
                "Configure how StickyText behaves and looks. Changes apply immediately.",
                MessageType.Info);
            EditorGUILayout.Space(6);

            // Wide enough for the longest field label in this asset ("Fixed Plane Distance Drag
            // Sensitivity") to display in full instead of being clipped mid-word — there's
            // plenty of window width to spare for it.
            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 230f;

            EditorGUI.BeginChangeCheck();
            m_Gui.DrawAsFoldouts(m_SerializedObject);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(m_SerializedObject.targetObject);

            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }
    }
}
