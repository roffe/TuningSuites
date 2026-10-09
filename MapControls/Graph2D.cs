using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace MapControls;

/// <summary>
/// T7Suite-style 2D graph of one map row/column, ported from txlogger's graph2d: a line with one marker per cell, value
/// callouts above the markers (staggered on two levels, then thinned, when they would overlap), "nice" y ticks with
/// alternating bands and the axis values along the bottom.
/// </summary>
public class Graph2D : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Graph2D>();

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private const double TickTextSize = 12, CalloutTextSize = 11, MarkerRadius = 4;
    private const double PadRight = 12, AxisGapX = 6, CalloutPadX = 4, CalloutPadY = 2;
    private const int MaxYTicks = 8;

    private static readonly IBrush PlotBg = Brushes.White;
    private static readonly IBrush Band = new ImmutableSolidColorBrush(Color.FromRgb(0xEF, 0xED, 0xD8));
    private static readonly IPen GridPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xC9)), 1);
    private static readonly IPen LinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xD8, 0x40, 0x28)), 2);
    private static readonly IPen MarkerPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x8A, 0x22, 0x12)), 1.5);
    private static readonly IPen CalloutPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), 1);
    private static readonly IPen CursorPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(180, 165, 55, 253)), 3);

    private double[] m_values = [];
    private double[] m_axis = [];
    private int m_axisPrec, m_valuePrec;
    private string m_axisLabel = "";
    private double m_min, m_max;
    private double m_cursor = -1;

    static Graph2D()
    {
        AffectsRender<Graph2D>(ForegroundProperty);
    }

    /// <summary>axis may be null, cells are numbered then. min/max set the colour scale (the whole map's range).</summary>
    public void SetData(double[] values, double[]? axis, double min, double max, int axisPrecision, int valuePrecision, string axisLabel = "")
    {
        m_values = values;
        if (axis != null && axis.Length == values.Length)
        {
            m_axis = axis;
        }
        else
        {
            m_axis = new double[values.Length];
            for (int i = 0; i < m_axis.Length; i++) m_axis[i] = i;
        }
        m_min = min;
        m_max = max;
        m_axisPrec = axisPrecision;
        m_valuePrec = valuePrecision;
        m_axisLabel = axisLabel;
        InvalidateVisual();
    }

    /// <summary>T7Suite's online palette for the markers.</summary>
    public bool OnlineMode
    {
        get => m_online;
        set { m_online = value; InvalidateVisual(); }
    }

    private bool m_online;

    public void SetCursor(double idx)
    {
        m_cursor = m_values.Length == 0 ? -1 : Math.Clamp(idx, 0, m_values.Length - 1);
        InvalidateVisual();
    }

    public void HideCursor()
    {
        m_cursor = -1;
        InvalidateVisual();
    }

    private static FormattedText Text(string s, double size, IBrush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    public override void Render(DrawingContext context)
    {
        int n = m_values.Length;
        Size size = Bounds.Size;
        if (n == 0 || size.Width <= 0 || size.Height <= 0) return;
        IBrush fg = Foreground ?? Brushes.Black;

        // callouts measured first so headroom can be reserved above the plot
        var callouts = new FormattedText[n];
        double maxCalloutW = 0, calloutH = 0;
        for (int i = 0; i < n; i++)
        {
            callouts[i] = Text(m_values[i].ToString("F" + m_valuePrec, CultureInfo.InvariantCulture), CalloutTextSize, Brushes.Black);
            maxCalloutW = Math.Max(maxCalloutW, callouts[i].Width + 2 * CalloutPadX);
            calloutH = Math.Max(calloutH, callouts[i].Height + 2 * CalloutPadY);
        }

        double padTop = 2 * (calloutH + 2) + MarkerRadius + 4;
        double xLabelH = Text("0", TickTextSize, fg).Height;
        double padBottom = xLabelH + 8 + (m_axisLabel != "" ? xLabelH + 2 : 0);
        double plotH = Math.Max(24, size.Height - padTop - padBottom);
        double plotTop = padTop, plotBottom = plotTop + plotH;

        // y scale with nice tick steps, extended to tick boundaries
        int maxTicks = Math.Clamp((int)(plotH / 36), 2, MaxYTicks);
        double rng = m_max - m_min;
        if (rng <= 0) rng = Math.Abs(m_max) == 0 ? 1 : Math.Abs(m_max);
        double step = NiceStep(rng, maxTicks);
        double yStart = Math.Floor(m_min / step) * step, yEnd = Math.Ceiling(m_max / step) * step;
        if (yEnd - yStart < step) yEnd = yStart + step;
        int ticks = (int)Math.Round((yEnd - yStart) / step) + 1;
        int decimals = Math.Max(0, -(int)Math.Floor(Math.Log10(step)));

        var tickTexts = new FormattedText[ticks];
        double maxYLabelW = 0;
        for (int i = 0; i < ticks; i++)
        {
            tickTexts[i] = Text((yStart + i * step).ToString("F" + decimals, CultureInfo.InvariantCulture), TickTextSize, fg);
            maxYLabelW = Math.Max(maxYLabelW, tickTexts[i].Width);
        }
        double padLeft = maxYLabelW + AxisGapX + 4;
        double plotW = Math.Max(10, size.Width - padLeft - PadRight);
        double scale = plotH / (yEnd - yStart);
        double YFor(double v) => plotBottom - (v - yStart) * scale;

        context.FillRectangle(PlotBg, new Rect(padLeft, plotTop, plotW, plotH));
        for (int i = 0; i < ticks; i++)
        {
            double y = YFor(yStart + i * step);
            // alternating band between this gridline and the one above it
            if (i + 1 < ticks && i % 2 == 0)
            {
                double yAbove = YFor(yStart + (i + 1) * step);
                context.FillRectangle(Band, new Rect(padLeft, yAbove, plotW, y - yAbove));
            }
            context.DrawLine(GridPen, new Point(padLeft, y), new Point(padLeft + plotW, y));
            context.DrawText(tickTexts[i], new Point(padLeft - AxisGapX - tickTexts[i].Width, y - tickTexts[i].Height / 2));
        }

        double xStep = plotW / n;
        double Cx(int i) => padLeft + (i + 0.5) * xStep;

        if (m_cursor >= 0)
        {
            double x = padLeft + (m_cursor + 0.5) * xStep;
            context.DrawLine(CursorPen, new Point(x, plotTop), new Point(x, plotBottom));
        }

        for (int i = 0; i < n - 1; i++)
            context.DrawLine(LinePen, new Point(Cx(i), YFor(m_values[i])), new Point(Cx(i + 1), YFor(m_values[i + 1])));
        for (int i = 0; i < n; i++)
        {
            var fill = new SolidColorBrush(HeatColor.Interpolate(m_min, m_max, m_values[i], m_online));
            context.DrawEllipse(fill, MarkerPen, new Point(Cx(i), YFor(m_values[i])), MarkerRadius, MarkerRadius);
        }

        // stagger the callouts on two levels when they would overlap, skip some when even that is not enough
        int levels = maxCalloutW + 4 > xStep ? 2 : 1;
        int skip = Math.Max(1, (int)Math.Ceiling((maxCalloutW + 4) / (xStep * levels)));
        int shown = 0;
        for (int i = 0; i < n; i += skip)
        {
            int level = levels == 2 ? shown % 2 : 0;
            shown++;
            double w = callouts[i].Width + 2 * CalloutPadX;
            double bottom = YFor(m_values[i]) - MarkerRadius - 3 - level * (calloutH + 2);
            double x = Math.Clamp(Cx(i) - w / 2, padLeft + 1, Math.Max(padLeft + 1, padLeft + plotW - 1 - w));
            var box = new Rect(x, bottom - calloutH, w, calloutH);
            context.DrawRectangle(PlotBg, CalloutPen, box, 2, 2);
            context.DrawText(callouts[i], new Point(x + (w - callouts[i].Width) / 2, bottom - calloutH + CalloutPadY));
        }

        var xTexts = new FormattedText[n];
        double maxXLabelW = 0;
        for (int i = 0; i < n; i++)
        {
            xTexts[i] = Text(m_axis[i].ToString("F" + m_axisPrec, CultureInfo.InvariantCulture), TickTextSize, fg);
            maxXLabelW = Math.Max(maxXLabelW, xTexts[i].Width);
        }
        int labelSkip = maxXLabelW + 6 > xStep ? (int)Math.Ceiling((maxXLabelW + 6) / xStep) : 1;
        for (int i = 0; i < n; i += labelSkip)
        {
            double x = Math.Clamp(Cx(i) - xTexts[i].Width / 2, 0, Math.Max(0, size.Width - xTexts[i].Width));
            context.DrawText(xTexts[i], new Point(x, plotBottom + 4));
        }
        if (m_axisLabel != "")
        {
            var label = Text(m_axisLabel, TickTextSize, fg);
            context.DrawText(label, new Point(padLeft + (plotW - label.Width) / 2, plotBottom + 4 + xLabelH + 2));
        }
    }

    /// <summary>1/2/5·10^n step covering the range in at most maxTicks intervals.</summary>
    internal static double NiceStep(double rng, int maxTicks)
    {
        double raw = rng / Math.Max(1, maxTicks);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        return norm <= 1 ? mag : norm <= 2 ? 2 * mag : norm <= 5 ? 5 * mag : 10 * mag;
    }
}
