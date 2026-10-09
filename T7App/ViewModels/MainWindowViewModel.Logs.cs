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
}
