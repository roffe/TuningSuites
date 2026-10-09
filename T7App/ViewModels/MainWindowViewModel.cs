using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using T7;
using TrionicCANLib.Firmware;

namespace T7App.ViewModels;

public record RecentFile(string Name, string Path);

/// <summary>
/// The main window: the opened binary (frmMain.OpenFile), its symbol list and the open map viewers.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private const string Suite = "T7SuitePro";
    private static readonly string Version =
        typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public AppSettings Settings { get; } = new(new T7SuiteRegistry());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private T7Binary? _binary;

    [ObservableProperty]
    private DataGridCollectionView? _symbols;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private SymbolHelper? _selectedSymbol;

    [ObservableProperty]
    private MapViewerViewModel? _selectedViewer;

    [ObservableProperty]
    private string _title = $"T7SuitePro v{Version}";

    [ObservableProperty]
    private string _openClosedText = "";

    [ObservableProperty]
    private string _fileNameText = "No file loaded";

    [ObservableProperty]
    private string _readOnlyText = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isBusy;

    public bool HasFile => Binary != null;

    public ObservableCollection<MapViewerViewModel> Viewers { get; } = new();
    public ObservableCollection<RecentFile> Recent { get; } = new();

    /// <summary>A message for the user (frmInfoBox / MessageBox), shown by the view.</summary>
    public event Action<string>? Info;

    public MainWindowViewModel()
    {
        Trionic7File.onProgress += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Percentage >= 55 ? "" : e.Info);
        LoadRecent();
    }

    /// <summary>A .bin on the command line, else the last file when AutoLoadLastFile is set (frmMain_Load).</summary>
    public async Task StartupAsync(string[] args)
    {
        if (args is [var arg, ..] && arg.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            await OpenFileAsync(arg, true);
        else if (Settings.AutoLoadLastFile && Settings.Lastfilename != "" && File.Exists(Settings.Lastfilename))
            await OpenFileAsync(Settings.Lastfilename, true);
    }

    /// <summary>frmMain.OpenFile: S19 is converted first, the file must be a T7 binary, then its symbols are read.</summary>
    public async Task<bool> OpenFileAsync(string path, bool showMessage)
    {
        IsBusy = true;
        try
        {
            if (path.EndsWith(".s19", StringComparison.OrdinalIgnoreCase))
            {
                string source = path;
                var (ok, converted) = await Task.Run(() => (new Srecord().ConvertSrecToBin(source, FileT7.Length, out string bin, true), bin));
                if (ok) path = converted;
                else Info?.Invoke("Failed to convert S19 file to binary");
            }
            Settings.Lastfilename = path;
            if (!await Task.Run(() => T7Binary.IsValidFile(path)))
            {
                Binary = null;
                Symbols = null;
                Title = $"T7SuitePro v{Version} [ none ]";
                if (showMessage) Info?.Invoke("File is not a Trionic 7 binary file!");
                return false;
            }
            AddRecent(path);
            ReadOnlyText = new FileInfo(path).IsReadOnly ? "File is READ ONLY" : "File access OK";
            T7Binary bin = await Task.Run(() => T7Binary.Open(path, Settings.ApplicationLanguage, Settings.AutoFixFooter));

            // sorted by length, largest first, grouped by category like the old grid
            var rows = bin.Symbols.Cast<SymbolHelper>().OrderByDescending(s => s.Length).ToList();
            var view = new DataGridCollectionView(rows) { Filter = MatchesSearch };
            view.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(SymbolHelper.Category)));
            Binary = bin;
            Symbols = view;
            Title = $"T7SuitePro v{Version} [ {Path.GetFileName(path)} ]";
            FileNameText = Path.GetFileNameWithoutExtension(path);
            OpenClosedText = bin.IsSoftwareOpen ? "Open/dev binary" : "Normal binary";
            return true;
        }
        finally
        {
            ProgressText = "";
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value) => Symbols?.Refresh();

    // the find panel: any shown column containing the text
    private bool MatchesSearch(object o)
    {
        if (string.IsNullOrWhiteSpace(SearchText) || o is not SymbolHelper sh) return true;
        string t = SearchText.Trim();
        return Contains(sh.Varname, t) || Contains(sh.Description, t) || Contains(sh.Userdescription, t)
            || Contains(SymbolFormat.Number(sh.Flash_start_address, Settings.ShowAddressesInHex), t)
            || Contains(SymbolFormat.Number(sh.Length, Settings.ShowAddressesInHex), t);
    }

    private static bool Contains(string? s, string t) => s != null && s.Contains(t, StringComparison.OrdinalIgnoreCase);

    /// <summary>Double-click / Enter on a symbol (gridViewSymbols_DoubleClick → StartTableViewer).</summary>
    [RelayCommand]
    private void OpenSymbol(SymbolHelper? sh)
    {
        if (Binary is not { } bin || sh == null) return;
        if (sh.Flash_start_address == 0 && sh.Start_address == 0) return;
        string title = $"Symbol: {sh.SmartVarname} [{Path.GetFileName(bin.FileName)}]";
        if (Viewers.FirstOrDefault(v => v.Title == title && v.FileName == bin.FileName) is { } open)
        {
            SelectedViewer = open;
            return;
        }
        if (MapViewerViewModel.Create(bin, sh, Settings) is not { } viewer)
        {
            // ponytail: SRAM-only symbols need the ECU connection (chunk 5)
            Info?.Invoke($"{sh.SmartVarname} only lives in the ECU's SRAM, reading it needs a connection to the ECU.");
            return;
        }
        Viewers.Add(viewer);
        SelectedViewer = viewer;
    }

    partial void OnSelectedViewerChanged(MapViewerViewModel? oldValue, MapViewerViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    [RelayCommand]
    private void CloseViewer(MapViewerViewModel viewer)
    {
        int i = Viewers.IndexOf(viewer);
        Viewers.Remove(viewer);
        if (SelectedViewer == viewer) SelectedViewer = Viewers.Count == 0 ? null : Viewers[Math.Min(i, Viewers.Count - 1)];
    }

    [RelayCommand]
    private Task OpenRecent(RecentFile file) => OpenFileAsync(file.Path, false);

    [RelayCommand]
    private static void Exit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    public FirmwareInfoViewModel? FirmwareInfo() =>
        Binary is { } bin && File.Exists(bin.FileName) ? new FirmwareInfoViewModel(T7.FirmwareInfo.Read(bin)) : null;

    // ---- MRU: file name and path per entry, appended when new, no limit (T7Suite's HKCU\Software\T7SuitePro\MRUList) ----

    private void LoadRecent()
    {
        // entries are numbered: a path's backslashes would read as subkeys
        using var key = SettingsKey.Open(Suite, "MRUList");
        var entries = key.GetValueNames().OrderBy(n => int.TryParse(n, out int i) ? i : int.MaxValue).Select(n => (string)key.GetValue(n)!).ToList();
        if (entries.Count == 0)
        {
            // once: what T7Suite kept outside HKCU\Software\MattiasC
            entries = SettingsKey.ImportRegistry(@"Software\T7SuitePro\MRUList").Values.Distinct().ToList();
            for (int i = 0; i < entries.Count; i++) key.SetValue(i.ToString(), entries[i]);
        }
        foreach (string path in entries.Distinct()) Recent.Add(new RecentFile(Path.GetFileName(path), path));
    }

    private void AddRecent(string path)
    {
        if (Recent.Any(r => r.Path == path)) return;
        Recent.Add(new RecentFile(Path.GetFileName(path), path));
        using var key = SettingsKey.Open(Suite, "MRUList");
        key.SetValue((Recent.Count - 1).ToString(), path);
    }
}

/// <summary>Address / length text in the symbol list: hex X6 or decimal (ShowAddressesInHex).</summary>
public static class SymbolFormat
{
    public static string Number(long value, bool hex) => hex ? value.ToString("X6") : value.ToString();
}
