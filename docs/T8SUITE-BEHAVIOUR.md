# T8Suite behaviour reference

How the old T8Suite (WinForms/DevExpress, `T8Suite/`) behaves, read from its code, as the reference for the port. It only lists what differs from T7Suite: read the matching section of [T7SUITE-BEHAVIOUR.md](T7SUITE-BEHAVIOUR.md) first. Line numbers are `T8Suite/Form1.cs` unless another file is named; "the library" is TrionicCANLib in the `Trionic` submodule. Where the port deliberately differs, PORTING.md says so.

T8 shares CommonSuite with T7 (AppSettings, SymbolHelper, SymbolXMLFile, Srecord, the project / transaction log classes and their forms), so those behave the same; the differences are in Form1 and the T8-only files. Non-UTF-8 sources: `Form1.Designer.cs` (UTF-16), `SymbolDictionary.cs`, `ctrlAirmassResult.cs`, `ctrlCompressorMap.cs`, `ctrlDisassembler.cs` (Windows-1252).

## Opening a binary

**Entry points.** There is no shared `OpenFile(path, showmessage)`: `OpenFile(f)` (1257) is used by "Open file" (1274) and Lookup partnumber; startup (2679), `LoadBinaryForProject` (9050) and Save as (5471) inline the same steps.
- "Open file" (1274): one filter `Trionic 8 binary files|*.bin;*.s19`. `.S19` → `Srecord.ConvertSrecToBin(f, 0x100000, out bin, true)` writes `<dir>\<name>.bin` next to it and opens that; no message when the conversion fails.
- `OpenFile` itself calls `CloseProject()` first (T7's ribbon handler did, `OpenFile` didn't).
- Command line (161): a `.BIN` path that exists is opened in `Form1_Load` (2686); `.S19` ignored. Same as T7Suite.
- Auto-load (2703): same as T7Suite (`AutoLoadLastFile`, `LastOpenedType`, `Lastfilename` / `Lastprojectname`).
- **No MRU list** anywhere in T8Suite (no `MRUList` key, no recent-files menu).
- No drag and drop, no dirty check. Same as T7Suite.

**`OpenFile` (1257)**, with `TryToOpenFile` (557), which compare, transfer and ConvertOriFiles also call:
```
CloseProject(); m_currentfile = f
TryToOpenFile(f):
    clear the SRAM file (m_currentsramfile, "SRAM:" caption) and the filename caption; TEM editor disabled
    if !Trionic8File.ValidateTrionic8File(f): m_currentfile = ""; return      (OpenFile carries on: title "[  ]", empty grid, Lastfilename = "")
    status "File is READ ONLY" / "File access OK"                            (same as T7)
    Trionic8File.TryToExtractPackedBinary(f, out symbols, out m_pids, out m_tems)
    symbolsLoaded = Trionic8File.TryToLoadAdditionalBinSymbols(f, symbols)  (symbol XML, below)
    Trionic8File.IsSoftwareOpen → status "Open/dev binary" / "Normal binary" (realtime AutoTune button shown / hidden)
    if MapDetectionActive && !symbolsLoaded: new SymbolFiller().CheckAndFillCollection(symbols)
    TEM editor enabled when the TEM table has > 1 entry
    SetDefaultFilters(); LoadRealtimeTable(rtsymbols.txt) when a file is set
title "T8SuitePro v<ver> [ <file> ]"; grid = symbols (unsorted, the grid sorts); Lastfilename = f
UpdateChecksum(f, AutoChecksum)          ← checksum checked on every open (T7 didn't)
DynamicTuningMenu(); LastOpenedType = 0
```
- Startup (2679): ribbon minimized unless ShowMenu, `SymbolViewLayout.xml` restored, skins, then the command-line file or auto-load through the same steps inline (they set Lastfilename but leave LastOpenedType / Lastprojectname alone), the Debug group when DebugMode, My Maps. Tuning packs are loaded in the constructor (wizard).
- **Checksum on open:** `ChecksumT8.VerifyChecksum(f, AutoChecksum, ShouldUpdateChecksum)`. With AutoChecksum (default true) a wrong checksum is silently rewritten in the file at open; without it each failing layer asks through frmChecksum. Status bar: "Checksum: OK" / "Checksum: Layer 1 invalid" / "Checksum: Layer 2 invalid".
- Progress texts: "Opening <file>", "Symbol: Symbolnumber N", "Adding symbol names: ", "Importing symbols", "Loading data into view... ", "Idle".

**Validation** (`Trionic8File.cs` 255): exists, length exactly 0x100000, bytes 0..3 = `00 (10|00) 0C 00`. Messages: "File has incorrect length: <name>", "File does not seem to be a Trionic 8 file: <name>". There is no footer / header repair at open (no AutoFixFooter in T8).

**Symbol table** (`Trionic8File.TryToExtractPackedBinary`, `Trionic8File.cs` 1553):
```
end  = first "sYMBOLtABLE" (73 59 4D 42 4F 4C 74 41 42 4C 45) from 0
nq   = the offset just after the first "NqNqNq" (4E 71 ×3) within 0x100 bytes of it (fewer: after the last Nq pair seen)
name table address = u32 BE at nq (used when 0 < a < 0xF0000), length = u16 BE at nq+4 (< 0x1000 → "No symboltable found!" in the log, no names)
address offset = u32 BE at nq − (2·(Nq pairs just before nq, max 3) + 6)       (SRAM → flash offset of closed bins)
names:
  table starts F1 1A 06 5B A2 6B CC 6F → Blowfish (key SymbolnamesDictionary.GetHeader()) on the next 16 bytes gives an id;
      the id's key (SymbolnamesDictionary) decrypts the table from +24, the result is a password-protected zip whose single entry is the names, CRLF separated;
      unknown id → no names
  else u32 LE unpacked length ≤ 0xFFFFFF → TrionicSymbolDecompressor.ExpandComprStream (T7's compressed format)
sanity check: some 00×8 20 after the name table, else "Could not find address table offset!" and stop (the offset found is not used)
address table: 10-byte records from nq + 11: addr u24 BE, length u16 BE, bitmask u16 BE, type, extended type, 00; ends at a record whose byte 9 ≠ 0
  record i → Varname "Symbolnumber <i>", Symbol_number = Symbol_number_ECU = i + 1, Internal_address, Length, BitMask, Symbol_type
names[i+1] → record i (names[0] is "SymbolNames", the name table itself has no record); Description = SymbolTranslator.ToDescription(name);
  Category = prefix before '.' ("Undocumented" without one)
then open/closed, address translation, PID / TEM tables (below)
```
- The off-by-one between the placeholder name (index) and `Symbol_number` (index + 1) is T8's: T7 used the same number for both. It breaks `SymbolXMLFile.SaveAdditionalSymbols`' swap-back test (`Userdescription == "Symbolnumber <Symbol_number>"`): once a name has been swapped into Varname (an XML import, or reopening a sidecar written after a CSV / AS2 import or map detection), the next save writes the real name as SYMBOLNAME and the following open can't match it, so the name is lost. The port has to decide whether to keep that (T7Core's SymbolFiles / SymbolXMLFile build "Symbolnumber {Symbol_number}").

**Open / closed software** (`DetermineBinaryOpenness`, `Trionic8File.cs` 1011): a score, open when ≥ 2:
- +1 a symbol at ≥ 0x100000 with length 0x101..0x400 named BFuelCal.LambdaOneFacMap, KnkFuelCal.fi_MaxOffsetMap or AirCtrlCal.RegMap;
- +1 any symbol at ≥ 0x108000 (0x100000 + 32 KB);
- +1 / −1 the pattern `20 3C 00 14 00 00` (mask `F1 BF FF FF FF 00`, `move.l #$0014xxxx`) found / not found from 0x20000.
- Open bins: a secondary offset from the init code (`DetermineSecondaryOffset`, 1206): u32 at 0x20004 → `jsr` → `jsr` → data copy routine, whose `move.l #sram,#flash` pair gives `sram − flash` (sram within 0x100000..0x107FFF).

**Address mapping** (`TranslateAddressOffsets`, `Trionic8File.cs` 1057), replaces T7's SRAM offset:
```
Flash_start_address = Internal_address                      (for every symbol)
if Internal_address ≥ 0x100000:
    Start_address = Internal_address                         (SRAM address)
    if type ≠ 0xFF and (type & 0x22) == 0x02:                 (calibration, not NVDM / adaption)
        closed: flash = addr − addressOffset            if addr+len ≤ 0x108000 and addr ≥ addressOffset
        open:   flash = addr − secondaryOffset          if addr+len ≤ 0x108000 and addr ≥ secondaryOffset   (internal SRAM)
                flash = addr − addressOffset            if addr ≥ 0x108000 and addr ≥ addressOffset          (external SRAM)
        Flash_start_address = flash when 0 < flash and flash+len ≤ 0x100000
```
SRAM-only symbols keep `Flash_start_address ≥ 0x100000`, which is how the rest of Form1 tells them apart (double-click: `≥ 0x100000` → connect and read from the ECU).

**PID / TEM tables** (`LoadPidTables`, `Trionic8File.cs` 1546) are located at open for the PID and TEM editors (see T8-only tools).

**Symbol XML lookup** (`TryToLoadAdditionalBinSymbols`, `Trionic8File.cs` 1878). Replaces T7's three-step lookup:
1. every `<StartupPath>\*.xml` whose file name (no extension) is a case-insensitive **prefix of the header's software version** (`T8Header.SoftwareVersion`, e.g. `FC0J_C_FMEP_63_FIE_82s`); the first one found is used, whether it imports anything or not;
2. else `<dir>\<name>.xml`.
- No `repository\` folder, no `<name>*.xml` fallback, no offer of shipped lists, and the sidecar is **not** rewritten at open (only on user-description edits and imports).
- Import (`ImportSymbols`, 1814): same matching and swap as T7 (SYMBOLNAME == Varname and FLASHADDRESS == Flash_start_address; a "Symbolnumber " Varname becomes the DESCRIPTION and the old name goes to Userdescription).
- A loaded XML (step 1 or 2) suppresses the SymbolFiller.

**Effective name.** Every lookup by name (viewers, axes, factors, Tuning buttons, compare, transfer, packages) uses `SmartVarname` (CommonSuite SymbolHelper 124): the User description when set and not starting "Symbolnumber ", else Varname. Same class as T7, but T8 leans on it for nameless bins.

**Symbol name recovery** (bins whose name table is missing, encrypted with an unknown id, or unreadable). T7Suite has nothing like it. Order at open: names from the bin, then the user XML, then map detection, also for compare files and transfer targets (they go through `TryToOpenFile`).
- Without names a symbol has Varname "Symbolnumber <i>", Description "", Category "Undocumented".
- **Map detection** (`SymbolFiller.CheckAndFillCollection`, `SymbolFiller.cs` 14, 748 lines): only with Settings "Auto mapdetection active" (`MapDetectionActive`, default off; tooltip "When checked, T8Suite will try to add names to symbols in binaries without symboltables. This is a guesstimate routine.") and no user XML loaded.
```
sort the collection by Symbol_number
if any Varname starts "Symbolnumber":
    pick the order of the nine 512-byte maps from the pattern of consecutive Symbol_numbers among them (SequenceOf512Maps / 576Maps)
    SetMapName(length, k, name): the k-th symbol of that length in flash (< 0x100000) → Userdescription, if empty; returns its number
    SetMapNameByIndex(number, length, name): neighbours of an anchor (up to ±18), exact length required
    torque limiters: the first run of lengths 16,2,32,32,32,32,2,2,2,32,32 (else 16,2,32,32,2,2,2,32,32) in number order → Trq_ManGear,
        Trq_MaxEngineManTab1 / AutTab1 / ManTab2 / AutTab2, n_EngYSP, Trq_OverBoostTab, CompressorNoise axes and map
    288-byte maps: four of them → BioPower set (LambdaOneFacMap, E85TempEnrichFacMap, TempEnrichFacMap, MAFCal.NormAdjustFacMap), else three
    every symbol: Description = ToDescription(Userdescription), Category from it
```
  - 61 anchors and 125 neighbours, 142 distinct names (seven have no SymbolDictionary entry: no description, unit or axes). Four 512-byte layouts ("new file from JZW", 2008, 2007, default), the JZW one also changes the 288-byte order, the KnkFuelCal.EnrichmentMap pick and the boost PID maps.
  - Guesses only go into an empty User description; Varname stays "Symbolnumber N". Nothing is saved by it; the next `<bin>.xml` save stores them and the next open swaps them into Varname (and from then on map detection no longer runs, an XML loaded).
  - Quirks: any exception aborts the run (logged, 619); `SequenceOf512Maps` throws with more than nine 512-byte symbols or none; names aren't checked for uniqueness; it never looks at Varname, so in a partly named bin a guess can override a real name through SmartVarname, and every symbol without a User description loses its Description.
- **SymbolDictionary** (`SymbolDictionary.cs`, 8670 lines, Windows-1252 without BOM: read it as 1252, not Latin-1): `Dictionary<string, MySymbol>` with 8500 entries from `Utils/as2parse.py` over the FF0L, FC01 and FXB4 AS2 files (source, type SCALAR / MAP / TABLE / TABLENOSP, unit value = correction factor, unit of measure, description, x / y axis with "Function of: …", duplicateName / duplicateExist). 1-D TABLEs keep their support points in the y axis; 29 static axes "N : v1 … vN"; a symbol whose axis differs between sources keeps the later one as `<name>.<source>` (never looked up) and both get duplicateName "X.<other axis>" / "Y.<other axis>". API: `GetSymbolUnit` (1.0 unknown), `GetSymbolUnitOfMeasure` / `GetSymbolDescription` / `GetSymbolXAxis` / `GetSymbolYAxis` ("" unknown), `doesDuplicateExist`. Replaces T7's SymbolTranslator (34.5k lines, NL / EN help texts) and SymbolAxesTranslator (372 hand-written cases): T8's `SymbolTranslator.ToDescription` (15 lines) and `SymbolAxesTranslator` (49 lines) only forward to it, English only.
- **SymbolnamesDictionary** (`SymbolnamesDictionary.cs`, 58 lines): keys, not names: a 16-byte header key and 32 ids (`TAG1`…`TAG16`, `TAG_Fx_17`…`TAG_Fx_32`) → 16-byte Blowfish keys. `blowfish.cs` (857 lines) runs `NonStandard`: each 32-bit half little-endian (480). The zip is ZipCrypto with a password (`Trionic8File.cs` 729, code page 850), which System.IO.Compression can't open.

**Create binary from TIS file** (File page, `btnCreateFromTISFile_ItemClick`, 13914). T8 only.
```
pick a base bin ("Select a binary file to base the new file on", starts in <StartupPath>\Binaries); must pass ValidateTrionic8File
new = 0x100000 × FF; new[0..0x1FFFF] = base[0..0x1FFFF]            (recovery bootloader and adaption data)
pick the TIS file ("Choose a TIS T8 file to build the new file with", *.gbf;*.s19)
  .s19 → Srecord.ConvertSrecToBin(f, 0x100000, out bin, pad=false) (writes <dir>\<name>.bin)
  try gunzip into "<name without extension>" in the current directory, use it; not gzip → delete it, use the file as is
  new[0x20000 + i] = gbf[i] XOR "9hwmG9"[i % 6]                 (key 39 68 77 6D 47 39)
  then, encoded with (b XOR 0x21) − 0xD6: length 10, id 0x10, "T8SuitePro"   (programming station footer field)
                                          01, F9, 01                  (adaption region flag)
