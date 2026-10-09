# Porting the TuningSuites to .NET 10

Goal: lift T7Suite, T8Suite and T5Suite from .NET Framework 4 / WinForms to .NET 10 / Avalonia so they run on Windows, Linux and macOS, and replace every non-free dependency (DevExpress, Nevron, Office Interop). T7Suite goes first.

This file is the tracker. Update the checkboxes and the log at the bottom as work lands. How the old T7Suite behaves, read from its code, is collected in `docs/T7SUITE-BEHAVIOUR.md`; keep adding to it before porting a feature.

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
| Repo | Work happens on branch `net10` in TuningSuites |
| TrionicCANLib | ProjectReference to the local sibling checkout `../Trionic/TrionicCANLib/TrionicCANLib.csproj`, set once in `Directory.Build.props` as `$(TrionicDir)` and overridable with `-p:TrionicDir=…`. Not everything in roffe/Trionic is committed yet. CI checks out roffe/Trionic next to this repo; switch to a submodule once Trionic `net10` is pushed |
| WidebandSupport | Vendored into `WidebandSupport/` with an SDK-style net10 csproj and the System.IO.Ports package. Upstream has no licence file, so ask Mattias |
| Settings | JSON at `<AppData>/MattiasC/T7SuitePro/settings.json`, plus a one-time import from `HKCU\Software\MattiasC\T7SuitePro` (and its MRU key `HKCU\Software\T7SuitePro\MRUList`) on Windows, as the flasher does |
| Versioning, CI, packaging | Copy the flasher's: version from git tags in `Directory.Build.props`; self-contained win-x86 / linux-x64 / linux-arm64 / osx builds; WiX MSI, tar.gz and zip |
| Threading | The ECU and realtime loop run on worker threads and report back with `Dispatcher.UIThread.Post`, never a blocking Invoke (the flasher deadlocked that way). Wrap the library's sync calls in a worker plus a `TaskCompletionSource`, like the flasher's `RunOnWorker` |
| Map controls | Own Avalonia controls in `MapControls/`. Behaviour (selection, editing, keys, colours, menus, clipboard) follows T7Suite; from txlogger only the meshgrid 3D projection/drawing and the graph2d layout |
| Shared T7/T8 code | Not yet. CommonSuite logic is copied into T7Core. Shared Core and Controls projects get extracted when T8 starts: T7 and T8 share 67 filenames but only 8 identical files, so a shared layer now would be guesswork |

## Layout (new projects)

```
TuningSuites.slnx          new solution (the old *.sln files stay for reference)
Directory.Build.props      from the flasher, plus $(TrionicDir) = ../Trionic
WidebandSupport/           vendored, net10
T7Core/                    net10 class library, no UI: file, symbols, axes, checksum glue, projects, transaction log, realtime engine, tuning logic
T7CoreTest/                MSTest, golden tests over T7Binaries/
MapControls/               Avalonia controls: MapGrid, Surface3D, Graph2D, MapData codec
MapControlsDemo/           standalone app for working on the controls with fake data
T7App/                     Avalonia MVVM app, AssemblyName T7Suite
SetupT7/                   WiX MSI (chunk 8)
```

## Dependency replacements

