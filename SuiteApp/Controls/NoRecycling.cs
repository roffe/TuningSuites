using Avalonia.Controls.Recycling;
using Avalonia.Controls.Recycling.Model;

namespace SuiteApp.Controls;

/// <summary>
/// Turns Dock's control recycling off (Dock builds its own when none is set). Recycling moved one view per document between
/// hosts, and the tabbed layout, once used, stayed alive behind the inner windows: its tab followed the active document and took
/// that window's view, leaving the window blank or grey. Each host now builds its own view; the inner windows live as long as
/// their documents, only a tab switch in the tabbed layout starts the map's view afresh.
/// </summary>
public class NoRecycling : IControlRecycling
{
    public bool TryToUseIdAsKey { get; set; }

    public bool TryGetValue(object? data, out object? control)
    {
        control = null;
        return false;
    }

    public void Add(object data, object control)
    {
    }

    // ControlRecycling's own build, from an empty cache that is thrown away
    public object? Build(object? data, object? existing, object? parent) => new ControlRecycling().Build(data, existing, parent);

    public void Clear()
    {
    }
}
