using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using CommonSuite;
using MapControls;

namespace SuiteApp.Controls;

/// <summary>The airmass result's limiter colours (the legend's swatches).</summary>
public static class LimiterColors
{
    public static readonly IReadOnlyDictionary<AirmassLimitType, Color> All = new Dictionary<AirmassLimitType, Color>
    {
        [AirmassLimitType.TorqueLimiterEngine] = Colors.Yellow,
        [AirmassLimitType.AirmassLimiter] = Colors.Blue,
        [AirmassLimitType.TurboSpeedLimiter] = Colors.Black,
        [AirmassLimitType.TorqueLimiterEngineE85] = Colors.Purple,
        [AirmassLimitType.TorqueLimiterEngineE85Auto] = Colors.White,
        [AirmassLimitType.TorqueLimiterGear] = Colors.SaddleBrown,
        [AirmassLimitType.FuelCutLimiter] = Colors.DarkGray,
        [AirmassLimitType.OverBoostLimiter] = Colors.CornflowerBlue,
    };
}

/// <summary>
/// The airmass result table: rpm across, pedal position down with full pedal on top, cells coloured by airmass and a triangle in
/// the corner in the colour of the limiter that capped the cell.
/// </summary>
public class AirmassGrid : Control
{
    public static readonly StyledProperty<AirmassResult?> ResultProperty = AvaloniaProperty.Register<AirmassGrid, AirmassResult?>(nameof(Result));
    public static readonly StyledProperty<string[,]?> TextsProperty = AvaloniaProperty.Register<AirmassGrid, string[,]?>(nameof(Texts));

    public AirmassResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }

    /// <summary>What each cell shows ([pedal row, rpm column]); the airmass when null.</summary>
    public string[,]? Texts { get => GetValue(TextsProperty); set => SetValue(TextsProperty, value); }

    static AirmassGrid() => AffectsRender<AirmassGrid>(ResultProperty, TextsProperty);

    private const double Header = 56;

    private static FormattedText Text(string s, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, brush);

    public override void Render(DrawingContext context)
    {
        if (Result is not { } r || r.Rpm.Length == 0 || r.Pedal.Length == 0) return;
        int rows = r.Pedal.Length, cols = r.Rpm.Length;
        double cw = Math.Max(24, (Bounds.Width - Header) / cols), ch = Math.Max(16, (Bounds.Height - 24) / rows);
        int max = r.Airmass.Cast<int>().DefaultIfEmpty(0).Max(), min = r.Airmass.Cast<int>().DefaultIfEmpty(0).Min();
        IBrush fg = Brushes.Gray;
        for (int c = 0; c < cols; c++)
        {
            var t = Text(r.Rpm[c].ToString(), fg);
            context.DrawText(t, new Point(Header + c * cw + (cw - t.Width) / 2, 4));
        }
        var grid = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(60, 128, 128, 128)), 1);
        for (int display = 0; display < rows; display++)
        {
            int p = rows - 1 - display;
            double y = 24 + display * ch;
            var label = Text((r.Pedal[p] / 10).ToString(), Brushes.SteelBlue);
            context.DrawText(label, new Point(4, y + (ch - label.Height) / 2));
            for (int c = 0; c < cols; c++)
            {
                var cell = new Rect(Header + c * cw, y, cw, ch);
                context.FillRectangle(new ImmutableSolidColorBrush(HeatColor.Interpolate(min, max, r.Airmass[p, c])), cell);
                context.DrawRectangle(grid, cell);
                if (LimiterColors.All.TryGetValue(r.Limiter[p, c], out Color lc))
                {
                    var tri = new StreamGeometry();
                    using (var g = tri.Open())
                    {
                        g.BeginFigure(cell.TopRight, true);
                        double size = Math.Min(10, ch / 2);
                        g.LineTo(new Point(cell.Right - size, cell.Top));
                        g.LineTo(new Point(cell.Right, cell.Top + size));
                        g.EndFigure(true);
                    }
                    context.DrawGeometry(new ImmutableSolidColorBrush(lc), null, tri);
                }
                var text = Text(Texts?[p, c] ?? r.Airmass[p, c].ToString(), Brushes.Black);
                context.DrawText(text, new Point(cell.X + (cw - text.Width) / 2, cell.Y + (ch - text.Height) / 2));
            }
        }
    }
}

/// <summary>A line of the dyno graph.</summary>
public sealed record ChartSeries(string Name, Color Color, double?[] Values);

