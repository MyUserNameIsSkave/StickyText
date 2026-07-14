using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Removes <see cref="TextLabel"/>s from each scene before a build, per
    /// <see cref="StickyTextSettings.developmentBuildStripMode"/> /
    /// <see cref="StickyTextSettings.releaseBuildStripMode"/> depending on whether the build is
    /// a Development Build. This physically strips labels out of the build, rather than relying
    /// solely on <see cref="TextLabel.Apply"/>'s runtime renderer-disable (which still applies
    /// on top of this, for whatever a None/Editor Only mode leaves behind).
    /// </summary>
    class StickyTextBuildStripper : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report is null when scenes are processed outside an actual player build (e.g. the
            // Editor's own scene-load pipeline) — nothing to strip in that case.
            if (report == null)
                return;

            var settings = StickyTextSettings.Instance;
            bool isDevelopment = report.summary.options.HasFlag(BuildOptions.Development);
            var mode = isDevelopment ? settings.developmentBuildStripMode : settings.releaseBuildStripMode;
            if (mode == BuildStripMode.None)
                return;

            foreach (var root in scene.GetRootGameObjects())
                foreach (var label in root.GetComponentsInChildren<TextLabel>(true))
                    if (label != null && (mode == BuildStripMode.All || label.EditorOnly))
                        Object.DestroyImmediate(label.gameObject);
        }
    }
}
