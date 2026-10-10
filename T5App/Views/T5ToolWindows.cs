using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommonSuite;
using MapControls;
using SuiteApp.Controls;
using SuiteApp.Services;
using T5App.ViewModels;
using Trionic5Tools;

namespace T5App.Views;

/// <summary>T5Suite's dyno graph, compressor map and injection timing windows, built in code (a few controls each).</summary>
public static class T5ToolWindows
{
    private static StackPanel Bar(params Control[] items)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        foreach (Control c in items) bar.Children.Add(c);
        return bar;
    }

    private static Button Button(string caption, Action click)
    {
        var b = new Button { Content = caption, MinWidth = 80 };
        b.Click += (_, _) => click();
        return b;
    }

    private static Window Frame(string title, double width, double height, Control top, Control content)
    {
        var dock = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(top, Avalonia.Controls.Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(content);
        return new Window { Title = title, Width = width, Height = height, Content = dock, WindowStartupLocation = WindowStartupLocation.CenterOwner };
    }

    /// <summary>frmDynoChart "Estimated dyno results": torque, power and injector duty cycle over rpm; Refresh re-reads the file, Export saves a PNG.</summary>
    public static Window Dyno(Window owner, T5MainWindowViewModel vm)
    {
        var chart = new LineChart();
        void Fill()
        {
            var rows = vm.Dyno();
            chart.Categories = rows.Select(r => r.Rpm.ToString()).ToList();
            chart.Series =
            [
                new ChartSeries("Torque (Nm)", Colors.SteelBlue, rows.Select(r => (double?)Math.Round(r.Torque)).ToArray()),
                new ChartSeries("Power (bhp)", Colors.IndianRed, rows.Select(r => (double?)Math.Round(r.Power)).ToArray()),
                new ChartSeries("Injector DC (%)", Colors.ForestGreen, rows.Select(r => (double?)Math.Round(r.DutyCycle, 1)).ToArray()),
            ];
        }
        Fill();
        Window? window = null;
        var bar = Bar(Button("Refresh", Fill), Button("Export", async () =>
        {
            if (await Dialogs.SaveFile(window!, "PNG images", "png", "dyno.png") is not { } file) return;
            var size = new PixelSize(Math.Max(1, (int)chart.Bounds.Width), Math.Max(1, (int)chart.Bounds.Height));
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(chart);
            bitmap.Save(file);
        }), Button("Close", () => window!.Close()));
        window = Frame("Estimated dyno results", 900, 560, bar, chart);
        window.Show(owner);
        return window;
    }

    /// <summary>
    /// ctrlCompressorMapEx "Compressor map plotter": the WOT boost per rpm on the chosen compressor's map. ponytail: one VE for every
    /// rpm (T5Suite had 16 boxes, all 90 by default).
    /// </summary>
    public static Window Compressor(Window owner, T5MainWindowViewModel vm)
    {
        var (index, cid) = vm.CompressorDefaults();
        var turbo = new ComboBox { ItemsSource = CompressorMap.Compressors.Select(c => c.Name).ToList(), SelectedIndex = index, MinWidth = 280 };
        var temp = new NumericUpDown { Value = 20, Minimum = -40, Maximum = 80, Increment = 1, FormatString = "0", Width = 120 };
        var ve = new NumericUpDown { Value = 90, Minimum = 0, Maximum = 120, Increment = 1, FormatString = "0", Width = 120 };
        var view = new CompressorView();
        void Draw()
        {
            Compressor c = CompressorMap.Compressors[Math.Clamp(turbo.SelectedIndex, 0, CompressorMap.Compressors.Length - 1)];
            using (var stream = AssetLoader.Open(new Uri("avares://SuiteApp/Assets/Compressormaps/" + c.Image))) view.Image = new Bitmap(stream);
            view.Compressor = c;
            view.Points = [vm.CompressorPoints(cid, (double)(temp.Value ?? 20), Enumerable.Repeat((double)(ve.Value ?? 90), 16).ToList())];
        }
        turbo.SelectionChanged += (_, _) => Draw();
        temp.ValueChanged += (_, _) => Draw();
        ve.ValueChanged += (_, _) => Draw();
        Draw();
        Window? window = null;
        var bar = Bar(new TextBlock { Text = "Select your turbo", VerticalAlignment = VerticalAlignment.Center }, turbo,
            new TextBlock { Text = "Temp (C)", VerticalAlignment = VerticalAlignment.Center }, temp,
            new TextBlock { Text = "VE %", VerticalAlignment = VerticalAlignment.Center }, ve, Button("Refresh", Draw), Button("Close", () => window!.Close()));
        window = Frame("Compressor map plotter", 1000, 800, bar, view);
        window.Show(owner);
        return window;
    }

    /// <summary>
    /// frmInjectionTiming "Fuel injection timing": the injection time or duty cycle per cell of the fuel, idle or knock map at an
    /// intake temperature, battery voltage and injector constant. ponytail: the grid's colour scale instead of T5Suite's red cells and
    /// duty cycle bars.
    /// </summary>
    public static Window InjectionTiming(Window owner, T5MainWindowViewModel vm)
    {
        var temps = T5InjectionModel.Temperatures;
        var iat = new ComboBox { ItemsSource = temps.Select(t => t.Celsius + " °C").ToList(), SelectedIndex = 4 };
        var condition = new ComboBox { ItemsSource = T5MainWindowViewModel.InjectionConditions.Select(c => c.Caption).ToList(), SelectedIndex = 0 };
        var volt = new ComboBox { ItemsSource = Enumerable.Range(4, 12).Select(v => v + " V").ToList(), SelectedIndex = 9 };
        var konst = new NumericUpDown { Value = vm.InjectorConstant, Minimum = 1, Maximum = 255, Increment = 1, FormatString = "0", Width = 120 };
        var show = new ComboBox { ItemsSource = new List<string> { "Injection duration", "Injection duty cycle" }, SelectedIndex = 0 };
        var grid = new MapGrid { IsReadOnly = true, ViewType = MapViewType.Easy };
        void Update() => grid.Map = vm.InjectionTiming(T5MainWindowViewModel.InjectionConditions[Math.Max(0, condition.SelectedIndex)].Map,
            temps[Math.Max(0, iat.SelectedIndex)].Ad, 4 + Math.Max(0, volt.SelectedIndex), (int)(konst.Value ?? 21), show.SelectedIndex == 1);
        foreach (ComboBox box in new[] { iat, condition, volt, show }) box.SelectionChanged += (_, _) => Update();
        konst.ValueChanged += (_, _) => Update();
        Update();
        Window? window = null;
        var bar = Bar(new TextBlock { Text = "Intake Air Temperature", VerticalAlignment = VerticalAlignment.Center }, iat,
            new TextBlock { Text = "Condition", VerticalAlignment = VerticalAlignment.Center }, condition,
            new TextBlock { Text = "Battery voltage", VerticalAlignment = VerticalAlignment.Center }, volt,
            new TextBlock { Text = "Injector constant", VerticalAlignment = VerticalAlignment.Center }, konst,
            new TextBlock { Text = "Show value for", VerticalAlignment = VerticalAlignment.Center }, show, Button("Close", () => window!.Close()));
        window = Frame("Fuel injection timing", 1250, 640, bar, grid);
        window.Show(owner);
        return window;
    }
}
