using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>How placement finds the surface under the cursor.</summary>
    public enum DepthTestMethod
    {
        Mesh,     // HandleUtility scene picking + triangle intersection — needs no collider
        Collider, // Physics.Raycast against colliders — cheaper, but blind to colliderless geometry
    }

    /// <summary>What <see cref="StickyTextBuildStripper"/> removes from a scene for a given
    /// build type.</summary>
    public enum BuildStripMode
    {
        None,       // keep every TextLabel, Editor Only or not
        EditorOnly, // remove only labels marked Editor Only
        All,        // remove every TextLabel regardless of that flag
    }

    /// <summary>
    /// Tweakable settings for <see cref="StickyTextTool"/>. Edit the generated asset (created
    /// automatically on first use, or via
    /// Assets ▸ Create ▸ StickyText ▸ Settings) from the
    /// <see cref="StickyTextSettingsWindow"/> (Tools ▸ StickyText) — grouped into
    /// tabs: general misc settings not in the overlay, gizmo colours, and everything also
    /// mirrored in the "StickyText" Scene view overlay.
    /// </summary>
    [CreateAssetMenu(fileName = "StickyTextSettings",
        menuName = "StickyText/Settings")]
    public class StickyTextSettings : ScriptableObject
    {
        /// <summary>Folder where auto-created StickyText assets (settings, tag registry,
        /// background material) land when none exists in the project yet. In Assets — not the
        /// package itself, which is read-only when installed from a registry/git URL — and
        /// created on demand. Existing assets are always found wherever they are (FindAssets),
        /// so moving them out of this folder afterward is fine.</summary>
        internal const string k_DefaultFolder = "Assets/StickyText";

        const string k_DefaultPath = k_DefaultFolder + "/StickyTextSettings.asset";

        internal static void EnsureDefaultFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_DefaultFolder))
                AssetDatabase.CreateFolder("Assets", "StickyText");
        }

        // ===================================================================================
        // General — misc settings not in the overlay
        // ===================================================================================

        [Tab("General", "Misc settings not exposed in the overlay.")]
        [Header("     Hierarchy")]
        [Tooltip("Optional parent every newly placed label is created under, instead of the " +
                 "scene root. World position/rotation is preserved regardless of this " +
                 "transform's own — leave empty to keep placing at the scene root.")]
        public Transform defaultParent;

        [Tooltip("Fixed prefix used when naming placed labels' GameObjects (\"<prefix> <text>\", " +
                 "or \"<prefix> Empty\" while blank) — purely cosmetic, for the Hierarchy.")]
        public string labelNamePrefix = "StickyText -";

        [Tooltip("How many characters of a label's text are used in its GameObject's default " +
                 "name — purely cosmetic, for spotting labels in the Hierarchy.")]
        [Range(0, 60)] public int labelNameCharacterCount = 20;

        [Space(10)]
        [Header("     Placement")]
        [Tooltip("How placement finds the surface under the cursor. Mesh: works on any renderer, " +
                 "no collider needed, but slower (uses the Scene view picking system). Collider: " +
                 "raycasts against colliders instead — noticeably faster, but ignores any mesh " +
                 "that has no collider on it.")]
        public DepthTestMethod depthTestMethod = DepthTestMethod.Mesh;

        [Tooltip("Grid resolution used to find the nearest geometry so the plane never pokes " +
                 "through. Only sampled once on drag release, so higher costs nothing while " +
                 "dragging — but each sample renders a picking pass, so keep this modest.")]
        [ShowIf(nameof(depthTestMethod), DepthTestMethod.Mesh)]
        [Range(1, 12)] public int meshDepthSamples = 8;

        [Tooltip("Grid resolution used to find the nearest geometry so the plane never pokes " +
                 "through. Only sampled once on drag release. Collider raycasts are cheap, so " +
                 "feel free to push this higher than the Mesh method's equivalent.")]
        [ShowIf(nameof(depthTestMethod), DepthTestMethod.Collider)]
        [Range(1, 24)] public int colliderDepthSamples = 16;

        /// <summary>The sample count for whichever <see cref="depthTestMethod"/> is active.</summary>
        public int depthSamples => depthTestMethod == DepthTestMethod.Collider
            ? colliderDepthSamples
            : meshDepthSamples;

        [Tooltip("Drags smaller than this (in screen pixels) are ignored.")]
        public float minDragPixels = 10f;

        // Edited exclusively via the DEFAULT tag's row in the Tags manager (Management page)
        // now, the same as backgroundColor above — not exposed as its own control here.
        [HideInInspector] public Color textColor = Color.white;

        [Space(10)]
        [Header("     Text")]
        [Tooltip("Padding between the text and the box edges, in world units.")]
        [Range(0f, 1f)] public float textMargin = 0.5f;

        [Tooltip("Smallest font size the auto-sizing text is allowed to shrink to.")]
        public float minFontSize = 0.5f;

        [Tooltip("Largest font size the auto-sizing text is allowed to grow to.")]
        public float maxFontSize = 1000f;

        [Space(10)]
        [Header("     Materials")]
        [Tooltip("Material every placed label's TextMeshPro uses. Leave empty to auto-create a " +
                 "dedicated instance (next to this settings file) from whatever font asset TMP " +
                 "currently defaults to (TMP Settings' Default Font Asset) - a StickyText-owned " +
                 "copy, so retuning it later never affects any other TMP text in the project " +
                 "sharing that same default font material.")]
        public Material textMaterial;

        [Tooltip("Base material instanced for each background plane. Leave empty to auto-create " +
                 "a transparent Unlit material asset next to this settings file.")]
        public Material backgroundMaterial;

        [Space(10)]
        [Header("     Build Stripping")]
        [Tooltip("What StickyTextBuildStripper removes from a Development Build. None keeps " +
                 "every TextLabel; Editor Only removes only labels marked Editor Only; All " +
                 "removes every TextLabel regardless of that flag.")]
        public BuildStripMode developmentBuildStripMode = BuildStripMode.None;

        [Tooltip("What StickyTextBuildStripper removes from a Release (non-development) build. " +
                 "None keeps every TextLabel; Editor Only removes only labels marked Editor " +
                 "Only; All removes every TextLabel regardless of that flag.")]
        public BuildStripMode releaseBuildStripMode = BuildStripMode.All;

        // ===================================================================================
        // Gizmo — colours and other visual-only settings
        // ===================================================================================

        [Tab("Gizmo", "Colours and other visual-only settings for the placement gizmos.")]
        [Header("     Face Camera")]
        public Color faceCameraFill = new Color(0.2509804f, 0.6f, 1f, 0.14901961f);
        public Color faceCameraOutline = new Color(0.4f, 0.8f, 1f, 0.9f);

        [Space(10)]
        [Header("     Align to Surface")]
        public Color surfaceFill = new Color(0.25f, 0.6f, 1f, 0.15f);
        public Color surfaceOutline = new Color(0.4f, 0.8f, 1f, 1f);

        [Space(10)]
        [Header("     Fixed Distance")]
        public Color fixedPlaneFill = new Color(1f, 0.7f, 0.2f, 0.12941177f);
        [Tooltip("Used for both the grid lines and the plane's outline border.")]
        public Color fixedPlaneGrid = new Color(1f, 0.75f, 0.3f, 0.9411765f);

        [Tooltip("World-space size (in units) of each grid square on the fixed plane gizmo. Fixed " +
                 "physical size — not a fixed division count — so the grid visibly gets denser " +
                 "as the plane moves farther away, giving a real sense of distance/scale.")]
        [Min(0.01f)] public float fixedPlaneGridCellSize = 5f;

        // ===================================================================================
        // Overlay Mirror — same settings shown in the "StickyText" Scene view overlay
        // ===================================================================================

        [Tab("Overlay Mirror", "Also shown in the \"StickyText\" Scene view overlay, for quick access while placing text.")]
        [Header("     Text")]
        [Tooltip("Whether newly placed labels default to dev-only markers that disappear once " +
                 "the game is running (Play Mode or a build). Turn on to default new labels to " +
                 "dev-only markers.")]
        public bool editorOnly;

        [Tooltip("Whether newly placed labels default to showing their text in uppercase " +
                 "(via TextMeshPro's own text case rendering, not by altering the stored text).")]
        public bool forceUppercase = true;

        // Legacy field (opposite polarity of editorOnly) — kept only so OnEnable's migration
        // can read a pre-existing asset's value once.
        [SerializeField, HideInInspector] bool visibleInGame = true;
        [SerializeField, HideInInspector] bool editorOnlyMigrated;

        // Hidden from the auto-generated Settings/Inspector UI (a raw text field wouldn't be
        // useful there) — set only via the coloured tag picker in the Scene view overlay, and
        // applied to every newly placed label by StickyTextTool.TryPlace.
        [HideInInspector] public string currentTag = "";

        // The overlay's own live/overridable Editor Only value — distinct from editorOnly above
        // (which is DEFAULT's persistent value, edited via its row in the Tags manager). Picking
        // a tag reclaims this from the tag's editorOnly (or from editorOnly itself for DEFAULT);
        // manually toggling it afterward overrides it until the next tag pick, without ever
        // touching editorOnly/the tag's own stored value. Mirrors currentTag; applied to every
        // newly placed label by StickyTextTool.TryPlace.
        [HideInInspector] public bool currentEditorOnly;

        // The background plane is always shown now (no more disable toggle) and its colour
        // always comes from a tag — this is only the DEFAULT tag's colour, edited exclusively
        // via its row in the Tags manager (Management page), not exposed as its own control.
        [HideInInspector] public Color backgroundColor = new Color(0f, 0f, 0f, 0.9411765f);

        [Space(10)]
        [Header("     Face Camera")]
        [Tooltip("Distance the plane is pushed toward the camera to avoid z-fighting.")]
        public float faceCameraOffset = 0.4545454f;

        [Space(10)]
        [Header("     Align to Surface")]
        [Tooltip("Distance the plane is pushed along the surface normal to avoid z-fighting.")]
        public float surfaceOffset = 0.1f;

        [Tooltip("On floors and ceilings only, snap the quad's tangent axes to the nearest " +
                 "world principal axis (X/Z). Fixes the view-dependent orientation when " +
                 "writing on horizontal surfaces; walls and ramps are left untouched.")]
        public bool alignFloorCeilingToWorldAxes = true;

        [Tooltip("Only snap when the tangent axis is within this many degrees of a world axis. " +
                 "Small = only correct slight offsets; 45 = always snap to the nearest axis.")]
        [Range(0f, 45f)] public float axisSnapMargin = 20f;

        [Tooltip("Rotation (degrees) applied to the reference axis when detecting the snap " +
                 "only — it biases which world axis is chosen, and does not rotate the " +
                 "final placement.")]
        [Range(-180f, 180f)] public float snapDetectionOffset = 0f;

        [Space(10)]
        [Header("     Fixed Distance")]
        [Tooltip("Distance from the camera, along its view direction, of the placement plane. " +
                 "Also used as the placement distance in any mode when nothing is hit under the " +
                 "cursor (e.g. pointing at open sky) — text floats there regardless of mode.")]
        [Min(0.01f)] public float fixedPlaneDistance = 84.409966f;

        [Tooltip("World units the plane distance changes per pixel of right-mouse-button drag " +
                 "while adjusting it (hold Ctrl, then drag right-click left/right).")]
        [Range(0.001f, 0.2f)] public float fixedPlaneDistanceDragSensitivity = 0.1f;

        // One-time migration from the old visibleInGame field (opposite polarity) — see
        // TextLabel.MigrateEditorOnly for the same pattern applied per-label.
        void OnEnable()
        {
            if (editorOnlyMigrated)
                return;
            editorOnly = !visibleInGame;
            editorOnlyMigrated = true;
        }

        static StickyTextSettings s_Instance;

        /// <summary>Cached settings asset. Found in the project, or created on first access.</summary>
        public static StickyTextSettings Instance
        {
            get
            {
                if (s_Instance != null)
                    return s_Instance;

                var guids = AssetDatabase.FindAssets($"t:{nameof(StickyTextSettings)}");
                if (guids.Length > 0)
                    s_Instance = AssetDatabase.LoadAssetAtPath<StickyTextSettings>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));

                if (s_Instance == null)
                {
                    EnsureDefaultFolder();
                    s_Instance = CreateInstance<StickyTextSettings>();
                    AssetDatabase.CreateAsset(s_Instance, k_DefaultPath);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[StickyText] Created settings asset at {k_DefaultPath}");
                }

                return s_Instance;
            }
        }
    }
}