| Old | New |
|---|---|
| DevExpress XtraGrid (map tables) | `MapGrid`: our own control, a `Render(DrawingContext)` override modelled on txlogger's mapviewer |
| DevExpress XtraGrid (symbol list, compare lists) | Avalonia `DataGrid` |
| Nevron 3D and 2D, XtraCharts | `Surface3D`: port meshgrid's CPU rasterizer and axes to Skia `DrawVertices` in an `ICustomDrawOperation`. Port the GLSL shader to SkSL later if needed. `Graph2D`: port txlogger's graph2d |
| XtraBars ribbon and docking, XtraTab | Menu, toolbar, `TabControl` (no docking library) |
| XtraWizard | A plain step-by-step view |
| XtraReports (TuningReport) | Drop it. Export HTML if anyone misses it |
| Office Excel COM, OleDb ACE | CSV export, plus copying as tab-separated text so it pastes into Excel |
| ICSharpCode.TextEditor (disassembler) | AvaloniaEdit (check that it supports Avalonia 12) |
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
- [ ] `.afr` maps, `.t7p` / `.t7x` tuning packs. The crypto is ported and tested against the shipped packs (T8Pub.pem ships from T7Core); reading and applying packs comes with chunk 7
- [x] `Encoding.Default` in T7SidEdit was ANSI on .NET Framework and is UTF-8 on .NET 10, now `Encoding.Latin1` (maps every byte 1:1; writing was ASCII anyway)
- [x] Source files: the old compiler read them in the ANSI code page, the SDK reads UTF-8. 24 old files are Windows-1252 and garble their non-ASCII literals ("Trollhättan") when lifted as they are. Convert with `iconv -f CP1252 -t UTF-8`; `SourceEncodingTest` fails on any new project file that isn't UTF-8
- [ ] Replace about 94 `"\\"` path joins with `Path.Combine`, and `Application.StartupPath` with `AppContext.BaseDirectory`. Done in T7Core; the rest are in frmMain and go with each extraction

## Chunks: T7Suite

