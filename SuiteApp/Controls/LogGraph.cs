using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace SuiteApp.Controls;

/// <summary>One line of the log viewer: a symbol's samples, seconds since the section's start.</summary>
public sealed class LogChannel
{
    public string Symbol { get; }
    public string Name { get; }
    public Color Color { get; }
    public double[] Time { get; }
    public double[] Value { get; }
    public double Min { get; }
    public double Max { get; }

    /// <param name="min">a fixed range (T5Suite's online graph), else the log viewer's from the values</param>
    public LogChannel(string symbol, string name, Color color, double[] time, double[] value, double? min = null, double? max = null)
    {
        (Symbol, Name, Color, Time, Value) = (symbol, name, color, time, value);
        // DetermineRange: 1.05 × the extremes, the minimum never above 0, never an empty range
        Max = max ?? value.DefaultIfEmpty(0).Max() * 1.05;
        Min = min ?? Math.Min(value.DefaultIfEmpty(0).Min(), 0) * 1.05;
        if (Max <= Min) Max = Min + 1;
    }
}

/// <summary>
/// RealtimeGraphControl: every channel scaled to its own range over the full height on black, time across, the legend on the
/// right (with the value under the cursor when less than 5 minutes are shown), the hovered line's scale on the left. Wheel
/// zooms around the mouse, drag pans, arrows pan / zoom.
/// </summary>
public class LogGraph : Control
{
    private const double Left = 70, Right = 160, Bottom = 36, Top = 8;

    private IReadOnlyList<LogChannel> m_channels = [];
    private DateTime m_start;
    private double m_span = 1;
    private double m_from, m_to = 1;
    private Point? m_mouse;
    private Point? m_drag;
    private LogChannel? m_hover;
    private int m_hoverIndex = -1;

