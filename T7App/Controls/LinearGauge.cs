using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace T7App.Controls;

/// <summary>
/// ProGauges' LinearGauge as the realtime panel used it: a green-yellow to orange-red bar up to the value, the value under it,
/// and a red peak line that fades out over about 13 seconds.
/// </summary>
public class LinearGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<LinearGauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<LinearGauge, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<LinearGauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string> CaptionProperty = AvaloniaProperty.Register<LinearGauge, string>(nameof(Caption), "");
    public static readonly StyledProperty<string> FormatProperty = AvaloniaProperty.Register<LinearGauge, string>(nameof(Format), "F1");
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<LinearGauge, IBrush?>(nameof(Foreground), Brushes.Gray);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static readonly IBrush Bar = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Colors.GreenYellow), new ImmutableGradientStop(1, Colors.OrangeRed)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(1, 0, RelativeUnit.Relative));
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.FromArgb(40, 128, 128, 128));

    private readonly DispatcherTimer m_fade = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double m_peak = double.NaN;
    private int m_peakOpacity;

    static LinearGauge() => AffectsRender<LinearGauge>(ValueProperty, MinimumProperty, MaximumProperty, CaptionProperty, FormatProperty, ForegroundProperty);

    public LinearGauge()
    {
        m_fade.Tick += (_, _) =>
        {
            m_peakOpacity -= 1;
            if (m_peakOpacity <= 0)
            {
                m_peak = double.NaN;
                m_fade.Stop();
            }
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => m_fade.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && (double.IsNaN(m_peak) || Value >= m_peak))
        {
            m_peak = Value;
            m_peakOpacity = 255;
            m_fade.Start();
        }
    }

    private double X(double v, Rect bar) => bar.X + bar.Width * Math.Clamp((v - Minimum) / Math.Max(Maximum - Minimum, 1e-9), 0, 1);

    public override void Render(DrawingContext context)
    {
        var bar = new Rect(4, 4, Math.Max(0, Bounds.Width - 8), Math.Max(4, Bounds.Height * 0.45));
        context.FillRectangle(Track, bar);
        context.FillRectangle(Bar, bar.WithWidth(X(Value, bar) - bar.X));
        if (!double.IsNaN(m_peak))
        {
            double x = X(m_peak, bar);
            context.DrawLine(new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)m_peakOpacity, 255, 0, 0)), 3), new Point(x, bar.Top), new Point(x, bar.Bottom));
        }
        var text = new FormattedText($"{Caption} {Value.ToString(Format, CultureInfo.CurrentCulture)}", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 14, Foreground);
        context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, bar.Bottom + 4));
    }
}
