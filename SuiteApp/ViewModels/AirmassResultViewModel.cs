using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteApp.Controls;

namespace SuiteApp.ViewModels;

/// <summary>A legend entry: the limiter's colour and name; clicking opens its maps.</summary>
public record LimiterLegend(AirmassLimitType Type, string Name, IBrush Brush);

/// <summary>
/// What the airmass result viewer needs from the suite: its calculation on a file as it is on disk (null when the file can't be
/// read), the compare file's check (it says why itself, or NotValidText does), the variant option's caption (T7: convertible,
/// T8: high output) and default, the gears, the legend and the compressor map's first guess.
/// </summary>
public sealed record AirmassSuite(
    Func<string, AirmassOptions, AirmassResult?> Calculate,
    Func<string, bool> IsValidFile,
    string? NotValidText,
    string VariantCaption,
    bool VariantDefault,
    IReadOnlyList<string> Gears,
    IReadOnlyList<(AirmassLimitType Type, string Name)> Legend,
    (int compressor, double litres) CompressorDefaults);

/// <summary>
/// ctrlAirmassResult, "Airmass result viewer: file": the table, the dyno graph from the full pedal row and the compressor map.
/// Every option recalculates from the file as it is on disk.
/// </summary>
public partial class AirmassResultViewModel : DocumentViewModel
{
    private readonly MainWindowViewModel m_owner;
    private readonly string m_file;
    private readonly AirmassSuite m_suite;
    private AirmassResult? m_compare;
    private string m_compareName = "";

    public override string Title => "Airmass result viewer: " + Path.GetFileName(m_file);

    [ObservableProperty] private bool _automatic;
    [ObservableProperty] private bool _e85;
    [ObservableProperty] private bool _variant;
    [ObservableProperty] private bool _overboost;
    [ObservableProperty] private bool _trionicTorque;
    [ObservableProperty] private bool _firmwareLimited = true;
    [ObservableProperty] private decimal? _ambientKpa = 100;
    [ObservableProperty] private int _gear = 5;
    [ObservableProperty] private int _displayMode;
    [ObservableProperty] private bool _powerInKw;
    [ObservableProperty] private bool _torqueInLbft;

    [ObservableProperty] private AirmassResult? _result;
    [ObservableProperty] private string[,]? _texts;
    [ObservableProperty] private IReadOnlyList<string> _categories = [];
    [ObservableProperty] private IReadOnlyList<ChartSeries> _series = [];

    [ObservableProperty] private bool _showPower = true;
    [ObservableProperty] private bool _showTorque = true;
    [ObservableProperty] private bool _showInjectorDc = true;
    [ObservableProperty] private bool _showLambda = true;
    [ObservableProperty] private bool _showEgt;
    [ObservableProperty] private bool _showFuelFlow;

    // compressor map
    [ObservableProperty] private int _compressorIndex;
    [ObservableProperty] private int _engineIndex;
    [ObservableProperty] private decimal? _ve = 90;
    [ObservableProperty] private decimal? _intakeTemp = 20;
    [ObservableProperty] private Bitmap? _compressorImage;
    [ObservableProperty] private List<(double lbmin, double pr)>[]? _compressorPoints;

    public Compressor Compressor => CompressorMap.Compressors[Math.Clamp(CompressorIndex, 0, CompressorMap.Compressors.Length - 1)];
    public string[] CompressorNames { get; } = CompressorMap.Compressors.Select(c => c.Name).ToArray();
    public string[] Engines { get; } = ["2.0 liter", "2.3 liter"];
    public IReadOnlyList<string> Gears => m_suite.Gears;
    public string VariantCaption => m_suite.VariantCaption;
    public string[] DisplayModes { get; } =
        ["Show airmass", "Show estimated torque", "Show estimated horsepower", "Show injector DC", "Show target lambda", "Show target AFR", "Show estimated EGT", "Show fuel flow"];

    public bool CanE85 => Result?.CanE85 == true;
    public bool CanVariant => Result?.CanVariant == true;
    public bool CanOverboost => Result?.CanOverboost == true;

    public IReadOnlyList<LimiterLegend> Legend { get; }

