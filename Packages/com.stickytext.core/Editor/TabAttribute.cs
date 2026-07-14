using UnityEngine;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Groups this field and every field after it (until the next <see cref="TabAttribute"/>)
    /// under a named tab in the Inspector — a small, self-contained take on VInspector's Tab
    /// attribute. Purely a marker; rendering is handled by <see cref="TabbedGUI"/>.
    /// </summary>
    public class TabAttribute : PropertyAttribute
    {
        public readonly string TabName;
        public readonly string Tooltip;

        public TabAttribute(string tabName, string tooltip = null)
        {
            TabName = tabName;
            Tooltip = tooltip;
        }
    }
}
