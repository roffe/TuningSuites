using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using CommonSuite;
using MapControls;
using T7;

namespace MapControlsDemo;

/// <summary>dotnet run --project MapControlsDemo [file.bin]: a few maps of a T7 bin in the map controls.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<DemoApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
}

public class DemoApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string bin = desktop.Args is [var a, ..] ? a : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "T7Binaries", "5168646.bin");
            desktop.MainWindow = new DemoWindow(Path.GetFullPath(bin));
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public class DemoWindow : Window
{
    // name, columns, 16-bit: what frmMain's width / sixteen-bit tables say for these
    private static readonly (string name, int cols, bool sixteen)[] Maps =
    [
        ("IgnNormCal.Map", 18, true),
        ("BFuelCal.Map", 18, false),
        ("TorqueCal.M_NominalMap", 18, true),
    ];

    private readonly string m_file;
    private readonly SymbolCollection m_symbols;
    private readonly MapGrid m_grid = new();
    private readonly Surface3D m_surface = new();
    private readonly Graph2D m_graph = new();
    private readonly Slider m_slice = new() { Minimum = 0, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private MapData? m_map;

    public DemoWindow(string file)
    {
        // a copy, ExtractFile writes <bin>.xml next to the file it parses
        m_file = Path.Combine(Directory.CreateTempSubdirectory("mapdemo").FullName, Path.GetFileName(file));
        File.Copy(file, m_file);
        m_symbols = new Trionic7File().ExtractFile(m_file, 0, "");
        Title = "Map controls: " + Path.GetFileName(file);
        Width = 1200;
        Height = 900;

        var maps = new ComboBox { ItemsSource = Maps.Select(m => m.name).ToList(), SelectedIndex = 0 };
        var view = new ComboBox { ItemsSource = Enum.GetNames<MapViewType>(), SelectedIndex = (int)MapViewType.Easy };
        var online = new CheckBox { Content = "Online" };
        var redWhite = new CheckBox { Content = "Red-white" };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        maps.SelectionChanged += (_, _) => Load(Maps[maps.SelectedIndex]);
        view.SelectionChanged += (_, _) => m_grid.ViewType = (MapViewType)view.SelectedIndex;
        online.IsCheckedChanged += (_, _) => m_grid.OnlineMode = m_surface.OnlineMode = m_graph.OnlineMode = online.IsChecked == true;
        redWhite.IsCheckedChanged += (_, _) => m_grid.IsRedWhite = redWhite.IsChecked == true;
        m_grid.OpenLoopMark = OpenLoopMark.Corner;
        m_grid.EditRejected += (_, msg) => status.Text = msg;
        m_grid.SelectionChanged += (_, _) => status.Text = $"{m_grid.SelectedCells.Count} selected";
        m_slice.ValueChanged += (_, _) => ShowSlice();

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        bar.Children.AddRange([maps, view, online, redWhite, status]);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "3D", Content = m_surface });
        tabs.Items.Add(new TabItem { Header = "2D", Content = new DockPanel { Children = { m_slice, m_graph } } });
        DockPanel.SetDock(m_slice, Dock.Bottom);
        var split = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
        split.Children.Add(m_grid);
        var splitter = new GridSplitter();
        Grid.SetRow(splitter, 1);
        split.Children.Add(splitter);
        Grid.SetRow(tabs, 2);
        split.Children.Add(tabs);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.AddRange([bar, split]);
        Content = root;
        Load(Maps[0]);
    }

    private SymbolHelper? Symbol(string name) => m_symbols.Cast<SymbolHelper>().FirstOrDefault(s => s.Varname == name || s.Userdescription == name);

    private byte[] Read(SymbolHelper sh) => Trionic7File.readdatafromfile(m_file, (int)sh.Flash_start_address, sh.Length);

    // frmMain.GetMapCorrectionFactor: "Resolution is X" in the symbol's help text, 1 otherwise
    private static double Factor(string name)
    {
        Match m = Regex.Match(SymbolTranslator.ToHelpText(name, 0), @"Resolution is\s+([0-9.,]+)");
        return m.Success && double.TryParse(m.Groups[1].Value.TrimEnd('.').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) && f != 0 ? f : 1;
    }

    private double[]? Axis(string name, bool signed)
    {
        if (Symbol(name) is not { } sh) return null;
        byte[] b = Read(sh);
        double f = Factor(name);
        return Enumerable.Range(0, b.Length / 2).Select(i =>
        {
            int v = (int)((b[i * 2] << 8 | b[i * 2 + 1]) * f);
            return (double)(signed && v > 0x8000 ? v - 0x10000 : v);
        }).ToArray();
    }

    private void Load((string name, int cols, bool sixteen) def)
    {
        if (Symbol(def.name) is not { } sh) return;
        new SymbolAxesTranslator().GetAxisSymbols(def.name, out string x, out string y, out string xd, out string yd, out string zd);
        m_map = new MapData(def.name, Read(sh), def.cols, def.sixteen)
        {
            Factor = Factor(def.name), XAxis = Axis(x, false), YAxis = Axis(y, true), XName = xd, YName = yd, ZName = zd,
        };
        if (xd.Equals("mg/c", StringComparison.OrdinalIgnoreCase) && yd.Equals("rpm", StringComparison.OrdinalIgnoreCase))
            m_map.OpenLoop = Axis("LambdaCal.MaxLoadNormTab", false);
        m_map.Changed += (_, _) => { m_surface.SetValues(m_map.PhysicalValues()); ShowSlice(); };
        m_grid.Map = m_map;
        m_surface.SetData(m_map.PhysicalValues(), m_map.Cols, m_map.Rows, m_map.XAxis, m_map.YAxis, xd, yd, zd, 0, 0, 1);
        m_slice.Maximum = m_map.Cols - 1;
        ShowSlice();
    }

    // T7Suite's 2D chart: one column (the slider) over the Y axis
    private void ShowSlice()
    {
        if (m_map == null) return;
        int col = (int)m_slice.Value;
        double[] all = m_map.PhysicalValues();
        double[] slice = Enumerable.Range(0, m_map.Rows).Select(r => r * m_map.Cols + col).Where(i => i < all.Length).Select(i => all[i]).ToArray();
        m_graph.SetData(slice, m_map.YAxis, all.Min(), all.Max(), 0, 1, $"{m_map.YName} @ {m_map.XName} {m_map.XAxis?.ElementAtOrDefault(col)}");
    }
}