/// <summary>The dyno graph: series over evenly spaced rpm categories, one shared value axis, the legend boxed at the top right.</summary>
public class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<string>?> CategoriesProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<string>?>(nameof(Categories));
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public IReadOnlyList<string>? Categories { get => GetValue(CategoriesProperty); set => SetValue(CategoriesProperty, value); }
    public IReadOnlyList<ChartSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    static LineChart() => AffectsRender<LineChart>(CategoriesProperty, SeriesProperty);

    private static FormattedText Text(string s, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, brush);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (Categories is not { Count: > 0 } cats || Series is not { Count: > 0 } series) return;
        double top = series.SelectMany(s => s.Values).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(1).Max();
        // about a dozen steps, as Nevron's axis had (20 or 30 for a 300 Nm dyno)
        double step = NiceStep(Math.Max(top, 1) / 12);
        int steps = Math.Max(1, (int)Math.Ceiling(top / step));
        double max = steps * step;
        var boxPen = new Pen(Brushes.DarkGray, 1);
        var names = series.Select(s => Text(s.Name, Brushes.Black)).ToList();
        var legend = new Rect(Math.Round(Bounds.Width - names.Max(n => n.Width) - 50) + 0.5, 10.5, Math.Ceiling(names.Max(n => n.Width)) + 40, series.Count * 20 + 8);
        var plot = new Rect(50, 30, Math.Max(1, legend.Left - 70), Math.Max(1, Bounds.Height - 60));
        var gridPen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(50, 0, 0, 0)), 1);
        for (int i = 0; i <= steps; i++)
        {
            double y = plot.Bottom - plot.Height * i / steps;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Text((step * i).ToString("0"), Brushes.Black);
            context.DrawText(t, new Point(plot.Left - t.Width - 6, y - t.Height / 2));
        }
        double X(int i) => plot.Left + (cats.Count == 1 ? plot.Width / 2 : plot.Width * i / (cats.Count - 1));
        for (int i = 0; i < cats.Count; i++)
        {
            var t = Text(cats[i], Brushes.Black);
            context.DrawText(t, new Point(X(i) - t.Width / 2, plot.Bottom + 6));
        }
        context.DrawRectangle(Brushes.White, boxPen, legend);
        var labels = new List<(Point pt, double value, IBrush brush)>();
        for (int k = 0; k < series.Count; k++)
        {
            ChartSeries s = series[k];
            var brush = new ImmutableSolidColorBrush(s.Color);
            var pen = new Pen(brush, 2);
            Point? last = null;
            for (int i = 0; i < s.Values.Length && i < cats.Count; i++)
            {
                if (s.Values[i] is not { } v)
                {
                    last = null;
                    continue;
                }
                var pt = new Point(X(i), plot.Bottom - plot.Height * v / max);
                if (last is { } l) context.DrawLine(pen, l, pt);
                context.DrawEllipse(brush, null, pt, 3, 3);
                labels.Add((pt, v, brush));
                last = pt;
            }
            double ly = legend.Top + 6 + k * 20;
            context.DrawLine(pen, new Point(legend.Left + 8, ly + 12), new Point(legend.Left + 26, ly + 4));
            context.DrawText(names[k], new Point(legend.Left + 32, ly));
        }
        // every point's value in a box up and to the right, over all lines (the old suites' Nevron data labels)
        foreach (var (pt, value, brush) in labels)
        {
            var t = Text(value.ToString("0"), brush);
            // whole pixels plus a half: the 1 px box lines stay sharp
            var box = new Rect(Math.Round(pt.X + 6) + 0.5, Math.Round(pt.Y - 10 - t.Height) + 0.5, Math.Ceiling(t.Width) + 6, Math.Ceiling(t.Height) + 2);
            context.DrawLine(new Pen(brush, 1), pt, box.BottomLeft);
            context.DrawRectangle(Brushes.White, boxPen, box);
            context.DrawText(t, new Point(box.X + 3, box.Y + 1));
        }
    }

    /// <summary>The smallest 1, 2, 3 or 5 × 10ⁿ at least <paramref name="raw"/>.</summary>
    public static double NiceStep(double raw)
    {
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (double m in (double[])[1, 2, 3, 5])
            if (m * magnitude >= raw) return m * magnitude;
        return 10 * magnitude;
    }
}

/// <summary>The compressor map image with the operating points for the three ambient pressures drawn over it.</summary>
public class CompressorView : Control
{
    public static readonly StyledProperty<Bitmap?> ImageProperty = AvaloniaProperty.Register<CompressorView, Bitmap?>(nameof(Image));
    public static readonly StyledProperty<Compressor?> CompressorProperty = AvaloniaProperty.Register<CompressorView, Compressor?>(nameof(Compressor));
    public static readonly StyledProperty<List<(double lbmin, double pr)>[]?> PointsProperty =
        AvaloniaProperty.Register<CompressorView, List<(double lbmin, double pr)>[]?>(nameof(Points));

    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public Compressor? Compressor { get => GetValue(CompressorProperty); set => SetValue(CompressorProperty, value); }
    public List<(double lbmin, double pr)>[]? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    static CompressorView() => AffectsRender<CompressorView>(ImageProperty, CompressorProperty, PointsProperty);

    public CompressorView() => ClipToBounds = true;

    // nominal green, high altitude blue, high pressure red
    private static readonly (Color line, Color dot)[] CurveStyles = [(Colors.LimeGreen, Colors.Green), (Colors.CornflowerBlue, Colors.Blue), (Colors.IndianRed, Colors.Red)];

    public override void Render(DrawingContext context)
    {
        if (Image is not { } img || Compressor is not { } c) return;
        // the image fitted into the control, aspect kept
        double scale = Math.Min(Bounds.Width / img.Size.Width, Bounds.Height / img.Size.Height);
        var dest = new Rect(0, 0, img.Size.Width * scale, img.Size.Height * scale);
        context.DrawImage(img, dest);
        if (Points is not { } curves) return;
        for (int k = 0; k < curves.Length && k < CurveStyles.Length; k++)
        {
            var pen = new Pen(new ImmutableSolidColorBrush(CurveStyles[k].line), 3);
            var dot = new ImmutableSolidColorBrush(CurveStyles[k].dot);
            Point? last = null;
            foreach (var (lbmin, pr) in curves[k])
            {
                var pt = new Point((c.XOffset + lbmin * c.PixelsPerLbMin) * scale, (c.YOffset - (pr - 1) * c.PixelsPerPr) * scale);
                if (last is { } l) context.DrawLine(pen, l, pt);
                context.DrawEllipse(dot, null, pt, 4, 4);
                last = pt;
            }
        }
    }
}
