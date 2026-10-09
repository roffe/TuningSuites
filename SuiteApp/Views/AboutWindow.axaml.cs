using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteApp.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    /// <summary>frmAbout with its suite's texts; the heading "T7Suite v&lt;version&gt;" as frmAbout.SetInformation set it.</summary>
    public AboutWindow(string caption, string version, string thanks, string support, string closing) : this()
    {
        Heading.Text = caption + " v" + version;
        Created.Text = caption + " Pro was created with the help of lots of people on ecuproject.com and trionictuning.com";
        Thanks.Text = thanks;
        Support.Text = support;
        Signoff.Text = closing;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
