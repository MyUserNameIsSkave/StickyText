using UnityEditor;
using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Shared "coloured tag picker" control — a button tinted with the tag's colour and showing
    /// its name (or "DEFAULT", tinted with the live Settings background colour, for no tag),
    /// opening a menu of every tag on click. Used by the Management window's label list, the
    /// TextLabel inspector, and the Scene view overlay's "new label" tag picker, so all three
    /// look and behave identically.
    /// </summary>
    static class StickyTextTagGUI
    {
        /// <summary>Colour and display name for a tag by name — "DEFAULT" falls back to the live
        /// Settings background colour rather than a fixed neutral colour.</summary>
        public static (Color color, string name) Resolve(string tagName)
        {
            var tag = StickyTextTagRegistry.Instance.Find(tagName);
            return tag != null
                ? (tag.color, tag.name)
                : (StickyTextSettings.Instance.backgroundColor, "DEFAULT");
        }

        /// <summary>Draws the picker in <paramref name="rect"/>. <paramref name="onSelect"/> is
        /// called with null for DEFAULT, or the chosen tag.</summary>
        public static void DrawPicker(Rect rect, string currentTagName, System.Action<StickyTextTag> onSelect)
        {
            var (color, name) = Resolve(currentTagName);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.35f));

            if (GUI.Button(rect, name, EditorStyles.label))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("DEFAULT"), string.IsNullOrEmpty(currentTagName), () => onSelect(null));
                foreach (var t in StickyTextTagRegistry.Instance.tags)
                {
                    var captured = t;
                    menu.AddItem(new GUIContent(captured.name), currentTagName == captured.name, () => onSelect(captured));
                }
                menu.ShowAsContext();
            }
        }
    }
}