    public AirmassResultViewModel(MainWindowViewModel owner, string file, AirmassSuite suite)
    {
        m_owner = owner;
        m_file = file;
        m_suite = suite;
        Legend = suite.Legend.Select(l => new LimiterLegend(l.Type, l.Name, new Avalonia.Media.Immutable.ImmutableSolidColorBrush(LimiterColors.All[l.Type]))).ToList();
        _variant = suite.VariantDefault;
        _compressorIndex = suite.CompressorDefaults.compressor;
        _engineIndex = suite.CompressorDefaults.litres > 2.1 ? 1 : 0;
        Calculate();
        LoadCompressorImage();
    }

    private AirmassOptions Options => new()
    {
        Automatic = Automatic, E85 = E85 && CanE85OrUnknown, Variant = Variant, Overboost = Overboost, TrionicTorque = TrionicTorque,
        FirmwareLimited = FirmwareLimited, AmbientKpa = (int)(AmbientKpa ?? 100), Gear = Gear,
    };

    private bool CanE85OrUnknown => Result == null || Result.CanE85;

    /// <summary>Calculate / Refresh: the file is read again.</summary>
    [RelayCommand]
    public void Calculate()
    {
        Result = m_suite.Calculate(m_file, Options);
        OnPropertyChanged(nameof(CanE85));
        OnPropertyChanged(nameof(CanVariant));
        OnPropertyChanged(nameof(CanOverboost));
        if (m_compareFile != null) m_compare = m_suite.Calculate(m_compareFile, Options);
        Redisplay();
    }

    partial void OnAutomaticChanged(bool value) => Calculate();
    partial void OnE85Changed(bool value) => Calculate();
    partial void OnVariantChanged(bool value) => Calculate();
    partial void OnOverboostChanged(bool value) => Calculate();
    partial void OnTrionicTorqueChanged(bool value) => Calculate();
    partial void OnFirmwareLimitedChanged(bool value) => Calculate();
    partial void OnAmbientKpaChanged(decimal? value) => Calculate();
    partial void OnGearChanged(int value) => Calculate();
    partial void OnDisplayModeChanged(int value) => Redisplay();
    partial void OnPowerInKwChanged(bool value) => Redisplay();
    partial void OnTorqueInLbftChanged(bool value) => Redisplay();
    partial void OnShowPowerChanged(bool value) => Redisplay();
    partial void OnShowTorqueChanged(bool value) => Redisplay();
    partial void OnShowInjectorDcChanged(bool value) => Redisplay();
    partial void OnShowLambdaChanged(bool value) => Redisplay();
    partial void OnShowEgtChanged(bool value) => Redisplay();
    partial void OnShowFuelFlowChanged(bool value) => Redisplay();
    partial void OnCompressorIndexChanged(int value) => LoadCompressorImage();
    partial void OnEngineIndexChanged(int value) => Redisplay();
    partial void OnVeChanged(decimal? value) => Redisplay();
    partial void OnIntakeTempChanged(decimal? value) => Redisplay();

    private int TorqueShown(AirmassResult r, int air, int rpm)
    {
        int tq = r.Torque(air, rpm);
        return TorqueInLbft ? AirmassResult.Lbft(tq) : tq;
    }

    private int PowerShown(AirmassResult r, int air, int rpm)
    {
        int tq = r.Torque(air, rpm);
        return PowerInKw ? AirmassResult.PowerKw(tq, rpm) : AirmassResult.Power(tq, rpm);
    }

    private static string F(double? v, string format = "0") => v?.ToString(format, CultureInfo.CurrentCulture) ?? "";

