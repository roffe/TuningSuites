using System;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
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
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DisassemblyViewModel vm) Editor.Document = vm.Document;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as DisassemblyViewModel)?.Save();
}
