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

    public string? Title
    {
        get => m_title.Text;
        set
        {
            m_title.Text = value;
            m_title.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    public string? Text
    {
        get => m_text.Text;
        set
        {
            m_text.Text = value;
            m_text.IsVisible = !string.IsNullOrEmpty(value);
        }
    }
}
