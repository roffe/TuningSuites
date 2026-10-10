using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteApp.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    /// <summary>
    /// frmAbout in the same words for every suite, only the thanks are the suite's; the heading "T7Suite v&lt;version&gt;" as
    /// frmAbout.SetInformation set it.
    /// </summary>
    public AboutWindow(string caption, string version, string thanks) : this()
    {
        Heading.Text = caption + " v" + version;
        Created.Text = caption + " was created with the help of lots of people on ecuproject.com and trionictuning.com";
        Thanks.Text = thanks;
        Support.Text = "No e-mail support currently, check out www.trionictuning.com and www.ecuproject.com";
        Signoff.Text = "Just4pLeisure ;-)";
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
