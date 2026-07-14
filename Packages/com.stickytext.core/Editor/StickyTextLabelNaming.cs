using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Keeps a placed <see cref="TextLabel"/>'s GameObject named after its content — easier to
    /// spot in the Hierarchy than a generic name shared by every label. Lives here (Editor-only)
    /// rather than in <see cref="TextLabel"/> itself, since the character-count option is a
    /// <see cref="StickyTextSettings"/> field and Runtime code can't reference the Editor
    /// assembly — called from every place that actually sets <see cref="TextLabel.Text"/>.
    /// </summary>
    static class StickyTextLabelNaming
    {
        public static void Apply(TextLabel label)
        {
            if (label == null)
                return;

            var settings = StickyTextSettings.Instance;
            string text = label.Text ?? string.Empty;

            string suffix;
            if (string.IsNullOrEmpty(text))
            {
                suffix = "Empty";
            }
            else
            {
                int count = Mathf.Clamp(settings.labelNameCharacterCount, 0, text.Length);
                suffix = text.Substring(0, count);
            }

            label.gameObject.name = $"{settings.labelNamePrefix} {suffix}";
        }
    }
}
