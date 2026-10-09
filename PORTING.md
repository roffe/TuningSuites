# Porting the TuningSuites to .NET 10

Goal: lift T7Suite, T8Suite and T5Suite from .NET Framework 4 / WinForms to .NET 10 / Avalonia so they run on Windows, Linux and macOS, and replace every non-free dependency (DevExpress, Nevron, Office Interop). T7Suite goes first.

This file is the tracker. Update the checkboxes and the log at the bottom as work lands.

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

- [ ] Settings and MRU imported from the registry on Windows
- [ ] Symbol XML (`DataTable` XML) next to the bin and in `repository/`; ship EU0AF01C.xml and EU09F01C.xml
- [ ] Projects: projectproperties.xml, TransActionLogV2.ttl (binary, auto-upgraded from v1), ProjectLogbook.log, backups
- [ ] rtsymbols.txt / .t7rtl, mymaps.xml. SymbolViewLayout.xml is a DevExpress layout and is dropped
- [ ] `.t7l` logs: write with the same format. Values use the system's decimal separator today, so read both `,` and `.`
- [ ] `.afr` maps, `.t7p` / `.t7x` tuning packs (AES with a hardcoded key, RSA check against T8Pub.pem)
- [ ] `Encoding.Default` in T7SidEdit is ANSI on .NET Framework and UTF-8 on .NET 10. Pin `Encoding.Latin1` or code page 1252
- [ ] Replace about 94 `"\\"` path joins with `Path.Combine`, and `Application.StartupPath` with `AppContext.BaseDirectory`

## Chunks: T7Suite

### 0. Scaffold
- [x] `TuningSuites.slnx`, `Directory.Build.props` (no `.gitignore` changes needed, `[Bb]in/` and `[Oo]bj/` were already ignored)
- [x] ProjectReference to `$(TrionicDir)/TrionicCANLib`
- [x] Empty `T7App` window (MVVM: `MainWindowViewModel`, File > Exit, status bar), runs on Linux. Windows not tried yet
- [x] CI: `.github/workflows/build.yml` builds the solution on ubuntu and windows, with roffe/Trionic `net10` checked out side by side. Tests get added with T7CoreTest. Not run on GitHub yet
- [ ] Tag a version (e.g. `v0.2.0`). Until then every build warns about the missing tag and uses `0.0.0-<sha>`. The old T7Suite was 0.1.60.1

### 1. T7Core (can run in parallel with chunk 2)
- [ ] Lift the pure-logic files.
  - From T7Suite: SymbolTranslator, PartNumberConverter, Disassembler, SymbolAxesTranslator, AFRMap, PartnumberCollection, SIDTranslator, TCMLimitEdit, SIDICollection, AFRMeasurement(Collection), IdaProIdcFile, SIDInformationTable, T7EspEdit, SIDIHelper, SymbolMapParser, PackageExporter, FuelMap(Information), Symbol, AirmassLimitType.
  - From CommonSuite: BitStream, VINDecoder, TrionicTransactionLog, TrionicSymbolDecompressor, CSVGenerator, SymbolCollection, TransactionCollection/Entry, CellHelper(Collection), MNemonic*, LogFilter(Collection), Srecord, XDFWriter, PressureToTorque, EngineStatus, SortableCollectionBase, DTCDescription, TrionicProjectLog, SymbolXMLFile, LogFile, ViewEnums, TurboType.
- [ ] Untangle the lightly coupled files:
  - Trionic7File: replace MessageBox with a prompt callback; drop `DoEvents`.
  - AppSettings, Channels, LogFilters, SymbolColors: move to JSON settings.
  - DifGenerator, SymbolHelper: keep using `Color` through a plain struct.
  - Crypto: replace the P/Invoke PEM import with `RSA.ImportFromPem`.
  - T7SidEdit: fix the encoding.
