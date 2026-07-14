using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>A single named tag with a text and background colour. Labels store only the
    /// name (see <see cref="TextLabel.Tag"/>) and look the colours up
    /// here, so re-colouring a tag retroactively updates every label using it.</summary>
    [Serializable]
    public class StickyTextTag
    {
        public string name = "New Tag";
        public Color textColor = Color.white;
        public Color color = Color.white; // background colour
        public bool editorOnly;

        // Legacy field (opposite polarity of editorOnly) — kept only so MigrateEditorOnly can
        // read a pre-existing tag's value once. See TextLabel.MigrateEditorOnly for the same
        // pattern.
        [SerializeField] bool visibleInGame = true;
        [SerializeField] bool editorOnlyMigrated;

        public void MigrateEditorOnly()
        {
            if (editorOnlyMigrated)
                return;
            editorOnly = !visibleInGame;
            editorOnlyMigrated = true;
        }
    }

    /// <summary>
    /// Editor-only registry of tags available for organizing <see cref="TextLabel"/>s —
    /// edited from the StickyText window's Management page (Tools ▸ StickyText).
    /// </summary>
    [CreateAssetMenu(fileName = "StickyTextTagRegistry", menuName = "StickyText/Tag Registry")]
    public class StickyTextTagRegistry : ScriptableObject
    {
        const string k_DefaultPath = StickyTextSettings.k_DefaultFolder + "/StickyTextTagRegistry.asset";

        public List<StickyTextTag> tags = new();

        void OnEnable()
        {
            foreach (var tag in tags)
                tag?.MigrateEditorOnly();
        }

        static StickyTextTagRegistry s_Instance;

        /// <summary>Cached registry asset. Found in the project, or created on first access.</summary>
        public static StickyTextTagRegistry Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;

                var guids = AssetDatabase.FindAssets($"t:{nameof(StickyTextTagRegistry)}");
                if (guids.Length > 0)
                    s_Instance = AssetDatabase.LoadAssetAtPath<StickyTextTagRegistry>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));

                if (s_Instance == null)
                {
                    StickyTextSettings.EnsureDefaultFolder();
                    s_Instance = CreateInstance<StickyTextTagRegistry>();
                    AssetDatabase.CreateAsset(s_Instance, k_DefaultPath);
                    AssetDatabase.SaveAssets();
                }

                return s_Instance;
            }
        }

        public StickyTextTag Find(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return null;
            return tags.Find(t => t.name == tagName);
        }
    }
}
