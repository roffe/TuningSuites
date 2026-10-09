using System;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
using AvaloniaHex.Document;
using T7App.ViewModels;

namespace T7App.Views;

public partial class DisassemblyView : UserControl
{
    // T7Suite's ASM-Mode.xshd (the old SharpDevelop format AvaloniaEdit still reads)
    private static readonly Lazy<IHighlightingDefinition> Asm = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://T7Suite/Assets/ASM-Mode.xshd"));
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    });

    public DisassemblyView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = Asm.Value;
        SearchPanel.Install(Editor);
        Hex.HexView.BytesPerLine = 16;
        Editor.TextArea.Caret.PositionChanged += (_, _) => ShowLineBytes();
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
        }
    }

    /// <summary>Caret_PositionChanged: select the caret line's instruction bytes in the hex view and scroll there.</summary>
    private void ShowLineBytes()
    {
        if (DataContext is not DisassemblyViewModel vm || vm.LineBytes(Editor.TextArea.Caret.Line) is not { } bytes) return;
        Hex.Caret.Location = new BitLocation(bytes.Start);
        Hex.Selection.Range = new BitRange(bytes.Start, bytes.End);
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

    // Dock's MDI panel measures every inner window at the whole workspace's size and the Grid then keeps its row that tall,
    // hiding the bottom of both views under the window's edge: measure at the size the window really gives us
    protected override Size MeasureOverride(Size availableSize) => base.MeasureOverride(Bounds.Width > 0
        ? new Size(Math.Min(availableSize.Width, Bounds.Width), Math.Min(availableSize.Height, Bounds.Height))
        : availableSize);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateMeasure();
        Panes.InvalidateMeasure(); // measuring again with a smaller size doesn't make the Grid arrange again on its own
    }

    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as DisassemblyViewModel)?.Save();
}
