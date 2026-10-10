using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
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

    /// <summary>
    /// frmKnockCounterMapSelect "Select a knock count map file...": file, snapshot time and total knocks; Ok shows one, Compare the
    /// difference of two.
    /// </summary>
    public static async Task KnockSnapshots(Window owner, T5MainWindowViewModel vm)
    {
        var snapshots = vm.KnockSnapshots();
        if (snapshots.Count == 0)
        {
            await Dialogs.Info(owner, "There are no knock counter snapshots yet (Settings → Knock counter snapshot after disconnect)", vm.Caption);
            return;
        }
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            ItemsSource = snapshots.Select(k => $"{System.IO.Path.GetFileName(k.File)}    {k.Time:yyyy-MM-dd HH:mm:ss}    {k.Knocks} knocks").ToList(),
            Height = 300,
        };
        Window? window = null;
        Button ok = Button("Ok", () => window!.Close(list.SelectedItems?.Count == 1 ? 1 : 0)), compare = Button("Compare", () => window!.Close(2));
        void Enable()
        {
            ok.IsEnabled = list.SelectedItems?.Count == 1;
            compare.IsEnabled = list.SelectedItems?.Count == 2;
        }
        list.SelectionChanged += (_, _) => Enable();
        Enable();
        var bar = Bar(ok, compare, Button("Cancel", () => window!.Close(0)));
        var body = new DockPanel();
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Bottom);
        body.Children.Add(bar);
        body.Children.Add(new Avalonia.Controls.Primitives.HeaderedContentControl { Header = "Select a knock counter file", Content = list });
        window = new Window { Title = "Select a knock count map file...", Width = 560, SizeToContent = SizeToContent.Height, Content = new Border { Padding = new Thickness(12), Child = body }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        int choice = await window.ShowDialog<int>(owner);
        var picked = list.Selection.SelectedIndexes.Select(i => snapshots[i].File).ToList();
        if (choice == 1 && picked.Count == 1) vm.ShowKnockMap(picked[0]);
        else if (choice == 2 && picked.Count == 2) vm.ShowKnockMap(picked[0], picked[1]);
    }

    private static readonly Avalonia.Data.Converters.FuncValueConverter<string?, IBrush?> LibraryBrush =
        new(library => library switch { "16 MHz" => Brushes.YellowGreen, "20 MHz" => Brushes.Orange, _ => null });

    /// <summary>A partnumber list row; Library is "16 MHz" / "20 MHz" for the stock bins in Binaries.</summary>
    public sealed record PartNumberRow(string Carmodel, string Enginetype, string Partnumber, string Power, string Torque, string Years, string Region, string Library);

    /// <summary>frmPartNumberList's rows: PartnumberCollection, the library bins checked for the 20 MHz code.</summary>
    public static List<PartNumberRow> PartNumbers() =>
        new PartnumberCollection().GeneratePartNumberCollection().Rows.Cast<System.Data.DataRow>().Select(r =>
        {
            string pn = r["Partnumber"]?.ToString() ?? "";
            string library = PartInfo.StockBinary(pn) is { } bin ? Trionic5File.Is20Mhz(bin) ? "20 MHz" : "16 MHz" : "";
            string from = r["FromMY"]?.ToString() ?? "", upto = r["UptoMY"]?.ToString() ?? "";
            return new PartNumberRow(r["Carmodel"]?.ToString() ?? "", r["Enginetype"]?.ToString() ?? "", pn, r["Power"]?.ToString() ?? "",
                r["Torque"]?.ToString() ?? "", from == upto ? from : $"{from}-{upto}", r["Region"]?.ToString() ?? "", library);
        }).ToList();

    /// <summary>
    /// frmPartNumberList "Partnumber list": every known partnumber grouped by car model and engine, the ones in the library coloured
    /// ("Available in library: 16 Mhz" yellow green, "20 Mhz" orange); double-click or Ok picks one.
    /// </summary>
    public static Task<string?> PartNumberList(Window owner)
    {
        var view = new Avalonia.Collections.DataGridCollectionView(PartNumbers());
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.Carmodel)));
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.Enginetype)));
        var grid = new DataGrid { ItemsSource = view, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Partnumber",
            SortMemberPath = nameof(PartNumberRow.Partnumber),
            // bound: the cell is made before its row is set
            CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PartNumberRow>((_, _) => new Border
            {
                Padding = new Thickness(6, 2),
                [!Border.BackgroundProperty] = new Avalonia.Data.Binding(nameof(PartNumberRow.Library)) { Converter = LibraryBrush },
                Child = new TextBlock { [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(PartNumberRow.Partnumber)) },
            }),
        });
        foreach (var (header, path) in new[] { ("Power", nameof(PartNumberRow.Power)), ("Torque", nameof(PartNumberRow.Torque)), ("MYs", nameof(PartNumberRow.Years)),
                     ("Region", nameof(PartNumberRow.Region)), ("Library", nameof(PartNumberRow.Library)) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Avalonia.Data.Binding(path) });
        Window? window = null;
        void Pick() => window!.Close((grid.SelectedItem as PartNumberRow)?.Partnumber);
        grid.DoubleTapped += (_, _) => Pick();
        var legend = new TextBlock { Text = "Available in library: 16 Mhz (yellow green), 20 Mhz (orange)", VerticalAlignment = VerticalAlignment.Center };
        var bar = Bar(Button("Ok", Pick), Button("Close", () => window!.Close(null)), legend);
        window = Frame("Partnumber list", 760, 640, bar, grid);
        return window.ShowDialog<string?>(owner);
    }

    /// <summary>
    /// frmUserLibrary "User library browser": the bins under the folders added, with T5Suite's guesses; Open selected, Compare to selected
    /// (with a file open), Clear library.
    /// </summary>
    public static async Task UserLibrary(Window owner, T5MainWindowViewModel vm)
    {
        var rows = new System.Collections.ObjectModel.ObservableCollection<T5UserLibrary.Row>(T5UserLibrary.Load());
        var grid = new DataGrid { ItemsSource = rows, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        foreach (var (header, path) in new[] { ("Filename", nameof(T5UserLibrary.Row.Name)), ("Engine type", nameof(T5UserLibrary.Row.EngineType)),
                     ("Stage", nameof(T5UserLibrary.Row.Stage)), ("Injectors", nameof(T5UserLibrary.Row.Injectors)), ("Mapsensor", nameof(T5UserLibrary.Row.Mapsensor)),
                     ("Torque", nameof(T5UserLibrary.Row.Torque)), ("E85", nameof(T5UserLibrary.Row.E85)), ("T7 BCV", nameof(T5UserLibrary.Row.T7Valve)),
                     ("Partnumber", nameof(T5UserLibrary.Row.Partnumber)), ("Software ID", nameof(T5UserLibrary.Row.SoftwareID)), ("CPU", nameof(T5UserLibrary.Row.Cpu)),
                     ("RAM locked", nameof(T5UserLibrary.Row.RamLocked)) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Avalonia.Data.Binding(path) });
        Window? window = null;
        string? action = null;
        async void Add()
        {
            var folders = await window!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select a folder with binaries" });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;
            vm.IsBusy = true;
            try
            {
                var all = await Task.Run(() => T5UserLibrary.AddFolder(rows.ToList(), folder));
                rows.Clear();
                foreach (var r in all) rows.Add(r);
                T5UserLibrary.Save(rows);
            }
            finally
            {
                vm.IsBusy = false;
            }
        }
        void Act(string what)
        {
            if (grid.SelectedItem is not T5UserLibrary.Row) return;
            action = what;
            window!.Close();
        }
        var compare = Button("Compare to selected", () => Act("compare"));
        compare.IsEnabled = vm.Binary != null;
        var bar = Bar(Button("Add files", Add), Button("Open selected", () => Act("open")), compare,
            Button("Clear library", () => { rows.Clear(); T5UserLibrary.Save(rows); }), Button("Close", () => window!.Close()));
        grid.DoubleTapped += (_, _) => Act("open");
        window = Frame("User library browser", 1200, 600, bar, grid);
        await window.ShowDialog(owner);
        if (grid.SelectedItem is not T5UserLibrary.Row row) return;
        if (action == "open") await vm.OpenFileAsync(row.File, true);
        else if (action == "compare") await vm.CompareToFileAsync(row.File);
    }
}