save dialog ("Choose a filename for the new binary file") → File.WriteAllBytes, "New file created"
```
- Cancelling the TIS dialog still offers to save (base bootloader + FF). The new file isn't opened and its checksum isn't touched.

## Symbol list

**Columns** (designer `Form1.Designer.cs`; a saved `SymbolViewLayout.xml` in the settings folder overrides them, as in T7):

| Caption | Field | Notes |
|---|---|---|
| Symbol name | `Varname` | visible |
| Length | `Length` | visible; X6 with ShowAddressesInHex |
| User description | `Userdescription` | visible, the only editable column; saves `<bin>.xml` |
| Number | `Symbol_number` | visible (index + 1) |
| Type | `Symbol_type` | visible (address-table type byte) |
| Address | `Flash_start_address` | hidden; X6 with ShowAddressesInHex |
| SRAM Address | `Start_address` | hidden; X6 |
| Bit mask | `BitMask` | hidden; X6 |
| Description | `Description` | hidden, shown as the preview line |
| Category | `Category` | grouped |

- Grouped by Category with a count; sorted Category ascending, then **Length descending, then name ascending** (T7 kept source order inside a group). The find panel is always visible.
- **No name colours** (T7's prefix colours don't exist in T8).
- **Default filter is on** (`SetDefaultFilters`, 724), re-applied after every open and import: "Only symbols within binary" = `[Flash_start_address] LIKE '0_____' AND [Length] <> '000000' AND [Flash_start_address] < 100000` (hex mode), with a second predefined filter "Only live-tuneable symbols" = `[Length] <> '000000' AND [Start_address] >= 100000`. Decimal mode uses `< 1048576` / `> 1048575` but keeps `LIKE '0_____'`, now applied to the raw number, which likely hides every row (not verified). Typing in the find panel doesn't drop the filter (`onFindFilterChange` / `onColFilterChange`).
- Hovering a name can pop up a map preview (map helper, see T8-only tools).
- Drag a row: the symbol (with its current bytes) is dragged, for the tuning package editor.

**Double-click / Enter** (1297): group rows ignored; `Flash_start_address ≥ 0x100000` → connect and `ShowRealtimeMapFromECU`, else `StartTableViewer(Auto)`. No open-software branch. `HideSymbolTable` hides the list afterwards. Ctrl+C copies the focused cell's text. Same keys as T7.

**Context menu** (designer order): "Add to realtime list", "Add to MyMaps", "Read from SRAM file" (enabled when an SRAM file was imported), "Browse axis info", "Export as tuning package" (enabled with a selection), "Export fixed tuning package", **"Edit an existing tuning package"**, "Export symbollist as CSV". No "Read symbol from ECU / binary".
- "Add to realtime list" presets: ActualIn.v_Vehicle2 and In.v_Vehicle 0..255 ×0.1, FFTrqProt.Trq_MaxEngineBefComp and FFTrqProt.Trq_MaxEngine 0..65535 ×0.1, else length 1 → 0..255, other 0..65535.

**Shortcuts:** Ctrl+F fullscreen, Shift+F1 realtime panel, F6 log marker. No F3 and no F9 (screenshot) in T8.

**My Maps:** same as T7Suite (editor, format, "My Maps" page at ribbon index 3 with "myMaps settings" → "Define myMaps", "Add to MyMaps" into category "Directly added"), with its own file `%APPDATA%\MattiasC\T8SuitePro\mymaps.xml` (T7: `…\MattiasC\T7\mymaps.xml`). The T7 "targetafr" / "feedbackafr" tags are commented out: every tag is a symbol name. A missing file is created by "Define myMaps" with T8 defaults: Fuel `BFuelCal.LambdaOneFacMap` "Main fuel map"; Boost `AirCtrlCal.RegMap` "Boost bias map", `AirCtrlCal.Ppart_BoostMap` "P factors map" (T7: BFuelCal.Map, BoostCal.RegMap, BoostCal.PMap).

**Not in T8:** the symbol grid's "Export to excel" / PDF.

## Map viewers

**Hosting** (`StartTableViewer(ECUMode)`, 2007). Same as T7Suite (panel title `Symbol: <name> [<file>]`, Tag = file, reuse of an existing panel, NewPanelsFloating, AutoDockSameSymbol / AutoDockSameFile, viewers of a previous file stay open, sizes per DefaultViewSize) except:
- A symbol with `BitMask > 0` opens the bitmask viewer instead (2026).
- Returns when both addresses are 0. The panel name uses `SmartVarname`.
- `Map_descr` = "" and `Map_cat` = Undocumented (T7 filled the help text).

**Viewer setup:**

| Property | T8 source |
|---|---|
| Axis symbols | `SymbolAxesTranslator.GetAxisSymbols` = SymbolDictionary's xAxis / yAxis for the map name. It returns false when there is no y axis, and then **no x axis is read either**. A duplicate entry (`doesDuplicateExist`, e.g. BstKnkCal.MaxAirmass → `X.BstKnkCal.OffsetXSP`) replaces the x axis when the listed one isn't in the file |
| Static axes | an axis string starting with a digit, `"N : v0 v1 …"`, gives the values directly (e.g. TrqLimCal.Trq_ManGear `8 : 0 1 2 3 4 5 6 7`). Replaces T7's hard-coded gear / BoostMeter axes |
| X/Y axis values | the axis symbol read from the file, 16-bit BE (8-bit: the y axis of FuelDynCal.FuelModFacTab, the x axis of EvapDiagCal.LeakFacTest1MAT / LeakFacTest2MAT), **raw: no axis correction factor** (T7 multiplied by it) |
| Y sign | > 0x8000 negative, for every map |
| X sign | never negative |
| Axis captions | units, not descriptions: X = unit of the x-axis symbol, Y = unit of the y-axis symbol (only set when there is an x axis), Z = unit of the map (SymbolDictionary `unitOfMeasure`) |
| Width | `GetTableMatrixWitdhByName` (1419): MisfCal.m_LoadLevelMAT 5, PedalMapCal.GainFactorMap 16, FrictionLoadCal.Trq_RequestT_EngMAP 9, CatDiagCal.t_Ph3MaxMAT 7, else by length (576→18, 672→16, 512→16, 504→14, 480→6, 416→16, 384→12, 336→16 or 12 for PurgeCal., 320→10, 306→9, 288→18, 256→8, 224→8, 220→10, 208→13, 204→6, 200→10, 192→8, 168→7, 140→7, 130→1, 128→8, 112→14, 100→10, 98→7, 80→8, 60→5, 50→5, 96→6, 64→4, 160→10, 72→9, 42→7), names ending YSP / XSP / TAB → 1; then overridden by the x-axis length when it has > 1 value |
| 16-bit | `isSixteenBitTable` (1598): true except KnkDetCal.RefFactorMap, BFuelCal.Map, BFuelCal.StartMap, TorqueCal.M_IgnInflTorqM, TCompCal.EnrFacMap, TCompCal.EnrFacAutMap, AftSt2ExtraCal.EnrFacMap, AftSt1ExtraCal.EnrFacMap, StartCal.HighAltFacMap, BFuelCal.TempEnrichFacMap, BFuelCal.E85TempEnrichFacMap, BFuelCal.LambdaOneFacMap, MAFCal.NormAdjustFacMap, FuelDynCal.m_FbetaMap*, FuelDynCal.m_FalphaMap*, CatModCal.TSoakFacMAP, StartCal.ScaleFacRpmMap, ECUIDCal.ApplicationFileName, FFFuelCal.TempEnrichFacMAP, any odd length, length 336 outside PurgeCal. |
| Factor | `GetMapCorrectionFactor` (1520) = `SymbolDictionary.GetSymbolUnit(name)` (unitVal, default 1.0). The "Resolution is" parsing and the hard-coded list above it are computed and then overwritten (dead) |
| Offset | 0 |
| UpsideDown | always |
| Open-loop limits | **none**: T8 never sets `Open_loop` / `StandardFill`, so no closed-loop marks |

**Data address:** `Flash_start_address < 0x100000` → read from the file; otherwise the cells start as zeros (a RAM-only symbol; "Read from ECU" fills it). There is no open-software SRAM mapping at viewer level: the mapping happened at open (`TranslateAddressOffsets`).
- Events: `onSymbolSave` / `onSymbolRead` only when 0 < address < 0x100000; `onWriteToSRAM` / `onReadFromSRAM` only when the SRAM address ≥ 0x100000 (subscribed before `ShowTable`, which matters, see MapViewerEx). Auto mode while connected: `OnlineMode` (new viewer only) and `IsRAMViewer`.
- `StartTableViewer(name)` (2851): clears the symbol list filter and the find text, selects the row and opens it; "Symbol <name> does not exist in this file" when it has neither a flash address > 0 nor an SRAM address ≥ 0x100000. Used by the Tuning page, My Maps, search results and the axis editor.
- Read from file (`tabdet_onSymbolRead`, 2435) reloads axes, data, factor; unlike T7 it leaves `IsRAMViewer` / `OnlineMode` alone.

### MapViewerEx (T8 vs T7)

`T8Suite/MapViewerEx.cs` is T7's with about 110 changed lines (T8 line numbers). Cells, decoding, signed values (0xF001–0xFFFF negative), editing, parsing, range checks, colours, keys, math toolbar, smooth, select by value, clipboard, 2D / 3D graph, sync events, designer, MapViewerFactory and SurfaceGraphViewer: **same as T7Suite**. Differences:
- **File / ECU buttons follow the host's events** (`ShowTable`, 850-853): "Save to file" ← `onSymbolSave`, "Read from file" ← `onSymbolRead`, "Save to ECU" ← `onWriteToSRAM`, "Read from ECU" ← `onReadFromSRAM`; T7 never touched them. A cell edit re-enables "Save to file" only when `onSymbolSave` is set (1823, 2123). So: a calibration map gets all four, a RAM-only symbol only the ECU pair (cells start as zeros), a flash-only symbol only the file pair. The compare viewer has none. The "Symbol difference" viewer subscribes `onSymbolSave` (3178): its "Save to file" (or "Save all") writes the |a−b| values into the compared file and updates its checksum (in T7 it stayed off).
- **Close** (1595): "Data was mutated, do you want to save these changes in you binary?" only when "Save to file" is enabled; a RAM-only or compare viewer closes without asking and drops its edits.
- **AutoUpdateIfSRAM** (1326): polls when `Map_address ≥ 0x100000` (T7 ≥ 0xF00000), i.e. RAM-only symbols.
- **OnlineMode setter** (609-626): false redraws and re-runs `ShowTable`, true only logs; the host never sets false.
- **Axis items in the table menu** (3910-3968): axis names from SymbolDictionary. A static axis (digit first, "8 : 0 1 2 …") gives a disabled "Static x-axis (<axis>)" / "Static y-axis (<axis>)"; otherwise an enabled "Edit x-axis (<axis>)" / "Edit y-axis (<axis>)", with the duplicate's alternative axis when the listed one isn't in the file (the y label tests the x axis, a bug at 3955). No axis: disabled "Edit x-axis" / "Edit y-axis" (same).
- **Axis editor event** (3976-4015): `onAxisEditorRequested` now carries `ReadSymbolEventArgs(axis symbol, file)`; the host (2430) calls `StartTableViewer(name)`, which opens the axis as a normal viewer (or "Symbol <name> does not exist in this file"). T7 never subscribed it, so the items did nothing there.
- `IMapViewer`: only the `AxisEditorRequested` delegate signature differs (339).
- **Legacy MapViewer** (UseNewMapViewer off): still selectable; same button / 0x100000 / axis changes; the `SliderPosition` setter swallows exceptions, the 2D legend is hidden, `ClearGrid()` added for the map helper.
- Host members only T7 set: `IsOpenSoftware`, `onSurfaceGraphViewChanged` (only the legacy viewer raises it, so it loses 3D sync in T8), `Open_loop` / `StandardFill`, `Afr_counter`, `Viewtype`.

## Saving, checksum, read-only

- `savedatatobinary` (1830 / 1889): same as T7Suite with the range 0 < address < 0x100000 (T7 0x80000); same "Failed to write to binary. Is it read-only? Details: …" box.
- Map save (`tabdet_onSymbolSave`, 1867): "Remark for change" as in T7, write, then `UpdateChecksum(file, true)`: the checksum is always corrected, without asking, whatever AutoChecksum says (T7: `ChecksumT7.UpdateChecksum` every time too).
- Read-only: same as T7Suite ("File is READ ONLY" / "File access OK" in the status bar, writes fail with the box above).

**Checksum** (TrionicCANLib `ChecksumT8.VerifyChecksum(file, autocorrect, ShouldUpdateChecksum)`, through `UpdateChecksum` 3557; ChecksumT8 is already in the net10 TrionicCANLib):
- CHPTR = u32 BE at 0x20140; > 0x100000 → InvalidFileLength.
- Layer 1: MD5 of [0x20000, CHPTR), each byte stored as `(h ^ 0x21) − 0xD6`, compared with the 16 bytes at CHPTR + 2 (PI container 0x0D).
- Layer 2: in the first 0x100 decoded PI bytes, containers FB (sum), FC (end), FD (start), each 6 bytes after the previous; sum = bytes of [start, end − 4) + byte[end − 1], or the u32 BE word sum when the top 12 bits differ; written back encoded. FB/FC/FD missing → Layer2Failed without a prompt. Refusing layer 1 stops there.
- `autocorrect` true fixes silently; false asks per failing layer with frmChecksum "Trionic checksum" (no close box): group "Checksum validation Layer 1" / "Checksum validation Layer 2", read-only "File checksum" / "Actual checksum", buttons "Update and close" / "Ignore". T7 asked once: "Checksums did not verify ok, do you want to recalculate and update the checksums?".
- Result only in the status bar: "Checksum: unknown" at start, then "Checksum: OK" / "Checksum: Layer 1 invalid" / "Checksum: Layer 2 invalid" (InvalidFileLength / UpdateFailed leave it). It also changes when the file checked is another one (transfer target 5600, copy address table target 13339). No message box (T7: "Checksums verified and all matched!" / "Checksums did not verify ok!").
- "Verify checksum" (Actions → Checksum, 3544): `UpdateChecksum(file, AutoChecksum)`, so with AutoChecksum on (default) it fixes silently.
- With AutoChecksum: every open (1268, 2698, 2719, 9075, 5495), firmware OK (3635), bitmask viewer (2544), Excel import (4967), transfer target (5600), PID (15478), TEM (15543).
- Always silent (`true`): map save (1884), tuning packages (8360), roll back / forward / rebuild (9349, 9401, 9512), copy address table target (13339), the tune-me-up code (6284-7069).

## Firmware information

**Entry point** (Actions → Information → "Firmware information", 3574): same file checks as T7. A new `T8Header.init(file)` (`T8Header.cs` 602) only reads: no footer, no AutoFixFooter. Dialog "Trionic 8 firmware information", one group "Firmware details": **no options, no read-only flags, no Import / Undo, no programming or SID date, no VIN decoder button** (T7's whole option list doesn't exist).

**Fields** (top to bottom; "memory only" = set on the header object, never written):

| Label | Source | On OK |
|---|---|---|
| Engine type by sw version | `DetermineCarInfoBySWVersion(software version)` (3641) | `CarDescription`, memory only |
| Engine type by VIN | `VINDecoder.DecodeVINNumber(Chassis ID)` → "<EngineType> MY<Makeyear> <GearboxDescription>" ("Unknown MY0 " for a blank VIN) | not read back |
| Software version | PI 0x08, trimmed | written only in software-version mode |
| Partnumber | MFS info record bytes 0–9, trimmed; PI 0xC1 only when there is no info record | memory only |
| Serial number (disabled) | info record bytes 10–25 = the immobilizer code | written in VIN/immo mode |
| Chassis ID (disabled) + "Clear" (disabled) | VIN of the last valid flash block | written in VIN/immo mode |
| Programming device | PI 0x10 | DEBUG builds only |
| Programmer name | PI 0x1D | DEBUG builds only |
| Release date | PI 0x0A ("yyyy-MM-dd HH:mm:ss") | DEBUG builds only |
| Hardware ID | PI 0x92 | DEBUG builds only |
| Hardware type | PI 0x97 | DEBUG builds only |
| ECU description | last valid flash block | memory only |
| Interface device | last valid flash block | memory only |
| #Flash blocks | count of valid flash blocks; its ellipsis button opens the flash block browser | not read back |

- Text fields keep their original length (same as T7); an empty value removes the limit.
- A checkbox "Enable overboost (TrqLimCal.EnableOverBoost)" that nothing reads or sets.

**DetermineCarInfoBySWVersion** (3641): the software version split on `_`; the last part `NNx` gives "<engine> MY<year>" (unknown x → engine only; non-numeric NN → ""; case-sensitive):

| NN | Engine | x → year |
|---|---|---|
| 80 | B207E | b 2003; c d e 2004/2005; f h 2007/2008; g "2007/2008 Biopower" |
| 81 | B207L | b 2003; c d e 2004/2005; f h 2005; i 2006; j 2007; l "2008 Biopower"; m 2009; t → whole text "B207M/F MY2011" |
| 82 | B207R | b 2003; c d e 2004/2005; f h 2005; n 2006; s 2007; r 2007/2008; x v 2009; 6 8 z 2010 |
| 83 | Z20NET | e 2003; f 2004; g 2005; h 2006; i 2008 |
| 85 | B207R | d f 2011; b → whole text "B207S MY2011" |

then a 4-char first part appends FA " MY03-06 Gasoline / front wheel drive", FC " MY07-11 Gasoline / front wheel drive", FD " MY07-10 BioPower / front wheel drive", FE " MY09-11 Gasoline / all wheel drive", FF " MY10 BioPower AWD or MY11 Gasoline/BioPower FWD/AWD".

**Edit modes** (label double-clicks, `frmFirmwareInformation.cs` 91, 267, 280):
- "Software version" (first time): removes the length limit, software-version mode, frmInfoBox "Warning: T8Suite only has experimental support for replacing software version string".
- "Chassis ID": enables Serial number, Chassis ID and "Clear", VIN/immo mode. "Serial number" enables the same three but doesn't set the mode, so those edits are dropped on OK. The first enable shows "Warning: T8Suite only has experimental support for changing VIN and immobilizer codes!!!".
- **"Clear"** (292) writes 17 spaces as the VIN into every valid flash block of the file at once, re-reads and shows the blank VIN; Cancel doesn't undo it.

**OK, in this order** (3608):
1. All fields copied into the header object.
2. VIN/immo mode: `UpdateVinAndImmoCode` (`T8Header.cs` 682): `UpdateVin` (nothing if > 17 chars; else every valid block decoded at open gets bytes 0xE0..0xF0, re-encoded, 0x130 bytes written back) and `UpdateSerialNumber` (16 chars raw at +10 of every 0xFF info record, both halves).
3. Software-version mode: `UpdateSoftwareVersion` (1017): `StoreStringContainer(0x08, text, 30)`: an existing container is overwritten in place (truncated / space padded to its length); a missing one is created at the F7 F7 terminator only if ≥ 32 F7 bytes follow, otherwise nothing happens, silently. PI area re-encoded and written.
4. DEBUG builds only: `UpdatePIarea` (784), broken (misparses after the first non-text container).
5. `UpdateChecksum(file, AutoChecksum)`.
- No transaction or logbook entry, no reload. In release builds every other edit is thrown away.

**T8Header** (`T8Header.cs`, 1042 lines; TrionicCANLib has no T8 header class). T7's header is a field footer at the end of the bin (`T7FileHeader`); T8 has two areas:
- *PI area* (`DecodeInfo`, 187): from CHPTR (u32 BE at 0x20140) to the first FF FF FF within 0x1000 bytes; bytes decoded `(b + 0xD6) ^ 0x21` (encode `(b ^ 0x21) − 0xD6`); containers `[len][type][data]` until F7 F7 (raw 00 00) or the end. Text types 0x92 0x97 0x0C 0xC1 0x08 0x1D 0x10 0x0A 0x0F 0x16, others hex. Used: 0x0D layer 1 checksum, 0x92 hardware ID ("GMPT 0100"), 0x97 hardware type ("ECM"), 0xC1 a part number (not the bin's), 0x08 software version (30 chars), 0xFB / 0xFC / 0xFD layer 2 sum / end / start, 0x0A release date, 0x1D programmer name, 0x10 programming device ("EOLStation2").
- *MFS area* (`DecodeExtraInfo`, 292): 0x4000–0x7FFF, two halves at 0x4000 and 0x6000, each "MFS*", a u32 write counter, then up to 16 12-byte entries `44 2A len16 00 00 addr16 … type`. Type 0xFF = info record (raw: bytes 0–9 part number, 10–25 immobilizer code), 0x03 / 0x01 = active / history flash block of 0x130 bytes.
- *Flash blocks* (`FlashBlock.cs`): decode `(b + 0x53) ^ 0xA4`, encode `(b ^ 0xA4) − 0x53`; invalid when the first 8 decoded bytes are F6 (erased). Decoded offsets: 0x40 secret code (4), 0xAF ECU description (16, "Trionic 8 P6.8"), 0xE0 VIN (17), 0x101 interface device (11, FF dropped, "PPCAN 210"). Valid blocks numbered in table order, low half first; the header shows the **last** valid block's VIN / ECU description / interface (usually the last history record).
- Quirks: the high half's info record is read at address − 0x2000 (the low copy) but written at the right address; bins can hold different VINs per block and only the last is shown; a serial number shorter than 16 chars throws in `UpdateSerialNumber` after the VIN was already written (the stored code is trimmed on read); `init` writes dumps and a log to `C:\T8Decode` when that folder exists. `CarDescription` is never read from the file, so a new project's car model is always empty.

**VIN decoder:** same dialog as T7 (frmDecodeVIN). Only from Actions → Information → "VIN decoder" (7683), with the VIN of `T8Header.init` (the last valid block's; 17 spaces shows "Not verified").

**Flash block browser** (`frmFlashBlockBrowser.cs`, "Flashblocks browser"; 29 + 91 lines, plus FlashBlock 242 + FlashBlockCollection 284): only from the "#Flash blocks" button. Re-reads the file (a Clear shows, unsaved edits don't). Read-only grid: Blocknumber, Blocktype (1 history / 3 active), Address (X8), VIN, ECU type, Interface, SecretCode; "Ok" closes. Nothing editable.

## Projects, transaction log, logbook

Same as T7Suite: the same CommonSuite classes and forms, the same settings (`ProjectFolder`, `RequestProjectNotes`, `LastOpenedType`, `Lastprojectname`, `AutoLoadLastFile`), the same disk layout and file names (`projectproperties.xml`, `TransActionLogV2.ttl`, `ProjectLogbook.log`, `Backups\<bin>-backup-MMddyyyyHHmmss.BIN`, `<P>\<N>rebuild.bin`), the same purge, logbook and transaction-log behaviour, the same ribbon group "Projects". Form1 8952-9618 differs from frmMain 1170-1830 only in:
- Titles "T8SuitePro [Project: name]" / "T8SuitePro".
- Create (9132) prefills from `T8Header`: car model = CarDescription (always empty: never read from the file), project name = "<PartNumber> <SoftwareVersion>" (PartNumber from the MFS info record).
- `LoadBinaryForProject` (9050) opens inline (no `CloseProject`), with the checksum check of an open (above).
- Roll back, roll forward and rebuild end with `UpdateChecksum(m_currentfile, true)`: the checksum is corrected without asking (T7 called `VerifyChecksum(false)`).
- Snapshots folder: ECU only.
- Both suites default to the same `MyDocuments\TxSuite\Projects` (shared AppSettings, separate registry keys), so each lists the other's projects; nothing records which suite made one.

## Compare

**Entry points** (Actions → General actions):
- "Compare symbols with other binary" (3519): `Trionic 8 binaries|*.bin` → `CompareToFile`.
- "Compare binary outside symbolrange" (3891): T8 only, frmBinCompare with the symbols (below).
- "Compare binary with other binary" (3806): frmBinCompare. Without an open file: "No file is currently opened, you need to open a binary file first to compare it to another one!".
- **No "Compare to original file"** and no SRAM compares (no `Binaries\<partnumber>.bin` lookup in T8).

**CompareToFile** (3323), differences from T7Suite:
- The other file is parsed with `TryToOpenFile`, the open routine, with side effects: the imported SRAM file is cleared, the filename caption is blanked, **the open file's PID and TEM tables are replaced by the other file's** (`m_pids` / `m_tems`), the open/closed status is recomputed for the other file, SymbolFiller runs on it when enabled, the read-only caption reflects it. No MRU (T8 has none).
- Matching by `SmartVarname` on both sides (the user description when it isn't a "Symbolnumber " placeholder, else Varname); empty names skipped; no SymbolNames / LocalID exclusions. Both flash addresses must be 0 < a < 0x100000.
- Stats: same formulas as T7 (bytes compared, `diffabs` halved for 16-bit, perc over bytes).
- Row: SYMBOLNAME = the current file's SmartVarname, DESCRIPTION = SymbolDictionary help text, CATEGORYNAME = the symbol's Category (prefix before '.').
- Calibration test (passes 2 and 3): `Cal.`, `Cal1.`–`Cal4.`, starts with `X_Acc` (no `DisplAdap.`).
- Panel "Compare results: <file>", docked left, width 700. Same.
- Double-click: `StartTableViewer(name)` plus the compare viewer `Symbol: <name> [<other file>]` (axes from the other file); "Show differences map": `Symbol difference: <name> [<other file>]`, axes from the current file, "Map lengths don't match...". Same as T7Suite, except the MapViewerEx button rules (the difference viewer's "Save to file" is enabled).

**CompareResults grid** (`CompareResults.cs` / `.designer.cs`): captions, hidden columns, grouping ("({0})"), AutoFilterRow, Salmon / CornflowerBlue rows, Enter / double-click and the context menu ("Show differences map", "Export to Excel" → `<StartupPath>\diffexport.xls`, "Save layout", "Export as tuning package"): same as T7Suite. Differences:
- Column order: "Description" first (width 384), then "Symbol " (trailing space), then as T7.
- Hex mode: X6 only on the hidden SRAM / Flash address columns; lengths and symbol numbers always decimal (T7 formatted all six).
- Layout key `HKCU\Software\MattiasC\T8SuitePro\CompareView`.
- "Export as tuning package": `Trionic 8 packages|*.t8p`, no open-software address offset.
- Pass-1 rows leave MissingInOriFile / MissingInCompareFile as DBNull (T7: false). "Map lengths don't match..." is a plain MessageBox.

**Binary compare** (frmBinCompare, "Binary compare (byte-by-byte)"): same window, lists and 16-byte line format. The open dialog filter is `Trionic 8 binaries|*.bin`. **"Compare binary outside symbolrange"** (3891, T8 only): `Symbols = m_symbols` (the open file's), `OutsideSymbolRangeCheck = true`:
```
inside(a) = any open-file symbol with Flash_start_address ≤ a < Flash_start_address + Length    (frmBinCompare.cs 46)
a 16-byte line is listed when a byte outside every symbol differs (59); the line still shows all 16 bytes of both files, nothing highlighted
```
SRAM calibration symbols count at their flash copy; RAM-only and zero-length symbols mask nothing; each byte scans the whole symbol list (slow, synchronous). T7's own "outside symbol boundary" mode was dead.

**Transfer maps** ("Transfer maps to another binary", 5631): same wizard texts ("Transfer maps to different binary wizard", "Happy driving!!!\nDilemma © 2008", summary checkbox, `TuningReport` "Data transfer report"). Differences:
- Offered (5544): `Flash_start_address > 0`, the symbol's address < 0x100000, length > 0, SmartVarname contains '.'. No MapChkCal.ST_Enable / SymbolNames / LocalID exclusions.
- Backup `<dir>\<name><yyyyMMddHHmmss>beforetransferringmaps.bin`, then the target is parsed with `TryToOpenFile` (same side effects as compare), sorted by flash address.
- Target symbols: 0 < flash < 0x100000, length < 0x1000; matched by SmartVarname against the open file's symbols containing '.'; copied when the lengths match ("Unable to transfer symbol <name> because source and target lengths don't match!", "Transferred symbol <name> successfully", "Failed to transfer symbol <name>: <error>"), with a transaction entry in the open project (as T7).
- Target checksum: `UpdateChecksum(target, AutoChecksum)` (asks per layer when AutoChecksum is off).

**Copy address table to another binary** (13268): "Select binary file to transfer the address table to...", filter `T8 binary files|*.bin`. Start = Form1's own `GetAddrTableOffsetBySymbolTable(file) + 7` (755: first NqNqNq after "sYMBOLtABLE", + 21) for both files, "Address table start addresses are not equal, continue anyway?" (Yes/No, "Attention!") when they differ. Copies 10-byte records from start − 17 (T7: start − 7) until a record whose byte 9 ≠ 0 (T7: byte 8 or 9), then **updates the target's checksum** (`UpdateChecksum(target, true)`, T7 didn't), "Transfer done" / "Transfer cancelled".

**Search map content** (5095): same dialog and search as T7Suite, with the same two bugs (16-bit tables scanned over the first half only, text matched only when longer than the map). Differences: factor from SymbolDictionary; "No results found..." as a MessageBox; panel title "Search results: " + " number <n>" / " string <s>"; rows named by SmartVarname; results open with `StartTableViewer(name)`.

## Settings dialog

**Window.** "Settings", File → Settings → "Settings" (4517). No tabs: three groups, "User interface settings", "Realtime settings", "Project settings", then Cancel / Ok. No "General settings" group, no "Autotune settings" / "Autologging settings" buttons. Loading and write-back as T7 (the handler first replaces `m_appSettings` with a fresh `new AppSettings(suiteRegistry)`, no visible effect). After OK: `SetupMeasureAFRorLambda` (gauge only), `SetupDocking` (same), `SetupDisplayOptions` (no realtime font; X6 also on the hidden "Bit mask" column). The default symbol-list filters are rebuilt only on open and import, not after OK.

**Storage.** Shared `CommonSuite/AppSettings.cs` under `HKCU\Software\MattiasC\T8SuitePro` (`T8SuiteRegistry`): the same value names and defaults as T7 (no suite-specific default), nothing shared with `…\T7SuitePro`. Sub keys as T7: `SymbolColors`, `LogFilters\<n>`, `Channels`, `CompareView`. **`HKCU\Software\T8SuitePro\TransferSettings`** (last transfer selection) is **outside MattiasC** (T7: `…\MattiasC\T7SuitePro\TransferSettings`), like T7's MRUList, so a plain import of the MattiasC tree misses it. No MRU key. Shell verb `SystemFileAssociations\.bin\shell\Edit in T8 Suite\command` (212). Settings folder for files: `%APPDATA%\MattiasC\T8SuitePro` (`UserAppDataPath`'s parent: SymbolViewLayout.xml, mymaps.xml, rtsymbols.txt).

**User interface settings** (three columns: Auto size new mapwindows, Auto size columns in mapviewer, Use red and white maps, Don't display colors in mapviewer, Show table upside down, Show graphs in mapviewer | Auto load last file on startup, Fancy docking, Hide symbol window, Auto dock maps from same file, Auto dock maps with same name, New panels are floating | Show mapviewers in seperate windows, Use new mapviewer, Synchronize mapviewers, Auto update checksum, Show addresses and lengths in Hex, Show map preview popup, Auto mapdetection active; below: Default view size for maps, Default view type for maps). Only the differences:

| Caption | Setting | Default | Notes |
|---|---|---|---|
| Show map preview popup | ShowMapPreviewPopup | false | T8 only: the map helper popup (T8-only tools). Still carries the tooltip of "Always re-create repository items", whose checkbox it replaced |
| Auto mapdetection active | MapDetectionActive | false | T8 only: SymbolFiller at open when no symbol XML was loaded |
| Show mapviewers in seperate windows | ShowViewerInWindows | false | enabled here (disabled in T7), still unused |
| Auto update checksum | AutoChecksum | true | moved here from "General settings"; drives the silent fixes (Checksum above) |
| Show addresses and lengths in Hex | ShowAddressesInHex | true | moved here; also picks the default filter literals |

- Absent: "Always re-create repository items", "Use T7Suite AFR maps" (AutoCreateAFRMaps), "Write timestamp marker in binary", the closed-loop indicator combo (StandardFill). T8 reads none of them.
- Everything else (captions, defaults, choices): same as T7. "Show table upside down" still has no effect.

**General settings:** gone. "Auto fix footer" and "Enable CAN logging" don't exist in T8.

**Project settings** (new group): "Request project notes" (RequestProjectNotes, false), "Project folder" (ProjectFolder; folder browser; empty → StartupPath\Projects). Same as T7 apart from the group.

**Realtime settings** (ECU, later; differences in bold): "Auto generate LogWorks file after session" (disabled, not wired), "Interpolate timescale for LogWorks" (false), "Only P-bus connection" (true), "Auto update SRAM viewers every" 5–60 "seconds" (false, 20), "Reset realtime symbol on tabpage switch" (true), **"Use Legion Bootloader"** (UseLegionBootloader, true: Read ECU / Flash / Recover use the Legion variants), **"Prefer dynamic retrieval of live data"** (PreferDynamicLiveData, true, tooltip "Massive boost to sample rate if the binary is compatible"), "CANBus adapter type" ("Lawicel CANUSB"), "Adapter", "Configuration" (Combi; ELM327 / Just4Trionic baud rate 38400), "Use wideband O2 on com port" + device + port (**excludes nothing: no pin-16 option**), "Notifications" (**symbols with SRAM address ≥ 0x100000**). **Absent:** "Use additional CANbus frames", "Use wideband O2 (pin 16) with symbol" + configuration, "Measure AFR in lambda", Autotune / Autologging settings (UseWidebandLambda, WideBandSymbol, the wideband voltage/AFR values and MeasureAFRInLambda are still read, registry only).

## Symbol import / export

**Import** (File → "Import..." submenu: "Import XML descriptor file", "Import CSV descriptor file", "Import AS2 descriptor file"; a duplicate top-level "Import XML descriptor" button is hidden). After each: grid refresh, `SetDefaultFilters`, `<bin>.xml` saved. No `DynamicTuningMenu` / open-status refresh (T7 did both).
- XML (14142): `Trionic8File.TryToLoadAdditionalXMLSymbols` → same as T7Suite (table name from the file, SYMBOLNAME + FLASHADDRESS match, swap of "Symbolnumber " names).
- CSV (14188): `number;name;…` sets `Userdescription` = name (+ Description, Category) on every symbol with that `Symbol_number`. **No Varname/Userdescription swap** afterwards (T7 swapped); the name works through SmartVarname. Note T8's `Symbol_number` is index + 1.
- AS2 (14236): the Nth `*` line names the Nth symbol with Length > 0 **in address-table order** (T8's collection isn't sorted; T7 counted in its length-sorted list). No swap.

**Sidecar `<bin>.xml`:** same writer as T7 (`SymbolXMLFile.SaveAdditionalSymbols`), written on user-description edits and imports only (not at open). See the Symbolnumber off-by-one under Opening.

**Export symbollist as CSV** (14032): `{Varname with ','→'.'},{address},{SRAM address},{length},{number},{type}`: six fields, **no user description** (T7 had seven). "Export done".

## Exports and file actions

- **Save as** (5471): same as T7Suite ("Save current file as... ", "Do you want to open the newly saved file?"); the reopen runs the open steps inline, checksum check included.
- **Create backup file** (5440): **no checksum check first** (T7 verified). Project open: `Backups\…-backup-MMddyyyyHHmmss.BIN` (same); otherwise `<dir>\<base>yyyyMMddHHmmss.binarybackup` and "Backup created: <path>" as a MessageBox.
- **Save all** (15244): same as T7Suite.
- **Export to S19** (4028): "Export file to motorola S19 format...", `ConvertBinToSrec(file, 0x100000, target)`. Otherwise same.
- **Generate Idc file** (14582): `IdaProIdcFile.create(file, symbols)`, no dialog; T8's segments and names under Disassembler.
- **Import SRAM file** (Actions → General actions, 12570): "Snapshots|*.RAM", sets the SRAM file ("SRAM: <name>"), cleared again by every open. "Read from SRAM file" in the symbol menu opens "SRAM Symbol: <Varname> [<ramfile>]" at `Start_address & 0xFFFF` (by Varname, not SmartVarname), as T7.
- **Export map to Excel / Import map from Excel** (4691-5046): same code as T7Suite (`<bin>~<map>.xls`, the same layout; import: "Found valid symbol for import: <map>. Are you sure you want to overwrite the map in the binary?", "Too much information in file, abort"). Differences: chart title is the raw name; the x-axis factor comes from SymbolDictionary and is applied once (T8's axis values are raw); import ends with `UpdateChecksum(file, AutoChecksum)`.
- **XDF:** no menu (same).

**Tuning packages** (`.t8p`): same format and code as T7Suite's `.t7p` import / edit / export (`symbol=` / `length=` / `data=`, `searchreplace=`, `binaction=`, ignored header lines, the WIZARD log, "Import results", `RefreshTableViewers` afterwards). Differences (7757-8925 vs frmMain 14504-16300):
- Filters `Trionic 8 packages|*.t8p` everywhere.
- `?` wildcards work (`SearchMask`, 8423: a masked byte always matches).
- No MapChkCal.ST_Enable special case.
- One checksum update at the end: `UpdateChecksum(file, true)` (corrected without asking).
- "Edit an existing tuning package" is also in the symbol list menu. Same editor ("You have another tuning package edit window open, please close that first", "Tuning package symbol: <name> [<package>]" viewer as RAM / online viewer), but its Ok **saves the open bin's bytes, not the edited rows**: T8's `PackageExporter` ignores `Currentdata` (T7's used it). Closing a "Tuning package symbol" viewer by its close event doesn't remove the panel (`tabdet_onClose` lacks that title).
- Writer (`PackageExporter.cs`): same lines (`symbol=` / `length=` / `data=`, no header), symbols with 0 < flash < 0x100000, bytes always read from the bin, no open-software address offset.
- **Export fixed tuning package**: 27 T8 maps: AirCtrlCal.PRatioMaxTab, BstKnkCal.MaxAirmass, BstKnkCal.MaxAirmassAu, BFuelCal.TempEnrichFacMap, BFuelCal.E85TempEnrichFacMap, KnkFuelCal.EnrichmentMap, KnkFuelCal.fi_OffsetEnrichEnable, KnkFuelCal.fi_MaxOffsetMap, IgnAbsCal.fi_highOctanMAP, IgnAbsCal.fi_lowOctanMAP, IgnAbsCal.fi_NormalMAP, IgnAbsCal.fi_StartMAP, DNCompCal.SlowDriveRelTAB, TrqLimCal.Trq_ManGear, TrqLimCal.Trq_MaxEngineManTab1, TrqLimCal.Trq_MaxEngineAutTab1, TrqLimCal.Trq_MaxEngineManTab2, TrqLimCal.Trq_MaxEngineAutTab2, TrqLimCal.Trq_OverBoostTab, MaxEngSpdCal.n_EngMin, TrqMastCal.Trq_NominalMap, TrqMastCal.m_AirTorqMap, TMCCal.Trq_MaxEngineTab, TMCCal.Trq_MaxEngineLowTab, InjCorrCal.BattCorrSP, InjCorrCal.BattCorrTab, InjCorrCal.InjectorConst (names the bin lacks are skipped).

**Lookup partnumber** (File → Settings → "Lookup partnumber", 14101): frmPartnumberLookup / frmPartNumberList code identical to T7, open / compare / create-new actions the same. Differences:
- Keys are "<partnumber>_<software version>" (e.g. "55353231_FA5I_C_FME2_72_FIEF_81c"), the names of the bins in `<StartupPath>\Binaries` (T8Extras ships 72 there); the buttons "Open this file" / "Compare to this file" / "Create new from original" need `Binaries\<key>.BIN`.
- `PartNumberConverter.cs` (698 lines) knows 64 keys: car model (Saab93, OpelVectra, OpelSignum, CadillacBTS), engine (B207E, B207L, B207R, Z20NET, Unknown), software version; Saab 9-3 B207E / L / R also get Stage-1 1200 mg/c and 280 / 300 / 320 Nm. Power, torque and the engine flags are never set; the dialog only shows "Carmodel" and "Engine type" (Power / Torque and the 2.0L / 2.3L / Turbo / Full pressure turbo boxes are hidden).
- Unknown key: "The entered partnumber was not recognized by T7Suite" (sic).
- "Partnumber list": columns Carmodel, Enginetype, Partnumber, SoftwareVersion, Makeyear (70 rows, 64 distinct); grouped Carmodel → Enginetype; library marking and "Export" as T7.

**Other ribbon items:** "Toggle fullscreen [CTRL+F]" and the Skin page as T7. Help: "T8Suite user manual" (`T8_manual.pdf`, "T8Suite user manual could not be found or opened!"), "Trionic 8 technical documentation" (`Trionic 8.pdf`, "Trionic 8 documentation could not be found or opened!"), "About T8Suite" (heading "T8Suite Pro", no version), "Check for updates" / "Release notes" (msiupdater on `http://develop.trionictuning.com/T8Suite/`, "t8suitepro", "T8Suite.msi"; also at startup). Version 0.1.57.0.

