using Avalonia.Data.Converters;
using SuiteApp.Views;

namespace T5App.Views;

public partial class SettingsWindow : SuiteSettingsWindow
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>T5Suite's captions for the wideband symbols; the setting keeps the bare name.</summary>
    public static readonly IValueConverter PinCaption = new FuncValueConverter<string?, string?>(s => s switch
    {
        "AD_cat" => "AD_cat (pin 70)",
        "AD_EGR" => "AD_EGR (pin 69)",
        _ => s,
    });
}
