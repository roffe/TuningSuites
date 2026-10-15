using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using MapControls;

namespace SuiteApp.ViewModels;

/// <summary>
/// One ECU's part of the main window (what T5Suite's, T7Suite's and T8Suite's main windows did their own way): how its binaries open
/// and check, its symbol list, its session with the ECU and realtime panel, and the features only it has. The main window holds one
/// per ECU; the open file's module is <see cref="MainWindowViewModel.Module"/>. The members below that the main window's own have
/// are that main window's, so a module reads as the suite's main window did.
/// </summary>
public abstract partial class EcuModule : ObservableObject
{
    /// <param name="main">The main window.</param>
    /// <param name="name">"T7": the settings section, the partnumber tab, the menus' ECU.</param>
    /// <param name="registry">The ECU's settings (a section of the program's, TxSuite/T7).</param>
    /// <param name="former">The old suite's settings (T7SuitePro), imported into the section once.</param>
    protected EcuModule(MainWindowViewModel main, string name, SuiteRegistry registry, string former)
    {
        Main = main;
        Name = name;
        Registry = registry;
        SettingsKey.ImportOnce(former, registry.getRegistryPath(), [.. AppSettings.SharedNames, .. ProgramNames]);
        Settings = new AppSettings(registry);
        main.PropertyChanged += (_, e) => OnMainPropertyChanged(e.PropertyName);
    }

    // what the program keeps once besides the shared options: the symbol list's width and the skin
    private static readonly string[] ProgramNames = ["SymbolListWidth", "SymbolListProportion", "Skin"];

    public MainWindowViewModel Main { get; }

    /// <summary>"T5", "T7" or "T8".</summary>
    public string Name { get; }

    /// <summary>The ECU's settings: the shared options and its own (Settings → Common and its tab).</summary>
    public AppSettings Settings { get; }

    /// <summary>The ECU's settings key, for what keeps its own subkeys (log filters, symbol colours, transfer selection).</summary>
    public SuiteRegistry Registry { get; }

    /// <summary>The ECU's section of the program's settings ("TxSuite/T7"): its subkeys, and its folder for My Maps and the realtime layout.</summary>
    public string SettingsPath => Registry.getRegistryPath();

    /// <summary>The main window changed (the open file, the connection, the snapshot, ...).</summary>
    protected virtual void OnMainPropertyChanged(string? name)
    {
    }

    /// <summary>The open file is this ECU's.</summary>
    public bool IsCurrent => Main.Module == this && Main.Binary != null;

    // ---- opening ----

    /// <summary>The file is this ECU's kind of file, as far as its size and identity show; nothing is said about it.</summary>
    public virtual bool Claims(string path) => IsValidFile(path);

    /// <summary>The binary's size, for converting S19 files.</summary>
    protected internal abstract uint FileLength { get; }

    /// <summary>Open file with an S19: the binary it converts to next to it, null when it didn't (T5 has its own converter).</summary>
    protected internal virtual string? ConvertS19(string path) => new Srecord().ConvertSrecToBin(path, FileLength, out string bin, true) ? bin : null;

    /// <summary>The file is one of this ECU's binaries.</summary>
    protected internal abstract bool IsValidFile(string path);

    /// <summary>Shown when a file isn't one; null when IsValidFile says why itself (T8).</summary>
    protected internal virtual string? InvalidFileMessage => null;

    /// <summary>Parses the file (on a worker thread).</summary>
    protected internal abstract SuiteBinary OpenBinary(string path);

    /// <summary>After a file opened and shows (T5 and T8 check the checksum then).</summary>
    protected internal virtual Task OnOpenedAsync(SuiteBinary bin) => Task.CompletedTask;

    /// <summary>Verify checksum, the ECU's way.</summary>
    protected internal abstract Task VerifyChecksumAsync();

    /// <summary>Create project: what to prefill from the open binary (car model, project name).</summary>
    protected internal abstract (string carModel, string projectName) ProjectDefaults(SuiteBinary bin);

    /// <summary>The open file's VIN for Actions → VIN decoder; "" where the files carry none (T5).</summary>
    public virtual string BinaryVin => "";

    /// <summary>After the settings dialog: what the ECU applies at once.</summary>
    public virtual void SettingsChanged()
    {
    }

    // ---- the symbol list ----

    /// <summary>A symbol list column the ECU fills (Type: T7 / T8, Bitmask: T8).</summary>
    protected internal virtual bool HasSymbolColumn(string path) => true;

    /// <summary>The symbol list's order inside its category groups (T7: largest first).</summary>
    protected internal virtual IEnumerable<SymbolHelper> OrderSymbols(IEnumerable<SymbolHelper> symbols) => symbols.OrderByDescending(s => s.Length);

    /// <summary>The symbol list's groups on open and for "Group by category": the category (T5: then the subcategory).</summary>
    protected internal virtual string[] SymbolGroupPaths => [nameof(SymbolHelper.Category)];