    /// <summary>The display mode's cell texts, the dyno graph and the compressor points.</summary>
    private void Redisplay()
    {
        if (Result is not { } r) return;
        int rows = r.Pedal.Length, cols = r.Rpm.Length;
        var texts = new string[rows, cols];
        for (int p = 0; p < rows; p++)
            for (int c = 0; c < cols; c++)
            {
                int air = r.Airmass[p, c], rpm = r.Rpm[c];
                texts[p, c] = DisplayMode switch
                {
                    1 => TorqueShown(r, air, rpm).ToString(),
                    2 => PowerShown(r, air, rpm).ToString(),
                    3 => F(r.InjectorDc(air, rpm) is { } dc ? Math.Min(dc, 100) : null),
                    4 => F(r.TargetLambda(air, rpm), "F2"),
                    5 => F(r.TargetAfr(air, rpm), "F2"),
                    6 => F(r.Egt(air, rpm)),
                    7 => F(r.FuelFlow(air, rpm), "F2"),
                    _ => air.ToString(),
                };
            }
        Texts = texts;

        // the dyno graph from the full pedal row
        Categories = r.Rpm.Select(v => v.ToString()).ToList();
        int wot = rows - 1;
        if (wot < 0)
        {
            Series = [];
            CompressorPoints = null;
            return;
        }
        double?[] Row(AirmassResult res, Func<int, int, double?> f) => Enumerable.Range(0, cols).Select(c => f(res.Airmass[wot, c], res.Rpm[c])).ToArray();
        var series = new List<ChartSeries>();
        string power = PowerInKw ? "Power (kW)" : "Power (bhp)", torque = TorqueInLbft ? "Torque (lbft)" : "Torque (Nm)";
        if (ShowPower) series.Add(new(power, Colors.Red, Row(r, (a, rpm) => PowerShown(r, a, rpm))));
        if (ShowTorque) series.Add(new(torque, Colors.Blue, Row(r, (a, rpm) => TorqueShown(r, a, rpm))));
        if (ShowInjectorDc) series.Add(new("Injector DC (%)", Colors.GreenYellow, Row(r, (a, rpm) => r.InjectorDc(a, rpm) is { } dc ? Math.Min(dc, 100) : null)));
        if (ShowLambda) series.Add(new("Target lambda (*100)", Colors.DarkGreen, Row(r, (a, rpm) => r.TargetLambda(a, rpm) * 100)));
        if (ShowEgt) series.Add(new("EGT estimate (°C)", Colors.Plum, Row(r, r.Egt)));
        if (ShowFuelFlow) series.Add(new("Fuel flow (l/h)", Colors.Black, Row(r, r.FuelFlow)));
        if (m_compare is { } other && other.Pedal.Length > 0)
        {
            int ow = other.Pedal.Length - 1;
            double?[] ORow(Func<int, int, double?> f) => Enumerable.Range(0, Math.Min(cols, other.Rpm.Length)).Select(c => f(other.Airmass[ow, c], other.Rpm[c])).ToArray();
            if (ShowPower) series.Add(new($"{power} {m_compareName}", Colors.Orange, ORow((a, rpm) => PowerShown(other, a, rpm))));
            if (ShowTorque) series.Add(new($"{torque} {m_compareName}", Colors.LightBlue, ORow((a, rpm) => TorqueShown(other, a, rpm))));
        }
        Series = series;

        CompressorPoints = CompressorMap.Points(r.Rpm, Enumerable.Range(0, cols).Select(c => r.Airmass[wot, c]).ToArray(),
            EngineIndex == 1 ? 2.3 : 2.0, (double)(Ve ?? 90), (double)(IntakeTemp ?? 20));
    }

    private void LoadCompressorImage()
    {
        OnPropertyChanged(nameof(Compressor));
        using var stream = AssetLoader.Open(new Uri("avares://SuiteApp/Assets/Compressormaps/" + Compressor.Image));
        CompressorImage = new Bitmap(stream);
        Redisplay();
    }

    private string? m_compareFile;

    /// <summary>Compare to another file: its power and torque in the dyno graph (the name cut to 16 characters).</summary>
    public void Compare(string file)
    {
        if (!m_suite.IsValidFile(file))
        {
            if (m_suite.NotValidText != null) m_owner.ShowInfo(m_suite.NotValidText);
            return;
        }
        m_compareFile = file;
        string name = Path.GetFileNameWithoutExtension(file);
        m_compareName = name.Length > 16 ? name[..14] + ".." : name;
        Calculate();
    }

    /// <summary>A legend entry's maps open in the file's viewers.</summary>
    [RelayCommand]
    private void OpenLimiter(AirmassLimitType type)
    {
        if (Result == null) return;
        foreach (string map in Result.LimiterMaps(type))
            if (m_owner.Binary?.FindAny(map) != null) m_owner.OpenSymbolByName(map);
    }

    [RelayCommand]
    private System.Threading.Tasks.Task Close() => m_owner.CloseViewerAsync(this);
}
