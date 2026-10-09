using Avalonia.Controls;
using Avalonia.Interactivity;
using T7;
using T7App.Services;

namespace T7App.Views;

/// <summary>ShowDialog&lt;bool&gt;: true for OK (update), false for Ignore or closed.</summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly Release? m_release;

    public UpdateAvailableWindow() => InitializeComponent();

    public UpdateAvailableWindow(Release release) : this()
    {
        m_release = release;
        Available.Text = "Available version: " + release.Version;
    }

    // the change log is the release's notes on GitHub
    private void OnChangeLog(object? sender, RoutedEventArgs e) => Dialogs.OpenWithShell(m_release?.Page ?? UpdateCheck.ReleasesPage);

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnIgnore(object? sender, RoutedEventArgs e) => Close(false);
}
