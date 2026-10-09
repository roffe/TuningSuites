using CommunityToolkit.Mvvm.ComponentModel;

namespace T7App.ViewModels;

/// <summary>Something open in the main window's tabs (T7Suite's dock panels): a map viewer, compare results, ...</summary>
public abstract partial class DocumentViewModel : ObservableObject
{
    /// <summary>The dock panel title T7Suite used, also how an already open document is found.</summary>
    public abstract string Title { get; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Dragged out of the main window into a window of its own (T7Suite's floating panels).</summary>
    [ObservableProperty]
    private bool _isFloating;
}