## Tuning ribbon page (quick map buttons)

Every button is `StartTableViewer("<symbol>")` (4277-4498, 14541, 14587-14597), so a missing map gives "Symbol <name> does not exist in this file". Groups and captions in ribbon order, symbol in brackets:

- **Tuning wizards:** "Tune me up™" (hidden), "Tuning Wizard", "Easy tune to stage III" (hidden): see T8-only tools.
- **Airmass controller:** "Max airmass map (manual)" [BstKnkCal.MaxAirmass], "Max airmass map (auto)" [old: BstKnkCal.MaxAirmassAu, new: FFAirCal.m_maxAirmass], "Airmass Fuelcut" [FCutCal.m_AirInletLimit].
- **Torque controller:** "Nominal torque map" [TrqMastCal.Trq_NominalMap], "Nominal Gas torque map" [TrqMastCal.Trq_NominalGasMap], "Airmass torque map" [TrqMastCal.m_AirTorqMap], "Airmass Gas torque map" [TrqMastCal.m_AirTorqGasMap], "Ambient pressure trq limiter" [TrqLimCal.Trq_CompressorNoiseRedLimMAP], "Trq limit in overboost" [TrqLimCal.Trq_OverBoostTab], "Trq limit auto 150 hp" [old: TrqLimCal.Trq_MaxEngineAutTab2, new: TrqLimCal.Trq_MaxEngineTab2], "Trq limit auto 175+ hp" [old: TrqLimCal.Trq_MaxEngineAutTab1, new: TrqLimCal.Trq_MaxEngineTab1], "Trq limit manual 150 hp" [old: TrqLimCal.Trq_MaxEngineManTab2, new: FFTrqCal.FFTrq_MaxEngineTab2], "Trq limit manual 175+ hp" [old: TrqLimCal.Trq_MaxEngineManTab1, new: FFTrqCal.FFTrq_MaxEngineTab1], "Manual gear trq limit" [TrqLimCal.Trq_ManGear], "FlexFuel torque limit" [FFTrqCal.M_maxMAP], "Max torque 150hp" [TMCCal.Trq_MaxEngineLowTab, new only], "Max torque 175/200hp" [TMCCal.Trq_MaxEngineTab, new only], "RPM limiter" [MaxEngSpdCal.n_EngLimTab].
- **Fuel controller:** "Fuel correction map" [BFuelCal.LambdaOneFacMap], "Fuel knock map" [KnkFuelCal.EnrichmentMap], "Injection end angle map" [InjAnglCal.Map], "Enrichment Petrol" [BFuelCal.TempEnrichFacMap], "Enrichment E85" [FFFuelCal.TempEnrichFacMAP], "Inj. Constant" [InjCorrCal.InjectorConst], "Dead times" [InjCorrCal.BattCorrTab], "Enrichment Petrol" [BFuelCal.Lambda1FacMap] (the same caption twice), "Jerk Enrichment Petrol" [BFuelCal.m_AirJerkTab], "Jerk Enrichment Fuelmaster" [BFuelCal.JerkEnrichFacTab].
- **Boost controller:** "P of PID controller" [AirCtrlCal.Ppart_BoostMap], "I of PID Controller" [AirCtrlCal.Ipart_BoostMap], "D of PID controller" [AirCtrlCal.Dpart_BoostMap], "Boost regulation map" [AirCtrlCal.RegMap].
- **Ignition controller:** "Normal ignition map" [IgnAbsCal.fi_NormalMAP], "High octane map" [IgnAbsCal.fi_highOctanMAP], "Low octane map" [IgnAbsCal.fi_lowOctanMAP], "Normal Gas ignition map" [IgnAbsCal.fi_NormalGasMAP], "MBT ignition map" [IgnAbsCal.fi_IgnMBTMAP], "MBT Gas ignition map" [IgnAbsCal.fi_IgnMBTGasMAP], "Fuel cut ignition map" [IgnAbsCal.fi_FuelCutMAP], "Startup map" [IgnAbsCal.fi_StartMAP].
- **Pedal controller:** "Pedal position map" [TrqMastCal.X_AccPedalMAP], "Torque request map" [PedalMapCal.Trq_RequestMap].
- **General:** "EGT estimate map" [ExhaustCal.T_Lambda1Map].

