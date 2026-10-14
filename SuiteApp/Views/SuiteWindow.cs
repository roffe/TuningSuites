using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The main window around a SuiteMainView: its title, size and closing.</summary>
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
