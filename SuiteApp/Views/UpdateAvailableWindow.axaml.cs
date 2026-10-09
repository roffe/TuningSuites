using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.Services;

namespace SuiteApp.Views;

/// <summary>ShowDialog&lt;bool&gt;: true for OK (update), false for Ignore or closed.</summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly Release? m_release;

    public UpdateAvailableWindow() => InitializeComponent();

    public UpdateAvailableWindow(Release release, string suite) : this()
    {
        m_release = release;
        Title = $"New version {suite} available...";
        Heading.Text = $"{suite} automatic updater found some new toys for you!";
        Available.Text = "Available version: " + release.Version;
    }

    // the change log is the release's notes on GitHub
    private void OnChangeLog(object? sender, RoutedEventArgs e) => Dialogs.OpenWithShell(m_release?.Page ?? UpdateCheck.ReleasesPage);

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnIgnore(object? sender, RoutedEventArgs e) => Close(false);
}
