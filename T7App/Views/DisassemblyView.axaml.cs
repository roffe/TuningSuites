using System.Xml;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using AvaloniaHex.Document;
using T7App.ViewModels;

namespace T7App.Views;

public partial class DisassemblyView : UserControl
{
    // T7Suite's ASM-Mode.xshd colours, and a lighter set of the same hues for the dark theme (MidnightBlue / DarkBlue on a dark
    // background were unreadable)
    private static readonly Lazy<IHighlightingDefinition> Asm = new(() => Load(null));
    private static readonly Lazy<IHighlightingDefinition> AsmDark = new(() => Load(new()
    {
        ["Comment"] = Color.FromRgb(0x9A, 0xA8, 0xB4), ["Label"] = Color.FromRgb(0x8C, 0xC8, 0xFF), ["Digits"] = Color.FromRgb(0x7F, 0xB4, 0xFF),
        ["Keyword1"] = Color.FromRgb(0x56, 0x9C, 0xD6), ["Keyword2"] = Color.FromRgb(0x9C, 0xDC, 0xFE), ["Keyword3"] = Color.FromRgb(0xF4, 0x87, 0x71),
        ["Keyword4"] = Color.FromRgb(0xFF, 0x70, 0x70), ["Puntuation"] = Color.FromRgb(0x6C, 0xC0, 0x6C),
    }));

    private static IHighlightingDefinition Load(System.Collections.Generic.Dictionary<string, Color>? colors)
    {
        using var stream = AssetLoader.Open(new Uri("avares://T7Suite/Assets/ASM-Mode.xshd"));
        using var reader = XmlReader.Create(stream);
        IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        if (colors != null)
            foreach (HighlightingColor c in definition.NamedHighlightingColors)
                if (colors.TryGetValue(c.Name, out Color color)) c.Foreground = new SimpleHighlightingBrush(color);
        return definition;
    }

    private IHighlightingDefinition Highlighting() => ActualThemeVariant == ThemeVariant.Dark ? AsmDark.Value : Asm.Value;

    public DisassemblyView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = Highlighting();
        SearchPanel.Install(Editor);
        Hex.HexView.BytesPerLine = 16;
        Editor.TextArea.Caret.PositionChanged += (_, _) => ShowLineBytes();
        Hex.Caret.LocationChanged += (_, _) => (DataContext as DisassemblyViewModel)?.HexCaretAt(Hex.Caret.Location.ByteIndex);
        ActualThemeVariantChanged += (_, _) => Editor.SyntaxHighlighting = Highlighting();
        // after the hex editor's own handler has moved its caret to the clicked byte
        Hex.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount == 2) ShowAddress(Hex.Caret.Location.ByteIndex);
        }, RoutingStrategies.Bubble, true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DisassemblyViewModel vm)
        {
            Editor.Document = vm.Document;
            Hex.Document = vm.Binary;
            vm.HexCaretAt(Hex.Caret.Location.ByteIndex);
        }
    }

    /// <summary>Caret_PositionChanged: select the caret line's instruction bytes in the hex view and scroll there.</summary>
    private void ShowLineBytes()
    {
        if (DataContext is not DisassemblyViewModel vm || vm.LineBytes(Editor.TextArea.Caret.Line) is not { } bytes) return;
        Hex.Caret.Location = new BitLocation(bytes.Start);
        Hex.Selection.Range = new BitRange(bytes.Start, bytes.End);
        // HexViewer.SelectText: 64 bytes (4 lines) past the selection come into view too
        if (vm.Binary.Length > 0) Hex.HexView.BringIntoView(new BitLocation(Math.Min(bytes.End + 63, vm.Binary.Length - 1)));
        Hex.HexView.BringIntoView(Hex.Caret.Location);
    }

    /// <summary>hexViewer1_onSelectionChanged: select the byte address in the listing and scroll there, the caret after it.</summary>
    public void ShowAddress(ulong address)
    {
        if (DataContext is not DisassemblyViewModel vm || vm.FindAddress(address) is not { } found) return;
        Editor.Select(found.Offset, found.Length);
        Editor.TextArea.Caret.Offset = found.Offset + found.Length;
        var at = vm.Document.GetLocation(found.Offset);
        Editor.ScrollTo(at.Line, at.Column);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as DisassemblyViewModel)?.Save();
}
