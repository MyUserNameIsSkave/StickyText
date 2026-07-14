using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Renders fields marked with <see cref="TabAttribute"/> as a tab bar instead of one long
    /// scrolling list. Fields before the first <see cref="TabAttribute"/> are drawn normally,
    /// above the tabs. Used by any <see cref="EditorWindow"/> that wants the same layout
    /// (e.g. <see cref="StickyTextSettingsWindow"/>).
    /// </summary>
    public class TabbedGUI
    {
        class Tab
        {
            public string Name;
            public string Tooltip;
            public readonly List<string> PropertyPaths = new();
        }

        readonly List<Tab> m_Tabs = new();
        readonly List<string> m_Untabbed = new();
        readonly List<bool> m_FoldoutStates = new();
        readonly Dictionary<string, ShowIfAttribute> m_ShowIf = new();
        int m_SelectedTab;

        public void Rebuild(SerializedObject serializedObject, Type targetType)
        {
            m_Tabs.Clear();
            m_Untabbed.Clear();
            m_ShowIf.Clear();
            Tab current = null;

            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script")
                    continue;

                var field = targetType.GetField(iterator.name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var tabAttribute = field?.GetCustomAttribute<TabAttribute>();
                if (tabAttribute != null)
                {
                    current = new Tab { Name = tabAttribute.TabName, Tooltip = tabAttribute.Tooltip };
                    m_Tabs.Add(current);
                }

                var showIf = field?.GetCustomAttribute<ShowIfAttribute>();
                if (showIf != null)
                    m_ShowIf[iterator.propertyPath] = showIf;

                (current?.PropertyPaths ?? m_Untabbed).Add(iterator.propertyPath);
            }
        }

        /// <summary>False if this property's <see cref="ShowIfAttribute"/> condition (if any)
        /// doesn't currently hold — the caller should skip drawing it.</summary>
        bool ShouldShow(string path, SerializedObject serializedObject)
        {
            if (!m_ShowIf.TryGetValue(path, out var showIf))
                return true;
            var condition = serializedObject.FindProperty(showIf.ConditionField);
            return condition != null && condition.enumValueIndex == showIf.ExpectedValue;
        }

        public void Draw(SerializedObject serializedObject, float availableWidth)
        {
            serializedObject.Update();

            foreach (var path in m_Untabbed)
                if (ShouldShow(path, serializedObject))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(path), true);

            if (m_Tabs.Count > 0)
            {
                if (m_Untabbed.Count > 0)
                    EditorGUILayout.Space(4);

                m_SelectedTab = Mathf.Clamp(m_SelectedTab, 0, m_Tabs.Count - 1);

                // Same segmented-pill look as GUILayout.Toolbar (miniButtonLeft/Mid/Right), but
                // each button's width is proportional to its own label instead of all being
                // equal — the whole bar is then stretched to fill the available width.
                var styles = new GUIStyle[m_Tabs.Count];
                var naturalWidths = new float[m_Tabs.Count];
                float totalNaturalWidth = 0f;
                for (int i = 0; i < m_Tabs.Count; i++)
                {
                    styles[i] = m_Tabs.Count == 1 ? EditorStyles.miniButton
                        : i == 0 ? EditorStyles.miniButtonLeft
                        : i == m_Tabs.Count - 1 ? EditorStyles.miniButtonRight
                        : EditorStyles.miniButtonMid;
                    naturalWidths[i] = styles[i].CalcSize(new GUIContent(m_Tabs[i].Name)).x;
                    totalNaturalWidth += naturalWidths[i];
                }

                float height = styles[0].CalcHeight(new GUIContent(m_Tabs[0].Name), 100f) * 1.6f;

                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < m_Tabs.Count; i++)
                {
                    float width = totalNaturalWidth > 0f
                        ? availableWidth * (naturalWidths[i] / totalNaturalWidth)
                        : availableWidth / m_Tabs.Count;

                    bool selected = GUILayout.Toggle(i == m_SelectedTab, m_Tabs[i].Name, styles[i],
                        GUILayout.Width(width), GUILayout.Height(height));
                    if (selected)
                        m_SelectedTab = i;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6);

                foreach (var path in m_Tabs[m_SelectedTab].PropertyPaths)
                    if (ShouldShow(path, serializedObject))
                        EditorGUILayout.PropertyField(serializedObject.FindProperty(path), true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Alternative to <see cref="Draw"/>: each tab becomes its own collapsible, boxed
        /// foldout group (Unity's native "Advanced" look) stacked vertically instead of a tab
        /// bar switching between them — so more than one group can be open at once. Used by
        /// <see cref="StickyTextSettingsWindow"/>.
        /// </summary>
        static GUIStyle s_FoldoutTitleStyle;

        /// <summary>The bold, slightly larger foldout title style used by every fold box on the
        /// Settings page — exposed so other fold boxes elsewhere (e.g. the Management page's Tags
        /// and Labels groups) can match it exactly instead of drifting to the plain default.</summary>
        public static GUIStyle FoldoutTitleStyle => s_FoldoutTitleStyle ??= new GUIStyle(EditorStyles.foldout)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
        };

        public void DrawAsFoldouts(SerializedObject serializedObject)
        {
            serializedObject.Update();

            foreach (var path in m_Untabbed)
                if (ShouldShow(path, serializedObject))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(path), true);

            if (m_Untabbed.Count > 0 && m_Tabs.Count > 0)
                EditorGUILayout.Space(6);

            while (m_FoldoutStates.Count < m_Tabs.Count)
                m_FoldoutStates.Add(m_FoldoutStates.Count == 0); // first group open by default

            for (int i = 0; i < m_Tabs.Count; i++)
            {
                // One box contains both the header and the content (when expanded), so the
                // whole group reads as a single card — not just a header bar sitting above
                // separately-laid-out fields.
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var titleContent = new GUIContent(m_Tabs[i].Name, m_Tabs[i].Tooltip);
                m_FoldoutStates[i] = EditorGUILayout.Foldout(
                    m_FoldoutStates[i], titleContent, true, FoldoutTitleStyle);

                // Top/bottom padding applies whether expanded or not — a collapsed box that's
                // just the header row alone reads as too thin/cramped next to an expanded one.
                EditorGUILayout.Space(2);
                if (m_FoldoutStates[i])
                {
                    EditorGUI.indentLevel++;
                    foreach (var path in m_Tabs[i].PropertyPaths)
                        if (ShouldShow(path, serializedObject))
                            EditorGUILayout.PropertyField(serializedObject.FindProperty(path), true);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.Space(4);

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(6);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