    private static readonly IBrush Back = Brushes.Black;
    private static readonly IPen GridPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(100, 245, 222, 179)), 1);
    private static readonly Typeface Face = new("Inter");

    public LogGraph()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>The section to show, starting at its first 3 minutes when it is longer (_maxInitialMinutes).</summary>
    public void SetData(IReadOnlyList<LogChannel> channels, DateTime start)
    {
        m_channels = channels;
        m_start = start;
        m_span = Math.Max(channels.SelectMany(c => c.Time).DefaultIfEmpty(0).Max(), 1);
        m_from = 0;
        m_to = Math.Min(m_span, 180);
        InvalidateVisual();
    }

    public double From => m_from;
    public double To => m_to;

    private Rect Plot => new(Left, Top, Math.Max(1, Bounds.Width - Left - Right), Math.Max(1, Bounds.Height - Top - Bottom));

    private double X(double t, Rect p) => p.X + (t - m_from) / (m_to - m_from) * p.Width;
    private double TimeAt(double x, Rect p) => m_from + (x - p.X) / p.Width * (m_to - m_from);
    private static double Y(LogChannel c, double v, Rect p) => p.Bottom - (v - c.Min) / (c.Max - c.Min) * p.Height;

    private FormattedText Text(string s, IBrush brush, double size = 11) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush);

    /// <summary>Zooms the window by factor around time t, kept inside the log (1 shows everything, at most 500 ×).</summary>
    public void Zoom(double factor, double t)
    {
        double width = Math.Clamp((m_to - m_from) / factor, m_span / 500, m_span);
        double f = (t - m_from) / Math.Max(m_to - m_from, 1e-9);
        SetWindow(t - f * width, width);
    }

    public void Pan(double seconds) => SetWindow(m_from + seconds, m_to - m_from);

    private void SetWindow(double from, double width)
    {
        from = Math.Clamp(from, 0, Math.Max(0, m_span - width));
        (m_from, m_to) = (from, from + width);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect p = Plot;
        context.FillRectangle(Back, new Rect(Bounds.Size));
        if (m_channels.Count == 0) return;

        // time labels and vertical lines
        bool ms = m_to - m_from <= 10;
        string format = ms ? "dd/MM HH:mm:ss:fff" : "dd/MM HH:mm:ss";
        double labelWidth = Text(m_start.ToString(format), Brushes.Wheat).Width + 10;
        int labels = Math.Max(1, (int)(p.Width / labelWidth));
        for (int i = 0; i <= labels; i++)
        {
            double x = p.X + p.Width * i / labels, t = TimeAt(x, p);
            context.DrawLine(GridPen, new Point(x, p.Top), new Point(x, p.Bottom));
            var label = Text(m_start.AddSeconds(t).ToString(format), Brushes.Wheat);
            context.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Bounds.Width - label.Width), p.Bottom + 4));
        }
        int rows = Math.Max(1, (int)(p.Height / 40));
        for (int i = 0; i <= rows; i++)
        {
            double y = p.Y + p.Height * i / rows;
            context.DrawLine(GridPen, new Point(p.X, y), new Point(p.Right, y));
        }

        // the lines, the samples in the window plus one on each side
        using (context.PushClip(p))
        {
            foreach (LogChannel c in m_channels)
            {
                var pen = new Pen(new ImmutableSolidColorBrush(c.Color), c == m_hover ? 2 : 1);
                int first = Math.Max(0, Array.BinarySearch(c.Time, m_from) is var f && f < 0 ? ~f - 1 : f);
                Point? last = null;
                for (int i = first; i < c.Time.Length; i++)
                {
                    var pt = new Point(X(c.Time[i], p), Y(c, c.Value[i], p));
                    if (last is { } l) context.DrawLine(pen, l, pt);
                    last = pt;
                    if (c.Time[i] > m_to) break;
                }
            }
        }

        // the hovered line's scale (or the only line's)
        LogChannel? scaled = m_hover ?? (m_channels.Count == 1 ? m_channels[0] : null);
        if (scaled != null)
        {
            var brush = new ImmutableSolidColorBrush(scaled.Color);
            for (int i = 0; i <= rows; i++)
            {
                double v = scaled.Max - (scaled.Max - scaled.Min) * i / rows;
                var t = Text(v.ToString("F2"), brush);
                context.DrawText(t, new Point(Left - t.Width - 4, p.Y + p.Height * i / rows - t.Height / 2));
            }
        }

        // legend, with the values under the cursor when the window is short enough
        bool values = m_mouse is { } mouse && p.Contains(mouse) && m_to - m_from < 300;
        double cursor = values ? TimeAt(m_mouse!.Value.X, p) : 0;
        double ly = Top;
        foreach (LogChannel c in m_channels)
        {
            string text = c.Name;
            if (values && Nearest(c, cursor) is var n and >= 0) text += " " + c.Value[n].ToString("0.##");
            context.DrawText(Text(text, new ImmutableSolidColorBrush(c.Color)), new Point(p.Right + 8, ly));
            ly += 18;
        }

        if (m_hover != null && m_hoverIndex >= 0)
        {
            string s = $"{m_hover.Symbol}={m_hover.Value[m_hoverIndex]:F2} at {m_start.AddSeconds(m_hover.Time[m_hoverIndex]):dd/MM/yyyy HH:mm:ss:fff}";
            var t = Text(s, new ImmutableSolidColorBrush(m_hover.Color), 12);
            context.FillRectangle(Back, new Rect(p.X + 4, p.Y + 4, t.Width + 8, t.Height + 4));
            context.DrawText(t, new Point(p.X + 8, p.Y + 6));
        }
    }

    private static int Nearest(LogChannel c, double t)
    {
        if (c.Time.Length == 0) return -1;
        int i = Array.BinarySearch(c.Time, t);
        if (i >= 0) return i;
        i = ~i;
        if (i == 0) return 0;
        if (i >= c.Time.Length) return c.Time.Length - 1;
        return t - c.Time[i - 1] <= c.Time[i] - t ? i - 1 : i;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Rect p = Plot;
        Zoom(e.Delta.Y > 0 ? 1.25 : 0.8, TimeAt(e.GetPosition(this).X, p));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        Point pt = e.GetPosition(this);
        Rect p = Plot;
        // a click under the plot recentres there
        if (pt.Y > p.Bottom) SetWindow(TimeAt(pt.X, p) - (m_to - m_from) / 2, m_to - m_from);
        else m_drag = pt;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        m_drag = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        m_mouse = null;
        m_hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point pt = e.GetPosition(this);
        Rect p = Plot;
        if (m_drag is { } d)
        {
            Pan(-(pt.X - d.X) * (m_to - m_from) / p.Width);
            m_drag = pt;
        }
        m_mouse = pt;
        // a sample point within 3 px
        m_hover = null;
        m_hoverIndex = -1;
        double t = TimeAt(pt.X, p);
        foreach (LogChannel c in m_channels)
        {
            int n = Nearest(c, t);
            if (n < 0) continue;
            if (Math.Abs(X(c.Time[n], p) - pt.X) <= 3 && Math.Abs(Y(c, c.Value[n], p) - pt.Y) <= 3)
            {
                (m_hover, m_hoverIndex) = (c, n);
                break;
            }
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double width = m_to - m_from;
        switch (e.Key)
        {
            case Key.Left: Pan(-width / 10); break;
            case Key.Right: Pan(width / 10); break;
            case Key.Up: Zoom(1.25, (m_from + m_to) / 2); break;
            case Key.Down: Zoom(0.8, (m_from + m_to) / 2); break;
            default: return;
        }
        e.Handled = true;
    }
}