- [ ] Pull frmMain's logic out into services:
  - file open, save and import (504-890, 5126-6030)
  - projects and transaction log (1170-1823)
  - compare (1827-3261)
  - SID, limiter and airmass math (3948-4239, 13637-13950)
  - SRAM and axis metadata (6032-6658)
  - checksum and feature detection/patching (8082-9042)
  - status codes and realtime table persistence (9129-9553)
  - TuneToStage (10269-10779)
  - tuning packs (14465-15290, 17340-17518, 18644-19139)
  - AFR and autotune (15292-15777, 17833-18234)
- [ ] Golden tests: dump symbol list, addresses, lengths, axes and checksum result for all 256 bins with the old app's logic and compare. Capture the reference output by running the lifted code once on master logic, then freeze it
- [ ] Tests for transaction log round-trip, `.t7l` round-trip and S19

### 2. MapControls (can run in parallel with chunk 1)
- [ ] `MapData` codec: bytes ↔ raw ↔ physical; 8/16-bit big-endian; Hex/Decimal/Easy/ASCII; factor and offset; the >0xF000 signed rule; upside-down
- [ ] `MapGrid`:
  - axis headers, heat map (green→red and red-white variants), open-loop overlay, live cell highlight
  - range selection with Shift and Ctrl
  - keys: ±1, PgUp/PgDn ±10, Home = max, End = 0, type a value and Enter
- [ ] Edit operations: add, multiply, divide, fill, smooth (proper bilinear for blocks), select by value
- [ ] Multi-step undo (an improvement; the old viewer only reverts everything)
- [ ] Clipboard in the T7Suite format (paste at the original position or at the selected cell), plus copy as tab-separated text
- [ ] `Surface3D`:
  - meshgrid port: orbit, roll, pan, zoom, axes labelled with real values, live cursor
  - compare overlay (original vs compare surfaces)
- [ ] `Graph2D`: one slice with a slice selector, 1-D maps, drag points to edit
- [ ] Sync camera and selection between viewers that show the same map
- [ ] Tests: codec round-trip, edit operations, clipboard round-trip, projection and axis geometry (port meshgrid's)

### 3. Read-only app (first usable release)
- [ ] Main window: open a bin, symbol list (DataGrid with search and filter), maps open in tabs
- [ ] Map viewer view: MapGrid with Surface3D or Graph2D, view type, axis lock
- [ ] Firmware information view

### 4. Offline tuning (replaces the old T7Suite for offline work)
- [ ] Edit and save the bin; checksum verify and update
- [ ] Projects, transaction log, rollback and roll-forward, backups
- [ ] Compare against a file: list of differing symbols, difference map
- [ ] Symbol import from XML, CSV and AS2; mymaps
- [ ] Settings view
- [ ] Export: CSV, XDF, S19, IDC

### 5. ECU
- [ ] Adapter selection via `setCANDevice((CANBusAdapter)index)`. This also fixes the missing SLCAN branch
- [ ] Read and write flash with progress
- [ ] Read and write maps in SRAM, auto-poll, SRAM snapshot and compare
- [ ] DTC read and clear
- [ ] Tested on a bench ECU

### 6. Realtime
- [ ] Realtime engine on a worker thread (the old one is a WinForms timer doing synchronous CAN on the UI thread)
- [ ] Dashboard: gauges and digital displays
- [ ] `.t7l` logging, DIF export, LogWorks
- [ ] Wideband through WidebandSupport
- [ ] Live cell tracking in open map viewers
- [ ] Autotune and AFR feedback maps
- [ ] Log viewer (replaces RealtimeGraph)

### 7. Tools
- [ ] TuneToStage and the tuning wizard
- [ ] Airmass result view (about 800 lines of logic to pull out of ctrlAirmassResult first)
- [ ] Compressor map
- [ ] Tuning packs: apply, create, search and replace
- [ ] SID information and editing
- [ ] Disassembler (AvaloniaEdit), hex view
- [ ] Matrix from log (mean/min/max)

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
