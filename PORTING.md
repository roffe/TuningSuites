# Porting the TuningSuites to .NET 10

Goal: lift T7Suite, T8Suite and T5Suite from .NET Framework 4 / WinForms to .NET 10 / Avalonia so they run on Windows, Linux and macOS, and replace every non-free dependency (DevExpress, Nevron, Office Interop). T7Suite goes first.

This file is the tracker. Update the checkboxes and the log at the bottom as work lands. How the old T7Suite behaves, read from its code, is collected in `docs/T7SUITE-BEHAVIOUR.md`, and how T8Suite differs from it in `docs/T8SUITE-BEHAVIOUR.md`; keep adding to them before porting a feature.

## Starting point (2026-10-09)

- The old code **cannot be built**: the DevExpress 11.2 licence has lapsed and the assemblies aren't installed. We can't migrate it one step at a time, so we build a new app alongside it and lift the logic over. Old projects stay in the tree as read-only reference until their replacement is done.
- T7Suite + CommonSuite: 150 non-designer files, 111k LOC, plus 21k LOC of designer code.
  - Pure logic: 58k LOC. SymbolTranslator (34.5k) and BitStream (6.3k) are data tables.
  - Logic with light UI coupling: 8.6k LOC.
  - UI: 44.5k LOC.
  - frmMain.cs is 19k lines, about two-thirds of it business logic that has to be pulled out first.
- TrionicCANLib is already ported (roffe/Trionic, branch `net10`, Avalonia 12 flasher on top). Every T7 API T7Suite calls still exists with the same signature. The library is synchronous and has no NuGet package.
- WidebandSupport source is in mattiasclaesson/wideband: 3.2k LOC, net40, depends only on System and SerialPort.
- T7Binaries/ has 256 stock bins to use as a golden test corpus.

## Decisions

| Topic | Decision |
|---|---|
| UI framework | Avalonia 12, Fluent theme, compiled bindings (same as TrionicCANFlasher) |
| UI pattern | MVVM with CommunityToolkit.Mvvm. The suites are too complex for code-behind |
| Copy/paste | Keep T7Suite's format byte for byte: `<viewtype digit><col>:<row>:<value>:~…` |
| Repo | T7Suite was ported on branch `net10` (merged), T8Suite is on `net10-t8` |
| TrionicCANLib | ProjectReference to `Trionic/TrionicCANLib/TrionicCANLib.csproj` in the `Trionic` submodule (roffe/Trionic), set once in `Directory.Build.props` as `$(TrionicDir)` and overridable with `-p:TrionicDir=…` (e.g. `../Trionic` while working on both) |
| WidebandSupport | Vendored into `WidebandSupport/` (from f0c0e87) with an SDK-style net10 csproj and the System.IO.Ports package. No licence file upstream, but every source file carries George Daswani's Apache License 2.0 header |
| Settings | JSON at `<AppData>/MattiasC/T7SuitePro/settings.json`, plus a one-time import from `HKCU\Software\MattiasC\T7SuitePro` (and its MRU key `HKCU\Software\T7SuitePro\MRUList`) on Windows, as the flasher does |
| Versioning, CI, packaging | Copy the flasher's: version from git tags in `Directory.Build.props`, one tag prefix per suite, upstream's release tags (`T7suite_v2.0.0`, later `T8suite_v`, `T5suite_v`), set by the app project's `VersionTagPrefix`, so a tag versions one suite; libraries and tests build as `0.0.0-<sha>`. One repo for all suites (revisit splitting after the T8 port, then with NuGet packages rather than submodules); self-contained win-x86 / linux-x64 / linux-arm64 / osx builds; WiX MSI, tar.gz and zip |
| Threading | The ECU and realtime loop run on worker threads and report back with `Dispatcher.UIThread.Post`, never a blocking Invoke (the flasher deadlocked that way). Wrap the library's sync calls in a worker plus a `TaskCompletionSource`, like the flasher's `RunOnWorker` |
| Map controls | Own Avalonia controls in `MapControls/`. Behaviour (selection, editing, keys, menus, clipboard) follows T7Suite; from txlogger the meshgrid 3D projection/drawing, the graph2d layout and the colour scale (green → yellow → red over the map's min..max; T7Suite's raw ÷ max made a fuel map red from its lowest cell). T7Suite's red-white option and online tint stay |
| Shared T7/T8 code | `SuiteCore` (no UI: the lifted CommonSuite code, `SuiteBinary` which T7Binary and T8Binary derive from, `SuiteProject`, compare / transfer, map search, symbol files, tuning packages, My Maps, the ECU worker thread and adapter setup, the realtime table, engine loop and logs, the update check, the DTC catalog) and `SuiteApp` (Avalonia: the main window and its view model with its ECU and realtime halves, symbol list, map viewer, workspace, status bar, project dialogs, the offline tuning windows, the realtime panel and log windows, the fault codes window, the settings base, theme, dialogs). The ECU protocols stay per suite (T7Ecu over KWP, T8Ecu over GMLAN). Each app keeps its menus and what only its suite has. The rest of T7Core / T7App moves there in the chunk where T8 needs it, so each piece is generalised against a second real consumer (T7 and T8 share 67 file names but only 8 identical files) |

## Layout (new projects)

```
TuningSuites.slnx          new solution (the old *.sln files stay for reference)
Directory.Build.props      from the flasher, plus $(TrionicDir) = Trionic (submodule)
WidebandSupport/           vendored, net10
SuiteCore/                 shared, no UI: the lifted CommonSuite code (Common/, namespace CommonSuite), SuiteBinary, SuiteProject, compare,
                           search, symbol files, tuning packages, My Maps, EcuWorker / CanAdapters, the realtime table and
                           engine, logs and matrix, update check, DTC catalog
SuiteCoreTest/             MSTest: settings, crypto, S19, VIN decoder, transaction log, update check, source encoding
SuiteApp/                  shared Avalonia library: the main window (SuiteMainWindow) and MainWindowViewModel, symbol list, map viewer,
                           workspace, status bar, project dialogs, compare / search / package / My Maps / part lookup / VIN decoder
                           windows, the realtime panel and log windows, the settings base, SuiteTheme.axaml (T7Suite's skin),
                           ViewLocator, dialogs
T7Core/                    net10 class library, no UI: file, symbols, axes, checksum glue, projects, transaction log, realtime engine, tuning logic
T7CoreTest/                MSTest, golden tests over T7Binaries/
MapControls/               Avalonia controls: MapGrid, Surface3D, Graph2D, MapData codec
MapControlsDemo/           standalone app for working on the controls with fake data
T7App/                     Avalonia MVVM app, AssemblyName T7Suite
T8App/                     Avalonia MVVM app, AssemblyName T8Suite, on SuiteApp's main window
T8Core/                    net10 class library, no UI: T8Suite's file logic (symbol table, header, dictionary), T8Binary
T8CoreTest/ T8AppTest/     golden test over T8Binaries/; T8App headless on a stock bin
SetupT7/ SetupT8/          WiX MSIs (chunk 8 of each suite)
packaging/linux/<Suite>/   udev rule, desktop entry installer and icon for each suite's tar.gz
```

## Dependency replacements

| Old | New |
|---|---|
| DevExpress XtraGrid (map tables) | `MapGrid`: our own control, a `Render(DrawingContext)` override modelled on txlogger's mapviewer |
| DevExpress XtraGrid (symbol list, compare lists) | Avalonia `DataGrid` |
| Nevron 3D and 2D, XtraCharts | `Surface3D`: meshgrid's fragment shader ported to an SkSL runtime effect (per-pixel ray cast) in an `ICustomDrawOperation`, with its CPU rasterizer on Skia `DrawVertices` as the fallback, and its axes. `Graph2D`: port txlogger's graph2d |
| ICSharpCode.TextEditor, Be.HexBox | Avalonia.AvaloniaEdit 12 and AvaloniaHex (both MIT) |
| XtraBars ribbon and docking, XtraTab | Menu bar; Dock.Avalonia 12.1 (MIT) for the workspace: documents as MDI inner windows or tabs, the symbol list as a dockable tool pane |
| XtraWizard | A plain step-by-step view |
| XtraReports (TuningReport) | Drop it. Export HTML if anyone misses it |
| Office Excel COM, OleDb ACE | CSV export, plus copying as tab-separated text so it pastes into Excel |
| Be.Windows.Forms.HexBox | AvaloniaHex or a small custom view |
| AquaGauge, ProGauges, LBIndustrialCtrls, Owf DigitalDisplay | One custom gauge/display control. AquaGauge, LBIndustrialCtrls and ProCharts are unused, so they just go |
| PSTaskDialog, SuiteLauncher, MouseGestures | Plain dialogs; drop the rest |
| RealtimeGraph (log viewer) | Rebuilt on Graph2D |
| msiupdater | The flasher's GitHub-release updater |
| System.Media.SoundPlayer | Defer; a cross-platform sound comes later if needed |
| Plot3D/FunctionCompiler (CodeDom) | Delete. It is dead code |

## Compatibility to preserve

- [x] Settings imported from the registry on Windows (`SettingsKey`: the whole `HKCU\Software\MattiasC\T7SuitePro` tree incl. Channels, SymbolColors, LogFilters, into one `settings.json`). Not tried on Windows yet
- [ ] MRU list, it lives under a different key (`HKCU\Software\T7SuitePro\MRUList`), with the main window
- [x] Symbol XML (`DataTable` XML) next to the bin and in `repository/` next to the executable; EU0AF01C.xml and EU09F01C.xml ship from T7Core
- [ ] Projects: projectproperties.xml, TransActionLogV2.ttl (binary, auto-upgraded from v1), ProjectLogbook.log, backups
- [ ] rtsymbols.txt / .t7rtl, mymaps.xml. SymbolViewLayout.xml is a DevExpress layout and is dropped
- [ ] `.t7l` logs: write with the same format. Values use the system's decimal separator today, so read both `,` and `.`
- [x] `.afr` maps, `.t7p` / `.t8p` tuning packages, and the `.t8x` packs of T8Suite's Tuning Wizard (signatures checked against T8Pub.pem, which ships from SuiteCore). T7Suite never read `.t7x` packs: its wizard is unreachable
- [x] `Encoding.Default` in T7SidEdit was ANSI on .NET Framework and is UTF-8 on .NET 10, now `Encoding.Latin1` (maps every byte 1:1; writing was ASCII anyway)
- [x] Source files: the old compiler read them in the ANSI code page, the SDK reads UTF-8. 24 old files are Windows-1252 and garble their non-ASCII literals ("Trollhättan") when lifted as they are. Convert with `iconv -f CP1252 -t UTF-8`; `SourceEncodingTest` fails on any new project file that isn't UTF-8
- [ ] Replace about 94 `"\\"` path joins with `Path.Combine`, and `Application.StartupPath` with `AppContext.BaseDirectory`. Done in T7Core; the rest are in frmMain and go with each extraction

## Chunks: T7Suite

