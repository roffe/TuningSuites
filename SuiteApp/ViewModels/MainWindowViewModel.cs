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

namespace SuiteApp.ViewModels;

public record RecentFile(string Name, string Path);

/// <summary>A column of the symbol list: its caption and SymbolHelper property; hidden ones only show from the column chooser.</summary>
public record SymbolColumn(string Header, string Path, bool Visible = true);

/// <summary>A predefined filter of the symbol list (T8Suite's "Only symbols within binary").</summary>
public record SymbolFilter(string Name, Func<SymbolHelper, bool> Matches);

/// <summary>
/// The main window the suites share (frmMain / Form1): the opened binary, its symbol list, the open documents and the project.
/// Each suite's subclass opens its own binaries and adds the features only it has.
/// </summary>
public abstract partial class MainWindowViewModel : ObservableObject
{
    /// <summary>"T7SuitePro": the window title, the settings folder and registry key.</summary>
    public string Suite { get; }

    /// <summary>"T7Suite": message captions.</summary>
    public string Caption { get; }

    /// <summary>The window title's name; the settings name unless the suite says otherwise (T5: settings "T5Suite2", title "T5Suite").</summary>
    protected virtual string TitleName => Suite;

    // the app's own version, not this library's
    protected string Version =>
        GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public AppSettings Settings { get; }

