using System;

namespace StickyText.EditorTools
{
    /// <summary>
    /// Hides this field in <see cref="TabbedGUI"/> unless another field (by name, on the same
    /// object) currently equals a given enum value — e.g. a tuning knob that only makes sense
    /// for one setting of a mode switch. Purely a marker; checked by TabbedGUI at draw time.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ShowIfAttribute : Attribute
    {
        public readonly string ConditionField;
        public readonly int ExpectedValue;

        public ShowIfAttribute(string conditionField, object expectedEnumValue)
        {
            ConditionField = conditionField;
            ExpectedValue = Convert.ToInt32(expectedEnumValue);
        }
    }
}
