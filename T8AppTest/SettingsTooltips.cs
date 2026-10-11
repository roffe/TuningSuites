using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>The settings controls with no tooltip on them or on their row (a tip sits on a CheckBox or on a label / box pair).</summary>
        internal static string[] Untipped(Window settings) => settings.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CheckBox or ComboBox or NumericUpDown or TextBox && c.IsEffectivelyVisible)
            .Where(c => !c.GetSelfAndVisualAncestors().OfType<Control>().TakeWhile(a => a is not HeaderedContentControl).Any(a => ToolTip.GetTip(a) != null))
            .Select(c => c is CheckBox b ? $"{b.Content}" : $"{c.GetType().Name} {(c.DataContext == settings.DataContext ? "" : "(templated)")}")
            .ToArray();
    }
}
