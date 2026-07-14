using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StickyText
{
    /// <summary>
    /// Intermediate wrapper that drives a child <see cref="TextMeshPro"/> text. Set the text
    /// and box size through <see cref="Text"/> and <see cref="Size"/> without touching TMP —
    /// but the TMP still lives on the child "Text" object, so it stays reachable for the odd
    /// manual tweak. Keeps the text centred both ways and auto-sized to fit the box.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase] // clicking the child text/background in the viewport selects this parent
    [DisallowMultipleComponent]
    [AddComponentMenu("StickyText/Text Label")]
    public class TextLabel : MonoBehaviour
    {
        [SerializeField, TextArea(1, 5)] string m_Text = "Text";
        [SerializeField, HideInInspector] Vector2 m_Size = Vector2.one;
        float m_MinFontSize = 0.5f;
        float m_MaxFontSize = 72f;
        const float k_PlaceholderAlpha = 0.2f;

        [Tooltip("Shows the text in uppercase (via TextMeshPro's own text case rendering, not " +
                 "by altering the stored text).")]
        [SerializeField] bool m_ForceUppercase;

        // Normally driven by the assigned tag's text colour (or DEFAULT's, for none) — editable
        // here as a manual override. Left untouched by later tag colour edits/reassignments once
        // set (see DrawTagRow/DrawDefaultTagRow's propagation — colour changes only follow
        // labels still matching the tag's old value, same as EditorOnly), same as
        // m_BackgroundColorOverride below.
        [Tooltip("Overrides this label's text colour. Sticks even if the assigned tag's colour " +
                 "changes afterward — reassign the tag (or match the override back to the tag's " +
                 "colour) to let it follow the tag again.")]
        [FormerlySerializedAs("m_Color")]
        [SerializeField] Color m_TextColorOverride = Color.white;
        string m_Placeholder = "Lorem Ipsum";
        [SerializeField] float m_Margin; // padding inside the box, world units

        // Legacy field, kept only so MigrateEditorOnly() can read a pre-existing scene's value
        // once — EditorOnly (below) is the opposite polarity of this old field, so it can't
        // just be renamed in place without inverting the meaning of already-serialized data.
        [SerializeField, HideInInspector] bool m_VisibleInGame = true;
        [SerializeField] bool m_EditorOnly;
        [SerializeField, HideInInspector] bool m_EditorOnlyMigrated;

        // Looked up by name against the tag registry (Editor-only) rather than storing a colour
        // here directly — so re-colouring a tag retroactively updates every label using it.
        [SerializeField] string m_Tag = "";

        // Normally driven by the assigned tag's colour (or DEFAULT's, for none) — editable here
        // as a manual override. Left untouched by later tag colour edits once set (see
        // DrawTagRow/DrawDefaultTagRow's propagation — colour changes only follow labels still
        // matching the tag's old value, same as EditorOnly). Assigning a *different* tag still
        // overwrites it immediately, same as it always has.
        [Tooltip("Overrides this label's background colour. Sticks even if the assigned tag's " +
                 "colour changes afterward — reassign the tag (or match the override back to the " +
                 "tag's colour) to let it follow the tag again.")]
        [FormerlySerializedAs("m_BackgroundColor")]
        [SerializeField] Color m_BackgroundColorOverride = Color.black;

        [SerializeField, HideInInspector] TextMeshPro m_Tmp; // on the child "Text" object
        [SerializeField, HideInInspector] MeshRenderer m_BackgroundRenderer; // on the child "Background" object, if any

        /// <summary>The displayed text. Setting it updates the TMP immediately.</summary>
        public string Text
        {
            get => m_Text;
            set { m_Text = value; Apply(); }
        }

        /// <summary>Size of the text box in world units (the placement rectangle).</summary>
        public Vector2 Size
        {
            get => m_Size;
            set { m_Size = value; Apply(); }
        }

        /// <summary>Colour of the actual text.</summary>
        public Color TextColorOverride
        {
            get => m_TextColorOverride;
            set { m_TextColorOverride = value; Apply(); }
        }

        /// <summary>Placeholder text shown while empty.</summary>
        public string Placeholder
        {
            get => m_Placeholder;
            set { m_Placeholder = value; Apply(); }
        }

        /// <summary>Padding between the text and the box edges, in world units.</summary>
        public float Margin
        {
            get => m_Margin;
            set { m_Margin = value; Apply(); }
        }

        /// <summary>Whether the text renders in uppercase (via TMP's own text case rendering —
        /// the stored <see cref="Text"/> itself is untouched).</summary>
        public bool ForceUppercase
        {
            get => m_ForceUppercase;
            set { m_ForceUppercase = value; Apply(); }
        }

        /// <summary>Smallest font size the auto-sizing text is allowed to shrink to.</summary>
        public float MinFontSize
        {
            get => m_MinFontSize;
            set { m_MinFontSize = value; Apply(); }
        }

        /// <summary>Largest font size the auto-sizing text is allowed to grow to.</summary>
        public float MaxFontSize
        {
            get => m_MaxFontSize;
            set { m_MaxFontSize = value; Apply(); }
        }

        /// <summary>Whether this label is a dev-only marker (blockout notes, bug pins) that
        /// disappears once the game is actually running (Play Mode or a build) — off means it
        /// stays visible in the game too.</summary>
        public bool EditorOnly
        {
            get => m_EditorOnly;
            set { m_EditorOnly = value; Apply(); }
        }

        /// <summary>Name of the tag assigned to this label (looked up against the tag registry
        /// for its colour), or empty for none.</summary>
        public string Tag
        {
            get => m_Tag;
            set => m_Tag = value;
        }

        /// <summary>Colour of the background plane, always visible.</summary>
        public Color BackgroundColorOverride
        {
            get => m_BackgroundColorOverride;
            set { m_BackgroundColorOverride = value; Apply(); }
        }

        /// <summary>The wrapped TMP component, for the occasional advanced tweak.</summary>
        public TextMeshPro TextMesh => m_Tmp;

        /// <summary>The background plane's renderer, if one was created for this label.</summary>
        public MeshRenderer Background => m_BackgroundRenderer;

        /// <summary>Wires up the background renderer this label controls. Used by the placement tool.</summary>
        public void SetBackground(MeshRenderer renderer, Color color)
        {
            m_BackgroundRenderer = renderer;
            m_BackgroundColorOverride = color;
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        // One-time migration from the old VisibleInGame field (opposite polarity) — runs once
        // per object, the first time it's loaded after the rename, then never again (the flag
        // itself is serialized). A brand-new label defaults m_VisibleInGame to true, which
        // correctly migrates to m_EditorOnly = false (visible in game), matching the old default.
        void MigrateEditorOnly()
        {
            if (m_EditorOnlyMigrated)
                return;
            m_EditorOnly = !m_VisibleInGame;
            m_EditorOnlyMigrated = true;
        }

        void Apply()
        {
            MigrateEditorOnly();

            // True in the Scene view at design time; false in Play Mode / a build unless
            // EditorOnly is off — dev-only markers simply disappear once the game is running.
            bool showInPlayMode = !Application.isPlaying || !m_EditorOnly;

            ApplyBackground(showInPlayMode);

            if (m_Tmp == null)
                m_Tmp = GetComponentInChildren<TextMeshPro>();
            if (m_Tmp == null)
                return;

            // Show a dimmed placeholder while empty; the real text takes over on first keystroke.
            // The placeholder colour is always the text colour at a fixed, low opacity — not a
            // separately configurable colour.
            bool empty = string.IsNullOrEmpty(m_Text);
            m_Tmp.text = empty ? m_Placeholder : m_Text;
            m_Tmp.color = empty
                ? new Color(m_TextColorOverride.r, m_TextColorOverride.g, m_TextColorOverride.b, k_PlaceholderAlpha)
                : m_TextColorOverride;
            // This TMP version has no separate textCase property — UpperCase is a FontStyles
            // flag instead. Safe to assign outright (not OR/AND a bit) since nothing else here
            // ever sets another FontStyles flag.
            m_Tmp.fontStyle = m_ForceUppercase ? FontStyles.UpperCase : FontStyles.Normal;
            m_Tmp.horizontalAlignment = HorizontalAlignmentOptions.Center;
            m_Tmp.verticalAlignment = VerticalAlignmentOptions.Middle;
            m_Tmp.enableAutoSizing = true;
            m_Tmp.fontSizeMin = m_MinFontSize;
            m_Tmp.fontSizeMax = m_MaxFontSize;

            m_Tmp.margin = new Vector4(m_Margin, m_Margin, m_Margin, m_Margin);

            var rt = m_Tmp.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = m_Size;

            if (m_Tmp.TryGetComponent<MeshRenderer>(out var textRenderer))
                textRenderer.enabled = showInPlayMode;

#if UNITY_EDITOR
            // TextMeshPro assigns its child object a Scene view gizmo icon that shows up
            // whenever the object is on screen, selected or not — distracting clutter for a
            // dev-only annotation tool. Overriding it with a blank icon hides it; the override
            // lives in EditorGUIUtility's in-memory table, not on the object, so it has to be
            // reapplied here every time (OnEnable/OnValidate) rather than set once.
            EditorGUIUtility.SetIconForObject(m_Tmp.gameObject, BlankIcon);
#endif
        }

#if UNITY_EDITOR
        static Texture2D s_BlankIcon;
        static Texture2D BlankIcon
        {
            get
            {
                if (s_BlankIcon == null)
                {
                    s_BlankIcon = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                        { hideFlags = HideFlags.HideAndDontSave };
                    s_BlankIcon.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                    s_BlankIcon.Apply();
                }
                return s_BlankIcon;
            }
        }
#endif

        void ApplyBackground(bool showInPlayMode)
        {
            if (m_BackgroundRenderer == null)
            {
                var t = transform.Find("Background");
                if (t != null)
                    m_BackgroundRenderer = t.GetComponent<MeshRenderer>();
            }

            if (m_BackgroundRenderer == null)
                return;

            m_BackgroundRenderer.enabled = showInPlayMode;

            var mat = m_BackgroundRenderer.sharedMaterial;
            if (mat == null)
                return;
            // _UnlitColor: HDRP/Unlit's actual colour property - unlike HDRP/Lit, it does NOT use
            // _BaseColor, which is why an HDRP background's colour could silently never update
            // without this (mirrors StickyTextTool.ApplyBackgroundColor - kept in sync manually,
            // Runtime code can't reference that Editor-only method).
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", m_BackgroundColorOverride);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", m_BackgroundColorOverride);
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", m_BackgroundColorOverride);
        }
    }
}
