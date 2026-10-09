using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>Something open in the main window's workspace (the suites' dock panels): a map viewer, compare results, ...</summary>
public abstract partial class DocumentViewModel : ObservableObject
{
    /// <summary>The dock panel title the suites used, also how an already open document is found.</summary>
    public abstract string Title { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Dragged out of the main window into a window of its own (the suites' floating panels).</summary>
    [ObservableProperty]
    private bool _isFloating;

    /// <summary>Asked before the document closes (unsaved changes); false keeps it open.</summary>
    public virtual Task<bool> CanCloseAsync(MainWindowViewModel owner) => Task.FromResult(true);

    /// <summary>The document was closed (the realtime panel stops polling).</summary>
    public virtual void Closed()
    {
    }
}
