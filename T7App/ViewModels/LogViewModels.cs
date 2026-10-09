using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;
using T7App.Controls;

namespace T7App.ViewModels;

/// <summary>A .t7l section in the log viewer ("CANBus logfile: name").</summary>
public class LogViewerViewModel : DocumentViewModel
{
    public string FileName { get; }
    public IReadOnlyList<LogChannel> Channels { get; }
    public DateTime Start { get; }

    public override string Title => "CANBus logfile: " + Path.GetFileName(FileName);

    public LogViewerViewModel(string file, IReadOnlyList<T7LogLine> section)
    {
        FileName = file;
        Start = section.Count > 0 ? section[0].Time : DateTime.MinValue;
        var colors = new SymbolColors(new T7SuiteRegistry());
        Channels = T7LogFile.Symbols(section).Select(name =>
        {
            var points = section.Select(l => (l.Time, v: l[name])).Where(p => p.v != null).ToList();
            System.Drawing.Color c = colors.GetColorFromRegistry(name);
            // black (no colour stored) shows as white
            var color = c.ToArgb() == System.Drawing.Color.Black.ToArgb() || c.A == 0 ? Avalonia.Media.Colors.White : Avalonia.Media.Color.FromRgb(c.R, c.G, c.B);
            return new LogChannel(name, T7LogFile.DisplayName(name), color,
                points.Select(p => (p.Time - Start).TotalSeconds).ToArray(), points.Select(p => p.v!.Value).ToArray());
        }).ToList();
    }
}

public partial class LogSymbolChoice(string name, bool selected) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _selected = selected;
}

/// <summary>frmPlotSelection: the symbols to export and the time range ("Plot from time" / "Plot upto time").</summary>
public partial class LogSelectionViewModel : ObservableObject
{
    public const string TimeFormat = "dd/MM/yyyy HH:mm:ss";

    public ObservableCollection<LogSymbolChoice> Symbols { get; }

    [ObservableProperty] private string _from;
    [ObservableProperty] private string _to;

    public LogSelectionViewModel(IReadOnlyList<T7LogLine> lines)
    {
        Symbols = new(T7LogFile.Symbols(lines).Select(n => new LogSymbolChoice(n, true)));
        _from = lines.Count > 0 ? lines[0].Time.ToString(TimeFormat, CultureInfo.InvariantCulture) : "";
        _to = lines.Count > 0 ? lines[^1].Time.AddSeconds(1).ToString(TimeFormat, CultureInfo.InvariantCulture) : "";
    }

    public void SetAll(bool selected)
    {
        foreach (LogSymbolChoice s in Symbols) s.Selected = selected;
    }

    public List<string> Selected => Symbols.Where(s => s.Selected).Select(s => s.Name).ToList();

    public DateTime FromTime => Parse(From, DateTime.MinValue);
    public DateTime ToTime => Parse(To, DateTime.MaxValue);

    private static DateTime Parse(string s, DateTime fallback) =>
        DateTime.TryParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t) ? t : fallback;
}

public partial class LogFilterRow : ObservableObject
{
    [ObservableProperty] private string _symbol = "";
    [ObservableProperty] private int _type;
    [ObservableProperty] private double _value;
    [ObservableProperty] private bool _active;
}

/// <summary>frmLogFilters: the filters the viewer and the exports drop lines with.</summary>
public class LogFiltersViewModel
{
    public ObservableCollection<LogFilterRow> Rows { get; }
    public IReadOnlyList<string> SymbolNames { get; }
    public static string[] Types { get; } = ["GreaterThan", "SmallerThan", "Equals"];

    public LogFiltersViewModel(LogFilterCollection filters, IReadOnlyList<string> symbols)
    {
        SymbolNames = symbols;
        Rows = new(filters.Cast<LogFilter>().OrderBy(f => f.Index).Select(f => new LogFilterRow
        {
            Symbol = f.Symbol, Type = (int)f.Type, Value = f.Value, Active = f.Active,
        }));
    }

    public LogFilterCollection ToCollection()
    {
        var filters = new LogFilterCollection();
        int i = 0;
        foreach (LogFilterRow r in Rows.Where(r => r.Symbol != ""))
            filters.Add(new LogFilter { Index = i++, Symbol = r.Symbol, Type = (LogFilter.MathType)r.Type, Value = (float)r.Value, Active = r.Active });
        return filters;
    }
}