### 0. Scaffold
- [x] `TuningSuites.slnx`, `Directory.Build.props` (no `.gitignore` changes needed, `[Bb]in/` and `[Oo]bj/` were already ignored)
- [x] ProjectReference to `$(TrionicDir)/TrionicCANLib`
- [x] Empty `T7App` window (MVVM: `MainWindowViewModel`, File > Exit, status bar), runs on Linux. Windows not tried yet
- [x] CI: `.github/workflows/build.yml` builds the solution and runs T7CoreTest on ubuntu and windows, with the Trionic submodule. Not run on GitHub yet
- [ ] Tag the first T7Suite release (`T7suite_vX.Y.Z`; upstream's last is `T7suite_v0.1.59.0`, the old source says 0.1.60.1). Until then T7App warns about the missing tag and builds as `0.0.0-<sha>`

### 1. T7Core (can run in parallel with chunk 2)
- [x] Lifted the pure-logic files into `T7Core/` (from T7Suite) and `T7Core/Common/` (from CommonSuite), original namespaces `T7` / `CommonSuite` kept so they diff against the old files.
  - From T7Suite: SymbolTranslator, PartNumberConverter, Disassembler, SymbolAxesTranslator, AFRMap, PartnumberCollection, SIDTranslator, TCMLimitEdit, SIDICollection, AFRMeasurement(Collection), IdaProIdcFile, SIDInformationTable, T7EspEdit, SIDIHelper, SymbolMapParser, PackageExporter, FuelMap(Information), Symbol, AirmassLimitType, T7SuiteRegistry, Trionic7File, T7SidEdit.
  - From CommonSuite: BitStream, VINDecoder, TrionicTransactionLog, TrionicSymbolDecompressor, CSVGenerator, DifGenerator, SymbolCollection, SymbolHelper, TransactionCollection/Entry, CellHelper(Collection), MNemonic*, LogFilter(Collection), Srecord, XDFWriter, PressureToTorque, EngineStatus, SortableCollectionBase, DTCDescription, TrionicProjectLog, SymbolXMLFile, LogFile, ViewEnums, TurboType, SuiteRegistry, AppSettings, Channels, LogFilters, SymbolColors, Crypto.
  - Dropped: Plot3D (replaced by Surface3D), FunctionCompiler (dead), Settings.cs (empty).
- [x] Untangled the lightly coupled files:
  - Trionic7File: MessageBox → `TrionicCANLib.API.UserPrompt.AskYesNo` (the app wires it to a dialog; unset = No); `DoEvents` dropped; `StartupPath` → `AppContext.BaseDirectory`.
  - AppSettings, Channels, LogFilters, SymbolColors: registry → `SettingsKey` (`T7Core/Common/SettingsKey.cs`), which keeps the registry's names and string values so the parsing code is unchanged. `RealtimeFont` is now the font string the registry held (`System.Drawing.Font` is Windows-only); the UI parses it.
  - DifGenerator, SymbolHelper, SymbolColors: `System.Drawing.Color` / `ColorTranslator` are in System.Drawing.Primitives and work everywhere, so they stay.
  - Crypto: rewritten to 40 lines on `Aes` / `RSA.ImportFromPem`, the crypt32/advapi32 P/Invoke is gone.
  - T7SidEdit: encoding, `DoEvents`.
  - BitStream: `ReadExactly` for its two whole-stream reads.
  - Path joins in TrionicTransactionLog, TrionicProjectLog, DifGenerator, CSVGenerator, AppSettings' default project folder.
- [x] Golden test (`T7CoreTest/BinGoldenTest.cs`): parses all 256 bins in `T7Binaries/` (header, checksum, symbols, addresses, lengths, axes, descriptions; 123 bins have stripped symbol names, 4 pull in the shipped EU0AF01C/EU09F01C lists) and compares a hash per bin with `golden.txt`. The baseline was taken from the lifted code before any refactor, which only had the MessageBox / `DoEvents` / path edits. `T7_GOLDEN_UPDATE=1` rewrites it, `T7_GOLDEN_DUMP=<dir>` writes readable dumps for diffing. Checked that it catches a one-off change in an address calculation.
- [x] Tests for settings (incl. subkeys), tuning-pack crypto against the 17 shipped .t8x (one experimental pack doesn't match its signature and is pinned as such), S19 (ported from CommonSuiteTest), VINDecoder (ported), transaction log round-trip and byte layout, source encoding. CI runs them on Linux and Windows (the golden dump avoids `AppendLine` so the hashes match on both).
- Changed plan: frmMain's logic gets pulled out in the chunk whose view needs it (listed there with line ranges), so every service is built against a real consumer and tested there instead of guessed up front. The `.t7l` round-trip test moved to chunk 6 with the logging code.

### 2. MapControls (can run in parallel with chunk 1)
Behaviour follows T7Suite's MapViewerEx and the DevExpress grid it used; only rendering techniques come from txlogger (see Decisions).
- [x] `MapData` (`MapControls/MapData.cs`): bytes ↔ raw ↔ display; 8-bit unsigned, 16-bit big-endian with 0xF001..0xFFFF negative; Hex/Decimal/Easy/ASCII text and parsing exactly as MapViewerEx (Easy is display only: raw*factor+offset, `F2`, `°`/`%` for the T5 names); partial last row; upside-down display; open-loop test (limit per data row > X value); every change through `Set` = one undo step
- [x] `MapGrid`: X/Y axis headers (hex in hex view), T7Suite cell colours (raw*255/max green→red, red-white alpha, online white→red tint, off), open-loop marks (box / SeaGreen corner), yellow live cell (`SetHighlight(col, dataRow)` = T7Suite's HighlightCell), DevExpress-style selection (click/drag, Shift, Ctrl, arrows), cell editor (typing / F2 / Enter, Easy starts from the value with `F2`, Enter commits the focused cell), steps on the selection (+/- 1, PgUp/PgDn 10 or 0x10, Home max, End 0), context menu with T7Suite's items (Copy selected cells, Paste selected cells at original position / at currently selected location, Smooth selection) plus undo/redo
- [x] `MapOps`: steps, add/multiply/divide/fill with the old formulas and truncation, smooth (old algorithm: integer-step line for a row/column, in-place neighbour average for a block), select by value (physical within 0.009), T7Suite clipboard copy/paste
- [x] Multi-step undo/redo (an improvement; the old viewer could only revert everything); Ctrl+Z/Y
- [x] `Surface3D`: txlogger meshgrid's projection and drawing (its SkSL-ported shader on GPU canvases: ray cast height field through the raw values, Blinn-Phong with fake AO and an unlit underside; otherwise Skia Gouraud triangles in painter's order, Lambert shading; axis scales with real axis values, orbit/roll/pan/zoom, live cursor), T7Suite's palette (green → yellow → orange → orange-red → red, online wheat → dark blue)
- [x] `Graph2D`: one row/column with markers, value callouts, nice ticks, live cursor; T7Suite palette for the markers
- [x] `MapControlsDemo`: `dotnet run --project MapControlsDemo [file.bin]` shows IgnNormCal.Map / BFuelCal.Map / TorqueCal.M_NominalMap of a bin (axes, factors and open-loop limits as frmMain computes them) with view type, online and red-white toggles, 3D and a 2D slice slider
- [x] Tests (`MapControlsTest`, 18): MapData decode/encode/format/parse/undo, every op, smooth, clipboard round trip, keyboard and mouse on a live headless window, and headless Skia renders of every control and of the demo on a real bin (`MAPCONTROLS_DUMP=<dir>` saves them as PNG to look at)
- Moved: the compare overlay in 3D (original / compare surfaces) goes with compare in chunk 4; viewer sync, the slice selector and the "Edit x-axis / y-axis" menu items go with the map viewer view in chunk 3. Dropped: dragging points in the 2D chart (only the legacy MapViewer had it).
- Deliberate differences from MapViewerEx, where the old behaviour was a bug:
  - 16-bit cells accept -0xFFF..0xF000, the values that survive save and reload (the old viewer accepted up to ±78643 and Home wrote 0xFFFF, which reloads as -1); 8-bit cells reject negatives (the old one accepted them and then dropped the byte on save, shifting every later value)
  - hex view shows a negative 16-bit value as its two bytes (`FFFF`), not `FFFFFFFF`
  - paste always treats clipboard values as raw and clamps them (pasting across view types wrote hex text into decimal cells or read decimals as hex)
  - ASCII view is read-only instead of throwing; smoothing works in hex view too (it always worked on raw values)
  - the colour scale uses the current largest value (the old one kept the value from load, only raised by + and Home)
  - copy with nothing selected copies the whole map without asking; clicking another cell commits a pending edit, like leaving the DevExpress editor did
- Additions on request: typing a value with several cells selected sets all of them (one undo step); the old editor set only the focused cell
- Left out on request: MapViewerEx's select-by-value box ("Select values")

### 3. Read-only app (first usable release)
- [x] `T7Core/T7Binary.cs`: an opened bin, lifted from frmMain: `Open` (TryToOpenFileUsingClass: header, SRAM offset, ExtractFile, BioPower E85 rename), `IsValidFile` (0x80000 bytes starting FF FF EF FC), symbol address with the open-software SRAM mapping, `ReadSymbol` (StartTableViewer), X/Y axis values, `TableWidth`, `IsSixteenBitTable`, `GetMapCorrectionFactor`, the open-loop table
- [x] `T7Core/FirmwareInfo.cs`: everything the firmware information dialog shows, detected like frmMain (header fields, part number lookup, programming stamp, checksum enabled, compressed / missing symbol table, the option checks, open/closed SID indicator, SID start screen / adaption and emission offsets)
- [x] Main window (`T7App/Views/MainWindow.axaml`, `MainWindowViewModel`):
  - File > Open (bin or S19, converted first), Recent (T7Suite's MRU: appended, no limit; imported once from `HKCU\Software\T7SuitePro\MRUList`), Exit; command-line .bin and AutoLoadLastFile at startup
  - symbol list: Symbol name (coloured by prefix like T7Suite), Address and Length (X6 or decimal per ShowAddressesInHex), Description, User description; grouped by category, sorted by length; search over every column; Enter / double-click opens the map
  - title `T7SuitePro v<version> [ file ]`, status bar with open/normal binary, file name, progress, read-only state
  - "File is not a Trionic 7 binary file!" and the known-symbol-list prompts as dialogs (`UserPrompt` wired; the worker thread waits for the answer)
- [x] Map viewers as tabs titled `Symbol: <name> [<file>]`; opening one again focuses it; viewers of a previous file stay open (as in T7Suite). `MapControls/MapViewer.cs` is the reusable composite (view type, table, 3D / 2D tabs with the column slider, splitter); viewers with the same map name follow each other's selection and 3D camera (`SyncGroup`, replaces frmMain's sync handlers). Settings used: DefaultViewType, ShowRedWhite, DisableMapviewerColors, ShowGraphs, StandardFill (open-loop marks)
- [x] Information > Firmware information: read-only, with the VIN decoder inline
- [x] Tests: `T7CoreTest` (T7Binary on a stock bin, firmware info and the SID indicator scan), `MapControlsTest` (viewer sync), `T7AppTest` (the real app headless with Skia: open a bin, symbol list and search, open a map twice, firmware information; `T7APP_DUMP=<dir>` saves the windows as PNG)
- Deliberate differences: the BioPower rename (BFuelCal.StartMap → BFuelCal.E85Map) now happens; in T7Suite it scanned the previous file's symbols and never fired at open. Feature checks read a symbol through the symbol that matched, not by looking its name up again (on the 123 stock bins with stripped names the old lookup found nothing and read offset 0). "Resolution is" factors are parsed culture-independently. The MRU is saved when it changes, not only on exit.
- Not yet: the map viewer's math toolbar, save / read file, Edit x-axis / y-axis, and firmware editing go with chunk 4 (they write the bin); SRAM-only symbols need the ECU (chunk 5); docking/floating windows, view sizes, My Maps, the ribbon's quick map buttons (DynamicTuningMenu) and the symbol-list context menu come with the features behind them.

### 4. Offline tuning (replaces the old T7Suite for offline work)
- [x] Edit and save the bin: `T7Binary.WriteSymbol` = savedatatobinary (only inside the file, a transaction entry when a project log is given) + checksum update; "Failed to write to binary. Is it read-only?" on errors
- [x] Actions > Verify checksum (with AutoChecksum a mismatch asks to recalculate); editable user description, saved to `<bin>.xml` like T7Suite
- [x] Map viewer: Save to file / Read from file, the math toolbar (operation, value "2", Execute), select by value (a box next to the view combo instead of typing into it), Edit x-axis / y-axis in the table's menu, "Data was mutated, do you want to save these changes in you binary?" (Yes/No/Cancel) when closing a changed viewer. Also asked per changed viewer when the app closes, which T7Suite didn't guard
- [x] Firmware information editing (`FirmwareInfo.Apply`, in T7Suite's order and conditions): footer fields (kept at their length, which T7Suite's save choked on), programming stamp, open/closed SID, torque limiters, OBDII (European / rest of world question), second lambda, (extra) fast throttle, catalyst light-off, ethanol sensor ("No TCS"), SID start screen / adaption and EU0AF01C emission patches, TIS footer fix with `.binarybackup`; Import / Undo of VIN and immobilizer code from another bin
- [x] Checksums: one `ChecksumT7.UpdateChecksum` pass computes FB before it writes the new FW checksum, which can lie inside the FB range, so a write that changes FW leaves FB stale (e.g. catalyst light-off on 5385356.bin). T7Suite never noticed because it updated after every single write. `T7Binary.UpdateChecksum` repeats until the file verifies (or throws); every write goes through it. The root cause is in TrionicCANLib's `ChecksumT7.updateChecksum`
- [x] Projects (`T7Core/T7Project.cs`, Project menu): create (prefilled from the open bin, binary copied in), open (list with backups / transactions / modified / version), close, edit (rename moves the folder), backup on open, transaction log window (notes editable, roll back / forward per entry), Roll back/undo and Roll forward/redo, rebuild file (up to a date, into the project or saved elsewhere), add note, logbook window, produce latest binary; purge offered above 2000 entries; "Remark for change" with RequestProjectNotes; opening a plain file closes the project; LastOpenedType / Lastprojectname at startup
- Deliberate differences: rollback and rebuild leave a verified checksum (rebuild didn't update it, rollback asked); a closed project's transaction log no longer collects later plain-file writes; viewers of the file without unsaved changes refresh after a rollback / rebuild / firmware change; create reopens by the folder name (T7Suite used the typed name and missed when characters were dropped); same-second backups are numbered instead of throwing
- [x] Compare (`T7Core/T7Compare.cs`, Actions menu): compare symbols with other binary (results tab grouped by category, salmon / cornflower rows for symbols missing on either side; Open shows the map of the open file and a read-only viewer of the other file's; Show differences map; export as CSV instead of Excel), compare to original file (`Binaries/<partnumber>.bin` next to the executable, which the T7Extras installer used to ship: packaging has to ship T7Binaries there), compare binary with other binary (16-byte line diff), transfer maps (warning, target file, symbol checklist remembering the last selection, backup `...beforetransferringmaps.bin`, report)
- Deliberate differences: "Number of values different" counts values and the percentage is over values (T7Suite halved differing bytes for 16-bit tables with integer division, so one changed byte showed 0 and a fully changed 16-bit map 50%); symbols are matched through the matched symbol's own address (stripped-name bins weren't compared at all); the other file isn't added to the MRU list
- [ ] Original / compare overlay in Surface3D, SRAM compare (needs .ram dumps, with chunk 5)
- [x] Symbol import (File menu): XML / CSV / AS2 descriptors, names saved to `<bin>.xml` (`T7Core/SymbolFiles.cs`). AS2 counts symbols in table order; T7Suite counted in its length-sorted list (open question: which order AS2 files use)
- [x] My Maps (mymaps.xml in the settings folder, the same file T7Suite used): menu per category, Define myMaps editor, Add to MyMaps in the symbol list menu; the AFR feedback entries wait for realtime
- [x] The ribbon's map buttons as a Maps menu with T7Suite's groups and DynamicTuningMenu's rules (BioPower names and E85 maps, B308 second maps, gas maps, boost control, cab gear limit)
- [x] Settings window: the offline settings that do something here (red-white, graphs, colours, auto-load, default view type, synchronize mapviewers, closed loop indicator, timestamp marker, auto checksum, hex addresses applied at once, auto fix footer, project notes, project folder). Docking / window size / column options don't apply to tabs; the realtime group comes with chunks 5-6
- [x] Exports: Save as (and open the copy), Export to S19, Generate Idc file, export as tuning package (selected symbols) and fixed tuning package, export symbollist as CSV, Export map to CSV (the Excel export's layout). XDF had no menu in T7 (XDFWriter is T5's)
- [x] Search map content (`T7Core/MapSearch.cs`): numeric value (raw*factor+offset) or text in the data, symbol names / descriptions, optional map length; results tab opens the maps. T7Suite scanned only the first half of 16-bit tables and matched text only when it was longer than the map; both fixed

### 5. ECU
- [x] `T7Core/T7Ecu.cs`: one dedicated thread per ECU session (TrionicCANLib's KWP handler is thread-affine), every call queued on it and awaited. Connect (SetupCanAdapter + openDevice at Latency.Low, alive polling), disconnect; flasher sessions at Latency.Default that close a realtime connection first, like FlasherConnect
- [x] Adapter setup from the settings (type by description, adapter, P-bus only, serial speed for ELM327 / Just4Trionic / SLCAN); SLCAN works (T7Suite had no branch for it and crashed); "Check settings, no CAN adapter has been selected!"
- [x] Settings window: Realtime settings group (adapter type, adapter list from the library, serial speed, Only P-bus connection, Auto update SRAM viewers every N seconds)
- [x] ECU (and Realtime) menu: Connect / Disconnect ECU, Read ECU, Flash current file to ECU, Get SRAM snapshot (`SRAM<time>.RAM` next to the bin, `Snapshots/Snapshot<time>.RAM` in a project), Get fault codes (OBDII), Clear DTC and knock counters, Synchronize to binary / to ECU (with T7Suite's warnings), Import SRAM snapshot; status bar shows the CAN status and the SRAM file
- [x] Map viewer: Read from ECU / Save to ECU (every viewer of the map follows), maps that only live in SRAM are read from the ECU when opened (no file buttons), viewers opened while connected are online (T7Suite's colours), auto update of online viewers; symbol list: Read symbol from ECU, Read from SRAM file ("SRAM Symbol: name [file]")
- [x] Fault codes window (Code / Description from `DTC_*.xml` next to the program, Clear selected, Close)
- Deliberate differences: flash read / write report the library's actual result (T7Suite said "Download done" / "Flash sequence done" whatever happened); flashing first fixes (AutoChecksum) or offers to fix a checksum that doesn't verify, and warns about unsaved map changes (T7Suite flashed the file as it was, unchecked); a map write the ECU refuses (closed binary) is reported (T7Suite ignored the answer); closing the app is blocked while a read / flash / snapshot runs, and closing always releases the adapter; fault codes without a description are listed (T7Suite hid them); the DTC description loader no longer stops reading a file at its first incomplete entry
- [x] Upload tuning package to ECU / Generate tuning package from ECU; Compare binary to SRAM snapshot / Compare SRAM snapshots (the compare tab now takes two sides: bin, other bin or snapshot). SRAM compares fill in the difference columns T7Suite left at 0; refused writes during upload and Synchronize to ECU are reported
- [ ] SaabOpenTech "Extra functions" (seatbelt ping, double unlocking, I-bus info): left out. They shell out to the bundled SaabOpenTech.exe, Windows and Lawicel only, no source. Revisit if the source turns up
- [x] Tested on a bench ECU: connect, read flash (identical to the flashed file), flash, SRAM write on an open binary, refused write reported on a closed one. Snapshot and fault codes not yet

### 6. Realtime
- [x] Realtime engine on the ECU thread (`RealtimeEngine` in T7Core): passes back to back, other ECU actions queue between two passes instead of T7Suite's m_prohibitReading; per-row Delay/Reload, ≤4 bytes by SRAM address, longer by symbol number, signed by name, per-cylinder knock/misfire rows, Performance.Mode every 21 passes
- [x] Realtime panel (a document tab until docking): dashboard (3 × 3 displays, AFR/λ and airmass gauges with the fading peak), bottom panel with decoded limiter / lambda / fuelcut status, Night/Day, Eco/Norm/Sport, Free logging grid (add / edit / remove / reorder, save / load .t7rtl, rtsymbols.txt, Add to realtime list), Shift+F1, F6
- [x] `.t7l` logging: a line per pass while the panel runs, `<bin>-yyyyMMdd-CanTraceExt.t7l`, invariant numbers, read with either decimal separator
- [x] Exports: LogWorks (.dif through the lifted DifGenerator; LogWorks isn't started, it's Windows-only) and CSV, with a symbol and time range selection; log filters (setup dialog, applied to the viewer and both exports). Deliberate differences: CSV exports the chosen symbols (T7Suite ignored the selection) and leaves a missing value's column empty (T7Suite shifted the rest left); removed filters stay removed; numbers parse with either separator
- [x] Wideband: the ECU symbol (AD_Scanner with the voltage → AFR settings, LambdaScanner) or a serial device through WidebandSupport, logged as "Wideband"; settings group instead of frmWidebandConfig
- [x] Live cell tracking in open map viewers (T7Suite's map → axis table, nearest breakpoint)
- [x] AFR target / feedback / counter maps (AFRMaps folder, viewers, clear, Actions → Import AFR feedback data) and autotune (fuel, open binaries; auto update or the accept grid on stop). Deliberate differences: .afr files written invariant and read with either separator (T7Suite's culture trick read "14.70" as 0 on Linux sv-SE); saving the target map keeps unsaved samples; lambda-mode feedback is compared as AFR on import; autotune writes the file once with a transaction entry and one checksum update (T7Suite updated the checksum per cell and logged nothing) and doesn't close the realtime panel on stop; the ping sound isn't ported; settings T7Suite stored but never used are left out
- [x] Log viewer (`LogGraph`, replaces RealtimeGraph): sections split at 10 s gaps with a chooser (the first line after a gap is kept), channels autoscaled on black with T7Suite's names and symbol colours, legend values under the cursor, hover readout, wheel zoom, drag pan, arrows; starts on the first 3 minutes
- [x] View knock count / false knock / real knock / misfire map from the ECU
- Deliberate differences: a failed read keeps the last value (T7Suite's failed short read threw and lost the rest of the pass); layouts and logs are written with invariant numbers and read with either separator; a layout's symbol takes the open bin's address (T7Suite trusted the saved one); the AFR digits show AFR in AFR mode (T7Suite showed λ there); Reset peak values is visible; sound notifications aren't ported yet
- [x] `.t7l` round-trip test, both decimal separators
- Moved to later (see After T7): sound notifications, Combi adapter ADC / thermocouple channels. Not ported: auto-logging triggers (T7Suite never used them), the CAN frame sniffing T7Suite kept switched off
- [x] Tested on a bench ECU: realtime values look right. Not yet on a running engine (wideband, autotune, live cells)

### 7. Tools
- [x] T7Suite's workspace with Dock.Avalonia: the documents are the DocumentDock's ItemsSource in MDI mode (inner windows: move by the whole title bar, resize, minimise, maximise; Window → Cascade / Tile), the symbol list is the left pane. A title bar dragged out of the main window floats the document in a window of its own ("Dock back into main window", Window → Dock all floating windows), like T7Suite's floating panels. Dock's own drag-to-dock (tabs, splits, its floating windows) is off: it made tabs T7Suite never had and windows that couldn't be brought back. Closing goes through the unsaved-changes question; selection follows both ways
- [x] Airmass result viewer (Actions menu): `AirmassResult` in T7Core with T7Suite's limiter order and interpolation, the table with limiter triangles and display modes, the dyno graph (with compare file), legend entries open their maps. Deliberate differences: tables the bin lacks don't limit (T7Suite read them as 0, which zeroed the result or showed nothing at all); a message instead of nothing when the bin lacks the needed tables; first gear opens TorqueCal.M_1GearTab, the table the calculation uses; the compare file doesn't replace the open file's tables
- [x] Compressor map (third tab of the airmass result viewer): T7Suite's 14 compressor images and calibrations, the WOT points at 14.7 / 12.5 / 15.4 psi, turbo and engine guessed from the part number, VE and intake temperature
- [x] Tuning packs: File → Import tuning package (.t7p symbols and search & replace with T8Suite's wildcard handling, one checksum update, transaction entries, the WIZARD log, "Import results") and File → Edit a tuning package (rows from a package or the symbol list's selection, a row's map in a viewer that saves into the row, saved as .t7p). Deliberate differences: a symbol whose package length differs from the bin's isn't written (T7Suite overwrote the following bytes); MapChkCal.ST_Enable on open software really is skipped; `?` wildcards work; unknown `*Symbol` references fail the pattern instead of vanishing; search & replace changes get transaction entries; the editor writes each row's own length. `.t7x` packs aren't loaded (T7Suite never did)
- [x] TuneToStage and the tuning wizard left out (unreachable in T7Suite since 2017)
- [x] SID information and editing (Actions → SID information): the "All" and "New" tables grouped by mode, short name (max 4), symbol (fills code, address, description, ID), ID, address, matched symbol (red when different), Export / Import SIDi settings (.sid); Ok writes the tables and the checksum. Deliberate differences: a short name longer than 4 is refused in place; the dialog no longer renames the binary's "Symbolnumber" symbols as a side effect
- [x] ESP calibration and TCM limit (Actions): the ESP byte with T7Suite's four choices; TCM MOD v1 (threshold) or v2 (per gear), mutually exclusive, the limit into VIOSCal.M_TCMOffset with a transaction entry. Deliberate differences: Ok needs a known ESP choice (T7Suite wrote 0x00 for an unknown value); a bin without VIOSCal.M_TCMOffset stops there
- [x] Smaller tools: File → Save all, Create backup file, Lookup partnumber (open / compare / create new from Binaries); Actions → Copy address table to another binary (now also updates the target's checksum, which T7Suite left stale), Browse axis information (also from the symbol list; double-click opens the map or the clicked axis); Realtime → Set ethanol content, Set symbol colors (the symbols with an SRAM address, sorted, with a search box, colours from Avalonia's MIT ColorPicker; titled "Set symbol colors" rather than T7Suite's reused "Logfile data selection"). Left out: the P&E micro programmer (batch files for an obsolete BDM interface), screenshot / fullscreen
- [x] Disassembler and hex view (Actions): Show interrupt vectors, Show disassembly (<bin>.asm, asked to redo when it exists), Show full disassembly (<bin>_full.asm, plain text instead of RTF) in AvaloniaEdit with T7Suite's ASM colours (a lighter set in the dark theme), search / replace and Save, and the bin's bytes beside the listing with linked cursors as ctrlDisassembler had: the caret's instruction selected in the hex pane (4 lines past it in view), a double-clicked byte's address found in the listing, the hex caret's offset and symbol shown; View file in hex (AvaloniaHex, MIT) for the bin and the imported SRAM file with the symbol under the caret, saved in place after a backup copy, asks on close
- [x] Matrix from log (Realtime → View matrix from logfile): x / y / z and mean / minimum / maximum (last choice remembered), 16 × 16 between the logged extremes, shown in a read-only map viewer with the 3D surface. Deliberate differences: lines count once all three symbols have been seen (T7Suite counted the zeros before that); a real 0 isn't treated as an empty cell in minimum / maximum; numbers parse with either separator
- [x] From frmMain: SID, limiter, torque/power/airmass math, tuning packs, matrix from log (TuneToStage left out)

### 8. Release
- [x] Packages from `.github/workflows/build.yml` as the flasher's: self-contained win-x86 / linux-x64 / linux-arm64 / osx-arm64 / osx-x64 publishes; the WiX 6 MSI (`SetupT7/`, plus T7Suite.zip with it as upstream's releases had) with libusb-1.0.dll, the Linux tar.gz with the CombiAdapter udev rule and `install-desktop.sh`, the macOS zip (plain unsigned folder). Pushes to `net10` update the `T7suite_nightly` pre-release, a `T7suite_v*` tag makes a release. Every package ships the stock bins in `Binaries` (T7Extras' job; ~47 MB of the ~106 MB), canlib32.dll on Windows and T7Suite's NLog.config. Not run on GitHub yet; WiX only runs on Windows, so the MSI is first built there
- [x] The MSI replaces the old T7Suite: its upgrade code and `Program Files (x86)\MattiasC\T7Suite`, per machine as it was (the VC++ 2010 merge module needs it), the merge module from the flasher's setup in the Trionic submodule. A build before the first `T7suite_v` tag is 0.0.0 and won't install over the old 0.1.59
- [x] `.bin` association as an Open with entry, not the default program: OpenWithProgids in the MSI (per machine, like the install), `install-desktop.sh` on Linux (per user, `application/octet-stream`); none on macOS without an .app bundle
- [x] Updater (`T7Core/UpdateCheck.cs`): on startup and Help → Check for updates, the newest non-pre-release `T7suite_v` release of roffe/TuningSuites (the release list, GitHub's latest is whichever suite went last), "No new version(s) found..." and the like in the status bar's update field, T7Suite's "found some new toys" dialog: Show change log opens the release page, OK downloads the MSI on Windows / opens the release page elsewhere (T7Suite ran the MSI and quit). Builds without a tag skip the startup check. Help → Release notes opens the releases page
- [x] Logs: T7Suite's NLog.config and files (`<AppData>/MattiasC/T7`), every CAN frame only with Settings → Enable CAN logging (T7Suite's option, which its config ignored)
- [x] README with an OS × adapter support table, installation, updates, building, versioning

## Chunks: T8Suite

### Starting point (2026-10-09)

- Read from the old code into `docs/T8SUITE-BEHAVIOUR.md`, which lists only where T8Suite differs from T7Suite.
- **The window is T7Suite's.** The same ribbon pages and mostly the same buttons. T8 lacks:
  - SID information, ESP, TCM and AFR maps / autotune;
  - Synchronize and the tuning packages to / from the ECU;
  - the SRAM snapshot and compares, Compare to original file, the MRU list and P&E micro.
- **T8 adds:**
  - the TEM and PID editors, the bitmask viewer and the map preview popup;
  - Get ECU information and Recover ECU (Legion bootloader by default);
  - Create binary from TIS file, Compare binary outside symbolrange;
  - a working tuning wizard (`.t8x` packs) and dynamic live data.
- **The file side is all new.**
  - **Bins:** 1 MB, starting `00 10 0C 00` or `00 00 0C 00`.
  - **Symbol table:** behind `sYMBOLtABLE`, packed the T7 way or Blowfish-encrypted into a password-protected zip.
  - **Map metadata:** factors, units, axes and descriptions come from SymbolDictionary (8,500 entries), English only.
  - **Checksum:** TrionicCANLib's two-layer ChecksumT8, checked (and with AutoChecksum silently fixed) on every open.
  - **Header:** a PI area plus MFS / flash blocks (VIN, immobilizer code) instead of T7's footer.
  - **Addresses:** SRAM addresses are mapped to flash once at open.
- **Size of the old code:** Form1.cs 15.5k lines, 59k lines of non-designer code in all. The T8-only logic: Trionic8File 1.9k, T8Header 1k, SymbolDictionary 8.7k (data), SymbolFiller 0.7k, blowfish 0.9k.
- **TrionicCANLib:** every Trionic8, ChecksumT8 and FileT8 call T8Suite makes still exists with the same signature. The library has no T8 header or symbol table code, so that stays in T8Core.
- **Stock bins:** `T8Binaries/` (72 bins) is the golden corpus. All verify their checksum and none is open software.
  - **Names:** 45 tables are packed and 27 encrypted (14 keys). 71 bins get names; 1 has a corrupt table.
  - **Tables:** every bin has a PID table, none a TEM table.
  - **File names:** 69 end in `.BIN`, so enumerate case-insensitively.

### Decisions (T8)

| Topic | Decision |
|---|---|
| Behaviour | T8Suite's behaviour wins for T8, as T7Suite's did for T7. The shared window's improvements (Recent files, multi-step undo, ...) stay, unless T8Suite contradicts them |
| Shared layer | Grows chunk by chunk (see the Decisions table above). Each suite keeps its own menus, in its ribbon's order and with its captions; the rest of the main window is shared |
| SharpZipLib | Package 1.4.2 (MIT, T8Core) for the encrypted name tables (a ZipCrypto zip, which System.IO.Compression can't open), without T8Suite's code page 850 line. With the old 0.86 on .NET 10 that line throws, the error is swallowed, and the 27 encrypted bins open without names |
| State | Per binary. Trionic8File's statics (open flag, address offsets) and Form1's static PID / TEM tables become T8Binary's, so opening a compare or transfer file no longer replaces the open file's |
| Settings | JSON at `<AppData>/MattiasC/T8SuitePro/settings.json`, imported once from `HKCU\Software\MattiasC\T8SuitePro` plus `HKCU\Software\T8SuitePro\TransferSettings` (outside MattiasC) |
| Releases | `T8suite_v` tags (T8App's VersionTagPrefix), `T8suite_nightly`; the MSI replaces the old T8Suite (its upgrade code) |

### 0. Shared layer and scaffold
- [x] `SuiteCore`: T7Core/Common (the lifted CommonSuite), UpdateCheck (the tag prefix is now a parameter), DtcCatalog and T8Pub.pem moved out of T7Core. `T7Log.Number` became `LogFile.Number`. `SuiteCoreTest` takes the tests of that code; SourceEncodingTest now checks every project in the solution
- [x] `SuiteApp`: T7App's theme (`SuiteTheme.axaml`, the `T7*` resource keys renamed `Suite*`), ArrangedMdiLayoutPanel, NoRecycling, LinearGauge, LogGraph, Dialogs and Logging. Headless renders of T7App are pixel-identical before and after (apart from timestamps and an expander caught mid-animation)
- [x] `T8App` scaffold: an empty window "T8SuitePro v<version>" with File → Exit, `T8suite_` versioning, T8Suite's icon, x86 apphost on Windows
- [x] CI runs SuiteCoreTest
- [x] `docs/T8SUITE-BEHAVIOUR.md`

### 1. T8Core
- [x] Lifted from T8Suite (namespace `T8SuitePro` kept): Trionic8File, T8Header, FlashBlock(Collection), pidCode, SymbolDictionary (converted from Windows-1252), SymbolTranslator, SymbolAxesTranslator, SymbolnamesDictionary, blowfish, SymbolFiller, T8SuiteRegistry.
  - Edits: MessageBox → `UserPrompt.Show`; the program folder for symbol lists is a parameter; SharpZipLib 1.4.2 without the code page line; no `C:\T8Decode` dumps or Console output (the header log goes to NLog at Trace).
  - Also: `using` streams (CountNq left a handle open on 10 of the stock bins); Trionic8File's open flag and offsets per instance; dead code dropped (four unused table finders, the DEBUG-only PI writer).
  - Moved to the chunks that use them: Disassembler (7), IdaProIdcFile and PartNumberConverter (4; PartnumberCollection only fed the part number list, not ported), AirmassLimitType (7).
  - Dropped: crc64 (unused), Plot3D, Settings, MapViewerFactory.
- [x] `T8Binary` (`T8Core/T8Binary.cs`), lifted from Form1 as T7Binary was from frmMain:
  - open (TryToOpenFile 557: extract, a symbol list from the program folder by software-version prefix or `<bin>.xml`, SymbolFiller when map detection is on);
  - addresses (GetSymbolAddress 1366), read / write (readdatafromfile 1627, savedatatobinary 1830), ChecksumT8 (UpdateChecksum 3557; a map write always corrects it, as T8Suite);
  - axes (GetX/YaxisValues 1656-1828: raw, X unsigned / Y signed, "N : v…" axes from the dictionary, the duplicate's x axis when the listed one is missing), table width (GetTableMatrixWitdhByName 1419, then the x axis' length as StartTableViewer did), 16-bit (isSixteenBitTable 1598), factor from SymbolDictionary.
  - Same members as T7Binary where the two agree, for the shared base class in chunk 3. Bitmask words and the PID / TEM write-back come with their editors (chunk 7).
- [x] `T8CoreTest`:
  - **Golden test** over the 72 bins. It hashes the header and flash blocks, checksum, symbol table, symbols with their addresses, the PID / TEM tables, every map's width / 16-bit / factor / axes, and the names map detection gives the stripped bin. It asserts that 71 bins get names.
  - **Baseline:** taken from the lifted code with only the edits needed to build, before the per-instance state and stream changes. It was checked to catch a one-off change.
  - **Speed:** the test parses the bins in parallel (5 s), which the per-file state allows.
  - **T8BinaryTest:** a stock bin's map, axes and SRAM-only symbol; a write leaves layer 1 stale and the update fixes it; the transaction entry.
- [x] SuiteCore: TrionicSymbolDecompressor (T7's and T8's packed name tables) decodes one table at a time. Its tables are static, so two files decoding at once mixed up their names: the parallel golden test showed it, and in the apps it could happen with an open while another runs
- Deliberate difference: EngTipLimCal.X_Koeff's dictionary axis "6: A B C D E F" made T8Suite's viewer throw, so the map never opened; its labels now count as their positions 0..5
- Known T8Suite bug fixed: the process-wide statics above

### 2. MapControls
- Nothing T8-specific expected. T8's MapViewerEx differences go with the viewer in chunk 3:
  - the file / ECU buttons follow the symbol's addresses;
  - static axes and units as captions;
  - no open-loop marks;
  - the axis menu opens the axis map.

### 3. Read-only app
- [x] The shared layer grown for it:
  - **`SuiteCore/SuiteBinary.cs`:** the binary the windows use; T7Binary and T8Binary derive from it.
    - Shared: lookups by SmartVarname, reads, writes, map saves.
    - Per suite: addresses, axes, widths, factors and the checksum.
    - T7Binary's footer fix is a property (`AutoFixFooter`) for the windows' writes; T7Core keeps passing it explicitly.
  - **`SuiteCore/SuiteProject.cs`:** T7Project serving both suites (T8Suite's projects behave the same), the backup with it.
  - **SuiteApp:**
    - The main window: `SuiteMainWindow`, which each app's MainWindow.axaml derives from with its own menus, the status bar, the workspace and the symbol list's row menu.
    - `MainWindowViewModel`: opening, symbol list, documents, projects, Recent, update check, with hooks for what the suites do differently.
    - The symbol list (every column either suite shows, the suite picks), the map viewer, the project dialogs, About and the update window.
    - `SuiteStartup`, and a `ViewLocator` (FooViewModel → FooView) instead of each app's template list.
  - **T7App** keeps its menus and what only T7Suite has (`T7MainWindowViewModel` with its ECU and log partials). T7's tests pass unchanged and its renders are identical, apart from timestamps and an expander caught mid-animation.
- [x] T8App:
  - **Open:** bin / S19, with T8Suite's messages.
  - **Checksum on open:** checked every time, as T8Suite did ("Checksum: OK" in the status bar). With AutoChecksum it is corrected without asking; otherwise it asks per layer.
  - **Symbol list:** T8Suite's columns, its order (category, length, name) and its "Only symbols within binary" / "Only live-tuneable symbols" filters.
  - **Map viewers:** saving already works (the shared viewer, T8Binary's ChecksumT8).
  - **Projects:** prefilled from the T8 header.
  - **Verify checksum:** status bar only, as T8Suite.
  - **Firmware information:** read only, every field of frmFirmwareInformation and the flash block browser.
  - **Also:** Recent, skins, help.
- [x] T8AppTest: the app headless on a stock bin. It checks the title, checksum, filter, columns and order; Enter opens a map with its width, factor and axes; an edit is saved with a valid checksum; it renders the firmware and flash block windows (`T8APP_DUMP=<dir>`)
- Deliberate differences:
  - Recent files (T8Suite had no MRU list).
  - A failed S19 conversion says so (T8Suite stayed silent).
  - The checksum status names a missing checksum area or a failed update; T8Suite kept the previous text.
  - Bit mask symbols say their viewer isn't ported yet (chunk 7), and maps that only live in SRAM say so (no ECU yet, chunk 5).

### 4. Offline tuning
- [x] Shared now: moved from T7Core / T7App instead of copied, over `SuiteBinary`, which says what differs per suite (the compare name, the calibration test, the description, the XML import, the fixed package, the quick maps, the address table, the Idc file):
  - **SuiteCore:** `SuiteCompare` (compare, SRAM compares, difference map, binary diff, transfer maps), `MapSearch`, `SymbolFiles` (XML / CSV / AS2 imports; S19, CSV and package exports), `TuningPackage` and `PackageExporter`, `MapMenus` (My Maps), `PartInfo`.
  - **SuiteApp:** the compare and search results, the package editor, the search, transfer selection, import results, My Maps, part lookup and VIN decoder windows. Their menu handlers are on `SuiteMainWindow` and the view model's half is `MainWindowViewModel.Tuning.cs`. `SuiteSettingsViewModel` / `SuiteSettingsWindow` hold the settings both suites have.
  - T7's tests pass with only the moved names changed, and its renders are identical (apart from the usual timestamps and expander).
- [x] T8App's menus, in the ribbon's order:
  - **File:** Export to S19, Import... (XML / CSV / AS2 descriptor files), Import tuning package, Edit a tuning package, Generate Idc file (T8Suite's MC68377 segments), Settings, Lookup partnumber.
    - Settings has T8Suite's groups: user interface (with Auto mapdetection active) and project.
    - Lookup partnumber takes T8Suite's 64 keys `<partnumber>_<software version>` and shows only the car model and engine.
  - **Actions:** VIN decoder, Compare symbols with other binary, Compare binary outside symbolrange, Compare binary with other binary, Transfer maps to another binary, Search map content, Copy address table to another binary, Export map to CSV.
    - Search names its results "Search results:  number n".
    - Copy address table copies from 17 bytes before the table, as T8Suite.
    - Export map to CSV applies the x axis' dictionary factor, as T8Suite's Excel export did.
  - **Tuning:** the ribbon page's map buttons with DynamicTuningMenu's rules: the old / new calibration captions and maps, the BioPower limiters, and the gas / jerk maps only when the bin has them.
  - **My Maps** (`<settings>/T8SuitePro/mymaps.xml`, T8Suite's defaults).
  - **Symbol list menu:** Add to MyMaps, the three package items, Export symbollist as CSV (without the user description, as T8Suite).
- [x] Firmware editing:
  - Double-click "Software version" to edit the software version container; double-click "Chassis ID" or "Serial number" to edit the VIN and immobilizer code, with Clear.
  - OK writes them (`FirmwareInfo.Apply`, transactions in a project) and checks the checksum as on open.
- [x] T8CoreTest:
  - compare, transfer and the outside-symbol diff;
  - the quick maps for old, petrol and BioPower calibrations;
  - search, a fixed package round trip, the S19 / Idc / CSV exports;
  - imported names surviving the sidecar, copy address table, firmware edits.
- [x] T8AppTest drives them headless and renders compare, search, settings, part lookup, firmware and the VIN decoder.
- Deliberate differences:
  - Imported names show in the Symbol name column at once, as T7Suite did (T8Suite only swapped them in on the next open).
  - Compare and transfer files are binaries of their own, so the open file keeps its PID / TEM tables, SRAM file, captions and open status.
  - A transfer target's checksum is corrected without asking, like a map save; T8Suite asked per layer when Auto update checksum was off. The status bar keeps the open file's checksum (T8Suite showed the target's).
  - "Clear" blanks the VIN in the dialog and OK writes it. T8Suite wrote it at once, and Cancel didn't undo it.
  - Firmware edits are transactions in a project.
  - A bin without a software version gets the old calibration's Tuning menu. T8Suite kept the designer captions and threw on the old / new buttons.
  - Compare results keep T7Suite's column order and show lengths in hex like the symbol list (T8Suite: Description first, lengths always decimal).
  - The settings' realtime group comes with chunks 5-6; "Show map preview popup" comes with the map helper (chunk 7).
  - Lookup partnumber's "not recognized" message names T8Suite (T8Suite's said T7Suite).
- Known T8Suite bugs fixed:
  - **"Symbolnumber N" one less than `Symbol_number`:** the shared sidecar writer puts swapped names back under their placeholder, so imported and detected names survive a save.
  - **Edit a tuning package:** saves the edited rows (the shared editor).
  - **Short VIN or immobilizer code:** no longer throws halfway; T8Header dropped PadRight's results.
  - **"Serial number" double-click:** edits made after it are kept.
  - **Idc file:** long names are cut as intended (Remove's result was dropped).
  - **"Clear" VIN:** it skipped the checksum update, which turns out not to matter: the MFS area and the PI containers lie outside both checksum layers. OK checks the checksum anyway.
- Not ported: Import map from Excel and the part number list (T7 has neither), Export as tuning package in the compare results, Toggle fullscreen. Moved: Import SRAM file / Read from SRAM file go to chunk 5 (with the snapshots), the Tuning Wizard to chunk 7.
- T7 changes from the sharing:
  - Define myMaps starts with T7Suite's defaults when there is no mymaps.xml yet (T7Suite created that file).
  - Menu entries with underscores (Recent files, My Maps) keep them; a single underscore marked an access key and vanished.
  - The part lookup window fits its buttons.
  - Compare rows name a symbol by the name it was matched on (the same as before for named symbols).

### 5. ECU
- [x] Per suite, as the protocols are: T7 speaks KWP2000 over CAN, T8 GMLAN, T5 its own, and TrionicCANLib's Trionic5 / 7 / 8 classes share little beyond `ITrionic` (adapter setup, progress events, Cleanup). Shared is only what doesn't touch the wire:
  - **SuiteCore:** `EcuWorker<T>` (the library object on one thread of its own, its events passed on, the closing) and `CanAdapters` (the adapter types and the setup from the settings). T7Ecu and T8Ecu derive.
  - **SuiteApp:** the ECU half of the main view model (`MainWindowViewModel.Ecu.cs`): the connection state and Connect / Disconnect, the viewers' Read from / Save to ECU and their auto update, maps that only live in SRAM, the .RAM file and Read from SRAM file, the flasher's busy state, the checks before a flash, the fault code descriptions. The fault codes window and the connection part of the settings dialog moved there too. A suite supplies the session calls (connect, read and write a map, read / flash, fault codes).
  - T7's tests pass and its renders are unchanged.
- [x] `T8Ecu` (T8Core): Trionic8 on its own thread (T8Suite ran it on its GUI thread).
  - **Connect** (Realtime menu): a session with security access at level FD and the keep-alive; the status bar shows "Connected: <software version>".
  - **ECU menu** (the Programmer page's CAN Flasher group): Read ECU, Flash current file to ECU, Recover ECU, with the Legion bootloader unless Settings → "Use Legion Bootloader" is off. The library's BackgroundWorker-shaped calls run on the ECU thread and their Result is the outcome.
  - **Get ECU information:** frmECUInformation's fields in a window.
  - **Get fault codes (OBDII)** with T8Suite's DTC files (DTC_SaabHSTRC / LSTRC / TRC); **Clear DTC and knock counters**.
  - **SRAM maps:** read and written with `readMemoryNew` / `writeMemoryNew` in 0x40-byte blocks; the viewer's ECU buttons only for symbols with an SRAM address, as T8Suite; the auto update only for the maps that live in SRAM alone.
  - **Actions → Import SRAM file** and the symbol menu's Read from SRAM file.
  - **Settings:** the realtime group's connection (adapter type, adapter, serial speed, Only P-bus connection), Use Legion Bootloader, Auto update SRAM viewers.
- [x] T8CoreTest: T8Ecu's thread, no adapter means nothing starts (and a flasher session isn't left busy), ReadDTC's lines, the DTC catalog. T8AppTest: connect without an adapter, the ECU buttons per symbol, an SRAM file, the fault codes and ECU information windows.
- Deliberate differences:
  - Every action closes what the last one left open first, and the flasher sessions, ECU information and fault codes close their device afterwards (T8Suite left them open).
  - The viewer's Read from / Save to ECU connect first, as T7Suite did and as T8Suite's double-click did (T8Suite's buttons asked for a connection instead). A write the ECU refuses says so (T8Suite: "Could not write SRAM" in the status bar).
  - Flash current file to ECU points out unsaved map changes and fixes or offers to fix the checksum first, as in the T7 port.
  - Recover ECU takes only a T8 bin whose checksum verifies, as TrionicCANFlasher checks (T8Suite took any file).
  - Read ECU, ECU information and fault codes say why they failed (T8Suite stayed silent or showed an empty window).
  - The Legion options stay at the library's defaults, as in T8Suite (the flasher has settings for them).
  - Not here: T8Suite's SRAM snapshot button had no handler since 2017; TrionicCANFlasher reads SRAM.
- Known T8Suite bugs fixed:
  - **Adapters:** every action built a new adapter without closing the last; now the last one is closed first.
  - **Read ECU:** it waited forever when security access was refused; that's now a failure.
  - **Flash → recover:** the "attempt to recover?" answer was compared with OK, so recovery never started. Yes now recovers with the same file.
  - **Recover ECU:** the adapters filtered out the ECU's 0x011 / 0x311 answers; now `SetCANFilterIds(FilterIdRecovery)` is set first, as the flasher does.
  - **Fault codes window:** Clear worked on a device that had been closed, so it did nothing; it now opens its own session.
- Bench, 2026-10-10: Read ECU, Flash current file to ECU, Connect ECU, Get ECU information and Get fault codes work. A bin flashed with TrionicCANFlasher read back identical in the application and HWIO areas; NVDM differed only by the ECU's new programming-history record, and boot by the bench ECU's own bootloader (not flashed). Still to test: the fault codes' Clear, SRAM map read / write, Recover.

### 6. Realtime
- [x] Shared now (moved from T7Core / T7App):
  - **SuiteCore:** `Realtime.cs` (the table's rows and passes, the `RealtimeEngine` loop, layouts, live cell tracking, the wideband conversions), `RealtimeLog.cs` (the .t7l / .t8l format, reader, sections, filters, CSV export and the writer) and `LogMatrix`. A suite's `RealtimeRules` gives its names and conventions: the signed list, the per-cylinder counters, the default rows, the "Add to realtime list" presets, the status texts, the maps whose cell is tracked, and the log and layout extensions.
  - **SuiteApp:** the panel (`RealtimeViewModel` / `RealtimeView`), the add / edit symbol dialog, the log viewer, log selection, matrix, log filters and symbol colours windows, and the Realtime menu's half of the main window. T7's AutoTune, AFR maps and Eco / Norm / Sport are its panel's subclass (`T7RealtimeViewModel`); the view locator falls back to a base class's view.
  - T7's tests pass with the moved names, and its renders are unchanged.
- [x] T8 (`T8Realtime`, `T8RealtimeEngine` in T8Core):
  - **Passes over GMLAN:** with "Prefer dynamic retrieval of live data" (default on) the table is one dynamic list (3B 17, read with 1A 18). Rows it can't hold are read by address. After a failure of the list everything is read by address for the rest of the session, as in T8Suite. The library's keep-alive rests while the panel polls.
  - **T8Suite's table:** its rows (battery voltage first, its names, no fuel consumption), signed list, per-cylinder counters (KnockCyl as bytes, KnkCntCyl, MisfCyl), status texts, the maps whose cell is tracked, and the "Add to realtime list" presets.
  - **Logs:** `<bin>-yyyyMMdd-CanTraceExt.t8l`, Load trionic 8 logfile, the CSV and LogWorks exports (LogWorks without a wideband symbol, as in T8Suite), matrix, log filters, Set symbol colors, Write log marker [F6], Toggle realtime panel [Shift+F1], layouts (.t8rtl).
  - **Menus in T8Suite's order:** the Realtime menu ending with View knock count map (KnkDetAdap.KnkCntMAP) and View misfire tab (MisfAdap.N_MisfCountCyl); File → Setup log filters; the symbol list's Add to realtime list.
  - **Settings:** Prefer dynamic retrieval of live data, and Use wideband O2 on com port with device and port (shared with T7 now).
- [x] T8CoreTest: the dashboard rows on a stock bin, the cell tracking, per-cylinder counters, status texts, the log's name, the dynamic list's data going to its rows. T8AppTest: the panel without hardware, a pass on screen, a .t8l in the log viewer.
- Deliberate differences:
  - The dashboard displays keep T7Suite's decimals (T8Suite showed all nine without).
  - The AFR display toggles AFR / λ on a click, as in T7Suite (T8Suite's didn't react).
  - Rows the dynamic list can't hold (more than 16 bytes, no SRAM address) are read by address in the same pass; T8Suite didn't read them while the list was in use.
  - When the panel stops, the library's keep-alive resumes; T8Suite left it stalled for the rest of the connection.
  - A log line's time is the start of its pass, as in T7Suite (T8Suite: the end).
- Known T8Suite bugs fixed:
  - **Dynamic list:** the data was matched to the rows by counting, so a row that couldn't go into the list shifted every later row's value; the data now goes to the rows it was read for.
  - **Boost map cell:** the tracking rule misspelt AirCtrlCal (AirCrtlCal), so the boost map never showed its cell.
- T7 fix from the sharing: the per-cylinder rows (KnockCyl1-4, MisfCyl1-4) and the serial wideband's row are marked as derived. After the table changed (a symbol added, moved or removed) the engine used to poll them as rows of their own, which put 0 on screen over their values.
- Not yet: sound notifications and the Combi adapter's ADC / thermocouple channels (for both suites, see After T7).
- Bench, 2026-10-10: the realtime panel works on a T8 ECU.
- Fixed after the bench test: Disconnect ECU while the panel polled ended it with "Realtime stopped: Object reference not set to an instance of an object". The disconnect waited on the ECU thread behind the running pass, so the loop still saw the session open and queued one more pass, which ran on the closed adapter. A pass that finds the session closed now ends the polling quietly (T7 too), and closing a T8 session ends the keep-alive stall, so the next connection keeps its tester present.

### 7. Tools
- [x] Shared now (moved from T7Core / T7App):
  - **SuiteCore:** `SuiteBinary.Disassemble`, `InterruptVectors` and `AxisRows`, each suite with its own disassembler (T7: the 68332 and 256 vectors; T8: the MC68377 and 120), and `Disassembly.WriteFunctions` for the listing. `Airmass.cs` holds the airmass result viewer's base: the grid, the interpolation, the torque, power, injector, lambda, EGT and fuel flow estimates, the options, the limiter types and the compressor map. Each suite keeps its tables and limiters (`T7AirmassResult`, `T8AirmassResult`).
  - **SuiteApp:** the disassembly, hex view, interrupt vectors and axis browser views; the airmass result viewer with its grid, dyno graph and compressor map (the suite passes its calculation, captions, gears and legend as an `AirmassSuite`); the compressor map images and the disassembly's highlighting; the Information handlers (`MainWindowViewModel.Tools.cs`, `SuiteMainWindow.Tools.cs`).
  - **Smaller shared changes:** the Tuning menu keeps the items an app's XAML puts in it before the map buttons (T8's Tuning Wizard). Open pickers can have a title and a first folder, save pickers a title. The symbol list can show a map preview (T8). `TuningPackage.Parse` reads a package's lines (the wizard decrypts its packs first).
  - T7's tests pass with the moved names, and its renders are identical (apart from the usual timestamps).
- [x] T8Suite's tools:
  - **Information:** Browse axis information (and the symbol list's Browse axis info), Show interrupt vectors (120), Show disassembly and Show full disassembly (T8Suite's Disassembler, lifted into T8Core).
  - **General actions:** View file in hex, and the Airmass result viewer:
    - the pedal map is a torque request, turned into airmass through TrqMastCal.m_AirTorqMap;
    - T8's limiters: TrqLimCal, FFTrqCal on E85, TMCCal with an automatic, Trq_ManGear per gear;
    - VE in 1/128, the EGT estimate always made richer;
    - "Car is high output (175/210 hp)" picks the Tab1 tables, else Tab2;
    - gears from Undefined to Sixth, then Reverse; six legend entries; the compressor map's first guess from the VIN.
  - **Edit:** TEM editor (only with a TEM table of more than one entry) and PID editor.
    - A copy of the table with T8Suite's columns, symbol lookups, validation messages and colours, and a find box.
    - Ok writes every row back (`T8Binary.WritePids` / `WriteTems`) and checks the checksum as on open.
  - **Bit mask symbols** open the bit mask viewer ("Bit masked view of symbol"): the 16 bits, named by the symbols at the same address. The word comes from the file, or from the ECU for an SRAM symbol. Ok writes it into the file with a transaction entry and checks the checksum, or writes it into SRAM.
  - **Map preview popup** (Settings → Show map preview popup, off by default): hovering a name in the symbol list shows that map's table, read only.
  - **File → Create binary from TIS file** (`TisFile`):
    - a base bin's bootloader and adaption data (0x00000-0x1FFFF);
    - the TIS file's program from 0x20000, decoded (a .gbf or .s19, gzipped or not);
    - T8Suite's programming station field and adaption flag after it, FF elsewhere.
  - **Tuning → Tuning wizards → Tuning Wizard** (`WizardPack`):
    - the 16 signed `.t8x` packs from `TuningPacks/`, which T8App copies next to the program, filtered by bintype, whitelist and blacklist;
    - T8Suite's pages;
    - the pack is applied as Import tuning package, after a backup `<bin>-<time>-BACKUP-BEFORE-WIZARD-<pack>.bin`; then the PI area gets the programmer name and release date, and the pack's message is shown.
- [x] T8CoreTest:
  - the PID / TEM write-back, the vectors, the disassembly, the axis rows;
  - the TIS builder (raw, gzipped, S19);
  - the packs: 16 verify, compatibility, applying with the backup and PI area;
  - the airmass result on a stock bin.
- [x] T8AppTest:
  - the PID editor (validation, colours, lookup, write-back);
  - the bit mask viewer (a flash write, an SRAM symbol without an ECU);
  - the map preview popup, the wizard's pages and result;
  - the information tools and the airmass viewer, with renders.
- Deliberate differences:
  - **PID / TEM editors:** symbols are named by SmartVarname, as in the symbol list. The find box filters rows (T8Suite's grid had a find panel). The PID editor is disabled without an open file, like the other Actions (T8Suite said "Please open a Trionic 8 file before playing with this feature"); a file without a table still opens it empty.
  - **Bit mask viewer:**
    - SRAM bit mask symbols open it from the symbol list too (T8Suite showed them there as a 2-byte SRAM map, and the viewer only when opened by name).
    - It connects first, as the port's viewers do.
    - Ok writes an SRAM word into the ECU (T8Suite's branch for that was empty).
  - **Map preview popup:** a tooltip beside the hovered name that hides when the pointer leaves it. T8Suite used a window right of the list at the mouse's height.
  - **Create binary from TIS file:**
    - An S19 is converted in a temporary folder and a gzipped file is unpacked in memory. T8Suite left `<name>.bin` next to the S19 and the unpacked file in the current folder.
    - A TIS file too large for a bin says so (T8Suite threw).
  - **Tuning wizard:**
    - A pack that can't be read is logged and skipped (T8Suite failed to start).
    - The packs are read when the wizard first opens (T8Suite: at startup).
    - The code page shows only for packs with a code; none of the shipped packs has one.
  - **Airmass result viewer:**
    - Tables the bin lacks don't limit, as in the T7 port. T8Suite read them as 0, which capped every cell at the first m_AirTorqMap column or zeroed it.
    - Target lambda rounds as T7Suite's did; T8Suite rounded after the injector correction, which is at most 0.01 apart.
    - An invalid compare file keeps the current compare (T8Suite dropped it).
    - A bin without the tables says so (T8Suite did nothing).
    - A legend entry whose table isn't in the bin does nothing (T8Suite: "Symbol … does not exist in this file").
  - **View file in hex** also shows an imported SRAM file, as T7Suite did (T8Suite's call was commented out).
  - **Disassembly tab:** titled "Disassembly: <bin>.asm", as in the T7 port (T8Suite: "T8Suite Disassembler").
- Known T8Suite bugs fixed:
  - **Disassembler:** it left the bin open until the garbage collector ran.
  - **Tuning wizard's code page:** Next compared the text from before the last keystroke, so it came one key late; after Back the page showed for every pack.
- Not ported: the Debug ribbon group (registry-only DebugMode), "Tune me up™" and "Easy tune to stage III" (hidden in T8Suite)

### 8. Release
- [x] **SetupT8** (WiX 6, a copy of SetupT7): T8SuitePro's upgrade code `{D1B8E08D-7E0E-4F1D-89D4-7AA63766CCF6}` and folder `Program Files (x86)\MattiasC\T8SuitePro`, so it replaces the old T8SuitePro in place. Per machine, x86, the VC++ 2010 merge module for the Lawicel driver, desktop and Start menu shortcuts, `.bin` Open with (`T8Suite.bin`), the whole self-contained publish folder.
- [x] **T8App's publish folder:** `Binaries/` from T8Binaries (T8Extras' job, publish only), `TuningPacks/`, T8Suite's NLog.config (its log files in `<AppData>/MattiasC/T8SuitePro`, plus the adapter drivers' loggers as in T7's), Kvaser's canlib32.dll on Windows, libusb-1.0.dll added by CI; the DTC lists, T8Pub.pem and manuals were there already.
- [x] **CI:** the package job has a suite dimension (`T7` / `T8` × five RIDs; a `T7suite_v` / `T8suite_v` tag packages only that suite, so a release carries only its own setup, which the update check downloads). Per-suite artifacts, nightlies `T7suite_nightly` and `T8suite_nightly`.
- [x] **Linux:** `packaging/linux/T8Suite/` (udev rule, install-desktop.sh with `StartupWMClass=T8App`, the icon); T7's files moved to `packaging/linux/T7Suite/`.
- [x] **README** covers both suites. The updater needed nothing: T8App already checks `T8suite_v` releases.
- Deliberate differences:
  - The setup is called T8Suite like the program (T8SuitePro before), and so are its shortcuts; the folder keeps the old name.
  - Not shipped from the old folder: DTCDescription.xsd (not read), ASM-Mode.xshd and the compressor maps (built in), knock.wav (sound notifications aren't ported), T8.ico, the old adapter wrapper DLLs.
- CAN frames are logged to canLog always, as T8Suite did (T7 has the Enable CAN logging setting).
- The nightlies move on `master` now, for both suites; they were on `net10`, which no longer exists on origin, so none was ever published.
- Not tested yet: WiX only runs on Windows, so SetupT8 is first built by CI. The first release needs a `T8suite_v` tag above the old 0.1.57 (e.g. `T8suite_v2.0.0`): an untagged build is 0.0.0, which the MSI treats as a downgrade.
- Known gap (T7 has it too): the old T8Extras stays installed. It owns the same `Binaries\*.BIN` paths, so uninstalling it afterwards removes the stock bins; uninstall T8Extras first, or repair T8Suite afterwards.

## Chunks: T5Suite

### Starting point (2026-10-10)

- The old T5Suite 2.0: T5Suite2.0/ (frmMain.cs 13.3k lines, 23k LOC non-designer), Trionic5Tools/ (32k: the file logic, firmware properties, tuner, autotune maps, translators), Trionic5Controls/ (49k: viewers, realtime panel, wizards, disassembler), T5CANLib/ (9.5k, replaced by TrionicCANLib's `Trionic5`).
- How it behaves, where it differs from T7Suite, goes into `docs/T5SUITE-BEHAVIOUR.md`.
- **Modernize while porting:** T5Suite is the oldest code; T5 gets the shared window (SuiteCore / SuiteApp) and the T7 / T8 improvements (Recent files, multi-step undo, docking, the shared map viewer, realtime panel and logs, tools), and shared code grows where T5 needs a feature T7 / T8 already have.
- **Stock bins:** `T5Binaries/` (85 bins: 81 T5.5 of 256 KB, 4 T5.2 of 128 KB), all with a valid checksum, are the golden corpus.
- **Settings:** T5Suite kept them in `HKCU\Software\T5Suite2` (outside MattiasC), in its own `T5AppSettings`.

### 0. Scaffold
- [x] `docs/T5SUITE-BEHAVIOUR.md`, this plan, branch `net10-t5`
- [x] T5App (AssemblyName T5Suite, `T5suite_` tags, T5Suite 2.0's icon), T5Core, T5CoreTest, T5AppTest in the solution; CI builds and tests them

### 1. T5Core
- [x] Lifted from Trionic5Tools (namespace `Trionic5Tools` kept): Trionic5File, the file information and properties, translators, tuner, anomalies, AFR / ignition / fuel maps; from Trionic5Controls the disassembler; from T5Suite2.0 the Idc file and SrecordT5. `symbolindex.xml` ships with T5Core (it names masked symbols). Not lifted: Trionic5Immo (a disabled software licence check, not the car immobiliser) and the empty Trionic5Bootloader
- [x] `T5Binary : SuiteBinary`; T5CoreTest: golden test over T5Binaries/ (taken from the lifted code before refactoring)
- Settings: `T5AppSettings` on the shared JSON settings (`<AppData>/MattiasC/T5Suite2/settings.json`); the legacy import reads `HKCU\Software\T5Suite2` (`SettingsKey.RegistryPaths`).
- Deliberate differences: footer strings shorter than the old value are padded with spaces (T5Suite left the old tail); the firmware writers' transaction entries use the file offset; backups and the symbol index are found with Path.Combine (the old backslash joins made odd file names on Linux)

### 2. MapControls
- Nothing T5-specific expected; T5's viewer differences go with chunk 3.

### 3. Read-only app
- [x] T5App on the shared window: open bin / S19, the symbol list with T5Suite's columns (Description, Symbol; addresses and length in the column chooser), map viewers with T5's factors, offsets, axes and units, projects, Recent, the status bar's "T5.5 | 16 Mhz | RAM locked"
- [x] Map viewers follow MapViewerEx: 16-bit values above 32000 read negative, `I_kyl_st!` / `I_luft_st!` / `Last_temp_st!` signed 8-bit, the injection maps unsigned (`SuiteBinary.SignAbove`); with a 3.0 / 3.5 / 4.0 / 5.0 bar sensor the pressure maps (MapIsScalableFor3Bar) and the "MAP" / "Pressure error (bar)" axes show × 1.2 / 1.4 / 1.6 / 2.0, edits round up (`ScalePercent`); the open-loop mark on the injection, ignition and knock fuel maps from `Open_loop!` / `Open_loop_knock!` when lambda control is on
- [x] The symbol filter "Only symbols within binary" after every open, switched to all symbols when the ECU connects or an SRAM snapshot opens (T5Suite's SetDefaultFilters)
- [x] The symbol list grouped by category, then subcategory (`SymbolGroupPaths`), the description cell coloured by category as in T5Suite (Fuel, Ignition, Boost control, Misc, Sensor, Correction, Idle); `Pgm_mod!` opens the firmware options (T5Suite: a read-only view of the file's or the ECU's bits); SRAM-only symbols offline open from the loaded snapshot, else "Symbol resides in SRAM..." (chunk 5)
- [x] Settings: the shared ones, plus "Advanced mode enabled" (the advanced tuning wizards are hidden without it, as in T5Suite) and "Auto detect mapsensor type"; T5Suite's My Maps defaults (Idle RPM, Boost Map, Reg Kon Mat)
- Not ported: Ctrl+Z / Ctrl+Shift+Z for project roll back / forward (the map viewer uses Ctrl+Z for its own undo; the menu items stay); "Show additional symbol information" (the Description column shows it); "Auto highlight selected map"; the skins and "Mapviewer to use"
- Deliberate differences: the map sensor view follows the file (T5Suite had 12 view types, Decimal / Easy per sensor, in one combo); the 3D graph's axis labels stay in the axis units (T5Suite showed MAP in bar there); negative values in signed 8-bit maps are saved (T5Suite saved them as 0); the viewer title is the shared `Symbol: <name> [<file>]` (T5Suite: `<file> [<name>]`)

### 4. Offline tuning
- [x] Manual tuning menu (T5Suite's map buttons by group, T5.2 without the gear limiters and the ignition retard limit), My Maps
- [x] Trionic options (firmware): one window with T5Suite's flags, the turbo / injector / sensor / stage markers, the footer fields and the grid-only fields; Ok writes them with transaction entries and the checksum
- [x] Tuning wizards (`T5Tuning` over the lifted Trionic5Tuner): Tune me up ® (stage 1-3 and stage 4 and higher with the free tune settings), Convert to a 2.5 / 3.0 / 3.5 / 4.0 / 5.0 bar MAP sensor, larger injectors (the proposed constant, crank factor and battery correction), E85, boost adaption ranges, boost bias range, RPM limit; the reports in a list window with Save (.txt)
- [x] Compare with another binary, Compare to original file, Binary compare files, Move data to another binary (T5Suite's wizard texts, the shared symbol selection), Search map content, Export / Import map to CSV, Examine binary, Check for anomalies, Open a saved report (.txt), Merge binary files, Split binary file, Lookup partnumber (with T5Suite's boosts, model years, region, Aero, high altitude), VIN decoder
- [x] Compare with another binary takes several files: one opens its results, more a "Compare list" (file, number of differing symbols; Enter / double-click opens its results); shared, T7 / T8 keep one file
- [x] The partnumber list (Lookup partnumber's "..." button): every known partnumber grouped by car model and engine, the stock bins in Binaries coloured (16 MHz yellow green, 20 MHz orange); Ok or double-click looks it up
- [x] User library (File): "Add files" scans a folder for T5 bins and lists each with T5Suite's guesses (stage, injectors, sensor, torque, E85, T7 valve, partnumber, software, CPU, RAM lock); Open selected, Compare to selected, Clear library. Kept in `UserLib.json` in the settings folder (T5Suite: UserLib.xml next to the program)
- Deliberate differences: the MAP sensor wizard converts to the sensor chosen (T5Suite always converted to 3.0 bar); the free tune updates the checksum after its last writes; the injector wizard and RPM limit write transaction entries in a project; boost adaption / bias say when the code pattern isn't found (T5Suite stayed silent); the wizards refresh open viewers; Compare to original file finds `Binaries/<partnumber>-<software id>.bin` or `Binaries/<partnumber>.bin` (T5Suite looked for the first and compared the second, so the item never lit up); compare skips SRAM-only symbols (T5Suite compared file offset 0 for them), lists flash symbols only one file has as "Missing in", and counts values as the shared compare does; transfer skips SRAM-only symbols; the CSV import scales values back (T5Suite's Excel import took them raw, so a round trip changed scaled maps) and the CSV export writes enough decimals for maps with factors below 0.01; reports are text (T5Suite's were DevExpress .prnx); Split asks nothing and overwrites chip1.bin / chip2.bin as before

### 5. ECU
- [x] `T5Ecu` over TrionicCANLib's `Trionic5` (CAN) on the shared ECU worker: connect (the software version in the status bar, a note when it isn't the open file's), SRAM maps (online tuning), download / upload flash (the T5 CAN Flasher's way, a session of its own), DTC codes (the SRAM error counters), clear knock counters. P&E Micro, DIY USB BDM and the DIY CAN adapter have no driver on .NET 10 and stay out
- [x] Online like T5Suite: while connected every map opens with the ECU's SRAM data and Save writes SRAM and the file in one go; a map only in SRAM opens from the loaded snapshot offline, else "Symbol resides in SRAM and you are in offline mode..."
- [x] Synchronization: "Data synchronization" on connect (the file's date at length - 0x1E0, the ECU's at SRAM 0x7FC0; Accept / Decline / Reverse) and Synchronize maps ("Sync: N%", "Synchronized", the project logbook entry)
- [x] SRAM: Download SRAM from ECU (into the project's Snapshots, else Snapshot-&lt;bin&gt;-&lt;time&gt;.RAM), Upload SRAM to ECU (and into the file when asked), Compare ECU with binary, Compare SRAM snapshot to binary, Compare SRAM snapshots (shared with T7), Import SRAM snapshot into binary (merge adaption data)
- Settings: T5Suite's CAN device becomes the shared adapter type once (Lawicel, CombiAdapter / Multiadapter, Just4Trionic, Kvaser); the legacy import also reads `Software\MattiasC\T5Suite2` (log filters, channels, symbol colours)
- Deliberate differences: an ECU without a sync date proposes binary to ECU (T5Suite stamped it "now" and proposed ECU to binary); the sync dialog comes once per connect; Compare ECU with binary writes its snapshot next to the bin (T5Suite: the working directory); flashing writes the open file after the shared checksum check (T5Suite let you pick any file, and needed a realtime connection first); clearing a DTC clears that counter, `_fel` ones included (T5Suite cleared every `_error` and left `_fel`); the knock counters a file lacks aren't written at SRAM 0; the adaption merge skips maps the file or snapshot lacks (T5Suite threw on T5.2 files, after the backup) and caps a corrected fuel cell at 255 (T5Suite threw), and drops "Knock information (used to retard ignition)", which never changed anything

### 6. Realtime
- [x] T5's realtime panel on the shared one (`T5Realtime`): one table with every symbol T5Suite's tabs polled (Rpm, P_medel, Regl_tryck, Max_tryck, Medeltrot, the temperatures, Pgm_status, injection time, the enrichments per cylinder, ignition and knock offsets, boost reduction, P / I / D, PWM, speed, torque, the knock counters, the lambda input), Trionic5SymbolConverter's conversions (the MAP sensor factor on the boost values, signed temperatures, the narrowband sond as λ, AD_EGR / AD_cat as wideband AFR over 0..255, Pgm_status little-endian), the lambda and fuel cut texts from Pgm_status, live cells on the fuel, ignition and boost maps by T5Suite's axis captions
- [x] Logs: the shared .t5l writer, viewer, exports, matrix and filters; T5Suite's graph names (Boost, Coolant, IAT, Inj.dur, ...)
- [x] AFR maps: AFR target / feedback / error and the idle ones (Online tuning), Generate AFR target, "Always create AFR maps"; the feedback fills from every pass with a wideband value
- [x] Autotune fuel (Settings → Advanced mode, Shift+F5): T5Suite's gate (CheckAutoTuneParameters, the enrichment filter, 500 ms after a throttle drop), closed loop off while tuning, the adaption folded into Insp_mat! first (T5.5), Reset fuel trims, auto update straight into SRAM or "Select mutations to accept" (the accept window is shared with T7 now)
- [x] Autotune ignition (T5.5, Settings → Advanced mode): Ign_map_0! from SRAM capped at the global maximum first, the knock pressure limit, knocks retard and lock a cell, changed cells straight into SRAM, "Keep adjusted ignition map?"; Ignition lock map (a read-only viewer), Release locked ignition cells; File → Autotune settings (T5Suite's fuel and ignition groups)
- [x] Knock map snapshots: "Knock counter snapshot after disconnect" (T5.5) writes Knock_count_map into Snapshots as .KNK; the list shows the total knocks, Ok shows one, Compare the difference of two
- [x] The panel's Settings tab (T5Suite's Pgm_mod! switches: read, flip, write the whole symbol, read back) and Engine status tab (the 40 Pgm_status LEDs); the shared panel shows them when a suite fills them
- [x] LogWorks export: the shared range and unit tables (CommonSuite's already named most T5 symbols) gain P_medel, Apc_decrese, TQ, AD_EGR / AD_cat and Knock_offset1234. T5Suite's own tables were keyed by the display names its logs used, which the port's logs don't
- Deliberate differences: one table polled instead of a watch list per panel tab (a pass reads about 30 symbols, the slow ones every 2nd to 5th pass); P / I / D read signed at 65536 (T5Suite: 65535); the wideband may come through AD_cat too (T5Suite converted only AD_EGR); a symbol the bin lacks isn't polled (T5Suite read SRAM 0); the panel's airmass and consumption displays are greyed (T5 has no such symbols); the first run's main AFR target is T5Suite's default (T5Suite put the idle default, a flat 14.7, in its place until the next start); the autotuned T5.2 adaption map stays in SRAM (T5Suite wrote it into the file's Insp_mat!); an accepted autotune is one file write with one transaction entry

### 7. Tools
- [x] Disassemble file (the functions listing at flash addresses; the hex pane follows through `SuiteBinary.FlashBase`), Show vector information (the CPU's vector names, now shared with T7), Generate Idc file (`<bin>-autogen.idc`), Show file in hex, Browse axis information (T5Suite's short texts for the map and its axes)
- [x] Show dyno graph ("Estimated dyno results": torque, power and injector duty cycle from the WOT boost, Refresh, Export PNG), Show compressor map (the boost-based curve, T5Suite's two extra compressors, the turbo guessed from the footer and part number), Injection timing viewer (ms or duty cycle per cell of the fuel, idle or knock map at an intake temperature, voltage and injector constant) over `T5InjectionModel`, T5Suite's injection model in one place
- [x] Compare SRAM snapshots covers every symbol in SRAM (T5Suite's, the adaption tables included); Binary compare SRAM snapshots; Import SRAM snapshot into binary (chunk 5)
- Not ported: frmBoostAdaption, AsmViewer, FunctionCompiler (dead in T5Suite); the hex view scrolling to the selected symbol; "Snapshot: <name>" as the status text (the shared "SRAM: <name>")
- Deliberate differences: the compressor map takes one VE for every rpm (T5Suite had 16 boxes, all 90 by default); the injection timing grid uses the map grid's colour scale (T5Suite: red cells and duty cycle bars); the dyno series are named Torque / Power / Injector DC (T5Suite's chart mislabelled them); the trap vectors read "Trap instruction vector N" (T5Suite's list said "vectors")

### 8. Release
- [x] SetupT5 replacing the old T5Suite 2.0 (its upgrade code and folder, `MattiasC\T5SuiteII`; only a `T5suite_v` tag above its 2.0.30 replaces it), the publish folder (stock bins in Binaries, the two manuals, NLog.config, canlib32.dll on Windows), CI packages and the `T5suite_nightly` pre-release, Linux packaging, updater on `T5suite_v`, README
- [x] The old code (T5Suite2.0/, Trionic5Tools/, Trionic5Controls/, T5CANLib/, T7Suite/, T8Suite/, CommonSuite/, the old controls, flashers, libraries, setups, solutions and release scripts) moved to `OldSuites/`, still in the tree for reference (its README lists what is where); the behaviour docs' paths are relative to it

## After T7

- **T8Suite:** ported on branch `net10-t8`, see Chunks: T8Suite above; waiting for its first CI run on Windows and a `T8suite_v` tag.
- **T5Suite2.0:** last. It is the largest UI (Trionic5Controls alone is 63k LOC) and the oldest code. T5 support is already in the new TrionicCANLib.
- **Realtime leftovers from chunk 6:** sound notifications (3 slots, needs a cross-platform audio player) and the Combi adapter's ADC / thermocouple channels with their settings.
- **Dead code to delete eventually:** T7CANFlasher/ (replaced by TrionicCANFlasher), the T7Libs/ wrapper DLLs, AquaGauge, LBIndustrialCtrls, ProCharts, MouseGestures.

## Open questions

- Does AvaloniaEdit support Avalonia 12? If not: an older Avalonia, a fork, or a plain read-only text view for the disassembler.

**T5Suite (branch `net10-t5`), each with the default taken:**
- The old code went into an `OldSuites/` folder (one commit at the end of the branch), not a branch: it stays searchable next to the new code. A branch instead? Then drop that commit.
- Advanced mode (Settings, off by default as in T5Suite) hides the advanced tuning wizards and the autotune, as T5Suite did, but no longer auto-hides the symbol list. Keep the setting, or show everything always?
- Realtime: one table polls every symbol T5Suite's panel tabs used (about 30 per pass, the slow ones every 2nd to 5th pass) instead of a watch list per tab. Fast enough on the P-bus? Needs a bench test.
- Logs use the shared writer (symbol names, one `<bin>-<date>-CanTraceExt.t5l` per day next to the bin) instead of T5Suite's per-session files in `Logs\` with display names; old .t5l files still open.
- Upload flash to ECU flashes the open file after the checksum check (T5Suite let you pick any .bin and needed a realtime connection first).
- The sync date stays (SRAM 0x7FC0, file length − 0x1E0) with the dialog on connect; an ECU without a date proposes binary → ECU (T5Suite stamped it "now" and proposed ECU → binary).
- An ECU running another software version than the open file gets a warning on connect; SRAM access is still allowed (T5Suite said nothing). Block writes instead?
- Projects use the shared folder (`<Documents>/TxSuite/Projects`), so T5, T7 and T8 projects list each other's there.
- Reports (examine, anomalies, tuning wizards) are text with Save (.txt); "Open a saved report" opens those (T5Suite's .prnx were DevExpress documents).
- Not ported: Ctrl+Z / Ctrl+Shift+Z for project roll back / forward (the map viewer's Ctrl+Z undoes its own edits; the menu items stay). Wanted?
- Not ported on purpose: "Browse tunes in internet repository" (the host is gone), the BDM groups (P&E / DIY USB BDM, no .NET 10 drivers), the DIY CAN adapter (mct_can.dll), the licence check.
- First release: SetupT5 replaces the old T5SuiteII only from a tag above 2.0.30, e.g. `T5suite_v2.1.0`.

## Log

- 2026-10-10: T5's compare list, partnumber list, user library, symbol filter and category colours, LogWorks names; every T5Suite feature on the list is ported except Ctrl+Z for roll back.
- 2026-10-10: T5's Settings (Pgm_mod!) and Engine status panel tabs.
- 2026-10-10: T5 autotune ignition, its lock map and settings, knock map snapshots.
- 2026-10-10: The old suites moved to `OldSuites/`; the root holds the .NET 10 suites, their data and docs.
- 2026-10-10: T5 chunk 8: SetupT5, packaging, CI and README. Waiting for a first CI run on Windows.
- 2026-10-10: T5 chunk 7 implemented: disassembly listing and hex sync, vectors, Idc, axis browser, dyno graph, compressor map, injection timing viewer, the SRAM snapshot compares.
- 2026-10-10: T5 chunk 6 implemented: the realtime table and conversions, Pgm_status texts, live cells, logs, AFR maps and fuel autotune. The panel's dashboard names, a per-row decode, the status casts and a per-pass hook are suite hooks now; the wideband symbol settings and the autotune accept window moved into the shared projects. Waiting for a bench test.
- 2026-10-10: T5 chunk 5 implemented: online maps from SRAM, the sync dialog and Synchronize maps, SRAM download / upload / compares, adaption merge, the CAN device migration. The SRAM compares moved into the shared view model. Waiting for a bench test.
- 2026-10-10: An imported project folder inside Program Files falls back to the default (the old suites defaulted to the program folder, which isn't writable there).
- 2026-10-10: T5 chunks 0-4 implemented on branch `net10-t5`: T5Core (the lifted Trionic5Tools with `T5Binary`, golden test over the 85 stock bins), T5App on the shared window with the symbol list, map viewers (T5's signs, MAP sensor scaling, open loop), firmware options, the tuning wizards, compare / transfer, reports, merge / split and the part number lookup.

- 2026-10-10: T8 chunk 8 implemented: SetupT8 replacing the old T8SuitePro, T8's publish folder (stock bins, NLog.config, Kvaser), CI packaging and nightlies for both suites, Linux packaging per suite, README. Waiting for a first CI run on Windows.

- 2026-10-10: T8 chunk 7 implemented: the disassembly, hex view, vectors, axis browser and airmass result viewer moved into the shared projects; T8Suite's PID / TEM editors, bit mask viewer, map preview popup, Create binary from TIS file and Tuning Wizard (.t8x packs). Disconnect ECU with the realtime panel polling no longer ends it with an error.

- 2026-10-10: T8 chunk 6 implemented: the realtime table, engine loop, panel and log tools moved into the shared projects; T8 reads over GMLAN's dynamic list (by address as the fallback) with T8Suite's rows, names and texts, and logs .t8l. Waiting for a bench test.

- 2026-10-10: T8 ECU bench tested: Read ECU (a bin flashed with TrionicCANFlasher reads back identical in the application and HWIO areas), Flash, Connect, ECU information and fault codes work.

- 2026-10-09: T8 chunk 5 implemented: T8Ecu over GMLAN (connect, Read ECU / Flash / Recover with the Legion bootloader, ECU information, fault codes, SRAM maps) on a shared ECU worker; the protocol stays per suite, the view model's ECU half, the fault codes window and the connection settings are shared. Waiting for a bench test.

- 2026-10-09: T8 chunk 4 done: compare, transfer, search, symbol imports and exports, tuning packages, My Maps and the quick map menu moved into the shared projects and serve both apps; T8App has them with T8Suite's menus and rules, plus its settings, part number lookup, VIN decoder and firmware editing. The sidecar now keeps names imported into T8 bins.

- 2026-10-09: T8 chunk 3 done: the main window is shared by both apps (SuiteBinary, SuiteProject, SuiteMainWindow and its view model in the shared projects), T7 unchanged; T8App opens bins with its symbol list, map viewers, projects and firmware information.
- 2026-10-09: T8 chunk 1 done: T8Core with the lifted file logic and T8Binary, T8CoreTest with a golden test over the 72 stock bins; the shared symbol table decoder no longer mixes up two files decoding at once.
- 2026-10-09: T8Suite port started on branch `net10-t8`. Its behaviour is read into `docs/T8SUITE-BEHAVIOUR.md` and the T8 chunks are planned. T8 chunk 0 is done: SuiteCore and SuiteApp were extracted from T7Core / T7App with T7 unchanged (tests green, renders identical), and T8App is scaffolded.
- 2026-10-09: Feasibility analysis done; plan agreed. Branch `net10` created.
- 2026-10-09: Chunk 0 done locally: solution, versioning props, T7App shell on Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.0, CI workflow.
- 2026-10-09: Chunk 8 implemented: packages and releases per tag (`T7suite_v`, upstream's scheme; `t7suite/` dropped), the MSI replacing the old T7Suite, Open with for `.bin`, the GitHub release update check, logs, README. Waiting for a first CI run and the first tag.
- 2026-10-09: The symbol list pinned to the side (Dock's auto hide) gives its width to the inner windows; the empty pane kept it as an invisible wall. Settings → Hide symbol window (T7Suite's option, imported) starts with the list pinned and slides it back in when a map opens.
- 2026-10-09: The light skin follows T7Suite's DevExpress one: grey workspace, white inner windows with blue titles, grey buttons, white map table headers framed in light steel blue, the symbol list's selected row in MediumBlue on light blue whether focused or not. Dark is unchanged. Not matched: DevExpress's thicker window frame and denser spacing, the white gutters between map cells, Fluent scrollbars / check boxes / tab strips.
- 2026-10-09: The 3D view draws with txlogger's meshgrid shader ported to SkSL on GPU canvases; the triangle renderer stays for CPU canvases, single columns and grids too large for the shader's loop.
- 2026-10-09: Inner windows measured at the workspace's size by Dock's MDI panel (the real cause of the off-centre, clipped 3D graph and cut tables): documents are now measured at their own size, map tables shrink their text to fit, the 3D view and its scales follow the light / dark skin; Set symbol colors and the disassembly's linked hex view added.
- 2026-10-09: Chunk 7 done: disassembler, hex view and interrupt vectors; tuning packs, SID, ESP / TCM, matrix from log and the smaller tools before that.
- 2026-10-09: Menus in T7Suite's ribbon order: File (with Project), Actions (with firmware information), Tuning, My Maps, Realtime, ECU (in Programmer's place), Skin (light / dark / system and window layout), Help (manuals, About).
- 2026-10-09: Inner windows: Dock's MDI panel is replaced (`ArrangedMdiLayoutPanel`) so whole windows, title bar included, are measured at their own size; a narrow window cuts its title short instead of pushing its buttons out. A window brought forward is repainted (Avalonia re-sorted the windows without redrawing them). The map viewer's Close button is gone, the title bar has one. Dock's control recycling is off: it moved one view per document between the tabbed and the inner window layouts, and the hidden tabbed layout took the active window's view (blank / grey windows after Layout mode → Tabbed → MDI); every host builds its own view, so only a tab switch in the tabbed layout starts a map's camera afresh. The status bar looks like T7Suite's (accent bar, dividers, empty items hidden).
- 2026-10-09: Menu items in the ribbon's order too: a page's groups between separators, the buttons column by column, with T7Suite's captions (Compare binary to SRAM snapshot, Transfer maps to another binary). That moves Compare to original file and Setup log filters to File, Import SRAM snapshot to Actions, Connect ECU and the fault code / sync / tuning package items to Realtime (ECU keeps the CAN Flasher group), and Set ethanol content into an Extra functions submenu; the Tuning menu drops the "Boost calibration" group, which wasn't on any ribbon page. Not on the menus: VIN decoder (part of Firmware information), the fullscreen and screenshot buttons, Check for updates and Release notes.
- 2026-10-09: Chunk 7: airmass result viewer with the dyno graph and compressor map; TuneToStage / tuning wizard found unreachable in T7Suite.
- 2026-10-09: Chunk 7: docking workspace (MDI inner windows / tabs, dockable symbol list).
- 2026-10-09: Chunk 6 bench tested (realtime values OK); sounds and Combi ADC moved to later. Chunk 7 started.
- 2026-10-09: Chunk 6 done apart from sounds and Combi ADC channels: log viewer, CSV / LogWorks exports, log filters, knock and misfire maps; waiting for a test on a running engine.
- 2026-10-09: Chunk 6: wideband, AFR maps and autotune; WidebandSupport vendored (Apache 2.0).
- 2026-10-09: Chunk 6 in progress: realtime engine, panel, .t7l logging and live cell tracking.
- 2026-10-09: Map colours switched to txlogger's scale; the window gets its own X11 class (T7App) so the taskbar shows T7Suite's icon instead of a launcher's named T7Suite.desktop; docking added to chunk 7.
- 2026-10-09: Chunk 5 done apart from SaabOpenTech: tuning packages to / from the ECU and the SRAM compares.
- 2026-10-09: Chunk 5 bench tested: flash read and write, SRAM writes; refused SRAM writes are now reported and closing waits for a running flash session.
- 2026-10-09: Chunk 5 implemented (ECU session, flashing, SRAM maps, snapshots, fault codes, sync); waiting for a bench test.
- 2026-10-09: Chunk 4 done: plus compare / transfer maps, symbol imports and exports, settings window, My Maps and the quick map menu.
- 2026-10-09: Chunk 4 in progress: map saving with the viewer toolbar, verify checksum, user descriptions, firmware editing, projects and the transaction log; checksum updates now repeat until the file verifies.
- 2026-10-09: Chunk 3 done: T7Binary and FirmwareInfo in T7Core, the main window with symbol list, map viewer tabs (MapViewer composite with sync) and firmware information, T7AppTest driving the app headless.
- 2026-10-09: Chunk 2 done: MapControls (MapData, MapOps, MapGrid, Surface3D, Graph2D), MapControlsDemo, MapControlsTest with headless Skia renders. Behaviour follows T7Suite, txlogger only for rendering.
- 2026-10-09: Chunk 1 done: T7Core with the lifted logic and JSON settings, T7CoreTest with the golden baseline over 256 bins (28 tests, ~22 s). frmMain extraction moved into chunks 3-7.
