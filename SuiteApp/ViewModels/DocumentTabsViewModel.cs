using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>
/// Documents as tabs along the bottom of one window, the suites' DevExpress tab container (DockAsTab: a compare's two maps). The
/// tabs stay in the main window's Viewers, this window only shows them; its caption is the selected tab's.
/// </summary>
public partial class DocumentTabsViewModel(MainWindowViewModel owner) : DocumentViewModel
{
    public ObservableCollection<DocumentViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private DocumentViewModel? _selected;

    public override string Title => Selected?.Title ?? "";

    // the tab picked is the main window's selected document
    partial void OnSelectedChanged(DocumentViewModel? value)
    {
        if (value != null) owner.SelectedViewer = value;
    }

    public void Insert(int index, DocumentViewModel doc)
    {
        doc.Tabs = this;
        Items.Insert(index, doc);
    }

    /// <summary>A tab out of the window, the next one selected in its place.</summary>
    public void Remove(DocumentViewModel doc)
    {
        int i = Items.IndexOf(doc);
        if (i < 0) return;
        bool selected = Selected == doc;
        Items.RemoveAt(i);
        doc.Tabs = null;
        if (selected) Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
    }

    /// <summary>A tab's own close button: that tab alone, asking about its changes.</summary>
    [RelayCommand]
    private Task CloseTab(DocumentViewModel tab) => owner.CloseViewerAsync(tab);
}