    /// <summary>Symbol names coloured by their prefix (T7Suite did, T8Suite didn't).</summary>
    public virtual bool ColorSymbolNames => true;

    /// <summary>T5Suite coloured the description cell by the symbol's category instead.</summary>
    public virtual bool ColorDescriptionsByCategory => false;

    /// <summary>The symbol list's predefined filters; none for T7.</summary>
    public virtual IReadOnlyList<SymbolFilter> SymbolFilters => [];

    /// <summary>The filter a newly opened file starts with.</summary>
    protected internal virtual SymbolFilter? DefaultSymbolFilter => null;

    /// <summary>A symbol that doesn't open as a map (T8's bit mask symbols, T5's Pgm_mod!); true when handled.</summary>
    protected internal virtual bool OpenOther(SuiteBinary bin, SymbolHelper sh) => false;

    // ---- map viewers ----

    /// <summary>The closed-loop mark: T7Suite's "Closed loop indicator" setting (none by default).</summary>
    public virtual OpenLoopMark OpenLoopMarkStyle => (OpenLoopMark)Math.Clamp(Settings.StandardFill, 0, 2);

    /// <summary>The view a map opens in: "Default view type for maps" as it is (T7Suite's and T8Suite's MapViewerFactory).</summary>
    public virtual MapViewType DefaultMapView =>
        (int)Settings.DefaultViewType is var v && v <= (int)MapViewType.Ascii ? (MapViewType)v : MapViewType.Easy;

    /// <summary>A map's cell marks in a bin (T5Suite's padlocks and adaption boxes); null for none, as T7Suite and T8Suite.</summary>
    public virtual Func<int, CellMarks>? CellMarks(SuiteBinary bin, string mapName) => null;

    // ---- the ECU ----

    /// <summary>The ECU's realtime session is open: new viewers show the ECU's data.</summary>
    protected internal abstract bool EcuConnected { get; }

    /// <summary>Opens the realtime session: the status bar's text, null when it didn't open. Throws a message when no adapter is set.</summary>
    protected internal abstract Task<string?> ConnectEcuAsync();

    /// <summary>The status bar's text when the session didn't open.</summary>
    protected internal abstract string ConnectFailedText { get; }

    protected internal abstract Task DisconnectEcuAsync();