**DynamicTuningMenu** (2754; replaces T7's rules), run after every open when the file exists. `sw` = `T8Header.SoftwareVersion` (e.g. `FC0J_C_FMEP_63_FIE_82s`):
```
if sw.Length > 2:
  old = sw[1] < 'C' or sw starts "FC01_O"                    ("breakpoint FC00" assumption; FC01 open is old)
  old: captions "Max airmass map (manual)", "Max airmass map (auto)", "Trq limit auto 175+ hp", "Trq limit auto 150 hp",
       "Trq limit manual 175+ hp", "Trq limit manual 150 hp"; hide "Max torque 150hp", "Max torque 175/200hp", "FlexFuel torque limit";
       show the two manual buttons
  new: captions "Max air Petrol", "Max air E85", "Trq limit 175/200hp", "Trq limit 150hp", "Trq limit E85 175/200hp", "Trq limit E85 150hp";
       show "Max torque 150hp" / "Max torque 175/200hp";
       sw starts FA / FC / FE (petrol) → hide "FlexFuel torque limit" and the two E85 limit buttons, else (FD, FF: BioPower) show them
  each button's Tag (Old / New) picks the symbol in its click handler
show only when the symbol exists (SmartVarname): "Enrichment E85" FFFuelCal.TempEnrichFacMAP, second "Enrichment Petrol" BFuelCal.Lambda1FacMap,
  "Jerk Enrichment Petrol" BFuelCal.m_AirJerkTab, "Jerk Enrichment Fuelmaster" BFuelCal.JerkEnrichFacTab, "Normal Gas ignition map"
  IgnAbsCal.fi_NormalGasMAP, "MBT Gas ignition map" IgnAbsCal.fi_IgnMBTGasMAP, "Nominal Gas torque map" TrqMastCal.Trq_NominalGasMap,
  "Airmass Gas torque map" TrqMastCal.m_AirTorqGasMap
```
- Before a file is opened every button is visible with the designer captions (the old ones), whose Tag is unset: clicking "Max airmass map (auto)" or a torque limit button then throws on the null Tag cast.

## Airmass result viewer and compressor map

**Opening** (Actions → "Airmass result viewer", 7289): panel "Airmass result viewer: <file>", width 800, re-read on every calculation, silently nothing when the check fails: same as T7. `CheckAllTablesAvailable` (7265) needs PedalMapCal.Trq_RequestMap, TrqMastCal.m_AirTorqMap, TrqMastCal.Trq_NominalMap, BstKnkCal.MaxAirmass, FCutCal.m_AirInletLimit, TrqLimCal.Trq_ManGear, TrqLimCal.Trq_MaxEngineManTab1 or Trq_MaxEngineTab1, TrqLimCal.Trq_MaxEngineAutTab1 or Trq_MaxEngineTab1; no axes checked. Tables are read from flash only; a missing symbol, or one starting below 0x20000 or ending past 0x100000, reads as a single 0 (`ctrlAirmassResult.cs` 132-216).

**Symbols** ("Tab1/2" = Tab1 when "high output" is ticked, else Tab2):

| T7Suite | T8Suite |
|---|---|
| PedalMapCal.m_RequestMap (mg/c) | PedalMapCal.Trq_RequestMap (0.1 Nm, signed); axes n_EngineMap / X_PedalMap |
| TorqueCal.m_AirTorqMap (M_EngXSP, n_EngYSP) | TrqMastCal.m_AirTorqMap (Trq_EngXSP, n_EngineYSP) |
| TorqueCal.M_NominalMap | TrqMastCal.Trq_NominalMap (m_AirXSP, n_EngineYSP), 0.1 Nm |
| TorqueCal.M_EngMaxTab / M_EngMaxAutTab | TrqLimCal.Trq_MaxEngineManTab1/2 / Trq_MaxEngineAutTab1/2, falling back to TrqLimCal.Trq_MaxEngineTab1/2 |
| TorqueCal.M_EngMaxE85Tab | FFTrqCal.FFTrq_MaxEngineTab1/2 |
| TorqueCal.M_OverBoostTab, EnableOverBoost | TrqLimCal.Trq_OverBoostTab, TrqLimCal.EnableOverBoost |
| TorqueCal.M_ManGearLim | TrqLimCal.Trq_ManGear |
| — | TMCCal.Trq_MaxEngineTab / Trq_MaxEngineLowTab (automatic, by high output) |
| BstKnkCal.MaxAirmass / MaxAirmassAu / OffsetXSP / n_EngYSP | same; fi_offsetXSP when OffsetXSP is absent, MaxAirmass when MaxAirmassAu is absent |
| — | FFAirCal.m_maxAirmass (FFAirCal.fi_offsetXSP signed, BstKnkCal.n_EngYSP), E85 only |
| BFuelCal.Map / E85Map | BFuelCal.TempEnrichFacMap / FFFuelCal.TempEnrichFacMAP |
| FCutCal.m_AirInletLimit, InjCorrCal.*, BFuelCal.AirXSP / RpmYSP, ExhaustCal.T_Lambda1Map | same |
| TorqueCal.ST_Loop, M_CabGearLim, M_1GearTab, M_5GearLimTab, M_EngMaxE85TabAut, LimEngCal.TurboSpeedTab(2), LambdaCal.MaxLoad*Tab | not used |

**Options:** automatic, Trionic calculation, firmware limited, display modes, kW, lbft, buttons: same. "Car runs E85": enabled only with FFTrqCal.FFTrq_MaxEngineTab1 or Tab2. "Car is high output (175/210 hp)" (on, always enabled, never set from the bin) replaces "convertible" and picks Tab1/2. "View in overboost": disabled with an automatic gearbox, else enabled when TrqLimCal.EnableOverBoost has a non-zero byte (a kept tick still applies with automatic). Gear: "Undefined gear", "First gear" … "Sixth gear", "Reverse gear" (index 0..7 as ECMStat.ManualGear), default "Fifth gear". Ambient pressure is shown and recalculates, but nothing reads it.

**Algorithm** (`CalculateDataTable`, `ctrlAirmassResult.cs` 1172): the pedal map is a torque request, turned into airmass first (`TorqueToAirmass` 249):
```
req = Trq_RequestMap[pedal][rpm]; if req > 32000: req -= 65535          (0xFFFF → 0, off by one)
air = m_AirTorqMap(x Trq_EngXSP = req, y n_EngineYSP = rpm)
torque limit (311), always (no ST_Loop switch), 0.1 Nm, every rpm table interpolated on TrqMastCal.n_EngineYSP:
  trq = firmware limited ? (automatic ? 3500 : 4000) : 10000;  lim = Gear
  E85:  if trq > FFTrq_MaxEngineTab1/2: trq = it, lim = E85
  else: eng = engine table; ob = overboost ticked ? Trq_OverBoostTab : 0
        if ticked and trq > ob: trq = ob, lim = Overboost
        elif ticked and eng < trq < ob: trq = ob, lim = Overboost
        elif trq > eng: trq = eng, lim = Engine
  gear = automatic ? TMCCal.Trq_MaxEngineTab/LowTab(rpm) : Trq_ManGear[gear index]; if trq > gear: trq = gear, lim = Gear
  air = min(air, m_AirTorqMap(x = trq, y = rpm))
airmass (434): MaxAirmassAu with automatic, else FFAirCal.m_maxAirmass with E85, else MaxAirmass; knock offset 0
no turbo speed limiter; fuel cut, limiter naming and interpolation as T7
```
- Tables not in the open check (TMCCal.*, the Tab2 tables, Trq_OverBoostTab, FFAirCal.m_maxAirmass) read as 0 when missing: a missing torque table caps every cell at the first m_AirTorqMap column, a missing airmass table zeroes everything.

**Display:** grid, colours, display modes, dyno graph: same. No Black (turbo speed) or White (E85 automatic) triangles (AirmassLimitType.cs lacks them); SaddleBrown also marks the TMCCal limit. Legend in two columns: "Airmass limiter", "E85 engine torque limiter", "Engine torque limiter" | "Gear torque limiter", "Fuelcut limiter", "Overboost limiter"; double-click opens the table in use (MaxAirmassAu with automatic even when absent → "Symbol BstKnkCal.MaxAirmassAu does not exist in this file").
- "Trionic calculation" torque: TrqMastCal.Trq_NominalMap (signed, integer ÷ 10).
- VE = map value / 128 (TempEnrichFacMap, or FFFuelCal.TempEnrichFacMAP with E85) instead of / 100: injector DC × VE/128; target λ = 128 / value (× DC/100 above 100 %); EGT = T_Lambda1Map × 128 / value at every load (the MaxLoad check is commented out); E85 −50 °C above 50 as T7.
- **Compare** (1488): `ValidateTrionic8File` first (its two messages; a rejected file drops the current compare), symbols loaded as on open but without map detection.

**Compressor map:** turbos, images, calibrations and formulas same as T7 (the Compressormaps folder is identical). Default: TD04-15G when the header VIN (`T8Header.ChassisID`) decodes to a Mitsubishi TD04L-14T engine (B207R / B207H / B207G / B207S), else GT17; engine always 2.0 l; no part number lookup.

## T8-only tools

### TEM editor

Actions → Edit → "TEM editor" (`btnTemEdit_ItemClick`, 15483). Disabled at every open, enabled when the file's TEM table has > 1 entry (624; the locator already wants > 4). None of the 72 bins in `T8Binaries/` has a TEM table. "Please open a Trionic 8 file before playing with this feature" only after a failed symbol extraction (`m_symbols == null`). Sizes: frmTEM 381 + 213, Form1 64, locator `Trionic8File.cs` 1437-1544 (108), PidCollection / PidHelper in `pidCode.cs` (338, shared with the PID editor).

**Table** (read at open, `LocateTemTable`, `Trionic8File.cs` 1446; the editor edits a copy of `m_tems`, Ok replaces it, so changes made elsewhere only show after a reopen):
```
from 0x20144, even offsets: match 4E 71|75 00 00 "OFF" 00|20        (nop / rts, index 0, "OFF" in any case)
table = match + 2; entry = [u16 symbol index][4 chars], 6 bytes
count entries while index < 0x4000 and every char is 0 or 0x20..0x7E; count > 4 → take it, else keep searching
label = chars up to the first < 0x20; entry 0 ("OFF") is protected
```
The symbol index is the 1-based address-table position (`Symbol_number_ECU`).

**Dialog** "TEM editor (N items)" (modal, 926×494, find panel, "Ok" / "Cancel"): columns " " (row), Label (editable), Symbol (lookup shown as name), Description, Type (X2), Address (Internal_address, X6), Size.
- Lookup: symbols with `Internal_address > 0` that aren't empty typedefs (`type != 0x20 || length > 0`), plus those already used; choosing one refreshes Description / Type / Address / Size by name.
- "OFF" row read-only, described "This message is displayed in idle mode (No data is read)".
- Label validation: "Enter a name of length 1 to 4!", "No special characters!" (outside 0x20..0x7E), "Parse exception! (<exception>)".
- Colours (not on "OFF"): OrangeRed for an empty / > 4 label, size missing or < 1, symbol index < 1 or > 20000; Yellow on Size / Type / Symbol when type and size disagree (t = type & ~0x23: byte 0x04 and size ≠ 1; long 0x08 / 0x40 and size ≠ 4; bitfield 0x10 and size > 2; word t = 0 and size ≠ 2; or 0x80 set). The mouse wheel closes the cell editor.

**Ok** (15500):
```
for every row (changed or not): data = [idx hi, idx lo, label (≤ 4), zero padded]
    if 0x20000 ≤ address, address + 6 ≤ 0x100000, 0 < idx < 65536: savedatatobinary(address, 6, data, file, false, "")   ("OFF" never written)
UpdateChecksum(file, AutoChecksum) once                                   no transaction entry, no backup
```

### PID editor

Actions → Edit → "PID editor" (`btnPidEdit_ItemClick`, 15420), always enabled; without a table (or file) it opens empty as "PID editor (0 items)" and Ok writes nothing. Sizes: frmPID 374 + 224, Form1 62, locator `Trionic8File.cs` 1250-1435 (186).

**Table** (read at open, `LocatePrimaryPidTable`, `Trionic8File.cs` 1369; same copy semantics as TEM):
```
from 0x20144, even offsets, the call that passes the table:
  tst.b dN; beq.w; pea (x).w; pea (count).w; clr.l -(sp); move.l #table,-(sp); jsr; move.l; lea $10(sp),sp; moveq #-1; cmp.l; beq
  → count = word at +12, table = long at +18
fallback (1294): movea.l $10(sp),aN; jsr; movea.l #table,aN; pea; pea (count).w; clr.l -(sp); move.l aN,-(sp); jsr …  → table at +12, count at +22
accept: table ≥ 0x20144, table + 8·count ≤ 0x100000, 0 < count < 10000, every entry has bytes 2-3 = 0 and byte 7 = 0
entry (8 bytes): [u16 PID][00 00][u16 symbol index][u8 flags][00]; flags: Read bits 7-6, Write 5-4, Control 3-2
```
Only the primary table (a secondary one is a TODO). Stock bins have about 600 entries from PID 0100, all flags 0x48 (Read Enabled, Write Disabled).

**Dialog** "PID editor (N items)": " ", PID (4 hex digits, editable), Read / Write (lookup 0 "Disabled", 1 "Enabled", 2 "Secured", 3 "Unknown"), Symbol (lookup), Description, Address (X6), Size. Control flag kept but hidden; no protected row.
- Lookup: symbols of length 1..7, plus those already used. PID input hex 0..FFFF stored X4, else "16-bit hex!" / "16-bit hex! (<exception>)".
- OrangeRed: PID empty / > 4 chars; size outside 1..7; Write not Disabled on a symbol below 0x100000 (flash); Read or Write = 3; symbol index < 1 or > 20000.

**Ok** (15437): every row → `[PID hi, PID lo, 00, 00, idx hi, idx lo, flags]` (7 bytes, the pad byte untouched) with `savedatatobinary(address, 7, …, false, "")` when 0x20000 ≤ address, address + 7 ≤ 0x100000, the PID parses 0..FFFF and 0 < idx < 65536; then one `UpdateChecksum(file, AutoChecksum)`. No transaction entry, no backup.

### Bitmask viewer

Every symbol with `BitMask > 0` (bytes 5-6 of its address-table record) opens `StartBitMaskViewer` (2469-2547) instead of a map viewer, from double-click / Enter and from every open by name. Sample bins have about 300 such symbols, all 2 bytes with one bit, almost all in SRAM (double-click sends those to the realtime path first, so the ECU branch below is only reached by name). Size: frmBitmaskViewer 222 + 298, Form1 79.
```
a = Flash_start_address
a > 0x100000: ECU open → t8can.readMemoryNew(a, 2); null → "Symbol outside of flash boundary and failed to read symbol from ECU"
              not open → "Symbol outside of flash boundary and no connection to ECU available"
else: 2 bytes from the file;  value = u16 BE (always 2 bytes, whatever the length)
group = every symbol with the same Flash_start_address
```
**Dialog** "Bit masked view of symbol" (modal, "Ok" / "Cancel"), group "Values in this symbol": 16 check boxes (0x0001-0x0080 left, 0x0100-0x8000 right). A box gets the `Varname` of the group symbol whose mask is exactly that bit (enabled, checked when set); other bits and multi-bit masks stay blank, disabled and unchecked; a later symbol with the same mask replaces the caption.

**Ok:** value = OR of the checked, enabled boxes (bits without a symbol become 0); SRAM symbol → nothing (the "save to ECU" branch is empty); else `savedatatobinary(a, 2, [hi, lo], file, true)` (transaction entry, no "Remark for change") and `UpdateChecksum(file, AutoChecksum)`.

### Map helper (map preview popup)

Settings → "Show map preview popup" (`ShowMapPreviewPopup`, default off). One `frmMapHelper` lives with Form1 (136, closed at 4005): a separate, unowned, captionless, non-topmost window with no taskbar entry, always hosting the **legacy** MapViewer. Size: 126 + 98, Form1 4071-4085, 4088-4200, 4202-4275.
```
hover a "Symbol name" cell of another row than last time (ShowSymbolHitInfo, 4088):
    popup at (grid X + grid width + 50, mouse screen Y)          (grid X is parent-relative but used as screen X)
    visible → refresh now; else restart tmrMapHelper (100 ms) and hide
any other cell / outside the rows → stop the timer, hide        (leaving the grid doesn't hide it)
tick (4202): view size / type, colours, auto-size columns, red-white from the settings; axes, factor, 16-bit as the table viewer;
    address = GetSymbolAddress(Varname) (by SmartVarname, 0 at ≥ 0x100000 → no popup for SRAM symbols or user-named ones)
    data from the file, table only, size columns·60 × (length/columns)·15 (16-bit maps double height), kept inside the working area
```
No viewer events are wired: edits in the popup go nowhere. Moving back to the row it was hidden on doesn't show it again.

### Axis browser

Same control and panel as T7 ("Axis browser: <file>", left, 700 px), from Actions → Information → "Browse axis information" (7127) and "Browse axis info" (7085). Differences: rows named by SmartVarname; axes and descriptions from SymbolDictionary (English only); the context-menu filter uses the selected symbol's `Varname` (a user-named symbol gets an empty list); double-click on a name or axis is always `StartTableViewer(name)` (T7 read from the ECU when connected).

### Hex viewer

Same control and panel as T7 ("Hexviewer: <file>", 580 px), Actions → General actions → "View file in hex" (5048). Differences: bin only (the SRAM hex viewer call is commented out, so an imported SRAM file gets no hex view); "Do you want to save changes?" titled "T8Suite", save errors and "Find reached end of file" titled "T5Suite 2.0"; a new `onClose` event nobody subscribes.

### Disassembler, interrupt vectors, Idc

Same code as T7Suite (CPU32 decoder, `<bin>.asm` / `<bin>_full.asm`, "Assemblerfile already exists, do you want to redo the disassembly?", hex view, find / replace, start address = the long at offset 4); `ctrlDisassembler` gets the file name and symbol list instead of a `Trionic7File`. Only the memory map differs:
- **Vectors** (`Trionic8File.GetVectorAddresses`, `Trionic8File.cs` 193): 120 (the first 480 bytes) instead of 256; names 0-63 as T7, 64-119 "User defined vector 0" … "55". "Show interrupt vectors" (7523) lists 120 rows.
- **Show disassembly** (7153): panel "T8Suite Disassembler"; functions followed from the 120 vectors.
- **Address ranges** (`Disassembler.cs`): flash < 0x100000, SRAM from 0x100000 (T7: < 0x80000 / 0xF00000). `LBL_` labels and JSR/BSR recursion only below 0x100000 (2120, 2556, 2667); operands `ROM_<name>` when the symbol's flash address < 0x100000, else `RAM_<name>` (517).
- **Registers:** the MC68377 map (531-913) instead of the 68332's 93 SIM/QSM/TPU names: 0x100000 = `RAM_FSRAM`; `<MODULE>.<REG>` or `<MODULE>.+0xNNN` for QADC64 (0xFFF000), QSM_B (0xFFF400), DPTRAM (0xFFF680), FASRAM (0xFFF6C0), CTM9 (0xFFF700), TPU3_B (0xFFF800, parameters 0xFFF900), BIM (0xFFFA00), TouCAN (0xFFFA80), SRAM_E (0xFFFB00), QSM_A (0xFFFC00), TPU3_A (0xFFFE00); blocks "unused in T8" as `<NAME>+0xNNN`; 0xFFF200-0xFFF2FF unnamed.
- **Show full disassembly** (7234): unchanged, sweeps 0x100000 bytes.
- **Generate Idc file** (14582, `IdaProIdcFile.cs`): same `<bin>-autogen.idc`, no dialog, `SetPrcsr("68330")`. Segments ROM 0-0x100000, RAM_FSRAM 0x100000-0x108000, RAM_DEVRAM 0x140000-0x180000 (open software only), one per module from QADC64 0xFFF000 to TPU3_A 0xFFFE00-0x1000000; `LowVoids(0)`, `HighVoids(0x1000000)`. Still marks 256 vector dwords. Names: non-alphanumerics → `_`, > 30 chars get `_<n>` appended, empty → `noname_<n>`. Per symbol: flash ≥ 0x100000 → `RAM_<name>` at it; else `ROM_<name>` at the flash address plus `RAM_<name>` at the SRAM address when ≥ 0x100000 (T7 wrote one name). No `Internal_*` register names (T7 wrote 94).

### Tuning wizard

T7's wizard is unreachable and never loads packs; **T8's works**. Tuning → Tuning wizards → "Tuning Wizard" (6573) opens frmTuningWizard ("Tuning Wizard"); "Tune me up™" next to it is hidden (2016, "Tune Me Up RIP, long live TuningWizard!"). Sizes: frmTuningWizard 158 + 441, Form1 14599-14983 (385).

**Loading** (`addWizTuneFilePacks`, 14881, once from the constructor): every `<StartupPath>\TuningPacks\*.t8x` (the installer ships the 17 packs from `TuningPacks/`). Format as `.t7x` in the T7 doc: `<SIGNATURE>` Base64 `</SIGNATURE>`, then lines decrypted with `Crypto.DecodeAES` and trimmed. Header lines: `packname=`, `bintype=` OLD / NEW / BOTH (anything else: never listed), `whitelist=` / `blacklist=` (comma lists, whitespace removed, `*` ends a prefix), `code=` (whitespace removed), `author=`, `msg=`.
- Verification: RSA PKCS#1 v1.5 / SHA-1 against `<StartupPath>\T8Pub.pem` over the MD5 hex of the decrypted, trimmed lines each + CRLF, as ASCII (`T7Core/Common/Crypto.cs` already does this). Failure only logs "Signature check failed for file: <file>" and skips the pack; a line that doesn't decrypt or a missing T8Pub.pem throws out of the constructor and aborts startup.
- Of the 17 shipped packs, `220129_All_XWD_TorqueLImDiffCode_WildcardTest_OK.t8x` ("Experimental R6 NEW FC0J-FF* Torque Limiters to 400nm and Airmass to 2000mg/c") fails (its source started with a BOM) and is never listed; the other 16 verify, none has `code=` or `binaction=`.

**Compatibility** (`compatibelSoftware`, 14661), against `T8Header.SoftwareVersion`:
```
old = sw[1] < 'C' (FA…, FB…) or sw starts "FC01_O"
old → bintype OLD or BOTH; new → NEW or BOTH
whitelist non-empty → sw starts with an entry (text before '*'); sw starts with no blacklist entry
```
With no file open the check throws on the null version and the wizard doesn't open (logged only).

**Pages** (`frmTuningWizard.cs`):
- "Tuning Wizard": "This wizard will help you apply a pre-defined set of Tuning Actions to your binary file. You will be given a choice of Tuning Actions in the coming pages."
- "Select Tuning Action" ("Selection your wanted Tuning Action from the list below and click Next"): the compatible packs (name + " [Protected]" with a code); "Software Version" shows the first 4 characters ("nAn!" when empty), "Author" the selection's author; an empty list disables Next.
- "Enter Tuning Code" (packs with `code=`): "The Tuning Package '<name>' requires that you enter the correct code.", "Hint: Try with authors first. For this Tuning Package it is <author>", "Please visit trionictuning.com and learn how to earn the code." (link); Next while "Enter code:" equals the plain-text code. After Back the page is never skipped again.
- "Confirm Tuning Action": "Your loaded binary file will now be modified with the following Tuning Action:" <name>, "The Tuning Action cannot be revered, ensure you have a copy of your binary stored."; Next needs "I fully understand the consequenses." (Back unticks it), applies, disables Cancel.
- "Completed Tuning Wizard": "You have now completed the Tuning Action '<name>'. Please check the modified maps below so that they are what you expect them to be. Easiest way to do that is to compare to the original binary.", the result list, then the pack's `msg=` in a box titled "Tuning Wizard Message". The failure text ("The Tuning Action '<name>' failed! …") is unreachable: actions always return 0.

**Applying** (`FileTuningAction.performTuningAction`, 14753):
```
packages = ReadTuningPackageFile(encoded = true, pack)            (decrypted again; header lines ignored; signature not rechecked)
if compatibelSoftware(sw):
    copy bin → <dir>\<bin>-yyyyMMddHHmmss-BACKUP-BEFORE-WIZARD-<packname>.bin   (invalid name chars removed, overwrite)
    ApplyTuningPackage(packages)      (as Import tuning package: -WIZARD.log, symbols with transaction entries, search & replace, one silent checksum update)
    results: "<Name>: N replacements" / "OK: <symbol>" / "Fail: <symbol>"; RefreshTableViewers()
then "Update PI Area" (DateAndName, 14795) runs:
    in the PI area: container 0x1D (programmer name), if ≥ 7 chars → "T8Suite" space padded; container 0x0A (release date), if 19 chars → now "yyyy-MM-dd HH:mm:ss"
    written encoded, no transaction entry, no checksum update (the PI area is outside both checksum layers)
```
File → "Import tuning package" (`.t8p`) uses the same reader / applier unencrypted, without the bintype / whitelist / blacklist check, backup or PI update.

### Debug ribbon group (developer only)

Group "Debug", last on the File page, `Visible = false`; shown only when the registry value `DebugMode` under `HKCU\Software\MattiasC\T8SuitePro` is true (2730; nothing writes it, no settings switch). Nothing here needs porting.
- "Test MFS Blocks" (7556): finds "MFS*" and decodes 0x1000 bytes per hit four ways plus 0xBAF06; all dumps commented out, it only logs "Block at XXXXXXXX".
- "Decode (D6 21)" / "Decode (53 A4)" / "Encode (D6 21)" / "Encode (53 A4)" (7343-7440): ask for a `*.bin`, transform 0x100000 bytes with `(b + 0xD6) ^ 0x21`, `(b + 0x53) ^ 0xA4`, `(b ^ 0x21) − 0xD6`, `(b ^ 0xA4) − 0x53`, write `<name>-decodedD621.bin`, `-decoded53A4.bin`, `-ecodedD621.bin`, `-ecoded53A4.bin` next to it (the PI-area and flash-block codings).
- "Parse" (14298): reads an AS2 file and appends `else if (symbolname == "X") returnvalue = <divisor>;` lines to `c:\t8fac.cs` (the source of the dead factor list in `GetMapCorrectionFactor`); the last symbol is never written.
- "ConvertOriFiles" (7619): for each `<StartupPath>\Binaries\Ori\*.bin`, parses it (leaving `m_currentfile` / `m_symbols` on it without updating the grid), copies it to `Binaries\<partnumber>_<swversion>.BIN` and appends C# lines to `C:\T8PartnumberCollection.cs` / `C:\T8PartnumberConverter.cs`.
- Leftover that does run for everyone: `T8Header.DecodeExtraInfo` writes `C:\t8decode\<bin>_memdump.bin` and `flashblock<n>.blk` on every header read if `C:\T8Decode` exists.

### Tune me up™ and Easy tune to stage III (unreachable)

Both buttons are `Visibility = Never` (designer 946, 961) and nothing else calls them; Form1 5669-7083 (1415 lines) is dead except the 5-line wizard handler.
- "Tune me up™" (6015 → `TuneMeUp` 6256): dialog "Tuning process settings" ("Maximum torque" 400 Nm, "Maximum engine power" 200 HP unused, "Car runs E85"), maxairmass = torque × 3.1 (÷ 1.07 E85), warning above 1300 mg/c, task dialog "Tune me up™ wizard". Would write a backup `<bin>-yyyyMMddHHmmss-beforetuningto-<N>-mg.bin`, a fixed TrqMastCal.Trq_NominalMap, TrqMastCal.m_AirTorqMap 6…100 % of maxairmass, BstKnkCal.MaxAirmass(Au) 40…100 % × maxairmass, 320 Nm into every TrqLimCal / TMCCal / FFTrqCal torque limiter, FCutCal.m_AirInletLimit = maxairmass × 1.2, one checksum update. `TuneToStageNew` (6291) is commented out of it.
- "Easy tune to stage III" (6579): empty handler; `TuneToStage` (6595) has no caller.

## T7Suite features T8Suite doesn't have (offline)

- MRU list; symbol XML from `repository\` and the `<name>*.xml` fallback; the shipped symbol-list offers; AutoFixFooter and every footer repair; the BioPower StartMap rename.
- Symbol name colours; "Read symbol from ECU / binary" in the symbol menu; the symbol grid's Excel / PDF export; F3 / F9.
- Axis correction factors, fixed gear axes, open-loop marks in the map viewer.
- Firmware information's options (torque limiters, OBDII, second lambda, fast throttle, light-off, ethanol sensor, emission limiting, SID start screen), read-only flags, programming / SID date, Import / Undo of VIN and immo, TIS footer fix.
- "Compare to original file", "Compare binary to SRAM snapshot", "Compare SRAM snapshots".
- SID information, ESP calibration, TCM limit, "Set ethanol content"; AFR target / feedback maps, autotune, "Import AFR feedback data"; the P&E micro group; SaabOpenTech "Extra functions".
- Settings: "Use T7Suite AFR maps", "Write timestamp marker in binary", the closed-loop indicator, "Auto fix footer", "Enable CAN logging", "Measure AFR in lambda", the pin-16 wideband option, Autotune / Autologging settings.

## ECU session

**Object and events.**
- One `Trionic8 t8can` for the whole run (123). The ctor (197-208) subscribes:
  - `onReadProgress` → caption "Downloading N %" plus the bar (237). `onWriteProgress` → "Sending N %" plus the bar (225).
  - `onCanInfo` → the caption only (232). Nothing reacts to `ActivityType`: no Cleanup, no box. Results come from the call's `Result` (see Programmer). T7 cleaned up and showed "Flash sequence done" / "Download done" on Finished*.
  - `onCanFrame` isn't subscribed.
- `SetProgress` / `SetProgressPercentage` (688 / 672) call `DoEvents` whenever the caption or value changes.

**Adapter setup, `SetCanAdapter` (15372).** Called before every open:
```
SecurityLevel = AccessLevelFD; OnlyPBus = setting
forceDynSymbolRefresh = true
T7's if-chain: LAWICEL, COMBI, ELM327 / JUST4TRIONIC (ForcedBaudrate = Baudrate), KVASER, J2534; no SLCAN branch
SetSelectedAdapter(Adapter); Combi may have none; else "Check settings, no CAN adapter has been selected!"
```
- No `Latency` (Trionic8 doesn't use it) and no `UseFlasherOnDevice`.
- `setCANDevice` (Trionic/TrionicCANLib/Trionic8.cs:104) always builds a new device. It also sets the ID filter to `FilterIdECU` (0x7E0, 0x7E8, 0x5E8). For ELM327 it sets `Sleeptime = ELM327`, which nothing resets.
- SetCanAdapter never calls Cleanup first. A call while a device is open drops that device without `close()`. This happens with Read ECU / Flash / Recover / Get ECU information while connected, and with Connect after Get ECU information or a flash, which both leave a device open. T7's FlasherConnect cleaned up first.
- The `if (!t8can.isOpen()) openDevice(...)` guards that follow SetCanAdapter always pass, because the device was just replaced. Every action therefore opens a fresh device.

**Connecting.**
- `RealtimeCheckAndConnect` (15327). Already connected → true. Otherwise status "Initializing CANbus interface", then SetCanAdapter, then `openDevice(true)`:
  - **OK:** status "Connected", button "Disconnect ECU", `m_RealtimeConnectedToECU = true`. The status bar's `barConnectedECUName` shows `GetSoftwareVersion()` (DID 0x08) (15360).
  - **Fail:** status "Failed to connect" (T7: "Failed to start KWP session"), Cleanup, false.
- **`openDevice(true)`** (Trionic8.cs:164): opens the device and sends `InitializeSession` (tester present `3E` on 0x011). Then up to 3 × `RequestSecurityAccess` at level FD (`27 FD` / `27 FE`), and finally starts the keep-alive timer.
- **`openDevice(false)`** only opens the device. It is used by flash, recover and DTCs.
- Library texts: "Open called in Trionic 8", "Open failed in Trionic 8", "Open succeeded in Trionic 8", "Session initialized", "Requesting security access", "Got seed value from ECU", "Security access : Key (XXXX) calculated from seed (XXXX)", "Security access granted", "Failed to get security access", "Open successful".
- **Connect button** (15304): toggles "Connect ECU" / "Disconnect ECU". Disconnect (15319): connected = false, Cleanup, caption "Connect ECU", status "". There's no alive polling to suspend or resume.
- **Form closing** (3991): connected = false, reading prohibited, rtsymbols.txt saved, Cleanup if open.

| Action | Opens with | Connected flag | Closed |
|---|---|---|---|
| Connect, Toggle realtime, SRAM-symbol double-click, knock / misfire buttons | `openDevice(true)` via RealtimeCheckAndConnect | set | on Disconnect |
| Get ECU information | `openDevice(true)` | not set | never |
| Read ECU, Flash, Recover | `openDevice(false)` | unchanged | never (no Cleanup afterwards) |
| Get fault codes, Clear DTCs | disconnect + hide panel, level 01, `openDevice(false)` | cleared | Cleanup straight after |

**Threading.**
- T8Suite has no BackgroundWorker, thread or async code. Everything runs on the GUI thread.
- The library's BackgroundWorker-shaped calls (`ReadFlash(this, args)`, ...) are called directly, with `sender` = the form, and they block. The handler then spins `while (args.Result == null) { DoEvents(); Sleep(10); }`. That loop only matters when the library returns without a Result: `ReadFlash` does this when security access is refused (Trionic8.cs:6429), and the UI then spins forever.
- The window stays alive only through the DoEvents in caption and progress updates. Other buttons can therefore run during a flash. `m_prohibitReading` (volatile, 10763) pauses the realtime pass.
- **Other threads:**
  - The keep-alive `System.Timers.Timer` (thread pool) and the adapters' reader threads.
  - Trionic8 has no lock, so a keep-alive that fires during a request shares `m_canListener` with it.
  - T7's KWP handler serialized requests with a thread-affine mutex.

**Keep-alive.**
- Trionic8 has its own 2 s timer (Trionic8.cs:80, 91). Only `openDevice(true)` starts it, after security access, and Cleanup stops it. Unless `StallKeepAlive` is set, it sends tester present (`3E`) to 0x7E0.
- The realtime tick sets `StallKeepAlive = true` before each pass and never clears it (10779). The reset is commented out: "This fixed some weid session terminations". Flash read/write and the SRAM snapshot set and clear it themselves.
- Requests send their own tester present when enough time has passed since the last one (shared stopwatch):
  - ≥ 500 ms for reads and writes by address;
  - ≥ 1 s for dynamic list configuration and reads.
- T7: a 1 s KWPHandler alive polling, suspended while the realtime panel shows.

## Programmer tab, "CAN Flasher"

Buttons in order: Read ECU, Flash current file to ECU, Recover ECU, and `btnSRAMSnapshot`, shown as "?". There's no P&E micro group.

**Bootloader choice.**
- The settings option "Use Legion Bootloader" (`UseLegionBootloader`, default **true**, CommonSuite/AppSettings.cs:1641) picks the routines:

  | Use Legion Bootloader | Read | Write | Recover |
  |---|---|---|---|
  | on (default) | `ReadFlashLegT8` | `WriteFlashLegT8` | `RecoverECU_Leg` |
  | off | `ReadFlash` | `WriteFlash` | `RecoverECU_Def` |
- Legion options are never set, so the library defaults apply: InterframeDelay 1200 µs, Faster off, UseLastMarker on, FormatBootPartition and FormatSystemPartitions off.

**Read ECU** (12457):
```
save dialog "Binary files|*.bin"
SetCanAdapter; caption "Starting flash download"; openDevice(false) if not open
open: prohibit reading; Legion ? ReadFlashLegT8 : ReadFlash (blocking); spin until Result
      true → "Download done"; false → nothing
open failed → nothing
SetProgressIdle → "Idle"
```
- Realtime isn't disconnected first (T7 did that). The file isn't opened afterwards (same as T7).
- **`ReadFlash`** (Trionic8.cs:6399):
  - Session `10 81` / `10 02`, security level 01, then uploads the bootloader.
  - Reads `21` local-id blocks of 0x80 from 0 up to the end-of-data pointer at 0x020140, + 0x200. The rest stays 0xFF.
  - Tester present every 3 s; 100 retries per block.
  - Writes the file and `<base>.md5`, then verifies the T8 checksum. Not Ok → "Checksum check failed: <result>" and Result false, but the file stays.
  - Texts: "Starting session", "Telling ECU to clear CANbus", "Requesting security access", "Uploading bootloader" / "Uploading bootloader FAILED", "Starting bootloader", "Downloading FLASH", "Downloading N Bytes.", "Frame dropped, retrying <addr> <n>", "Failed to download FLASH content", "Download done", "Could not write file... <msg>".
- **`ReadFlashLegT8`** (Trionic8.cs:8006 → ReadFlashLegion 8996):
  - Starts the Legion loader. If it is still running: "Loader was left running. Starting over".
  - Reads the full 0x100000. After 3 dropped frames it slows down: "Too many dropped frames: Slowing down..".
  - Compares the MD5 with the ECU's:
    - match: "Download done and verified"; the file and .md5 are written;
    - "Local data does not match ECU! Discarding data..";
    - "Could not fetch md5! Discarding data..".
  - Always asks the loader to exit.

**Flash current file to ECU** (12498):
```
no file on disk → nothing (T7: "No file has been loaded")
SetCanAdapter; "Starting flash download"; openDevice(false) if not open
open failed → "Unable to connect to Trionic 8 ECU"
prohibit reading; Sleep(1000); Legion ? WriteFlashLegT8 : WriteFlash; spin until Result
true → "Flash sequence done"
false and NeedRecovery → "Flash was erased but programming failed, do you which to attempt to recover the ECU?" ("Warning!", Yes/No)
                         compared with DialogResult.OK, so recovery never starts and nothing else is shown
false otherwise → "Failed to update flash"
```
- Like T7: no checksum check, no confirmation, and pending viewer edits aren't saved.
- With the current library only a Legion write that erased but didn't verify leaves `NeedRecovery` set. `WriteFlash` clears it before returning.
- **`WriteFlash`** (Trionic8.cs:6230): bootloader at 0x102460, erase, then writes 0xEA-byte blocks from 0x020000 to the end-of-data pointer.
  - Texts: "Starting session", "Telling ECU to clear CANbus", "Failed to get security access", "Uploading bootloader", "Failed to upload bootloader", "Starting bootloader", "Failed to start bootloader", "Erasing FLASH", "Programming FLASH", "Got incorrect response <data>", "FLASH upload completed" / "FLASH upload failed", "Failed to erase FLASH", "Session ended".
- **`WriteFlashLegT8`** (Trionic8.cs:8016 → WriteFlashLegion 8707):
  - Compares the MD5 of each partition: "Comparing md5 for selective erase..", "Partition N: Tagged for erase and write", "Selected N out of 9 partitions for erase and flash".
  - With the default options only partitions 5-9 (0x020000-0x0FFFFF) are candidates. Boot (0-0x3FFF), NVDM (0x4000-0x7FFF) and HWIO (0x8000-0x1FFFF) are left alone.
  - UseLastMarker also erases the partitions past the last used address.
  - Writes 0x80-byte blocks, skipping 0xFF blocks, verifies the MD5s and marries the MCP.
  - Texts: "FLASH upload completed and verified", "Everything is identical", "FLASH upload failed (Wrong checksum) Please try again!" ×5, "FLASH upload failed, please try again!" ×5, "Failed to erase FLASH".
- **Library prompts** go through `UserPrompt` (Trionic/TrionicCANLib/UserPrompt.cs):
  - "Do you REALLY want to write a new boot partition?!" and "Do you REALLY want to flash new VIN and key data?!" (caption "Point of no return");
  - the "Boot is broken!!" warning.

  T8Suite installs no handler, so the questions answer No and the warning isn't shown. The old library used MessageBox.

**Recover ECU** (14051):
```
open dialog "Binary files|*.bin"            (no size or checksum check; TrionicCANFlasher checks both)
SetCanAdapter; "Starting recovery procedure"; openDevice(false) if not open
Legion ? RecoverECU_Leg : RecoverECU_Def; spin until Result
"Recovery done" / "Failed to recover ECU"; open failed → "Unable to connect to Trionic 8 ECU"
```
- **`RecoverECU`** (Trionic8.cs:6049):
  1. Only when DID 0x9A, sent to 0x011, returns nothing or `00 00`. Otherwise "Recovery not needed...".
  2. Broadcast session and shutup, then the programming state from 0x311: "Recovery needed phase 1" / "Recovery needed phase 2", "Recovery not needed...", "Unable to communicate with the ECU...".
  3. Security over 0x011, then the bootloader upload.
  4. Writes the file: "Recovery completed" / "Recovery failed". With Legion it goes through WriteFlashLegion.
- T8Suite never calls `SetCANFilterIds(Trionic8.FilterIdRecovery)`, which TrionicCANFlasher does. With the filtering adapters the 0x311 / 0x011 answers never arrive, so recovery should stop at "Unable to communicate with the ECU..." → "Failed to recover ECU".

**SRAM snapshot ("?").**
- `btnSRAMSnapshot` has no caption and no ItemClick, so it does nothing.
- Its handler was removed in 881328f (2017, "T8 Flash current file and remove sram read"). It used to:
  1. open a save dialog "Snapshots|*.RAM";
  2. SetCanAdapter, caption "Starting snapshot download", `openDevice(false)`;
  3. `t8can.ReadSRAMSnapshot()` → the file, then "Snapshot done".
- `ReadSRAMSnapshot` no longer exists. The library has `GetSRAMSnapshot(sender, DoWorkEventArgs)` (Trionic8.cs:2826):
  - 0x8000 bytes from 0x100000 with `readMemory` (`23`, 0x40-byte blocks, 100 retries per block, a tester present after each block);
  - it writes the file itself;
  - texts: "Snapshot done" / "Failed to download SRAM content" / "Could not write file... <msg>".
- Nothing in T8Suite writes `.RAM` files any more.

## ECU information, fault codes, ECU writes

**Get ECU information** (13136). T8 only: T7Suite never called GetECUInfo.
```
SetCanAdapter; openDevice(true) if not open      (not marked connected; stays open with the keep-alive running)
open: prohibit reading; frmECUInformation.Show() (modeless, "ECU information"); fill field by field, DoEvents each
open failed → nothing
```
- Each field is a KWP `1A` ReadDataByIdentifier, in ASCII unless noted. `RequestECUInfoAsString` drops the last byte of the answer.

| Group | Label | Field 1 | Field 2 |
|---|---|---|---|
| ECU related data | ECU description | 0x72 `GetECUDescription` | 0x71 `GetECUHardware` |
| | Hardware type | 0x97 | 0x92 (supplier id) |
| | Build date | 0x0A | |
| | Serial number | 0xB4 | |
| | SAAB partnumber | 0x7C, u32 decimal | |
| | Basemodel partnumber | 0xCC, u32 | 0xCB, u32 (end model) |
| Calibration data | Calibration set | 0x74 | |
| | Codefile version | 0x73 | |
| | Software version | 0x08, NULs removed, trimmed | |
| | Software version file | 0x0F | |
| | Software IDs | 0xC1 / 0xC2, 0xC3 / 0xC4, 0xC5 / 0xC6 (three rows) | |
| | VIN number | 0x90 | |
| | Engine type | 0x0C | |
| | Speedlimit | 0x02 u16 / 10, + " km/h" | |

- Button: "Close".
- A DID that gets no answer is given up after 5 s: "No answer from the ECU to ReadDataByIdentifier 0xNN" (library).

**Get fault codes (OBDII)** (14984):
```
RealtimeDisconnectAndHide (15293: hide panel, timer off, Disconnect)
forceDynSymbolRefresh = true
SetCanAdapter; SecurityLevel = 01; openDevice(false)       (no security access requested)
frmFaultcodes (CommonSuite) ← t8can.ReadDTC(), each "DTC: P0123 StatusByte: XX" → Substring(5, 5)
Show() (modeless); RealtimeDisconnectAndHide → Cleanup
```
- **`ReadDTC`** (Trionic8.cs:4739): session `10 02`, then KWP `A9 81 12` (status mask: current + history).
  - Answers come on 0x5E8 as `81 hi lo type status`, until `00 00 00`; then `20` (return to normal).
  - The list ends with "No more errors!". That becomes "re er", which has no description, so it isn't shown.
- **Differences from T7:**
  - The codes come from KWP, not from the obdFaults SRAM symbol.
  - Nothing checks that the open worked: a failed open gives an empty form, or none when the adapter throws (the exception is swallowed).
  - Opening the form also ends the realtime session and hides the panel.
  - The serial wideband reader keeps running; the next panel show opens its port a second time.
- **Descriptions:** the same frmFaultcodes / DTCDescription loader as T7. T8 ships its own files, read in name order with the first entry per code winning; 7-character WIS codes are cut to 5:
  - `DTC_SaabHSTRC.xml` (3782 entries)
  - `DTC_SaabLSTRC.xml` (13)
  - `DTC_SaabTRC.xml` (3795)
- **Clear** (15022):
  - Acts only on codes starting with "P", and `ClearDTCCodes()` clears all codes ("TODO" for a single code), then re-reads.
  - The device was cleaned up when the form opened, so the call throws on the null device and the exception is swallowed. Clear does nothing.

**Clear DTC and knock counters** (15054):
- Same disconnect and hide, level 01 and `openDevice(false)` as above.
- `ReadDTC()` (result ignored), then `ClearDTCCodes()`.
- Shows "Clear DTC codes was successful" / "Clear DTC codes was failed" (T7 showed nothing), then disconnects again.
- `ClearDTCCodes` (Trionic8.cs:4818) is a functional 0x7DF `04` (OBD mode 4), positive answer `44` from 0x7E8, then `20`.

**Writes to the ECU.**
- Besides flashing, recovery and DTC clearing, only two things write to the ECU:
  - the map viewer's "Save to ECU" (`WriteMapToSRAM`, below);
  - the realtime engine's dynamic list configuration.
- T8Suite has none of T7's Synchronize to binary / ECU, upload / generate tuning package, Set ethanol content, performance mode or Extra functions (SaabOpenTech).
- The library's T8 setters are unused: VIN, top speed, RPM limiter, E85, oil quality, PI, DID files.

## SRAM maps and online viewers

**Addresses** (T8Suite/Trionic8File.cs:1057, `TranslateAddressOffsets`).
- Flash is 0x000000-0x0FFFFF (the bin). SRAM starts at 0x100000: 0x100000-0x107FFF is internal, ≥ 0x108000 external (open bins).
- Every symbol gets `Flash_start_address = Internal_address`.
- Internal ≥ 0x100000 → `Start_address = Internal_address` (its SRAM address).
- A calibration symbol (type ≠ 0xFF, type & 0x22 == 0x02) also gets its flash copy as its Flash_start_address, when 0 < copy + length ≤ 0x100000:
  - closed bin: internal − primary offset;
  - open bin: internal SRAM − secondary offset, external SRAM − primary offset.
- How symbols are told apart:

  | Condition | Meaning |
  |---|---|
  | `Flash_start_address ≥ 0x100000` | SRAM only: needs the ECU |
  | `Start_address ≥ 0x100000` | has an SRAM copy ("live-tuneable") |

  `GetSymbolAddress` (1366) returns 0 for SRAM-only symbols. T7 used the SRAM offset 0xEFFC04 and open software instead.
- **Symbol grid presets** (724): "Only symbols within binary" is active after opening a file, which hides SRAM-only symbols. "Only live-tuneable symbols" (`Length ≠ 0 AND Start_address ≥ 0x100000`) is the other preset. T7 had the first one, off by default.

**Double-click / Enter** (1297).
- `Flash_start_address ≥ 0x100000` → RealtimeCheckAndConnect, then `ShowRealtimeMapFromECU(name)`. Otherwise `StartTableViewer(Auto)`. There's no open-software branch.
- **Bitmask symbols** (`BitMask > 0`) open frmBitmaskViewer (2469).
  - SRAM-only ones read 2 bytes with `readMemoryNew(Flash_start_address, 2, 2)` when the device is open.
  - Errors: "Symbol outside of flash boundary and failed to read symbol from ECU" / "Symbol outside of flash boundary and no connection to ECU available".
  - OK writes nothing back for them: the write to the ECU was never implemented.

**Viewer setup** (`StartTableViewer(ECUMode)`, 2007).
- **Data:** read from the file when the address is below 0x100000, zeros otherwise.
- **When connected:** `IsRAMViewer` is set; `OnlineMode` (blue) only with UseNewMapViewer.
- **Buttons:** "Save to file" / "Read from file" get handlers only when 0 < address < 0x100000. "Save to ECU" / "Read from ECU" get handlers only when `Start_address ≥ 0x100000`. MapViewerEx enables a button only when its handler exists.

**Reading and writing SRAM.**
- **`ReadMapFromSRAM`** (12881):
  - Prohibits reading, caption "Reading map from SRAM", then `readMemoryNew(Start_address, Length, 0x40, true)`.
  - Failure → null, caption "Could not read SRAM". T7 returned a zero buffer.
  - Success → caption "Idle", and the status-bar progress item is hidden until the next SRAM read or write.
- **`WriteMapToSRAM`** (12910):
  - Caption "Writing map to SRAM", then `writeMemoryNew(Start_address, data, 0x40, true)`: KWP `3B 15 <24-bit address> <n>` + data in 0x40-byte chunks, answered `7B`, 3 retries.
  - Failure only sets the caption "Could not write SRAM"; there's no box.
  - There's no branch by symbol number (T7: WriteSymbolToSRAM below 0xF00000).

**Viewer "Read from ECU" / "Save to ECU"** (12943 / 13089).
- Both need an existing realtime connection; they don't connect on demand (T7 did). Messages: "An active CAN bus connection is needed to get data from the ECU" / "... to write data to the ECU".
- Read refreshes every "Symbol: " / "SRAM" viewer of that map, matched by `Varname`. Write refreshes nothing.

**Auto update** (settings "Auto update SRAM viewers every N seconds", 5-60, default 20).
- MapViewerEx's timer runs for viewers with `Map_address ≥ 0x100000` (SRAM-only symbols) while they are unedited.
- It raises Read from ECU, so after a disconnect the box above appears at every interval.

**`ShowRealtimeMapFromECU`** (13804):
```
RealtimeCheckAndConnect
symbol by SmartVarname (its Symbol_number is overwritten with the realtime-list number, which is always its own)
ReadMapFromSRAM; null → stop
StartTableViewer(name); each viewer of that map: Map_content = data, IsRAMViewer, OnlineMode
```
- A symbol the bin lacks does nothing. `StartAViewer` (13792) is never called.

**.RAM files.**
- Actions → "Import SRAM file" (12570; T7: "Import SRAM snapshot"): filter "Snapshots|*.RAM", status "SRAM: <name>".
- Symbol menu "Read from SRAM file" (13126), enabled while that file exists: as T7, reading at `Start_address & 0xFFFF` and wrapping to the file length. When that address is 0 an empty panel opens: the panel is created before the check.
- T8 has no "Compare binary to SRAM snapshot" / "Compare SRAM snapshots" and no "Read symbol from ECU" menu entry.

## Realtime engine and dashboard

**Loop.**
- A WinForms `tmrRealtime`, 30 ms (T7: 1 ms), disabled during its own pass (10770). When connected and not prohibited:
  1. `StallKeepAlive = true`;
  2. the read method;
  3. AutoTune button enabled when a wideband is configured.
- **Read method:**

  | "Prefer dynamic retrieval of live data" (`PreferDynamicLiveData`, default true) | Method |
  |---|---|
  | on | GetSRAMVarsFromTableDynamic; after its first failure GetSRAMVarsFromTableOld until restart |
  | off | GetSRAMVarsFromTableOld |

  - The fallback sets `bypassDynSymbolMode = true` and logs "Forcing readByAddress for the duration of this session".
  - `GetSRAMVarsFromTablePackedSymbols` (11211, merges neighbours within 12 bytes into one read) is never called.
- **"Toggle realtime panel [SHIFT+F1]"** (9647): T7's toggle without alive polling and Performance.Mode. Hiding stops the timer and the serial wideband reader but doesn't disconnect.
- **Rows every method skips** (by name): KnockCyl1-4, KnkCntCyl1-4, "MisfCyl11"-"MisfCyl14", the five ADC channel names, the thermo channel name, "Wideband". The MisfCyl names are a typo: the real MisfCyl1-4 rows aren't skipped.

**Dynamic list** (GetSRAMVarsFromTableDynamic, 10923):
```
if forceDynSymbolRefresh or dynLastCount != list count:
    list = rows not skipped with 1 ≤ Length ≤ 16, SRAMAddress ≥ 0x100000, 0 ≤ ConvertedSymbolnumber < 65536
    empty → refresh again next pass
    else ConfigureDynamicListByAddress(address, size each); false → list cleared, failure
buf = ReadDynamicSymbols(); null or length ≠ Σ sizes → failure ("Could not fetch dynamic data"), the old bytes are reused
dynLastCount = 0
for each row not skipped:
    data = list[dynLastCount] when its name and number match; dynLastCount++
    no data → forceDynSymbolRefresh, "Failed to read SRAM, symbol: ..."
```
- Delay / Reload are ignored: every row is read on every pass.
- **Bug:** dynLastCount counts every row that isn't skipped, not the list entries, so any row that doesn't qualify shifts the matching. That includes SRAM address 0 (flash-only symbols), more than 16 bytes, and MisfCyl1-4.
  - Every later row then gets no value.
  - The list is reconfigured on every pass.
  - This doesn't count as a failure, so there's no fallback.
- **Library side** (Trionic8.cs:3936-4600):
  - Reset with `3B 17 F0 04`, then `3B 17 F0` + `03 00 <size> <24-bit address>` per entry, at most 9 entries per request.
  - 1-99 entries, otherwise the call returns false.
  - Read with `1A 18 F0` → `5A 18` + the data in list order.
  - A comment limits the total to 255 bytes; nothing checks it.
- A refresh is forced by SetCanAdapter and by both DTC actions. The "Symbolnumber (ECU)" is always the bin's number: the realtime address table stays empty (15364).

**By address** (GetSRAMVarsFromTableOld, 11521):
- T7's Delay / Reload.
- Per row `readMemoryNew(SRAMAddress, Length, 0x40)` = KWP `23 <24-bit address> <16-bit length>`, answered `63 <address> data`. Flow control `30 00 00` (not on ELM), 3 retries. SRAMAddress 0 → logged and skipped.
- A failed read keeps the old value and doesn't throw (T7's ≤ 4-byte read aborted the pass).
- `m_prohibitReading` during the pass returns at once, without a log line.
- T7 read by symbol number instead: `ReadValueFromSRAM` up to 4 bytes, `ReadSymbolNumber` above.

**Conversion.** As T7, with T8 names.
- **Signed list:** ActualIn.T_Engine, ActualIn.T_AirInlet, Out.fi_Ignition, Out.M_EngTrqAct, ECMStat.P_Engine, IgnMastProt.fi_Offset, Lambda.LambdaInt, MAF.m_AirInlet, AdpFuelProt.MulFuelAdapt, ECMStat.p_Diff, BoostProt.PFac, BoostProt.IFac, BoostProt.LoadDiff, IgnKnk.fi_MeanKnock, Ign.fi_OtherOff, IgnJerkProt.fi_Offset.
- **Per cylinder:**

  | Symbol | Layout | Rows |
  |---|---|---|
  | `KnkDet.KnockCyl` | 4 × u8 | KnockCyl1-4 |
  | `KnkDetAdap.KnkCntCyl` | 4 × u16 | KnkCntCyl1-4 |
  | `MisfAdap.N_MisfCountCyl` | 4 × u16 | MisfCyl1-4 |

  T7: KnockCyl1-4 from either 8-byte knock symbol, MisfCyl from the 12-byte MissfAdap.MissfCntCyl.
- Power comes from Out.M_EngTrqAct (rpm × torque / 7121).

**Default rows** (FillRealtimeTable, 10648). As T7 except:
- A new first row: ActualIn.U_Battery "Battery voltage", offset 0, correction 0.1, 0..16. It shows in the grid and the log only.
- Renamed: Out.X_AccPos "TPS %", IgnMastProt.fi_Offset "Ignition offset", AirMassMast.m_Request "Requested airmass", Out.M_EngTrqAct "Calculated torque".
- No BFuelProt.CurrentFuelCon ("TODO: Fix for Trionic 8"), so L/100km stays 0.0.

**UI.** T7's panel, tabs, bottom strip, Night/Day and free logging grid, except:
- The nine 3 × 3 dashboard displays all have 0 decimals. T7: km/h, I offset, Duty cycle %, Degrees BTDC 1 decimal; Boost 2.
- Eco / Norm / Sport are hidden, disabled and have no handlers.
- AutoTune shows for open bins and is enabled with a wideband, but has no handler.
- The AFR gauge doesn't toggle on click.
- The status bar adds the ECU software version.
- "Add to realtime list" presets (14546): ActualIn.v_Vehicle2 and In.v_Vehicle 0..255 × 0.1, FFTrqProt.Trq_MaxEngineBefComp and FFTrqProt.Trq_MaxEngine 0..65535 × 0.1; otherwise 0..255 for one byte, 0..65535 above.

**Status texts** (12198 / 12295 / 12373). T8's own tables; an unknown value shows the number.
- **FCut.CutStatus:**
  - 0 No fuelcut, 1 Ignition key turned off, 2 Accelerator pedal pressed during start, 3 RPM limiter (engine speed guard)
  - 4 Throttle block adaption active 1st time, 5 Engine position lost, 6 Airmass limit (pressure guard), 7 Immobilizer code incorrect
  - 9 Starter control relay circuit short to ground, 11 Tampering protection of throttle, 12 Error on all ignition trigger outputs, 13 ECU not correctly programmed
  - 14 Forced fuelcut by user, 15 Transmission requests fuelcut, 16 Kill engine, after engine has started, rpm too low
  - 20 Application conditions for fuel cut -SAAB, 21 Application conditions for fuel cut -OPEL
  - 31-36 Power management fuelcut on one / two / three / four / five / six cylinders
- **Lambda.Status:**
  - 0 Closed loop activated, 1 Closed loop not activated, 2 Load too low
  - 3 Fuel enrichment in progress (no knock), 4 Fuel enrichment in progress (knock)
  - 5 CW temp too low, closed throttle, 6 CW temp too low, open throttle, 7 Engine speed too low
  - 8 Negative throttle transient in progress, 9 Positive throttle transient in progress, 10 Fuel cut, 11 Throttle in limp home
  - 12 Diagnostic failure that affects the lambda control, 13 Engine not started
  - 14 Waiting number of combustion before hardware check, 15 Waiting until engine probe is warm, 16 Waiting until number of combustions have past after probe is warm
  - 17 Hot soak in progress, 18 SAI: Number of combustion to start closed loop has not passed, 19 Lambda integrator is frozen to 0 by SAI lean clamp
  - 20 Catalyst diagnose for V6 controls the fuel, 21 Lambda start not finished, 22 Lambda probe diagnose request open loop
- **ECMStat.ST_ActiveAirDem:**
  - 10 PedalMap, 11 Cruise control, 12 Idle control
  - 20 Max engine torque, 21 Traction control, 22 Manual gearbox limit, 23 Automatic gearbox limit, 24 Stall limit (Aut), 25 Special mode, 26 Reverse limit, 27 Max vehicle speed, 28 Brake management, 29 System action
  - 30 Max engine speed, 31 Max vehicle speed, 40 Min load, 41 Min load
  - 50 Knock airmass limit, 51 Max engine speed, 52 Max turbo speed, 53 Max turbo speed, 54 Crankcase vent error, 55 Faulty APC
  - 61 Engine tipin limit, 62 Engine tipout limit

**Persistence.**
- Same format and rules as T7.
- Layout files are `.t8rtl` ("Realtime layout files|*.t8rtl").
- `rtsymbols.txt` lives in `%APPDATA%\MattiasC\T8SuitePro\`, the parent of UserAppDataPath.

**Combi ADC / thermo.** Same as T7.

**Notifications.** Same as T7, except:
- The symbol list offers symbols with `Start_address ≥ 0x100000` (T7: > 0xF00000).
- Slot 1 defaults to `knock.wav`. T8 ships only knock.wav (no ping.wav or autotune.wav).

**Symbol colours.** Same as T7, under the T8SuitePro registry key.

**Live cell tracking** (UpdateOpenViewers, 11850). Same mechanism as T7 (StartsWith match, nearest breakpoint of the axis read from the file), with T8 maps:

| Map | X axis ← value | Y axis ← value |
|---|---|---|
| IgnAbsCal.fi_NormalMAP, fi_lowOctanMAP, fi_highOctanMAP | IgnAbsCal.m_AirNormXSP ← airmass | IgnAbsCal.n_EngNormYSP ← rpm |
| KnkFuelCal.fi_MaxOffsetMap | KnkFuelCal.m_AirXSP ← airmass | BstKnkCal.n_EngYSP ← rpm |
| IgnKnkCal.IndexMap | IgnKnkCal.m_AirXSP ← airmass | IgnKnkCal.n_EngYSP ← rpm |
| BFuelCal.LambdaOneFacMap | BFuelCal.AirXSP ← airmass | BFuelCal.RpmYSP ← rpm |
| PedalMapCal.Trq_RequestMap | PedalMapCal.n_EngineMap ← rpm | PedalMapCal.X_PedalMap ← TPS (axis × 0.1) |
| TrqMastCal.m_AirTorqMap | TrqMastCal.Trq_EngXSP ← torque | TrqMastCal.n_EngineYSP ← rpm |
| TrqMastCal.Trq_NominalMap | TrqMastCal.m_AirXSP ← airmass | TrqMastCal.n_EngineYSP ← rpm |
| AirCrtlCal.RegMap | AirCrtlCal.SetLoadXSP ← airmass | AirCrtlCal.n_EngYSP ← rpm |
| BstKnkCal.MaxAirmass, MaxAirmassAu | BstKnkCal.OffsetXSP, else fi_offsetXSP ← ignition offset (axis × 0.1) | BstKnkCal.n_EngYSP ← rpm |

The boost map row is misspelt: the map is AirCtrlCal.RegMap, so it never highlights.

## Realtime logging and log viewer

**Writing `.t8l`** (LogRealTimeInformation, 11975). As T7, except:
- The file is `<bin dir>/<bin base>-yyyyMMdd-CanTraceExt.t8l`.
- The timestamp is `DateTime.Now` when the line is written: the end of the pass, after the ADC and wideband reads. T7 used the start of the pass.

**Reading, exports, filters, viewer.** The same shared code: CommonSuite LogFile / DifGenerator / CSVGenerator / LogFilters and RealtimeGraphControl. Differences:
- Every open dialog uses "Trionic 8 logfiles|*.t8l"; title "Open CAN bus logfile".
- **LogWorks** (9707, 9763): `WidebandSymbol = ""` (T7 passed the setting). The wideband symbol therefore gets no "WB Lambda" unit, no 7..23 range and no AD_Scanner conversion in the DIF.
- **CSV** (15155): same as T7.
- **Log viewer** ("Load trionic 8 logfile" → btnViewLogFile, 13391; dock "CANBus logfile: <file>"):
  - Short names and fixed ranges are keyed to T5 / T7 names.
  - T8-only channels (IgnMastProt.fi_Offset, AirMassMast.m_Request, Out.M_EngTrqAct, Out.X_AccPos, ActualIn.U_Battery) keep their raw names and are autoscaled.
- **Log filters:** same as T7.

**Write log marker [F6].** Same as T7.

**View matrix from logfile** (13407). T7's algorithm. The T8 copies of frmMatrixSelection / frmMatrixResult behave the same. Differences:
- `*.t8l` files.
- Always the new map viewer.
- Modal (`ShowDialog`; T7 used `Show`).
- The Maximum view is titled " (Mean values)": the title check tests type 1 twice.

## Tuning in realtime

Two buttons, both through `ShowRealtimeMapFromECU` (13904 / 13909):
- "View knock count map" → `KnkDetAdap.KnkCntMAP`.
- "View misfire tab" → `MisfAdap.N_MisfCountCyl`: the 4 × u16 per-cylinder counters, not a map.

The viewer is "Symbol: <name> [<bin>]", filled from SRAM as above; a symbol the bin lacks does nothing. T7's false / real knock maps, misfire map, Set ethanol content, Synchronize and tuning packages to or from the ECU don't exist in T8.

## Wideband, AFR maps and autotune

- **Serial wideband** ("Use wideband O2 on com port", device, port): same as T7.
- **Wideband through an ECU symbol:** the code is T7's (FillRealtimeTable 10718, dashboard 342, ConvertToWidebandAFR 417). The settings dialog has none of its options: "Use wideband O2 (pin 16) with symbol", the voltage / AFR configuration, "Measure AFR in lambda". They only come from the registry, with the defaults UseWidebandLambda false, WideBandSymbol "DisplProt.AD_Scanner", MeasureAFRInLambda false.
- **AFR maps and autotune:** none.
  - The LogWidebandAFR / ProcessAutoTuning calls are commented "FIXME".
  - There are no AFR map buttons and no "Import AFR feedback data".
  - There are no autotune or autologging settings, and the AutoTune button has no handler.

## Settings (ECU part)

- **Removed from T7's realtime group:** "Use wideband O2 (pin 16) with symbol" with its Configuration, "Measure AFR in lambda", "Autotune settings", "Autologging settings".
- **Added:**
  - "Use Legion Bootloader" (default true);
  - "Prefer dynamic retrieval of live data" (default true, tooltip "Massive boost to sample rate if the binary is compatible").
- **Unchanged:** the rest, including SLCAN in the adapter type list without a SetCanAdapter branch, so it sets nothing up and SetSelectedAdapter can hit a null device.

## TrionicCANLib calls

Every T8Suite call matches the current library's signatures:
- **Session:** `isOpen`, `openDevice(bool)`, `Cleanup`, `setCANDevice`, `SetSelectedAdapter`, `OnlyPBus`, `ForcedBaudrate`, `SecurityLevel`, `StallKeepAlive`, `NeedRecovery`.
- **Flash:** `ReadFlash`, `ReadFlashLegT8`, `WriteFlash`, `WriteFlashLegT8`, `RecoverECU_Def`, `RecoverECU_Leg` (object, DoWorkEventArgs).
- **Memory:** `readMemoryNew(int, int, int, bool)`, `writeMemoryNew(int, byte[], int, bool)`, `ConfigureDynamicListByAddress(List<dynAddrHelper>)`, `ReadDynamicSymbols()`.
- **Info and DTCs:** `RequestECUInfoAsString(uint)`, `GetInt64FromIdAsString(uint)`, the string `Get*` calls, `GetTopSpeed()`, `ReadDTC()`, `ClearDTCCodes()`.
- **Combi and settings:** `GetADCValue(uint)`, `GetThermoValue()`, `ITrionic.GetAdapterNames`.
- **Events and helpers:** the three events with their EventArgs, `ChecksumT8`, `FileT8`.

The public Trionic8 / ITrionic API is unchanged since before the Avalonia flasher port, except that `ResetECU` is now public. What changed underneath T8Suite:
- `dynAddrHelper` has been declared in `ITrionic` since 2022. `Trionic8.dynAddrHelper` still resolves through inheritance.
- **Prompts:** questions go through `UserPrompt.YesNo` / `UserPrompt.Notify`, which the app must install. Unset, Legion's boot / NVDM questions answer No and "Boot is broken!!" is silent.
- **Flash results:** failed flash operations set `Result = false` since d0d1c18. The old WriteFlash reported true after a failed write, which T8Suite showed as "Flash sequence done".
- `ReadFlash` still returns without a Result when security access is refused (Trionic8.cs:6429).
- ReadDataByIdentifier now gives up after 5 s of silence. It used to wait forever on a silent ECU.
- `ReadSRAMSnapshot()` is gone; `GetSRAMSnapshot` replaces it. Only the handler removed in 2017 used it.
- Recovery needs `SetCANFilterIds(Trionic8.FilterIdRecovery)`, because setCANDevice filters to the ECU ids. T8Suite never sets it.

## Dead or unreachable (don't port)

- Form1 5669-7083 (1415 lines): "Tune me up™", `TuneToStageNew`, "Easy tune to stage III", `TuneToStage` and their helpers; only the 5-line Tuning Wizard handler (6573) lives there.
- The Debug group (Form1 7343-7440, 7442-7550, 7556-7681, 14298-14540, about 470 lines), behind a registry-only flag.
- `GetMapCorrectionFactor`'s "Resolution is" parsing and hard-coded list (1520-1566, overwritten); Form1's copies of the symbol-table helpers (755-1255): `GetAddrTableOffsetBySymbolTable` and its helpers only serve Copy address table, `GetStartOfAddressTableOffset` / `GetLastNqStringFromOffset` / `GetNqNqNqStringFromOffset` are unused; `DisposeTableViewers` (1979, no caller); `ImportCSVDescriptor(string)` (14179, no caller); the duplicate-axis locals at 1353, 2075, 4231.
- `crc64.cs` (168 lines, unreferenced); `SymbolDictionary.GetSymbolX/YAxisFunction`, `returnOldSoftware`, the `<name>.<source>` keys; `T8Header.UpdatePIarea` (DEBUG only, broken), `ReadContainer`, `GetInfoBlockAddress`; FlashBlock `VerifyChecksums` / `DumpVariables`; the firmware dialog's overboost checkbox.
- The airmass viewer's ambient pressure box; MapViewerEx's OnlineMode-false branch; `HexViewer.onClose`; the PID Control flag and secondary table; the map helper's fade timer.
- Settings stored but unused: "Show mapviewers in seperate windows", "Show table upside down", `Showpopupmap`.
- In practice: the TEM editor (no stock bin has a TEM table); the shipped BOM pack that never verifies.