### 0. Scaffold
- [x] `TuningSuites.slnx`, `Directory.Build.props` (no `.gitignore` changes needed, `[Bb]in/` and `[Oo]bj/` were already ignored)
- [x] ProjectReference to `$(TrionicDir)/TrionicCANLib`
- [x] Empty `T7App` window (MVVM: `MainWindowViewModel`, File > Exit, status bar), runs on Linux. Windows not tried yet
- [x] CI: `.github/workflows/build.yml` builds the solution and runs T7CoreTest on ubuntu and windows, with roffe/Trionic `net10` checked out side by side. Not run on GitHub yet
- [ ] Tag a version (e.g. `v0.2.0`). Until then every build warns about the missing tag and uses `0.0.0-<sha>`. The old T7Suite was 0.1.60.1

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
- [x] `Surface3D`: txlogger meshgrid's projection and drawing (Skia Gouraud triangles in painter's order, Lambert shading, axis scales with real axis values, orbit/roll/pan/zoom, live cursor), T7Suite's palette (green → yellow → orange → orange-red → red, online wheat → dark blue)
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
- [x] ECU menu: Connect / Disconnect ECU, Read ECU, Flash current file to ECU, Get SRAM snapshot (`SRAM<time>.RAM` next to the bin, `Snapshots/Snapshot<time>.RAM` in a project), Get fault codes (OBDII), Clear DTC and knock counters, Synchronize to binary / to ECU (with T7Suite's warnings), Import SRAM snapshot; status bar shows the CAN status and the SRAM file
- [x] Map viewer: Read from ECU / Save to ECU (every viewer of the map follows), maps that only live in SRAM are read from the ECU when opened (no file buttons), viewers opened while connected are online (T7Suite's colours), auto update of online viewers; symbol list: Read symbol from ECU, Read from SRAM file ("SRAM Symbol: name [file]")
- [x] Fault codes window (Code / Description from `DTC_*.xml` next to the program, Clear selected, Close)
- Deliberate differences: flash read / write report the library's actual result (T7Suite said "Download done" / "Flash sequence done" whatever happened); flashing first fixes (AutoChecksum) or offers to fix a checksum that doesn't verify, and warns about unsaved map changes (T7Suite flashed the file as it was, unchecked); a map write the ECU refuses (closed binary) is reported (T7Suite ignored the answer); closing the app is blocked while a read / flash / snapshot runs, and closing always releases the adapter; fault codes without a description are listed (T7Suite hid them); the DTC description loader no longer stops reading a file at its first incomplete entry
- [ ] Upload tuning package to ECU / Generate tuning package from ECU; Compare binary to SRAM snapshot / Compare SRAM snapshots; SaabOpenTech "Extra functions"
- [x] Tested on a bench ECU: connect, read flash (identical to the flashed file), flash, SRAM write on an open binary, refused write reported on a closed one. Snapshot and fault codes not yet

### 6. Realtime
- [ ] Realtime engine on a worker thread (the old one is a WinForms timer doing synchronous CAN on the UI thread)
- [ ] Dashboard: gauges and digital displays
- [ ] `.t7l` logging, DIF export, LogWorks
- [ ] Wideband through WidebandSupport
- [ ] Live cell tracking in open map viewers
- [ ] Autotune and AFR feedback maps
- [ ] Log viewer (replaces RealtimeGraph)
- [ ] From frmMain: realtime engine (10929-12607), status codes and realtime table persistence (9129-9553), AFR and autotune (15292-15777, 17833-18234). `.t7l` round-trip test (decimal separator!)

### 7. Tools
- [ ] TuneToStage and the tuning wizard
- [ ] Airmass result view (about 800 lines of logic to pull out of ctrlAirmassResult first)
- [ ] Compressor map
- [ ] Tuning packs: apply, create, search and replace
- [ ] SID information and editing
- [ ] Disassembler (AvaloniaEdit), hex view
- [ ] Matrix from log (mean/min/max)
- [ ] From frmMain: SID, limiter, torque/power/airmass math (3948-4239, 13637-13950), TuneToStage (10269-10779), tuning packs (14465-15290, 17340-17518, 18644-19139), matrix from log (16460-16747)

### 8. Release
- [ ] WiX MSI, Linux tar.gz, macOS zip, nightly and tagged releases
- [ ] `.bin` file association (per user, no elevation)
- [ ] Updater
- [ ] README with an OS × adapter support table

## After T7

- **T8Suite:** extract the shared parts of T7Core and the app into a shared Core and Controls layer, then port T8 on top. Expect much of the T7 UI to carry over.
- **T5Suite2.0:** last. It is the largest UI (Trionic5Controls alone is 63k LOC) and the oldest code. T5 support is already in the new TrionicCANLib.
- **Dead code to delete eventually:** T7CANFlasher/ (replaced by TrionicCANFlasher), the T7Libs/ wrapper DLLs, AquaGauge, LBIndustrialCtrls, ProCharts, MouseGestures.

## Open questions

- WidebandSupport has no licence file upstream. Ask Mattias, and add one when vendoring.
- Does AvaloniaEdit support Avalonia 12? If not: an older Avalonia, a fork, or a plain read-only text view for the disassembler.

## Log

- 2026-10-09: Feasibility analysis done; plan agreed. Branch `net10` created.
- 2026-10-09: Chunk 0 done locally: solution, versioning props, T7App shell on Avalonia 12.1.3 + CommunityToolkit.Mvvm 8.4.0, CI workflow.
- 2026-10-09: Chunk 5 implemented (ECU session, flashing, SRAM maps, snapshots, fault codes, sync); waiting for a bench test.
- 2026-10-09: Chunk 5 bench tested: flash read and write, SRAM writes; refused SRAM writes are now reported and closing waits for a running flash session.
- 2026-10-09: Chunk 4 done: plus compare / transfer maps, symbol imports and exports, settings window, My Maps and the quick map menu.
- 2026-10-09: Chunk 4 in progress: map saving with the viewer toolbar, verify checksum, user descriptions, firmware editing, projects and the transaction log; checksum updates now repeat until the file verifies.
- 2026-10-09: Chunk 3 done: T7Binary and FirmwareInfo in T7Core, the main window with symbol list, map viewer tabs (MapViewer composite with sync) and firmware information, T7AppTest driving the app headless.
- 2026-10-09: Chunk 2 done: MapControls (MapData, MapOps, MapGrid, Surface3D, Graph2D), MapControlsDemo, MapControlsTest with headless Skia renders. Behaviour follows T7Suite, txlogger only for rendering.
- 2026-10-09: Chunk 1 done: T7Core with the lifted logic and JSON settings, T7CoreTest with the golden baseline over 256 bins (28 tests, ~22 s). frmMain extraction moved into chunks 3-7.
