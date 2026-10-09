using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using T7;

namespace T7App.ViewModels;

/// <summary>.t7l logs: the viewer, the CSV and LogWorks exports and the log filters (frmMain's Realtime ribbon).</summary>
public partial class MainWindowViewModel
{
    private LogFilters Filters => new(new T7SuiteRegistry());

    public LogFilterCollection LoadLogFilters() => Filters.GetFiltersFromRegistry();

    public void SaveLogFilters(LogFilterCollection filters) => Filters.SaveFiltersToRegistry(filters);

    /// <summary>Setup log filters offers the bin's non-calibration symbols, as T7Suite did.</summary>
    public IReadOnlyList<string> LogFilterSymbols =>
        Binary?.Symbols.Cast<SymbolHelper>().Select(s => s.SmartVarname).Where(n => !T7Compare.IsCalibration(n)).Distinct().OrderBy(n => n).ToList() ?? [];

    private List<T7LogLine> FilteredLines(string file)
    {
        LogFilter[] filters = LoadLogFilters().Cast<LogFilter>().ToArray();
        return T7LogFile.Read(file).Where(l => T7LogFile.Passes(l, filters)).ToList();
    }

    /// <summary>
    /// Load trionic 7 logfile: the lines through the log filters, split at gaps of 10 s or more; with several sections the
    /// chosen one (null cancels). The viewer opens as a tab.
    /// </summary>
    public async Task OpenLogAsync(string file, Func<IReadOnlyList<string>, Task<int?>> chooseSection)
    {
        List<List<T7LogLine>> sections = T7LogFile.Sections(FilteredLines(file));
        if (sections.Count == 0)
        {
            ShowInfo("No data was found in " + Path.GetFileName(file));
            return;
        }
        int index = 0;
        if (sections.Count > 1)
        {
            if (await chooseSection(sections.Select(T7LogFile.Describe).ToList()) is not { } chosen) return;
            index = chosen;
        }
        ShowDocument(new LogViewerViewModel(file, sections[index]));
    }

    public LogSelectionViewModel LogSelection(string file) => new(FilteredLines(file));

    /// <summary>Export logfile to CSV: &lt;log&gt;.csv with the chosen symbols in the chosen time range (T7Suite exported every symbol).</summary>
    public void ExportLogCsv(string file, LogSelectionViewModel selection)
    {
        var lines = FilteredLines(file).Where(l => l.Time >= selection.FromTime && l.Time <= selection.ToTime).ToList();
        if (lines.Count == 0)
        {
            ShowInfo("No data was found to export!");
            return;
        }
        string target = Path.ChangeExtension(file, ".csv");
        T7LogFile.ExportCsv(lines, selection.Selected, target);
        ShowInfo("Exported to " + target);
    }

    /// <summary>
    /// Export logfile to LogWorks: &lt;log&gt;.dif through T7Suite's DifGenerator, colours from the symbol colours. LogWorks
    /// isn't started; open the file in it.
    /// </summary>
    public void ExportLogDif(string file, LogSelectionViewModel selection)
    {
        var symbols = new SymbolCollection();
        var colors = new SymbolColors(new T7SuiteRegistry());
        foreach (string name in selection.Selected)
            symbols.Add(new SymbolHelper { Varname = name, Color = colors.GetColorFromRegistry(name) });
        var dif = new DifGenerator { AppSettings = Settings, WidebandSymbol = Settings.WideBandSymbol, UseWidebandInput = false };
        dif.SetFilters(LoadLogFilters());
        bool interpolate = Settings.InterpolateLogWorksTimescale;
        if (dif.ConvertFileToDif(file, symbols, selection.FromTime, selection.ToTime, interpolate, interpolate))
            ShowInfo("Exported to " + Path.ChangeExtension(file, ".dif"));
        else ShowInfo("No data was found to export!");
    }

    /// <summary>View matrix from logfile: the symbols for the dialog, with the last x / y / z when this log has them.</summary>
    public (List<T7LogLine> lines, MatrixSelectionViewModel selection) MatrixSelection(string file)
    {
        List<T7LogLine> lines = T7LogFile.Read(file);
        List<string> symbols = T7LogFile.Symbols(lines);
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
    public void ShowMatrix(List<T7LogLine> lines, MatrixSelectionViewModel s)
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
