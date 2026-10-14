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

        /// <summary>TxSuite's settings dialog on a tab (Common 0, T5 1, T7 2, T8 3), T8's by default; edit changes it before it shows.</summary>
        internal static Window ShowSettings(TxSuite.ViewModels.MainViewModel vm, int tab = 3, System.Action<TxSuite.ViewModels.TxSettingsViewModel>? edit = null)
        {
            var settings = new TxSuite.ViewModels.TxSettingsViewModel(vm);
            edit?.Invoke(settings);
            var window = new TxSuite.Views.TxSettingsWindow { DataContext = settings };
            window.Show();
            window.FindControl<TabControl>("Tabs")!.SelectedIndex = tab;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return window;
        }
    }
}
