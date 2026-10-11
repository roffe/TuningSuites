using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>
/// The suites' main window around a SuiteMainView: its title, size and closing. Each app's MainWindow is one around its MainView;
/// TxSuite keeps one and puts another suite's view in it (Show).
/// </summary>
public class SuiteWindow : Window
{
    public SuiteWindow(SuiteMainView view)
    {
        Width = 1400;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this[!TitleProperty] = new Binding(nameof(MainWindowViewModel.Title));
        Content = view;
    }

    public SuiteMainView View => (SuiteMainView)Content!;

    /// <summary>Another suite's view and view model in place of the ones before, which close as the window would (TxSuite).</summary>
    public void Show(SuiteMainView view, MainWindowViewModel vm)
    {
        // the view inherits the window's view model; each keeps its own here: the old one out of the window, the new one never
        // seeing the one before (setting it from the window's DataContextChanged left the view's bindings stuck)
        SuiteMainView old = View;
        old.DataContext = old.DataContext;
        view.DataContext = vm;
        Content = view;
        DataContext = vm;
        old.Closed();
    }

    // ---- closing ----

    // unsaved maps ask before the app closes; Cancel keeps it open
    private bool m_closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainWindowViewModel { CloseBlocker: { } blocker } busy)
        {
            // stopping a flash halfway leaves the ECU without a working program
            e.Cancel = true;
            busy.ShowInfo(blocker);
            return;
        }
        if (m_closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.Viewers.OfType<MapViewerViewModel>().Any(v => v.Map.Mutated)) return;
        e.Cancel = true;
        if (!await vm.CloseMutatedViewersAsync()) return;
        m_closeConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        View.Closed();
    }
}
