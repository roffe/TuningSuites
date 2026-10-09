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
using TrionicCANLib.API;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T7App.ViewModels;

public record RecentFile(string Name, string Path);

/// <summary>
/// The main window: the opened binary (frmMain.OpenFile), its symbol list and the open map viewers.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public const string Suite = "T7SuitePro";
    private static readonly string Version =
        typeof(MainWindowViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    public AppSettings Settings { get; } = new(new T7SuiteRegistry());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(OriginalFile))]
    private T7Binary? _binary;

    [ObservableProperty]
    private DataGridCollectionView? _symbols;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private SymbolHelper? _selectedSymbol;

    [ObservableProperty]
    private DocumentViewModel? _selectedViewer;

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

    /// <summary>The open project, null when a plain file is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectOpen))]
    private T7Project? _project;

    public bool IsProjectOpen => Project != null;

    [ObservableProperty] private bool _canRollBack;
    [ObservableProperty] private bool _canRollForward;
    [ObservableProperty] private bool _hasTransactions;

    /// <summary>A line of text from the user (frmChangeNote), null when cancelled; answered by the view.</summary>
    public Func<string, Task<string?>>? AskText { get; set; }

    /// <summary>Ok / Cancel question, answered by the view.</summary>
    public Func<string, Task<bool>>? AskOkCancel { get; set; }

    /// <summary>The open tabs: map viewers, compare results.</summary>
    public ObservableCollection<DocumentViewModel> Viewers { get; } = new();
    public ObservableCollection<RecentFile> Recent { get; } = new();

    /// <summary>A message for the user (frmInfoBox / MessageBox), shown by the view.</summary>
    public event Action<string>? Info;

    public void ShowInfo(string text) => Info?.Invoke(text);

    /// <summary>Yes / No / Cancel question (null = Cancel), answered by the view.</summary>
    public Func<string, Task<bool?>>? AskYesNoCancel { get; set; }

    public MainWindowViewModel()
    {
        Trionic7File.onProgress += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Percentage >= 55 ? "" : e.Info);
        LoadRecent();
        InitEcu();
        WatchFloating();
    }

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
        if (Viewers.OfType<MapViewerViewModel>().FirstOrDefault(v => v.Title == title && v.FileName == bin.FileName) is { } open)
        {
            SelectedViewer = open;
            return;
        }
        if (MapViewerViewModel.Create(this, bin, sh) is not { } viewer)
        {
            // only in SRAM (not in the file): read it from the ECU
            _ = OpenSramSymbolAsync(bin, sh);
            return;
        }
        viewer.OnlineMode = Ecu.IsConnected;
        Viewers.Add(viewer);
        SelectedViewer = viewer;
    }

    /// <summary>Shows a document, or the open one with the same title (T7Suite reused dock panels by title).</summary>
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

    /// <summary>Actions → Airmass result viewer, when the bin has the tables it needs (T7Suite silently did nothing otherwise).</summary>
    [RelayCommand]
    private void ShowAirmassResult()
    {
        if (Binary is not { } bin) return;
        if (!AirmassResult.Available(bin))
        {
            ShowInfo("This file lacks the pedal, torque or airmass tables the airmass result viewer needs");
            return;
        }
        ShowDocument(new AirmassResultViewModel(this, bin));
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
    public System.Collections.Generic.IReadOnlyList<SymbolHelper> SelectedSymbols { get; set; } = [];

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

    /// <summary>MapViewerEx's close: unsaved changes ask Yes (save) / No (discard) / Cancel (keep open). False when cancelled.</summary>
    public async Task<bool> CloseViewerAsync(DocumentViewModel viewer)
    {
        if (viewer is MapViewerViewModel { Map.Mutated: true } map && AskYesNoCancel != null)
        {
            SelectedViewer = viewer;
            bool? save = await AskYesNoCancel("Data was mutated, do you want to save these changes in you binary?");
            if (save == null) return false;
            if (save == true)
            {
                await map.SaveCommand.ExecuteAsync(null);
                if (map.Map.Mutated) return false; // the save failed, keep the changes on screen
            }
        }
        if (viewer is RealtimeViewModel realtime) realtime.Stop();
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

    /// <summary>The ribbon's Verify checksum: with AutoChecksum a mismatch offers to recalculate.</summary>
    [RelayCommand]
    private async Task VerifyChecksum()
    {
        if (Binary is not { } bin) return;
        ChecksumResult result = await Task.Run(() => ChecksumT7.VerifyChecksum(bin.FileName, Settings.AutoChecksum, Settings.AutoFixFooter,
            (_, _, _) => UserPrompt.AskYesNo("Checksums did not verify ok, do you want to recalculate and update the checksums?", "Question")));
        ShowInfo(result == ChecksumResult.Ok ? "Checksums verified and all matched!" : "Checksums did not verify ok!");
    }

    /// <summary>gridViewSymbols_CellValueChanged: an edited user description is saved to &lt;bin&gt;.xml.</summary>
    public void SaveUserDescriptions()
    {
        if (Binary is { } bin) SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
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

    // ---- map menus ----

    public string MyMapsFile => Path.Combine(SettingsKey.Folder(Suite), "mymaps.xml");

    /// <summary>My Maps changed: the view rebuilds its menu.</summary>
    public event Action? MyMapsChanged;

    public void SaveMyMaps(IEnumerable<MapShortcut> maps)
    {
        MapMenus.SaveMyMaps(MyMapsFile, maps);
        MyMapsChanged?.Invoke();
    }

    public void AddToMyMaps(SymbolHelper sh)
    {
        MapMenus.AddToMyMaps(MyMapsFile, sh.Varname);
        MyMapsChanged?.Invoke();
    }

    /// <summary>A map button (quick maps / My Maps). The AFR feedback maps of My Maps need the realtime features.</summary>
    [RelayCommand]
    private void OpenShortcut(MapShortcut shortcut)
    {
        if (shortcut.Symbol is "targetafr" or "feedbackafr")
        {
            ShowInfo("The AFR maps come with the realtime features.");
            return;
        }
        OpenSymbolByName(shortcut.Symbol);
    }

    /// <summary>After the settings dialog: what applies at once (T7Suite's SetupDisplayOptions); viewer settings apply to new viewers.</summary>
    public void SettingsChanged()
    {
        Views.SymbolNumberConverter.Hex = Settings.ShowAddressesInHex;
        Symbols?.Refresh();
        RestartSramTimer();
        OnPropertyChanged(nameof(FeedbackMapCaption));
        OnPropertyChanged(nameof(ClearFeedbackCaption));
    }

    /// <summary>A descriptor import changed names: the list shows them (the import saved &lt;bin&gt;.xml).</summary>
    public void ImportSymbols(Action<T7Binary> import)
    {
        if (Binary is not { } bin) return;
        try
        {
            import(bin);
        }
        catch (Exception e) when (e is IOException or System.Data.DataException or System.Xml.XmlException)
        {
            ShowInfo("Failed to import: " + e.Message);
        }
        Symbols?.Refresh();
    }

    /// <summary>Search map content: "No results found..." or a results tab.</summary>
    public void SearchMaps(MapSearchOptions options)
    {
        if (Binary is not { } bin) return;
        List<SymbolHelper> hits = MapSearch.Find(bin, options);
        if (hits.Count == 0) ShowInfo("No results found...");
        else ShowDocument(new SearchResultsViewModel(this, bin.FileName, hits));
    }

    // ---- compare ----

    /// <summary>"Compare symbols with other binary": the results open as a tab.</summary>
    public async Task CompareToFileAsync(string otherFile)
    {
        if (Binary is not { } bin || bin.Symbols.Count == 0) return;
        if (!T7Binary.IsValidFile(otherFile))
        {
            ShowInfo("File is not a Trionic 7 binary file!");
            return;
        }
        IsBusy = true;
        try
        {
            var (other, rows) = await Task.Run(() =>
            {
                T7Binary o = T7Binary.Open(otherFile, Settings.ApplicationLanguage, false);
                return (o, T7Compare.Compare(bin, o, Settings.ApplicationLanguage));
            });
            ShowDocument(CompareResultsViewModel.Binaries(this, bin, other, rows));
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    /// <summary>"Compare to original file": the stock bin with this part number, when Binaries has exactly one.</summary>
    public string? OriginalFile => Binary is { } bin ? T7Compare.OriginalFile(bin) : null;

    [RelayCommand]
    private Task CompareToOriginal() => OriginalFile is { } file ? CompareToFileAsync(file) : Task.CompletedTask;

    /// <summary>Transfer maps: the selection remembered between runs (T7Suite's TransferSettings key).</summary>
    public HashSet<string> LastTransferSelection()
    {
        using var key = SettingsKey.Open(Suite, "TransferSettings");
        return key.GetValueNames().ToHashSet();
    }

    public List<string> TransferMaps(string target, HashSet<string> selected)
    {
        using (var key = SettingsKey.Open(Suite, "TransferSettings"))
        {
            foreach (string old in key.GetValueNames()) key.DeleteValue(old);
            foreach (string name in selected) key.SetValue(name, "1");
        }
        int before = TransactionLog?.TransCollection.Count ?? 0;
        List<string> report = T7Compare.TransferMaps(Binary!, target, selected, Settings.ApplicationLanguage, Settings.AutoFixFooter, TransactionLog);
        TransactionsAdded(before);
        return report;
    }

    // ---- projects ----

    /// <summary>OpenProject: the project's binary, its transaction log (purge offered above 2000 entries), a backup.</summary>
    public async Task<bool> OpenProjectAsync(string name)
    {
        if (T7Project.Open(Settings.ProjectFolder, name) is not { } project) return false;
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
        Title = $"T7SuitePro [Project: {name}]";
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
        Title = "T7SuitePro";
        UpdateRollControls();
    }

    /// <summary>Create project: prefilled from the open file, the binary copied into the new project, which is opened.</summary>
    public ProjectPropertiesViewModel NewProjectProperties()
    {
        var p = new ProjectPropertiesViewModel();
        if (Binary is { } bin)
        {
            var header = new TrionicCANLib.Checksum.T7FileHeader();
            header.init(bin.FileName, false);
            p.BinaryFile = bin.FileName;
            p.CarModel = header.getCarDescription().Trim();
            p.ProjectName = header.getPartNumber().Trim() + " " + header.getSoftwareVersion().Trim();
        }
        return p;
    }

    public async Task CreateProjectAsync(ProjectPropertiesViewModel p)
    {
        string name;
        try
        {
            name = T7Project.Create(Settings.ProjectFolder, p.ToProperties(), p.BinaryFile);
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
            project.Roll(bin, entry, back, Settings.AutoFixFooter);
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
        if (Project is not { } project) return null;
        string rebuilt = project.Rebuild(upTo, Settings.AutoFixFooter);
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

    public FirmwareInfoViewModel? FirmwareInfo() =>
        Binary is { } bin && File.Exists(bin.FileName) ? new FirmwareInfoViewModel(T7.FirmwareInfo.Read(bin), Settings.WriteTimestampInBinary) : null;

    /// <summary>The firmware information dialog's OK: patches the current bin and updates its checksum.</summary>
    public void ApplyFirmware(FirmwareInfoViewModel firmware, Func<string, bool> askYesNo)
    {
        if (Binary is not { } bin) return;
        try
        {
            int before = TransactionLog?.TransCollection.Count ?? 0;
            T7.FirmwareInfo.Apply(bin, firmware.ToEdit(), Settings.AutoFixFooter, Settings.WriteTimestampInBinary, askYesNo, TransactionLog);
            TransactionsAdded(before);
            RefreshViewers(bin.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
    }

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
