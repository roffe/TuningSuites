using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>A document dragged out of the main window. Closing it closes the document (asking about unsaved changes).</summary>
public partial class FloatingDocumentWindow : Window
{
    private readonly MainWindowViewModel? m_owner;
    private bool m_closing;

    public FloatingDocumentWindow() => InitializeComponent();

    public FloatingDocumentWindow(MainWindowViewModel owner, DocumentViewModel doc) : this()
    {
        m_owner = owner;
        DataContext = doc;
        Activated += (_, _) => owner.SelectedViewer = doc;
    }

    private DocumentViewModel Doc => (DocumentViewModel)DataContext!;

    private void OnDock(object? sender, RoutedEventArgs e) => Doc.IsFloating = false;

    /// <summary>Closed by the main window (docked back or the document closed): no question.</summary>
    public void CloseQuietly()
    {
        m_closing = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (m_closing || m_owner == null) return;
        e.Cancel = true;
        if (await m_owner.CloseViewerAsync(Doc)) CloseQuietly();
    }
}
