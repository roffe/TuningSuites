using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>
/// The Realtime menu both suites have: the panel and its commands, the logs' viewer, CSV and LogWorks exports, matrix and log
/// filters, Set symbol colors. The suite's RealtimeRules say its names and file types; its engine reads.
/// </summary>
public abstract partial class MainWindowViewModel
{
    /// <summary>The suite's realtime table: names, conversions, the logs' extension.</summary>
    public abstract RealtimeRules RealtimeRules { get; }

    /// <summary>The realtime panel over the suite's session.</summary>
    protected abstract RealtimeViewModel CreateRealtimePanel(SuiteBinary bin);

    /// <summary>"Trionic 7 logfiles": the logs' open dialogs.</summary>
    public string LogFilesName => RealtimeRules.LogFilesName;

    // ---- the panel ----

    /// <summary>The open realtime panel.</summary>
    public RealtimeViewModel? Realtime => Viewers.OfType<RealtimeViewModel>().FirstOrDefault();

    /// <summary>Toggle realtime panel [SHIFT+F1]: opens the panel and starts polling, or closes it; a panel opened to configure it starts.</summary>
    [RelayCommand]
    private async Task ToggleRealtimePanel()
    {
        if (Realtime is { HasStarted: false } configured)
        {
            _ = configured.StartAsync();
            return;
        }
        if (Realtime is { } open)
        {
            await CloseViewerAsync(open);
            return;
        }
        if (Binary is not { } bin) return;
        RealtimeViewModel panel = CreateRealtimePanel(bin);
        ShowDocument(panel);
        // not awaited: the command has to stay free to close the panel again
        _ = panel.StartAsync();
    }

    /// <summary>Configure realtime panel: the panel without connecting (T5Suite's Advanced actions); Toggle realtime panel then starts it.</summary>
    [RelayCommand]
    private void ConfigureRealtimePanel()
    {
        if (Realtime == null && Binary is { } bin) ShowDocument(CreateRealtimePanel(bin));
    }

    /// <summary>The Realtime menu's map buttons (ShowRealtimeMapFromECU): read from SRAM.</summary>
    [RelayCommand]
    private async Task ShowEcuMap(string name)
    {
        if (Binary is not { } bin) return;
        if (bin.FindAny(name) is not { } sh)
        {
            ShowInfo($"Symbol {name} does not exist in this file");
            return;
        }
        await OpenSramSymbolAsync(bin, sh);
    }

    /// <summary>Write log marker [F6].</summary>
    [RelayCommand]
    private void WriteLogMarker() => Realtime?.WriteLogMarkerCommand.Execute(null);

    /// <summary>Add to realtime list (symbol list): into the open panel, or into rtsymbols.txt for the next one.</summary>
    public void AddToRealtime(SymbolHelper sh)
    {
        RealtimeSymbol symbol = RealtimeRules.FromSymbol(sh);
        if (Realtime is { } panel)
        {
            panel.Add(symbol);
            return;
        }
        string file = Path.Combine(SettingsKey.Folder(Suite), "rtsymbols.txt");
        var rows = CommonSuite.Realtime.LoadLayout(file, Binary).Where(r => r.Name != symbol.Name).Append(symbol).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        CommonSuite.Realtime.SaveLayout(file, rows);
    }

    // ---- logs ----

    private LogFilters Filters => new(Registry);

    public LogFilterCollection LoadLogFilters() => Filters.GetFiltersFromRegistry();

    public void SaveLogFilters(LogFilterCollection filters) => Filters.SaveFiltersToRegistry(filters);

    /// <summary>Setup log filters offers the bin's non-calibration symbols, as the suites did.</summary>
    public IReadOnlyList<string> LogFilterSymbols =>
        Binary is { } bin ? bin.Symbols.Cast<SymbolHelper>().Select(s => s.SmartVarname).Where(n => !bin.IsCalibration(n)).Distinct().OrderBy(n => n).ToList() : [];

    /// <summary>Set symbol colors lists the bin's symbols that have an SRAM address, as the suites did.</summary>
    public SymbolColorsViewModel SymbolColorChoices() =>
        new(Binary?.Symbols.Cast<SymbolHelper>().Where(s => s.Start_address > 0).Select(s => s.SmartVarname).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() ?? [], Registry);

    private List<RealtimeLogLine> FilteredLines(string file)
    {
        LogFilter[] filters = LoadLogFilters().Cast<LogFilter>().ToArray();
        return RealtimeLog.Read(file).Where(l => RealtimeLog.Passes(l, filters)).ToList();
    }

    /// <summary>
    /// Load trionic 7 / 8 logfile: the lines through the log filters, split at gaps of 10 s or more; with several sections the
    /// chosen one (null cancels). The viewer opens as a tab.
    /// </summary>
    public async Task OpenLogAsync(string file, Func<IReadOnlyList<string>, Task<int?>> chooseSection)
    {
        List<List<RealtimeLogLine>> sections = RealtimeLog.Sections(FilteredLines(file));
        if (sections.Count == 0)
        {
            ShowInfo("No data was found in " + Path.GetFileName(file));
            return;
        }
        int index = 0;
        if (sections.Count > 1)
        {
            if (await chooseSection(sections.Select(RealtimeLog.Describe).ToList()) is not { } chosen) return;
            index = chosen;
        }
        ShowDocument(new LogViewerViewModel(file, sections[index], Registry));
    }

