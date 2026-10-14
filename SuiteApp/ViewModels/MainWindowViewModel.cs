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
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapControls;
using SuiteApp.Services;

namespace SuiteApp.ViewModels;

public record RecentFile(string Name, string Path);

/// <summary>A column of the symbol list: its caption and SymbolHelper property; hidden ones only show from the column chooser.</summary>
public record SymbolColumn(string Header, string Path, bool Visible = true);

/// <summary>A predefined filter of the symbol list (T8Suite's "Only symbols within binary").</summary>
public record SymbolFilter(string Name, Func<SymbolHelper, bool> Matches);

/// <summary>
/// The main window (the old suites' frmMain / Form1, now one for every ECU): the opened binary, its symbol list, the open documents and
/// the project. What a T5, T7 or T8 binary needs its own way is that ECU's module (<see cref="EcuModule"/>): the open file's is
/// <see cref="Module"/>, and a file of another ECU makes its module the open one.
/// </summary>
public abstract partial class MainWindowViewModel : ObservableObject
{
    /// <summary>"TxSuite": the window title, the settings folder (SettingsKey.Program).</summary>
    public string Suite { get; }

    /// <summary>"TxSuite": message captions.</summary>
    public string Caption { get; }

    /// <summary>The open file's VIN for Actions → VIN decoder; "" where the files carry none (T5).</summary>
    public string BinaryVin => Module.BinaryVin;