    /// <summary>The symbol's bytes from the ECU's SRAM, null when the read failed.</summary>
    protected internal abstract Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh);

    /// <summary>The bytes into the ECU's SRAM; false when the ECU didn't accept them.</summary>
    protected internal abstract Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data);

    /// <summary>Why the ECU didn't take a map.</summary>
    protected internal virtual string WriteRefusedText(string map) => $"The ECU did not accept {map}.";

    /// <summary>Viewers the auto update re-reads: online ones without edits.</summary>
    protected internal virtual bool AutoUpdates(MapViewerViewModel viewer) => viewer.OnlineMode && !viewer.Map.Mutated;

    /// <summary>Read ECU: the flash into a file.</summary>
    public abstract Task ReadEcuAsync(string file);

    /// <summary>Flash current file to ECU (askYesNo for the questions on the way).</summary>
    public abstract Task FlashEcuAsync(Func<string, Task<bool>> askYesNo);

    /// <summary>Get fault codes (OBDII): the ECU's codes with descriptions, null when there was nothing to show.</summary>
    public abstract Task<List<FaultCode>?> ReadFaultCodesAsync();

    /// <summary>The fault code window's Clear: the code (or all of them, where the ECU can only do that), then the codes again.</summary>
    public abstract Task<List<FaultCode>?> ClearFaultCodeAsync(string code);

    /// <summary>The view the SRAM compares' difference maps open in (snapshot to bin, two snapshots), null for the default one.</summary>
    protected internal virtual MapViewType? SramToBinDifferenceView => null;
    protected internal virtual MapViewType? SramDifferenceView => null;

    /// <summary>T5Suite: while connected every map shows the ECU's SRAM, and a viewer's Save writes SRAM and the file in one go.</summary>
    protected internal virtual bool OnlineMapsFromEcu => false;

    /// <summary>A map that only lives in SRAM; T7Suite / T8Suite connected and read it.</summary>
    protected internal virtual Task OpenSramOnlyAsync(SuiteBinary bin, SymbolHelper sh) => OpenSramSymbolAsync(bin, sh);

    /// <summary>Why the program can't close (or open another ECU's file) now, null when it can.</summary>
    public virtual string? CloseBlocker => null;

    /// <summary>The program closes: release the ECU.</summary>
    public virtual void Shutdown()
    {
    }

    // ---- realtime ----

    /// <summary>The ECU's realtime table: names, conversions, the logs' extension.</summary>
    public abstract RealtimeRules RealtimeRules { get; }

    /// <summary>The realtime panel over the ECU's session.</summary>
    protected internal abstract RealtimeViewModel CreateRealtimePanel(SuiteBinary bin);

    // ---- offline tuning ----

    /// <summary>The package dialogs' filter: "Trionic 7 packages".</summary>
    public abstract string PackageFilesName { get; }

    /// <summary>"t7p" / "t8p".</summary>
    public abstract string PackageExtension { get; }

    /// <summary>Tuning package imports and exports (T7 / T8; T5Suite had none).</summary>
    public virtual bool HasTuningPackages => true;

    /// <summary>A file to compare with, opened without changing it.</summary>
    protected internal virtual SuiteBinary OpenCompareBinary(string path) => OpenBinary(path);

    /// <summary>Export symbollist as CSV writes the user description too (T7Suite; T8Suite didn't).</summary>
    public virtual bool SymbolCsvUserDescription => true;

    /// <summary>The search results' tab: "Search results: &lt;file&gt;" (T7Suite).</summary>
    protected internal virtual string SearchTitle(SuiteBinary bin, MapSearchOptions options) => "Search results: " + System.IO.Path.GetFileName(bin.FileName);

    /// <summary>The last transfer selection the old suite kept outside HKCU\Software\MattiasC, imported once; null when it didn't.</summary>
    protected internal virtual string? LegacyTransferKey => null;

    /// <summary>Lookup partnumber: what the entered part number is, null when the ECU doesn't know it.</summary>
    public abstract PartInfo? LookupPartNumber(string partNumber);

    /// <summary>What Define myMaps starts with when there is no mymaps.xml yet.</summary>
    public abstract IReadOnlyList<MapShortcut> MyMapsDefaults { get; }

    /// <summary>A My Maps entry that isn't a symbol (T7Suite's AFR maps); true when handled.</summary>
    protected internal virtual bool OpenSpecialShortcut(MapShortcut shortcut) => false;

    /// <summary>"Compare to original file": the stock bin of the open file's part number, null without one (or where the ECU has none).</summary>
    public virtual string? OriginalFile => null;

    // ---- the main window, as the suites' main windows had it ----

    public SuiteBinary? Binary => Main.Binary;

    public SuiteProject? Project => Main.Project;

    public TrionicTransactionLog? TransactionLog => Main.TransactionLog;

    public System.Collections.ObjectModel.ObservableCollection<DocumentViewModel> Viewers => Main.Viewers;

    public RealtimeViewModel? Realtime => Main.Realtime;

    public string Caption => Main.Caption;

    public string ProgressText { get => Main.ProgressText; set => Main.ProgressText = value; }

    public bool IsBusy { get => Main.IsBusy; set => Main.IsBusy = value; }

    public string ChecksumText { get => Main.ChecksumText; set => Main.ChecksumText = value; }

    public string OpenClosedText { get => Main.OpenClosedText; set => Main.OpenClosedText = value; }

    public SymbolFilter? SymbolFilter { get => Main.SymbolFilter; set => Main.SymbolFilter = value; }

    public bool IsConnected => Main.IsConnected;

    public string? SramFile => Main.SramFile;

    public bool HasSramFile => Main.HasSramFile;

    public Func<string, string, string[], Task<int?>>? AskButtons => Main.AskButtons;

    public Func<string, Task<bool?>>? AskYesNoCancel => Main.AskYesNoCancel;

    public Func<double[], Task<IReadOnlyCollection<int>?>>? AcceptAutotune => Main.AcceptAutotune;

    public void ShowInfo(string text) => Main.ShowInfo(text);

    public DocumentViewModel ShowDocument(DocumentViewModel document) => Main.ShowDocument(document);

    public void TransactionsAdded(int before) => Main.TransactionsAdded(before);

    public void RefreshViewers(string file) => Main.RefreshViewers(file);

    public MapViewerViewModel? OpenSymbolByName(string name) => Main.OpenSymbolByName(name);

    public Task<bool> EnsureConnectedAsync() => Main.EnsureConnectedAsync();

    public Task DisconnectRealtimeAsync() => Main.DisconnectRealtimeAsync();

    public Task RunFlasherAsync(Func<Task> operation) => Main.RunFlasherAsync(operation);

    public Task<SuiteBinary?> FlashableBinaryAsync(Func<string, Task<bool>> askYesNo) => Main.FlashableBinaryAsync(askYesNo);

    public List<FaultCode> Describe(IEnumerable<string> codes) => Main.Describe(codes);

    public Task OpenSramSymbolAsync(SuiteBinary bin, SymbolHelper sh) => Main.OpenSramSymbolAsync(bin, sh);

    public DocumentViewModel? OpenFromSramFile(SymbolHelper sh, string? file = null) => Main.OpenFromSramFile(sh, file);

    public Task CompareToFileAsync(string otherFile) => Main.CompareToFileAsync(otherFile);

    public Task CompareToSramAsync(string file) => Main.CompareToSramAsync(file);

    public Task<bool> OpenFileAsync(string path, bool showMessage) => Main.OpenFileAsync(path, showMessage);
}
