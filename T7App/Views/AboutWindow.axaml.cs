using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    /// <summary>frmAbout.SetInformation: "T7Suite v&lt;version&gt;".</summary>
    public AboutWindow(string version) : this() => Version.Text = "T7Suite v" + version;

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
