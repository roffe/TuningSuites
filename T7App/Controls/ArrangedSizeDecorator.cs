using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace T7App.Controls;

/// <summary>
/// Hosts a document inside Dock's MDI panel, which measures every inner window at the whole workspace's size and then arranges
/// it at its own, smaller size: Grids inside kept the oversized measure as their column / row minimum and were laid out wider
/// and taller than the window (clipped content, an off-centre 3D graph). This measures the child at no more than the size it
/// was last arranged at, the same fix MapViewer carries for itself.
/// </summary>
public class ArrangedSizeDecorator : Decorator
{
    private Size m_available, m_arranged = new(double.PositiveInfinity, double.PositiveInfinity), m_measuredAt;

    protected override Size MeasureOverride(Size availableSize)
    {
        m_available = availableSize;
        Size cap = Cap(availableSize);
        if (cap != m_measuredAt)
        {
            // measured again at a new size but arranged at the same rect as before, a control skips its arrange and keeps
            // the old layout
            foreach (Layoutable l in this.GetVisualDescendants().OfType<Layoutable>()) l.InvalidateArrange();
            m_measuredAt = cap;
        }
        return base.MeasureOverride(cap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        m_arranged = finalSize;
        if (Cap(m_available) != m_measuredAt) InvalidateMeasure();
        return base.ArrangeOverride(finalSize);
    }

    // only finite constraints are capped
    private Size Cap(Size available) => new(
        double.IsInfinity(available.Width) ? available.Width : Math.Min(available.Width, m_arranged.Width),
        double.IsInfinity(available.Height) ? available.Height : Math.Min(available.Height, m_arranged.Height));
}