    public LogSelectionViewModel LogSelection(string file) => new(FilteredLines(file));

    /// <summary>Export logfile to CSV: &lt;log&gt;.csv with the chosen symbols in the chosen time range (the suites exported every symbol).</summary>
    public void ExportLogCsv(string file, LogSelectionViewModel selection)
    {
        var lines = FilteredLines(file).Where(l => l.Time >= selection.FromTime && l.Time <= selection.ToTime).ToList();
        if (lines.Count == 0)
        {
            ShowInfo("No data was found to export!");
            return;
        }
        string target = Path.ChangeExtension(file, ".csv");
        RealtimeLog.ExportCsv(lines, selection.Selected, target);
        ShowInfo("Exported to " + target);
    }

    /// <summary>
    /// Export logfile to LogWorks: &lt;log&gt;.dif through the suites' DifGenerator, colours from the symbol colours. LogWorks
    /// isn't started; open the file in it.
    /// </summary>
    public void ExportLogDif(string file, LogSelectionViewModel selection)
    {
        var symbols = new SymbolCollection();
        var colors = new SymbolColors(Registry);
        foreach (string name in selection.Selected)
            symbols.Add(new SymbolHelper { Varname = name, Color = colors.GetColorFromRegistry(name) });
        var dif = new DifGenerator { AppSettings = Settings, WidebandSymbol = RealtimeRules.LogWorksWidebandSymbol(Settings), UseWidebandInput = false };
        dif.SetFilters(LoadLogFilters());
        bool interpolate = Settings.InterpolateLogWorksTimescale;
        if (dif.ConvertFileToDif(file, symbols, selection.FromTime, selection.ToTime, interpolate, interpolate))
            ShowInfo("Exported to " + Path.ChangeExtension(file, ".dif"));
        else ShowInfo("No data was found to export!");
    }

    /// <summary>View matrix from logfile: the symbols for the dialog, with the last x / y / z when this log has them.</summary>
    public (List<RealtimeLogLine> lines, MatrixSelectionViewModel selection) MatrixSelection(string file)
    {
        List<RealtimeLogLine> lines = RealtimeLog.Read(file);
        List<string> symbols = RealtimeLog.Symbols(lines);
        string? Last(string s) => symbols.Contains(s) ? s : null;
        return (lines, new MatrixSelectionViewModel(symbols)
        {
            X = Last(Settings.LastXAxisFromMatrix), Y = Last(Settings.LastYAxisFromMatrix), Z = Last(Settings.LastZAxisFromMatrix),
        });
    }

    /// <summary>
    /// The matrix in a read-only map viewer "Matrix [x : y : z] (Mean values)": table and 3D surface, y ascending upwards.
    /// Values are stored 16-bit with a power-of-ten factor that fits the largest one; empty cells show 0.
    /// </summary>
    public void ShowMatrix(List<RealtimeLogLine> lines, MatrixSelectionViewModel s)
    {
        if (Binary is not { } bin || !s.IsComplete) return;
        Settings.LastXAxisFromMatrix = s.X!;
        Settings.LastYAxisFromMatrix = s.Y!;
        Settings.LastZAxisFromMatrix = s.Z!;
        if (LogMatrix.Build(lines, s.X!, s.Y!, s.Z!, (MatrixMode)s.Mode) is not { } m)
        {
            ShowInfo("No data to display ... x or y axis contains no differentiated values");
            return;
        }
        double max = m.Values.Cast<double>().Where(v => !double.IsNaN(v)).Select(Math.Abs).DefaultIfEmpty(1).Max();
        double factor = Math.Pow(10, Math.Ceiling(Math.Log10(Math.Max(max, 1e-9) / 30000)));
        var data = new byte[LogMatrix.Size * LogMatrix.Size * 2];
        for (int r = 0; r < LogMatrix.Size; r++)
            for (int c = 0; c < LogMatrix.Size; c++)
            {
                double v = m.Values[r, c];
                int raw = double.IsNaN(v) ? 0 : (int)Math.Round(v / factor);
                int i = (r * LogMatrix.Size + c) * 2;
                data[i] = (byte)(raw >> 8);
                data[i + 1] = (byte)raw;
            }
        string title = $"Matrix [{s.X} : {s.Y} : {s.Z}] ({s.Modes[s.Mode]})";
        var map = new MapControls.MapData(title, data, LogMatrix.Size, true)
        {
            Factor = factor, UpsideDown = true, XAxis = m.X.Select(v => Math.Round(v, 2)).ToArray(), YAxis = m.Y.Select(v => Math.Round(v, 2)).ToArray(), XName = s.X!, YName = s.Y!, ZName = s.Z!,
        };
        ShowDocument(new MapViewerViewModel
        {
            Owner = this, Binary = bin, Symbol = new SymbolHelper { Varname = title }, MapName = title, Map = map, Address = -1,
            TitleOverride = title, IsReadOnly = true, NoEcu = true, ViewType = MapControls.MapViewType.Easy, GraphVisible = true,
        });
    }
}
