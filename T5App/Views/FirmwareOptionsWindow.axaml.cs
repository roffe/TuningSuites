using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using T5App.ViewModels;

namespace T5App.Views;

public partial class FirmwareOptionsWindow : Window
{
    public FirmwareOptionsWindow() => InitializeComponent();

    public static readonly IValueConverter CaptionOf = new FuncValueConverter<Enum?, string>(e => e == null ? "" : FirmwareOptionsViewModel.Caption(e));

    /// <summary>The partnumber's lookup button (frmEasyFirmwareSettings' txtPartnumber_ButtonClick).</summary>
    public Func<Task>? LookupPartnumber { get; set; }

    private async void OnLookup(object? sender, RoutedEventArgs e)
    {
        if (LookupPartnumber != null) await LookupPartnumber();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