    // the program's own version, not this library's
    protected string Version =>
        GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>A module per ECU, in the order their old suites' settings are imported (the first one's shared options win).</summary>
    public IReadOnlyList<EcuModule> Modules { get; }

    /// <summary>The open file's ECU; with no file open the one of the file opened last (the program's last one at startup).</summary>
    [ObservableProperty]
    private EcuModule _module;

    partial void OnModuleChanged(EcuModule value)
    {
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(SymbolFilters));
        OnPropertyChanged(nameof(SymbolColumns));
        OnPropertyChanged(nameof(OriginalFile));
        using var settings = SettingsKey.Open(Suite);
        settings.SetValue(EcuKey, value.Name);
    }

    // the program's settings: the ECU of the file opened last
    private const string EcuKey = "Suite";

    /// <summary>The open file's ECU's settings: the shared options (Settings → Common) and its own.</summary>
    public AppSettings Settings => Module.Settings;

    /// <summary>The ECU's settings key, for what keeps its own subkeys (log filters, symbol colours).</summary>
    protected SuiteRegistry Registry => Module.Registry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(OriginalFile))]
    private SuiteBinary? _binary;

    [ObservableProperty]
    private DataGridCollectionView? _symbols;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private SymbolHelper? _selectedSymbol;

    private DocumentViewModel? m_selectedViewer;

    /// <summary>The document in front; a tabbed window stands for its selected tab, and a tab selected shows in its window.</summary>
    public DocumentViewModel? SelectedViewer
    {
        get => m_selectedViewer;
        set
        {
            if (value is DocumentTabsViewModel { Selected: { } tab }) value = tab;
            if (value == m_selectedViewer) return;
            if (m_selectedViewer != null) m_selectedViewer.IsSelected = false;
            m_selectedViewer = value;
            if (value != null) value.IsSelected = true;
            if (value?.Tabs is { } window) window.Selected = value;
            // the map in front is the symbol list's selection (its category unfolded by the list); not another file's map
            if (value is MapViewerViewModel { } map && map.Binary == Binary) SelectedSymbol = map.Symbol;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _openClosedText = "";

    [ObservableProperty]
    private string _fileNameText = "No file loaded";

    /// <summary>T8Suite's "Checksum: OK" / "Checksum: Layer 1 invalid" in the status bar.</summary>
    [ObservableProperty]
    private string _checksumText = "";

    /// <summary>The imported SRAM snapshot, "SRAM: name".</summary>
    [ObservableProperty]
    private string _sramFileText = "";

    /// <summary>The status bar's ECU field (SetCANStatus).</summary>
    [ObservableProperty]
    private string _canStatus = "";

    [ObservableProperty]
    private string _readOnlyText = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isBusy;

    public bool HasFile => Binary != null;

    /// <summary>The open project, null when a plain file is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen))]
    private SuiteProject? _project;

    public bool IsProjectOpen => Project != null;

    [ObservableProperty] private bool _canRollBack;
    [ObservableProperty] private bool _canRollForward;
    [ObservableProperty] private bool _hasTransactions;

    /// <summary>A line of text from the user (frmChangeNote), null when cancelled; answered by the view.</summary>
    public Func<string, Task<string?>>? AskText { get; set; }

    /// <summary>Ok / Cancel question, answered by the view.</summary>
    public Func<string, Task<bool>>? AskOkCancel { get; set; }

    /// <summary>The open documents: map viewers, compare results, ...</summary>
    public ObservableCollection<DocumentViewModel> Viewers { get; } = new();
    public ObservableCollection<RecentFile> Recent { get; } = new();

    /// <summary>A message for the user (frmInfoBox / MessageBox), shown by the view.</summary>
    public event Action<string>? Info;

    public void ShowInfo(string text) => Info?.Invoke(text);

    /// <summary>Yes / No / Cancel question (null = Cancel), answered by the view.</summary>
    public Func<string, Task<bool?>>? AskYesNoCancel { get; set; }

    /// <summary>Autotune without auto update: the cells (data indices) to take from the proposed percentages, null to cancel.</summary>
    public Func<double[], Task<IReadOnlyCollection<int>?>>? AcceptAutotune { get; set; }

    /// <summary>Text, caption and button labels: the index clicked, null when closed.</summary>
    public Func<string, string, string[], Task<int?>>? AskButtons { get; set; }

    /// <param name="name">The program ("TxSuite"): the title, message captions, the settings.</param>
    /// <param name="releaseTagPrefix">Its release tags ("Txsuite_v"), for the update check.</param>
    /// <param name="modules">A module per ECU over this main window.</param>
    protected MainWindowViewModel(string name, string releaseTagPrefix, Func<MainWindowViewModel, IReadOnlyList<EcuModule>> modules)
    {
        Suite = name;
        Caption = name;
        ReleaseTagPrefix = releaseTagPrefix;
        Modules = modules(this);
        string? last;
        using (var settings = SettingsKey.Open(Suite)) last = settings.GetValue(EcuKey) as string;
        // T7 the first time, as T7Suite was the suite TxSuite started with
        _module = Modules.FirstOrDefault(m => m.Name == last) ?? Modules.FirstOrDefault(m => m.Name == "T7") ?? Modules[0];
        ApplyColorBlindMode();
        ApplySymbolListFont();
        ApplyMapCellGap();
        Logging.ApplyCanLogging(Settings.EnableCanLog);
        Title = $"{Suite} v{Version}";
        ColumnFilters = new(SymbolColumns.Where(c => c.Visible).Select(NewFilter));
        LoadRecent();
        WatchFloating();
        RestartSramTimer();
    }

    /// <summary>
    /// The symbol list's columns in order, the same for every ECU (each old suite had its own); the ones the open file's ECU has
    /// (HasSymbolColumn). The hidden ones stay in the column chooser.
    /// </summary>
    public IReadOnlyList<SymbolColumn> SymbolColumns =>
    [
        .. new SymbolColumn[]
        {
            new("Symbol name", nameof(SymbolHelper.Varname)), new("Number", nameof(SymbolHelper.Symbol_number), false),
            new("Address", nameof(SymbolHelper.Flash_start_address)), new("SRAM address", nameof(SymbolHelper.Start_address), false),
            new("Length", nameof(SymbolHelper.Length)), new("Type", nameof(SymbolHelper.Symbol_type), false),
            new("Bitmask", nameof(SymbolHelper.BitMask), false), new("Description", nameof(SymbolHelper.Description)),
            new("User description", nameof(SymbolHelper.Userdescription)), new("Category", nameof(SymbolHelper.Category), false),
        }.Where(c => Module.HasSymbolColumn(c.Path)),
    ];

    /// <summary>Symbol names coloured by their prefix (T7Suite did, T8Suite didn't).</summary>
    public bool ColorSymbolNames => Module.ColorSymbolNames;

    /// <summary>T5Suite coloured the description cell by the symbol's category instead.</summary>
    public bool ColorDescriptionsByCategory => Module.ColorDescriptionsByCategory;

    /// <summary>The closed-loop mark of the open file's ECU.</summary>
    public OpenLoopMark OpenLoopMarkStyle => Module.OpenLoopMarkStyle;

    /// <summary>The view a map opens in, the open file's ECU's way.</summary>
    public MapViewType DefaultMapView => Module.DefaultMapView;

    /// <summary>A map's cell marks in a bin (T5Suite's padlocks and adaption boxes); null for none, as T7Suite and T8Suite.</summary>
    public Func<int, CellMarks>? CellMarks(SuiteBinary bin, string mapName) => Module.CellMarks(bin, mapName);

    /// <summary>
    /// T8Suite's map preview popup (Settings → Show map preview popup), for every ECU: the hovered map from the file, read only; null
    /// for one that only lives in SRAM.
    /// </summary>
    public MapViewerViewModel? MapPreview(SymbolHelper sh) => Settings.ShowMapPreviewPopup && Binary is { } bin ? MapViewerViewModel.Create(this, bin, sh, readOnly: true) : null;

    /// <summary>The symbol list's predefined filters; none for T7.</summary>
    public IReadOnlyList<SymbolFilter> SymbolFilters => Module.SymbolFilters;

    /// <summary>The open file's ECU's realtime session is open: new viewers show the ECU's data.</summary>
    protected bool EcuConnected => Module.EcuConnected;

    /// <summary>Why the program can't close now (a flash or an autotune in progress), null when it can.</summary>
    public string? CloseBlocker => Modules.Select(m => m.CloseBlocker).FirstOrDefault(b => b != null);

    /// <summary>The program closes: every ECU released.</summary>
    public void Shutdown()
    {
        foreach (EcuModule m in Modules) m.Shutdown();
    }

    /// <summary>"Compare to original file": the stock bin with the open file's part number (T5, T7), null without one.</summary>
    public string? OriginalFile => Binary == null ? null : Module.OriginalFile;

    [RelayCommand]
    private Task CompareToOriginal() => OriginalFile is { } file ? CompareToFileAsync(file) : Task.CompletedTask;

    // ---- opening ----

    /// <summary>A .bin on the command line, else the last file when AutoLoadLastFile is set (frmMain_Load).</summary>
    public async Task StartupAsync(string[] args)
    {
        if (args is [var arg, ..] && arg.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            await OpenFileAsync(arg, true);
        else if (Settings.AutoLoadLastFile && Settings.LastOpenedType == 0 && Settings.Lastfilename != "" && File.Exists(Settings.Lastfilename))
            await OpenFileAsync(Settings.Lastfilename, true);
        else if (Settings.AutoLoadLastFile && Settings.LastOpenedType == 1 && Settings.Lastprojectname != "")
            await OpenProjectAsync(Settings.Lastprojectname);
    }

    /// <summary>
    /// The file a path names, through symbolic links: on Linux .NET's FileInfo.Length is the link's own, and every ECU checks a binary's
    /// length (a linked stock bin was "File has incorrect length").
    /// </summary>
    protected static string LinkTarget(string path) => File.Exists(path) ? File.ResolveLinkTarget(path, true)?.FullName ?? path : path;

    /// <summary>
    /// frmMain.OpenFile: S19 is converted first, the file must be a T5, T7 or T8 binary (another ECU's makes that ECU the open one,
    /// SwitchToAsync), then its symbols are read.
    /// </summary>
    public async Task<bool> OpenFileAsync(string path, bool showMessage)
    {
        path = LinkTarget(path);
        IsBusy = true;
        try
        {
            if (path.EndsWith(".s19", StringComparison.OrdinalIgnoreCase))
            {
                // ponytail: converted at the size of the ECU of the file opened last (T5 with its own converter); the S19's addresses
                // could tell the ECU
                string source = path;
                EcuModule converter = Module;
                if (await Task.Run(() => converter.ConvertS19(source)) is { } converted) path = converted;
                else Info?.Invoke("Failed to convert S19 file to binary");
            }
            Settings.Lastfilename = path;
            string file = path;
            EcuModule? ecu = await Task.Run(() => Modules.FirstOrDefault(m => m.Claims(file)));
            if (ecu == null || !await Task.Run(() => ecu.IsValidFile(file)))
            {
                Binary = null;
                Symbols = null;
                Title = $"{Suite} v{Version} [ none ]";
                if (showMessage && (ecu == null ? NotABinary : ecu.InvalidFileMessage) is { } message) Info?.Invoke(message);
                return false;
            }
            if (ecu != Module && !await SwitchToAsync(ecu)) return false;
            AddRecent(path);
            ReadOnlyText = new FileInfo(path).IsReadOnly ? "File is READ ONLY" : "File access OK";
            SuiteBinary bin = await Task.Run(() => ecu.OpenBinary(file));

            // grouped by category and sorted by it ascending for every ECU (its order inside a category); the view filters as it's
            // made, so the ECU's filter first
            SymbolFilter = ecu.DefaultSymbolFilter;
            var rows = ecu.OrderSymbols(bin.Symbols.Cast<SymbolHelper>()).ToList();
            var view = new DataGridCollectionView(rows) { Filter = MatchesSearch };
            foreach (string group in ecu.SymbolGroupPaths) view.GroupDescriptions.Add(new DataGridPathGroupDescription(group));
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(SymbolHelper.Category)));
            // no symbol selected yet: the grid would make the first row current, which unfolds its category
            view.MoveCurrentToPosition(-1);
            Binary = bin;
            Symbols = view;
            Title = $"{Suite} v{Version} [ {Path.GetFileName(path)} ]";
            FileNameText = Path.GetFileNameWithoutExtension(path);
            OpenClosedText = bin.IsSoftwareOpen ? "Open/dev binary" : "Normal binary";
            await ecu.OnOpenedAsync(bin);
            return true;
        }
        finally
        {
            ProgressText = "";
            IsBusy = false;
        }
    }

    /// <summary>A file no ECU takes.</summary>
    public const string NotABinary = "File is not a Trionic 5, 7 or 8 binary file!";

    /// <summary>
    /// Another ECU's file is about to open: what belonged to the one before goes first, as when the old suites were separate programs.
    /// Nothing switches while that ECU is being flashed or autotuned; every document closes (unsaved maps asked about first, Cancel
    /// keeps everything), its ECU session and SRAM snapshot are let go. False when it can't or the user cancelled.
    /// </summary>
    private async Task<bool> SwitchToAsync(EcuModule ecu)
    {
        if (Module.CloseBlocker is { } blocker)
        {
            ShowInfo(blocker);
            return false;
        }
        if (!await CloseMutatedViewersAsync()) return false;
        foreach (DocumentViewModel d in Viewers.ToList())
            if (Viewers.Contains(d)) await CloseViewerAsync(d);
        await DisconnectRealtimeAsync();
        SramFile = null;
        Binary = null;
        Symbols = null;
        Module = ecu;
        Logging.ApplyCanLogging(Settings.EnableCanLog);
        return true;
    }

    // ---- symbol list ----

    partial void OnSearchTextChanged(string value) => RefreshSymbols();

    /// <summary>
    /// Set by the symbol list, called before the symbol view refreshes, sorts or regroups: it ends a cell edit in its grid (which then
    /// saves the user descriptions) and remembers its current category header, which the refresh would lose.
    /// </summary>
    public Action? SymbolsChanging { get; set; }

    /// <summary>The symbol view with a user description edit committed: an open edit forbids its refresh, sorting and grouping.</summary>
    private DataGridCollectionView? CommittedSymbols()
    {
        SymbolsChanging?.Invoke();
        if (Symbols is not { } view) return null;
        if (view.IsAddingNew) view.CommitNew();
        if (view.IsEditingItem) view.CommitEdit();
        return view;
    }

    /// <summary>The symbol list again (search, filters, colours).</summary>
    public void RefreshSymbols() => CommittedSymbols()?.Refresh();

    /// <summary>The symbol list's auto filter row: per column text the cell has to contain.</summary>
    public ObservableCollection<ColumnFilter> ColumnFilters { get; }

    private ColumnFilter NewFilter(SymbolColumn c)
    {
        var f = new ColumnFilter(c.Header, c.Path);
        f.PropertyChanged += (_, _) => RefreshSymbols();
        return f;
    }

    /// <summary>The filter row follows the grid's shown columns in their order; a box keeps its text while its column shows.</summary>
    public void SetFilterColumns(IEnumerable<string?> paths)
    {
        List<SymbolColumn> wanted = [.. paths.Select(p => SymbolColumns.FirstOrDefault(c => c.Path == p)).OfType<SymbolColumn>()];
        if (ColumnFilters.Select(f => f.Path).SequenceEqual(wanted.Select(c => c.Path))) return;
        Dictionary<string, ColumnFilter> old = ColumnFilters.ToDictionary(f => f.Path);
        ColumnFilters.Clear();
        foreach (SymbolColumn c in wanted) ColumnFilters.Add(old.Remove(c.Path, out ColumnFilter? f) ? f : NewFilter(c));
        // a hidden column's text no longer filters
        if (old.Values.Any(f => !string.IsNullOrWhiteSpace(f.Text))) RefreshSymbols();
    }

    [ObservableProperty]
    private bool _showFilterRow;

    partial void OnShowFilterRowChanged(bool value)
    {
        if (!value) foreach (ColumnFilter f in ColumnFilters) f.Text = "";
    }

    [ObservableProperty]
    private SymbolFilter? _symbolFilter;

    partial void OnSymbolFilterChanged(SymbolFilter? value) => RefreshSymbols();

    private string? CellText(SymbolHelper sh, string path) => path switch
    {
        nameof(SymbolHelper.Varname) => sh.Varname,
        nameof(SymbolHelper.Flash_start_address) => SymbolFormat.Number(sh.Flash_start_address, Settings.ShowAddressesInHex),
        nameof(SymbolHelper.Start_address) => SymbolFormat.Number(sh.Start_address, Settings.ShowAddressesInHex),
        nameof(SymbolHelper.Length) => SymbolFormat.Number(sh.Length, Settings.ShowAddressesInHex),
        nameof(SymbolHelper.BitMask) => SymbolFormat.Number(sh.BitMask, Settings.ShowAddressesInHex),
        nameof(SymbolHelper.Description) => sh.Description,
        nameof(SymbolHelper.Symbol_number) => sh.Symbol_number.ToString(),
        nameof(SymbolHelper.Symbol_type) => sh.Symbol_type.ToString(),
        nameof(SymbolHelper.Category) => sh.Category,
        _ => sh.Userdescription,
    };

    /// <summary>The header menu's sorting: by one column, or none (the grid's own order).</summary>
    public void SortSymbols(string? path, bool descending = false, bool add = false)
    {
        if (CommittedSymbols() is not { } view) return;
        if (!add) view.SortDescriptions.Clear();
        if (path != null)
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(path, descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending));
    }

    /// <summary>Group by this column / by category (the suites' default) / not at all.</summary>
    public void GroupSymbols(string? path)
    {
        if (CommittedSymbols() is not { } view) return;
        view.GroupDescriptions.Clear();
        foreach (string group in GroupPaths(path)) view.GroupDescriptions.Add(new DataGridPathGroupDescription(group));
    }

    private string[] GroupPaths(string? path) => path == nameof(SymbolHelper.Category) ? Module.SymbolGroupPaths : path != null ? [path] : [];

    /// <summary>The header menu's ticks: sorted by this column only, this way.</summary>
    public bool IsSortedBy(string? path, bool descending) =>
        Symbols is { SortDescriptions: [var sort] } && sort.PropertyPath == path
        && sort.Direction == (descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending);

    /// <summary>The header menu's ticks: grouped as GroupSymbols(path) groups (null: not grouped).</summary>
    public bool IsGroupedBy(string? path) =>
        Symbols is { } view && view.GroupDescriptions.OfType<DataGridPathGroupDescription>().Select(g => g.PropertyName).SequenceEqual(GroupPaths(path));

    // the find panel: any shown column containing the text, and every filter row text in its column
    private bool MatchesSearch(object o)
    {
        if (o is not SymbolHelper sh) return true;
        if (SymbolFilter is { } filter && !filter.Matches(sh)) return false;
        foreach (ColumnFilter f in ColumnFilters)
            if (!string.IsNullOrWhiteSpace(f.Text) && !Contains(CellText(sh, f.Path), f.Text.Trim())) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        string t = SearchText.Trim();
        return Contains(sh.Varname, t) || Contains(sh.Description, t) || Contains(sh.Userdescription, t)
            || Contains(SymbolFormat.Number(sh.Flash_start_address, Settings.ShowAddressesInHex), t)
            || Contains(SymbolFormat.Number(sh.Length, Settings.ShowAddressesInHex), t);
    }

    private static bool Contains(string? s, string t) => s != null && s.Contains(t, StringComparison.OrdinalIgnoreCase);

    /// <summary>Double-click / Enter on a symbol (gridViewSymbols_DoubleClick → StartTableViewer).</summary>
    [RelayCommand]
    private void OpenSymbol(SymbolHelper? sh) => OpenMap(sh);

    /// <summary>StartTableViewer: the symbol's viewer, opened or the open one; null when none shows (yet).</summary>
    private MapViewerViewModel? OpenMap(SymbolHelper? sh)
    {
        if (Binary is not { } bin || sh == null) return null;
        if (sh.Flash_start_address == 0 && sh.Start_address == 0) return null;
        if (Module.OpenOther(bin, sh)) return null;
        string title = $"Symbol: {sh.SmartVarname} [{Path.GetFileName(bin.FileName)}]";
        if (Viewers.OfType<MapViewerViewModel>().FirstOrDefault(v => v.Title == title && v.FileName == bin.FileName) is { } open)
        {
            SelectedViewer = open;
            return open;
        }
        if (MapViewerViewModel.Create(this, bin, sh) is not { } viewer)
        {
            // only in SRAM (not in the file): read it from the ECU
            _ = Module.OpenSramOnlyAsync(bin, sh);
            return null;
        }
        viewer.OnlineMode = EcuConnected;
        Viewers.Add(viewer);
        SelectedViewer = viewer;
        if (Module.OnlineMapsFromEcu && EcuConnected && sh.Start_address > 0) _ = ReadMapFromEcuAsync(viewer);
        return viewer;
    }

    /// <summary>Shows a document, or the open one with the same title (the suites reused dock panels by title); the one shown.</summary>
    public DocumentViewModel ShowDocument(DocumentViewModel document)
    {
        // a tabbed window carries its selected tab's title, the tab is the document
        if (Viewers.FirstOrDefault(v => v is not DocumentTabsViewModel && v.Title == document.Title) is { } open)
        {
            SelectedViewer = open;
            return open;
        }
        Viewers.Add(document);
        SelectedViewer = document;
        return document;
    }

    /// <summary>
    /// tabdet_onSymbolSelect: a compare's two maps as tabs along the bottom of one window, first first and selected (the compare side,
    /// T7Suite's DockAsTab at index 0). It docked the compare viewer as a tab onto the visible panel of the same
    /// symbol (AutoDockSameSymbol), so a map open on its own moves into the window, and one that already has a window takes the
    /// other in. One side alone keeps its own window.
    /// </summary>
    public void ShowAsTabs(DocumentViewModel? first, DocumentViewModel? second)
    {
        if (first == null || second == null || first == second) return;
        DocumentTabsViewModel window = first.Tabs ?? second.Tabs ?? new DocumentTabsViewModel(this);
        foreach (DocumentViewModel d in new[] { first, second }.Where(d => d.Tabs != window))
        {
            LeaveTabs(d);
            window.Insert(d == first ? 0 : window.Items.Count, d);
            d.IsFloating = false;
        }
        // the caption before the window shows: Dock reads it when it makes the window
        window.Selected = first;
        if (!Viewers.Contains(window)) Viewers.Add(window);
        SyncDocked();
        SelectedViewer = first;
    }

    // a tab out of its window; the window goes with its last tab
    private void LeaveTabs(DocumentViewModel d)
    {
        if (d.Tabs is not { } window) return;
        window.Remove(d);
        if (window.Items.Count == 0) Viewers.Remove(window);
    }

    /// <summary>File → Save all: every viewer with changes saved; T7Suite's two messages.</summary>
    [RelayCommand]
    private async Task SaveAll()
    {
        var mutated = Viewers.OfType<MapViewerViewModel>().Where(v => v.Map.Mutated && v.CanSaveToFile).ToList();
        foreach (MapViewerViewModel v in mutated) await v.SaveCommand.ExecuteAsync(null);
        ShowInfo(mutated.Count > 0 ? "All pending changes saved to binary" : "Binary was already up to date!");
    }

    /// <summary>File → Create backup file.</summary>
    [RelayCommand]
    private void CreateBackupFile()
    {
        if (Binary is not { } bin) return;
        try
        {
            ShowInfo("Backup created: " + SuiteProject.Backup(bin, Project));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowInfo("Failed to create a backup: " + e.Message);
        }
    }

    /// <summary>The documents inside the main window (the workspace's inner windows); floating ones are left out.</summary>
    public ObservableCollection<DocumentViewModel> DockedViewers { get; } = [];

    // DockedViewers follows Viewers and each document's IsFloating, keeping Viewers' order; tabs show in their window
    private void SyncDocked()
    {
        var wanted = Viewers.Where(v => !v.IsFloating && v.Tabs == null).ToList();
        foreach (DocumentViewModel gone in DockedViewers.Except(wanted).ToList()) DockedViewers.Remove(gone);
        for (int i = 0; i < wanted.Count; i++)
            if (!DockedViewers.Contains(wanted[i])) DockedViewers.Insert(Math.Min(i, DockedViewers.Count), wanted[i]);
    }

    private void WatchFloating()
    {
        Viewers.CollectionChanged += (_, e) =>
        {
            foreach (DocumentViewModel d in e.NewItems?.OfType<DocumentViewModel>() ?? []) d.PropertyChanged += OnDocumentChanged;
            foreach (DocumentViewModel d in e.OldItems?.OfType<DocumentViewModel>() ?? []) d.PropertyChanged -= OnDocumentChanged;
            SyncDocked();
            // the realtime panel opened or closed: what binds to it (Shift+F5's autotune) follows
            OnPropertyChanged(nameof(Realtime));
        };
    }

    private void OnDocumentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.IsFloating)) SyncDocked();
    }

    /// <summary>Window → Dock all floating windows.</summary>
    [RelayCommand]
    private void DockAll()
    {
        foreach (DocumentViewModel d in Viewers.Where(v => v.IsFloating).ToList()) d.IsFloating = false;
    }

    /// <summary>The symbol list's selected rows (Export as tuning package).</summary>
    public IReadOnlyList<SymbolHelper> SelectedSymbols { get; set; } = [];

    /// <summary>StartTableViewer(name): clear the search, select the symbol and open it (axis editing, My Maps); the viewer shown.</summary>
    public MapViewerViewModel? OpenSymbolByName(string name)
    {
        if (Binary?.Find(name) is not { } sh)
        {
            ShowInfo($"Symbol {name} does not exist in this file");
            return null;
        }
        SearchText = "";
        SelectedSymbol = sh;
        return OpenMap(sh);
    }

    [RelayCommand]
    private Task CloseViewer(DocumentViewModel viewer) => CloseViewerAsync(viewer);

    /// <summary>Closes a document once it agrees (unsaved changes ask Yes / No / Cancel). False when cancelled.</summary>
    public async Task<bool> CloseViewerAsync(DocumentViewModel viewer)
    {
        // a tabbed window asks each tab about its own changes first and closes them all only then: Cancel on one keeps every tab
        if (viewer is DocumentTabsViewModel window)
        {
            foreach (DocumentViewModel tab in window.Items.ToList())
                if (!await tab.CanCloseAsync(this)) return false;
            foreach (DocumentViewModel tab in window.Items.ToList()) Close(tab);
            return true;
        }
        if (!await viewer.CanCloseAsync(this)) return false;
        Close(viewer);
        return true;
    }

    private void Close(DocumentViewModel viewer)
    {
        viewer.Closed();
        LeaveTabs(viewer);
        int i = Viewers.IndexOf(viewer);
        Viewers.Remove(viewer);
        if (SelectedViewer != viewer) return;
        // the neighbour as it shows: a tab's window keeps its selected tab
        DocumentViewModel? next = Viewers.Count == 0 ? null : Viewers[Math.Min(i, Viewers.Count - 1)];
        SelectedViewer = next?.Tabs ?? next;
    }

    /// <summary>Before the app closes: every viewer with unsaved changes gets the same question. False when cancelled.</summary>
    public async Task<bool> CloseMutatedViewersAsync()
    {
        foreach (MapViewerViewModel viewer in Viewers.OfType<MapViewerViewModel>().Where(v => v.Map.Mutated).ToList())
            if (!await CloseViewerAsync(viewer)) return false;
        return true;
    }

    [RelayCommand]
    private Task VerifyChecksum() => Module.VerifyChecksumAsync();

    /// <summary>gridViewSymbols_CellValueChanged: an edited user description is saved to &lt;bin&gt;.xml (not into a read-only folder).</summary>
    public void SaveUserDescriptions()
    {
        if (Binary is not { } bin) return;
        try
        {
            SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowInfo("The user descriptions could not be saved: " + e.Message);
        }
    }

    [RelayCommand]
    private Task OpenRecent(RecentFile file) => OpenPlainFileAsync(file.Path, false);

    /// <summary>File > Open / Recent: closes the project first (CloseProject; Lastprojectname = ""; LastOpenedType = 0).</summary>
    public async Task<bool> OpenPlainFileAsync(string path, bool showMessage)
    {
        CloseProject();
        Settings.Lastprojectname = "";
        bool ok = await OpenFileAsync(path, showMessage);
        Settings.LastOpenedType = 0;
        return ok;
    }

    /// <summary>
    /// After the settings dialog: what applies at once (SetupDisplayOptions) and the colour blind mode, which open viewers take
    /// too; the other viewer settings apply to new viewers. Each ECU applies its own.
    /// </summary>
    public void SettingsChanged()
    {
        Views.SymbolNumberConverter.Hex = Settings.ShowAddressesInHex;
        ApplyColorBlindMode();
        ApplySymbolListFont();
        ApplyMapCellGap();
        RefreshSymbols();
        RestartSramTimer();
        Logging.ApplyCanLogging(Settings.EnableCanLog);
        foreach (EcuModule m in Modules) m.SettingsChanged();
    }

    /// <summary>The colour blind mode into the application's resources, where SuiteTheme.axaml's styles hand it to every map control.</summary>
    private void ApplyColorBlindMode()
    {
        if (Application.Current is { } app) app.Resources["SuiteColorBlindMode"] = MapControls.HeatColor.ParseMode(Settings.ColorBlindMode);
    }

    /// <summary>The map viewers' cell gap into the application's resources, where SuiteTheme.axaml's style hands it to every map table.</summary>
    private void ApplyMapCellGap()
    {
        if (Application.Current is { } app) app.Resources["SuiteMapCellGap"] = (double)Math.Clamp(Settings.MapCellGap, 0, SuiteSettingsViewModel.MaxCellGap);
    }

    /// <summary>The symbol list's text size into the application's resources, its rows as tall as Fluent's grid makes them for its 15 px.</summary>
    private void ApplySymbolListFont()
    {
        if (Application.Current is not { } app) return;
        double size = Math.Clamp(Settings.SymbolListFontSize, SuiteSettingsViewModel.MinFontSize, SuiteSettingsViewModel.MaxFontSize);
        app.Resources["SuiteSymbolListFontSize"] = size;
        app.Resources["SuiteSymbolListRowHeight"] = Math.Round(size * 32 / 15);
    }

    // ---- projects ----

    /// <summary>OpenProject: the project's binary, its transaction log (purge offered above 2000 entries), a backup.</summary>
    public async Task<bool> OpenProjectAsync(string name)
    {
        if (SuiteProject.Open(Settings.ProjectFolder, name) is not { } project) return false;
        Settings.LastOpenedType = 1;
        if (!await OpenFileAsync(project.BinaryFile, false)) return false;
        int count = project.TransactionLog.TransCollection.Count;
        if (count > 2000 && AskOkCancel != null
            && await AskOkCancel($"The transaction log of this project holds {count} records, which slows down opening and saving. Purge it to the last 1000 records (the full log is kept as a .btl copy)?"))
        {
            project.TransactionLog.Purge();
        }
        project.CreateBackup();
        Project = project;
        Settings.Lastprojectname = name;
        Title = $"{Suite} [Project: {name}]";
        UpdateRollControls();
        return true;
    }

    /// <summary>CloseProject: no file, no project; open viewers stay.</summary>
    public void CloseProject()
    {
        if (Project == null) return;
        Project = null;
        Binary = null;
        Symbols = null;
        FileNameText = "No file";
        Settings.Lastfilename = "";
        Title = Suite;
        UpdateRollControls();
    }

    /// <summary>Create project: prefilled from the open file, the binary copied into the new project, which is opened.</summary>
    public ProjectPropertiesViewModel NewProjectProperties()
    {
        var p = new ProjectPropertiesViewModel();
        if (Binary is { } bin)
        {
            p.BinaryFile = bin.FileName;
            (p.CarModel, p.ProjectName) = Module.ProjectDefaults(bin);
        }
        return p;
    }

    public async Task CreateProjectAsync(ProjectPropertiesViewModel p)
    {
        string name;
        try
        {
            name = SuiteProject.Create(Settings.ProjectFolder, p.ToProperties(), p.BinaryFile);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException)
        {
            ShowInfo(e.Message);
            return;
        }
        // the folder name: T7Suite reopened by the typed name, which missed when characters were dropped
        await OpenProjectAsync(name);
    }

    public async Task EditProjectAsync(ProjectPropertiesViewModel p)
    {
        if (Project is not { } project) return;
        string before = project.Name;
        project.Edit(p.ToProperties());
        if (project.Name != before) await OpenProjectAsync(project.Name);
    }

    [RelayCommand]
    private void CloseProjectMenu()
    {
        CloseProject();
        Settings.Lastprojectname = "";
    }

    public void UpdateRollControls()
    {
        var entries = Project?.TransactionLog.TransCollection.Cast<TransactionEntry>().ToList() ?? [];
        HasTransactions = entries.Count > 0;
        CanRollBack = entries.Any(e => !e.IsRolledBack);
        CanRollForward = entries.Any(e => e.IsRolledBack);
    }

    /// <summary>The transaction log writes go to, null without a project.</summary>
    public TrionicTransactionLog? TransactionLog => Project?.TransactionLog;

    /// <summary>A note for the transaction when RequestProjectNotes is set and a project is open ("Remark for change").</summary>
    public async Task<string> AskTransactionNoteAsync()
    {
        if (Project == null || !Settings.RequestProjectNotes || AskText == null) return "";
        return await AskText("Remark for change") ?? "";
    }

    /// <summary>SignalTransactionLogChanged: the logbook line for the newest entry and the roll buttons.</summary>
    public void TransactionsAdded(int before)
    {
        if (Project is not { } project || Binary is not { } bin) return;
        foreach (TransactionEntry e in project.TransactionLog.TransCollection.Cast<TransactionEntry>().Skip(before))
            project.LogTransaction(bin, e);
        UpdateRollControls();
    }

    public void Roll(TransactionEntry entry, bool back)
    {
        if (Project is not { } project || Binary is not { } bin) return;
        try
        {
            project.Roll(bin, entry, back);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
        RefreshViewers(bin.FileName);
        UpdateRollControls();
    }

    [RelayCommand]
    private void RollBack()
    {
        if (Project?.UndoTarget is { } e) Roll(e, true);
    }

    [RelayCommand]
    private void RollForward()
    {
        if (Project?.RedoTarget is { } e) Roll(e, false);
    }

    /// <summary>Viewers of the file without unsaved changes show the file again after it changed underneath them.</summary>
    public void RefreshViewers(string file)
    {
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.FileName == file && !v.Map.Mutated && !v.IsReadOnly))
            v.ReadCommand.Execute(null);
    }

    [RelayCommand]
    private async Task AddNote()
    {
        if (Project is not { } project || AskText == null) return;
        if (await AskText("Remark for change") is { Length: > 0 } note) project.Logbook.WriteLogbookEntry(LogbookEntryType.Note, note);
    }

    /// <summary>Rebuild file: replace the project's binary, or return the rebuilt file for the view to save elsewhere.</summary>
    public string? Rebuild(DateTime upTo, bool storeAsCurrent)
    {
        if (Project is not { } project || Binary is not { } bin) return null;
        string rebuilt = project.Rebuild(upTo, bin);
        if (!storeAsCurrent) return rebuilt;
        File.Copy(rebuilt, project.BinaryFile, true);
        File.Delete(rebuilt);
        RefreshViewers(project.BinaryFile);
        UpdateRollControls();
        return null;
    }

    // closing the main window, not Shutdown(): the window asks about unsaved maps first
    [RelayCommand]
    private static void Exit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close();

    // ---- MRU: file name and path per entry, appended when new, no limit (T7Suite's HKCU\Software\T7SuitePro\MRUList); one list for
    // every ECU, the old suites' lists added once (SettingsKey.ImportOnce) ----

    private void LoadRecent()
    {
        // entries are numbered: a path's backslashes would read as subkeys
        using var key = SettingsKey.Open(Suite, "MRUList");
        var entries = key.GetValueNames().OrderBy(n => int.TryParse(n, out int i) ? i : int.MaxValue).Select(n => (string)key.GetValue(n)!).ToList();
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

/// <summary>A cell of the symbol list's auto filter row.</summary>
public partial class ColumnFilter(string header, string path) : ObservableObject
{
    public string Header { get; } = header;
    public string Path { get; } = path;

    [ObservableProperty]
    private string _text = "";
}

