using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace SuiteApp.Controls;

/// <summary>
/// Dock's MDI panel measures every inner window at the whole workspace's size and then arranges it at its own, smaller size:
/// Grids inside kept the oversized measure as their column / row minimum and were laid out wider than the window (the title
/// pushing the window buttons out, clipped tables, an off-centre 3D graph). This one measures each window at no more than the
/// size it was last arranged at, and again when a move, resize or maximize arranges it at another size.
/// </summary>
public class ArrangedMdiLayoutPanel : MdiLayoutPanel
{
    private readonly Dictionary<Control, Size> m_measuredAt = [];
    private Size m_available;

    // the workspace's size until the window has been arranged, then no more than its arranged size
    private static Size Cap(Size available, Control child) => child.Bounds.Width > 0 && child.Bounds.Height > 0
        ? new(Math.Min(available.Width, child.Bounds.Width), Math.Min(available.Height, child.Bounds.Height))
        : available;

    protected override Size MeasureOverride(Size availableSize)
    {
        m_available = availableSize;
        foreach (Control gone in m_measuredAt.Keys.Except(Children).ToList()) m_measuredAt.Remove(gone);
        foreach (Control child in Children)
        {
            Size at = Cap(availableSize, child);
            if (m_measuredAt.TryGetValue(child, out Size before) && before != at)
            {
                // measured again at a new size but arranged at the same rect as before, a control skips its arrange and keeps
                // the old layout
                foreach (Layoutable l in child.GetVisualDescendants().OfType<Layoutable>()) l.InvalidateArrange();
            }
            m_measuredAt[child] = at;
            child.Measure(at);
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size size = base.ArrangeOverride(finalSize);
        if (Children.Any(c => m_measuredAt.TryGetValue(c, out Size at) && at != Cap(m_available, c))) InvalidateMeasure();
        return size;
    }
}