    /// <summary>The suite's settings key, for what keeps its own subkeys (log filters, symbol colours).</summary>
    protected SuiteRegistry Registry { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    private SuiteBinary? _binary;

    [ObservableProperty]
    private DataGridCollectionView? _symbols;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private SymbolHelper? _selectedSymbol;

    [ObservableProperty]
    private DocumentViewModel? _selectedViewer;

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

    protected MainWindowViewModel(string suite, string caption, SuiteRegistry registry)
    {
        Suite = suite;
        Caption = caption;
        Registry = registry;
        Settings = new AppSettings(registry);
        Title = $"{TitleName} v{Version}";
        ColumnFilters = new(SymbolColumns.Where(c => c.Visible).Select(NewFilter));
        LoadRecent();
        WatchFloating();
        RestartSramTimer();
    }

    // ---- what each suite does its own way ----

    /// <summary>The binary's size, for converting S19 files.</summary>
    protected abstract uint FileLength { get; }

    /// <summary>Open file with an S19: the binary it converts to next to it, null when it didn't (T5Suite has its own converter).</summary>
    protected virtual string? ConvertS19(string path) => new Srecord().ConvertSrecToBin(path, FileLength, out string bin, true) ? bin : null;

    /// <summary>The file is one of this suite's binaries.</summary>
    protected abstract bool IsValidFile(string path);

    /// <summary>Shown when an opened file isn't one; null when IsValidFile says why itself (T8Suite).</summary>
    protected virtual string? InvalidFileMessage => null;

    /// <summary>Parses the file (on a worker thread).</summary>
    protected abstract SuiteBinary OpenBinary(string path);

    /// <summary>After a file opened and shows (T8Suite checks the checksum then).</summary>
    protected virtual Task OnOpenedAsync(SuiteBinary bin) => Task.CompletedTask;

    /// <summary>
    /// The symbol list's columns in order, the same for every suite (each old suite had its own); the ones a suite has
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
        }.Where(c => HasSymbolColumn(c.Path)),
    ];

    /// <summary>A symbol list column the suite fills (Type: T7 / T8, Bitmask: T8).</summary>
    protected virtual bool HasSymbolColumn(string path) => true;

    /// <summary>The symbol list's order inside its category groups (T7Suite: largest first).</summary>
    protected virtual IEnumerable<SymbolHelper> OrderSymbols(IEnumerable<SymbolHelper> symbols) => symbols.OrderByDescending(s => s.Length);

    /// <summary>Symbol names coloured by their prefix (T7Suite did, T8Suite didn't).</summary>
    public virtual bool ColorSymbolNames => true;

    /// <summary>T5Suite coloured the description cell by the symbol's category instead.</summary>
    public virtual bool ColorDescriptionsByCategory => false;

    /// <summary>T8Suite's map preview popup (Settings → Show map preview popup); T7Suite has none.</summary>
    public virtual bool ShowsMapPreview => false;

    /// <summary>The map preview popup's content: the hovered map from the file, read only; null for one that only lives in SRAM.</summary>
    public MapViewerViewModel? MapPreview(SymbolHelper sh) => ShowsMapPreview && Binary is { } bin ? MapViewerViewModel.Create(this, bin, sh, readOnly: true) : null;

    /// <summary>The symbol list's predefined filters; none for T7Suite.</summary>
    public virtual IReadOnlyList<SymbolFilter> SymbolFilters => [];

    /// <summary>The filter a newly opened file starts with.</summary>
    protected virtual SymbolFilter? DefaultSymbolFilter => null;

    /// <summary>A symbol that doesn't open as a map (T8Suite's bit mask symbols); true when handled.</summary>
    protected virtual bool OpenOther(SuiteBinary bin, SymbolHelper sh) => false;

    /// <summary>The suite's realtime session is open: new viewers show the ECU's data.</summary>
    protected abstract bool EcuConnected { get; }

    /// <summary>Verify checksum, the suite's way.</summary>
    protected abstract Task VerifyChecksumAsync();

    /// <summary>Create project: what to prefill from the open binary (car model, project name).</summary>
    protected abstract (string carModel, string projectName) ProjectDefaults(SuiteBinary bin);

    /// <summary>Why the app can't close now (a flash in progress), null when it can.</summary>
    public virtual string? CloseBlocker => null;

    /// <summary>The app closes: release the ECU.</summary>
    public virtual void Shutdown()
    {
    }

    /// <summary>The MRU list the old suite kept outside HKCU\Software\MattiasC, imported once; null when it had none.</summary>
    protected virtual string? LegacyMruKey => null;

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

    /// <summary>frmMain.OpenFile: S19 is converted first, the file must be one of the suite's binaries, then its symbols are read.</summary>
    public async Task<bool> OpenFileAsync(string path, bool showMessage)
    {
        IsBusy = true;
        try
        {
            if (path.EndsWith(".s19", StringComparison.OrdinalIgnoreCase))
            {
                string source = path;
                if (await Task.Run(() => ConvertS19(source)) is { } converted) path = converted;
                else Info?.Invoke("Failed to convert S19 file to binary");
            }
            Settings.Lastfilename = path;
            if (!await Task.Run(() => IsValidFile(path)))
            {
                Binary = null;
                Symbols = null;
                Title = $"{TitleName} v{Version} [ none ]";
                if (showMessage && InvalidFileMessage is { } message) Info?.Invoke(message);
                return false;
            }
            AddRecent(path);
            ReadOnlyText = new FileInfo(path).IsReadOnly ? "File is READ ONLY" : "File access OK";
            SuiteBinary bin = await Task.Run(() => OpenBinary(path));

            // grouped by category like the old grid; the view filters as it's made, so the suite's filter first
            SymbolFilter = DefaultSymbolFilter;
            var rows = OrderSymbols(bin.Symbols.Cast<SymbolHelper>()).ToList();
            var view = new DataGridCollectionView(rows) { Filter = MatchesSearch };
            foreach (string group in SymbolGroupPaths) view.GroupDescriptions.Add(new DataGridPathGroupDescription(group));
            Binary = bin;
            Symbols = view;
            Title = $"{TitleName} v{Version} [ {Path.GetFileName(path)} ]";
            FileNameText = Path.GetFileNameWithoutExtension(path);
            OpenClosedText = bin.IsSoftwareOpen ? "Open/dev binary" : "Normal binary";
            await OnOpenedAsync(bin);
            return true;
        }
        finally
        {
            ProgressText = "";
            IsBusy = false;
        }
    }

    // ---- symbol list ----

    partial void OnSearchTextChanged(string value) => RefreshSymbols();

    /// <summary>Set by the symbol list: ends a cell edit in its grid (which then saves the user descriptions).</summary>
    public Action? EndSymbolEdit { get; set; }

    /// <summary>The symbol view with a user description edit committed: an open edit forbids its refresh, sorting and grouping.</summary>
    private DataGridCollectionView? CommittedSymbols()
    {
        EndSymbolEdit?.Invoke();
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

    /// <summary>The symbol list's groups on open and for "Group by category": the category (T5Suite: then the subcategory).</summary>
    protected virtual string[] SymbolGroupPaths => [nameof(SymbolHelper.Category)];

    /// <summary>Group by this column / by category (the suites' default) / not at all.</summary>
    public void GroupSymbols(string? path)
    {
        if (CommittedSymbols() is not { } view) return;
        view.GroupDescriptions.Clear();
        foreach (string group in GroupPaths(path)) view.GroupDescriptions.Add(new DataGridPathGroupDescription(group));
    }

    private string[] GroupPaths(string? path) => path == nameof(SymbolHelper.Category) ? SymbolGroupPaths : path != null ? [path] : [];

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
    private void OpenSymbol(SymbolHelper? sh)
    {
        if (Binary is not { } bin || sh == null) return;
        if (sh.Flash_start_address == 0 && sh.Start_address == 0) return;
        if (OpenOther(bin, sh)) return;
        string title = $"Symbol: {sh.SmartVarname} [{Path.GetFileName(bin.FileName)}]";
        if (Viewers.OfType<MapViewerViewModel>().FirstOrDefault(v => v.Title == title && v.FileName == bin.FileName) is { } open)
        {
            SelectedViewer = open;
            return;
        }
        if (MapViewerViewModel.Create(this, bin, sh) is not { } viewer)
        {
            // only in SRAM (not in the file): read it from the ECU
            _ = OpenSramOnlyAsync(bin, sh);
            return;
        }
        viewer.OnlineMode = EcuConnected;
        Viewers.Add(viewer);
        SelectedViewer = viewer;
        if (OnlineMapsFromEcu && EcuConnected && sh.Start_address > 0) _ = ReadMapFromEcuAsync(viewer);
    }

    /// <summary>Shows a document, or the open one with the same title (the suites reused dock panels by title).</summary>
    public void ShowDocument(DocumentViewModel document)
    {
        if (Viewers.FirstOrDefault(v => v.Title == document.Title) is { } open)
        {
            SelectedViewer = open;
            return;
        }
        Viewers.Add(document);
        SelectedViewer = document;
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

    // DockedViewers follows Viewers and each document's IsFloating, keeping Viewers' order
    private void SyncDocked()
    {
        var wanted = Viewers.Where(v => !v.IsFloating).ToList();
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

    partial void OnSelectedViewerChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    /// <summary>StartTableViewer(name): clear the search, select the symbol and open it (axis editing, My Maps).</summary>
    public void OpenSymbolByName(string name)
    {
        if (Binary?.Find(name) is not { } sh)
        {
            ShowInfo($"Symbol {name} does not exist in this file");
            return;
        }
        SearchText = "";
        SelectedSymbol = sh;
        OpenSymbol(sh);
    }

    [RelayCommand]
    private Task CloseViewer(DocumentViewModel viewer) => CloseViewerAsync(viewer);

    /// <summary>Closes a document once it agrees (unsaved changes ask Yes / No / Cancel). False when cancelled.</summary>
    public async Task<bool> CloseViewerAsync(DocumentViewModel viewer)
    {
        if (!await viewer.CanCloseAsync(this)) return false;
        viewer.Closed();
        int i = Viewers.IndexOf(viewer);
        Viewers.Remove(viewer);
        if (SelectedViewer == viewer) SelectedViewer = Viewers.Count == 0 ? null : Viewers[Math.Min(i, Viewers.Count - 1)];
        return true;
    }

    /// <summary>Before the app closes: every viewer with unsaved changes gets the same question. False when cancelled.</summary>
    public async Task<bool> CloseMutatedViewersAsync()
    {
        foreach (MapViewerViewModel viewer in Viewers.OfType<MapViewerViewModel>().Where(v => v.Map.Mutated).ToList())
            if (!await CloseViewerAsync(viewer)) return false;
        return true;
    }

    [RelayCommand]
    private Task VerifyChecksum() => VerifyChecksumAsync();

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

    /// <summary>After the settings dialog: what applies at once (SetupDisplayOptions); viewer settings apply to new viewers.</summary>
    public virtual void SettingsChanged()
    {
        Views.SymbolNumberConverter.Hex = Settings.ShowAddressesInHex;
        RefreshSymbols();
        RestartSramTimer();
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
        Title = $"{TitleName} [Project: {name}]";
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
        Title = TitleName;
        UpdateRollControls();
    }

    /// <summary>Create project: prefilled from the open file, the binary copied into the new project, which is opened.</summary>
    public ProjectPropertiesViewModel NewProjectProperties()
    {
        var p = new ProjectPropertiesViewModel();
        if (Binary is { } bin)
        {
            p.BinaryFile = bin.FileName;
            (p.CarModel, p.ProjectName) = ProjectDefaults(bin);
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

    // ---- MRU: file name and path per entry, appended when new, no limit (T7Suite's HKCU\Software\T7SuitePro\MRUList) ----

    private void LoadRecent()
    {
        // entries are numbered: a path's backslashes would read as subkeys
        using var key = SettingsKey.Open(Suite, "MRUList");
        var entries = key.GetValueNames().OrderBy(n => int.TryParse(n, out int i) ? i : int.MaxValue).Select(n => (string)key.GetValue(n)!).ToList();
        if (entries.Count == 0 && LegacyMruKey is { } legacy)
        {
            // once: what the old suite kept outside HKCU\Software\MattiasC
            entries = SettingsKey.ImportRegistry(legacy).Values.Distinct().ToList();
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

/// <summary>A cell of the symbol list's auto filter row.</summary>
public partial class ColumnFilter(string header, string path) : ObservableObject
{
    public string Header { get; } = header;
    public string Path { get; } = path;

    [ObservableProperty]
    private string _text = "";
}
