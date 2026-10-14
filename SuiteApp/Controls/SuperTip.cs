using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SuiteApp.Controls;

/// <summary>
/// A DevExpress SuperToolTip as the old settings dialogs had: the title in bold and the text indented under it (its LeftIndent 6).
/// Use as a control's tooltip: &lt;ToolTip.Tip&gt;&lt;c:SuperTip Title="..." Text="..." /&gt;&lt;/ToolTip.Tip&gt;.
/// </summary>
public class SuperTip : StackPanel
{
    private readonly TextBlock m_title = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock m_text = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 0), IsVisible = false };

    public SuperTip()
    {
        Spacing = 4;
        MaxWidth = 380;
        Children.Add(m_title);
        Children.Add(m_text);
    }

    // bindable, for a tip in a template (Settings → Logging's rows)
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SuperTip, string?>(nameof(Title));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SuperTip, string?>(nameof(Text));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) Show(m_title, Title);
        else if (change.Property == TextProperty) Show(m_text, Text);
    }

    private static void Show(TextBlock block, string? text)
    {
        block.Text = text;
        block.IsVisible = !string.IsNullOrEmpty(text);
    }
}
