# T7Suite behaviour reference

How the old T7Suite (WinForms/DevExpress, `T7Suite/`) behaves, read from its code, as the reference for the port. Line numbers are `T7Suite/frmMain.cs` unless another file is named. Where the port deliberately differs, PORTING.md says so.

## Opening a binary

**Entry points.** Every entry point ends in `OpenFile(path, showmessage)` (5815).
- Ribbon "Open file" (504): filter `binary files|*.bin|Motorola S19 files|*.S19`. Sequence: `CloseProject(); OpenFile(f, true)`.
- MRU lookup (812): the same, with `showmessage=false`.
- Command line: a `.BIN` path given to the ctor is opened in `frmMain_Load` (5747); `.S19` is ignored there.
- Auto-load at startup (5757), only when there is no command line and `AutoLoadLastFile` is set: `LastOpenedType==0` opens `Lastfilename`, otherwise `OpenProject(Lastprojectname)`.
- Other callers: Save As, part number lookup, `LoadBinaryForProject`, and Read-ECU.
- There is no drag and drop. With a file already open there is no prompt and no dirty check. `CloseProject` (1561) clears the grid and the title, and leaves open viewers open.

**`OpenFile`**:
```
if .S19: Srecord.ConvertSrecToBin(f, 0x80000, out bin, true); on failure "Failed to convert S19 file to binary"
m_currentfile = Lastfilename = f
if ValidateFile (exists, 0x80000 bytes, starts FF FF EF FC):
    TryToOpenFileUsingClass (5197):
        header.init(f, AutoFixFooter) → software version, SRAM offset
        AddFileToMRUList
        Trionic7File.ExtractFile(f, ApplicationLanguage, swversion)
        SRAM offset = header's, else the one ExtractFile found
        IsSoftwareOpen → status "Open/dev binary" (AutoTune button shown) / "Normal binary"
        BioPower bin (TorqueCal.M_EngMaxE85Tab present): BFuelCal.StartMap renamed BFuelCal.E85Map   [never fired: scanned the previous file's symbols]
    symbols sorted by Length descending into the grid; title "T7SuitePro v<ver> [ <file> ]"
else: empty grid, title "[ none ]", "File is not a Trionic 7 binary file!"
AutoCreateAFRMaps: AFR maps sized from BFuelCal.Map
btnCompareToOriginal enabled when <StartupPath>\Binaries\<partnumber>.bin exists
LoadRealtimeTable(rtsymbols.txt); DynamicTuningMenu()
```
- The checksum is not verified on open. It is verified by the Verify button, and by backup/project/export actions with `ShouldUpdateChecksum` ("Checksums did not verify ok, do you want to recalculate and update the checksums?").

**ExtractFile.**
- Symbol XML lookup order:
  1. `<dir>\<name>.xml`
  2. `<StartupPath>\repository\<name><creation yyyyMMddHHmmss><partno><swver>.xml`
  3. the first `<dir>\<name>*.xml`
- If nothing is found, EU0AF01C/EU0BF01C/EU0CF01C bins offer EU0AF01C.xml, and EU09F01C bins offer EU09F01C.xml.
- `<dir>\<name>.xml` is always rewritten afterwards.

**MRU.** Stored in `HKCU\Software\T7SuitePro\MRUList` as file name → full path. Entries are appended if new, with no limit and no reordering. The list is saved on exit.

**DynamicTuningMenu** (18644) adapts the ribbon's quick map buttons to the bin:
- BioPower: "Petrol VE Map" / "E85 VE Map", plus the E85 torque and ignition buttons.
- B308: VE map2 / ignition map2.
- Boost control: shown when BoostCal.RegMap is present.
- Cab gear torque: TorqueCal.M_CabGearLim.
- Gas maps: IgnNormCal.GasMap, MyrtilosCal.Fuel_GasMap or BFuelCal.GasMap.

**Projects** (`OpenProject`, 1235): open the project log, then `OpenFile(BINFILE)`, then the transaction log (purge dialog above 2000 entries), enable the project buttons, create a backup. The title becomes "T7SuitePro [Project: name]".

## Symbol list

**Columns.**

| Caption | Field | Notes |
|---|---|---|
| Symbol name | `Varname` | |
| Address | `Flash_start_address` | X6 with ShowAddressesInHex, else decimal |
| Length | `Length` | X6 with ShowAddressesInHex, else decimal |
| Description | `Description` | |
| User description | `Userdescription` | the only editable column; saves `<bin>.xml` |

**Hidden columns:** Category (grouped), Number, SRAM address, Symbolnumber ECU.

**Grouping and display.**
- Grouped by Category, ascending, with a count per group. Inside a group rows stay in source order (length descending).
- Find panel: incremental, searches every column.
- A preview line shows Description under each row.
- The "Only symbols within binary" filter exists but is off by default.

**Name colours** (first match wins):

| Prefix | Colour |
|---|---|
| TorqueCal. | Orange |
| BoostCal. | OrangeRed |
| BFuelCal., Inj, FCutCal., FCompCal. | LightSteelBlue |
| Ign, DI | LightGreen |
| BstKnkCal. | LightGray |
| Knk | Plum |
| MAFCal. | Yellow |
| Cruise | SandyBrown |
| Evap | Orchid |
| Idle | BurlyWood |
| Lambda, O2 | Goldenrod |
| Missf | Bisque |
| Purge | Khaki |
| SAI | GreenYellow |
| StartCal. | SeaGreen |

**Double-click / Enter:**
- Open software: an SRAM viewer if connected, otherwise an offline viewer.
- Otherwise, address > 0x80000: needs the ECU.
- Otherwise: `StartTableViewer(Auto)`.
- `HideSymbolTable` hides the list afterwards.

**Context menu:** Read symbol from ECU / binary, Add to realtime list, Add to MyMaps, Read from SRAM file, Browse axis info, Export as tuning package, Export fixed tuning package, Export symbollist as CSV.

**Shortcuts:**

| Key | Action |
|---|---|
| Ctrl+F | fullscreen |
| Shift+F1 | realtime panel |
| F3 | panel fullscreen |
| F6 | log marker |
| F9 | screenshot |

There is no Ctrl+O.

**My Maps** (mymaps.xml, loaded once at startup): a ribbon page with one group per category and one button per map, which opens `StartTableViewer(symbol)`.

## Map viewers

**Hosting** (`StartTableViewer(ECUMode)`, 6666):
- One DevExpress DockPanel per map, titled `Symbol: <name> [<file>]` with Tag = file. An existing panel with that title and file is shown again instead of a new one.
- Placement and size: docked right, or floating (NewPanelsFloating), width `30 + (xAxisLen+1)·35` clamped per view size.
- Tabbing: tabbed onto the same symbol (AutoDockSameSymbol) or the same file (AutoDockSameFile).
- Viewers of a previous file stay open.

**Viewer setup:**

| Property | Source |
|---|---|
| X/Y axis values | `GetX/YaxisValues`: the axis symbol as 16-bit BE × the axis symbol's correction factor, truncated |
| Y sign | values > 0x8000 negative (after the factor) |
| X sign | > 32000 negative only for BstKnkCal.MaxAirmass(Au) |
| Fixed Y axes | gear maps and BoostMeter |
| Width | `GetTableMatrixWitdhByName` |
| 16-bit | `isSixteenBitTable` |
| Factor | `GetMapCorrectionFactor` (see below) |
| Offset | 0 |
| UpsideDown | always |
| Open-loop limits | LambdaCal.MaxLoadNormTab, or MaxLoadE85Tab for the E85 maps; drawn on mg/c × rpm maps per StandardFill |

`GetMapCorrectionFactor` reads "Resolution is X" from the help text, else 1. The hard-coded fallbacks only apply when that text parses to 0.

**Data address:** below 0xF00000 the data is read from the file. Above it, only in open software, at address − SRAM offset (default 0xEFFC04).

### MapViewerEx

**Cells:**
- 16-bit cells are big-endian. Values 0xF001–0xFFFF read as negative. 8-bit cells are unsigned.
- Cells hold raw integers in every view. Easy view is display only: `(float)raw*factor+offset`, formatted F2. T5 names: `Ign_map_0!`/`Ign_map_4!` use F1 plus "°", `Reg_kon_mat` uses F0 plus "%".
- Hex view: X4 / X2.

**Editing:**
- The editor starts in Easy view from the value with F2.
- Hex input is parsed as hex. Easy input becomes `(v-offset)/factor`, rounded half to even. Other views take an integer.
- Out of range: "Value not valid...".

**Colours:**
- `b = raw*255/MaxValueInTable` (integer division).
- Normal: (b, 255−b, 0). Red-white: alpha red. Online: b/2 as a white→red tint. DisableColors: none.
- Open-loop marks are drawn even with DisableColors: StandardFill 1 is a black box, 2 a SeaGreen corner triangle.
- Live cell: yellow.

**Keys:** the + key adds 1, PgUp/PgDn ±10 (±0x10 in hex), Home sets max, End sets 0, on every selected cell.

**Math toolbar** (operation combo plus value, default "2"):
- Hex and Decimal views: `v ± (int)Round(w)`, `v * (int)Round(w)`, `v / (int)Round(w)`, fill `(int)w`.
- Easy view: on `raw*f+o`, the result is converted back and truncated.
- Clamped to [0, 255] for 8-bit and ≤ 0xFFFF for 16-bit.

**Smooth selection** (more than 2 cells, refused in hex view):
- A row or column is interpolated linearly with an integer step.
- A block replaces each interior cell, in place, with `((L+R)/2 + (U+D)/2)/2`.

**Select by value:** type values in the view combo and press Enter. Selects cells whose `raw*f+o` is within 0.009 of a value.

**Clipboard** (context menu only):
- Copy: the view-type digit, then `col:row:raw:~` for each cell. Rows are display rows.
- Paste at the original position, or shifted so the first entry lands on the first selected cell.

**Save, read and close:**
- Save to file: `onSymbolSave(address, length, bytes)`.
- Read from file reloads.
- Close asks "Data was mutated, do you want to save these changes in you binary?" (Yes/No/Cancel).
- Save/Read ECU go through SRAM.
- AutoUpdateIfSRAM polls every AutoUpdateInterval seconds.

**2D graph:** the slider's column, over the data rows, plotted against the Y axis.

**3D palette:** Green, Yellow, Orange, OrangeRed, Red. Online: Wheat, LightBlue, SteelBlue, Blue, DarkBlue. Original overlay in YellowGreen at 50%, compare overlay in BlueViolet.

**Sync:** `onSelectionChanged` (exactly one cell selected) and the surface view are mirrored to other viewers of the same map name.

**Compare viewer:**
- "Symbol difference" shows `|a−b|` per value. It is read-only, with no math, paste, smooth or save.
- `Map_original_content` / `Map_compare_content` drive the 3D overlays.

## Firmware information

**Entry point:** the ribbon "Firmware information" button (`Information_firmwareInformation_ItemClick`, 4686). It only acts when the file exists. Opening re-reads the header with `init(file, AutoFixFooter)`, which can already repair the footer.

**Fields:**

| Label | Source | Editable / setter |
|---|---|---|
| Engine type | `getCarDescription` | `setCarDescription` |
| Software version | `getSoftwareVersion` | `setSoftwareVersion` |
| Partnumber | `getPartNumber` | read-only |
| Immobilizer code | `getImmobilizerID` | `setImmobilizerID` |
| Chassis ID | `getChassisID` | `setChassisID`; its button opens the VIN decoder |
| Original cartype / enginetype | `PartNumberConverter.GetECUInfo(partnumber)` | read-only |
| Programming date | 6 bytes at 0x7FD00 (D M Y−2000 h m s; 0xFF = none → shown as now) | only with WriteTimestampInBinary |
| SID date | `getSIDDate` | `setSIDDate` |

Text fields are padded to the original length. There are also Import / Undo buttons, which take the VIN and immobilizer code from another bin.

**Read-only flags:**
- **Checksum enabled:** false if any of MapChkCal/ROM339ChksmCal/MapChk.ST_Enable is 0, or if ROMChecksum.BottomOffFlash equals TopOffFlash.
- **Compressed symboltable:** the 0x9B footer marker.
- **No symboltable present:** more than 10 "Symbolnumber " names.

**Options** (checkbox: enabled when / state detected):
- **Open SID info:** always / the first "Yes" (closed) or "No."/"No\0" (open) in the file.
- **Torque limiters:** TorqueCal.ST_Loop exists / byte ≠ 0 (missing = on).
- **Catalyst lightoff:** any IgnLOffCal.* / not (IdleCal.ST_EnableLOffRpm = 0 and IgnLOffCal.ST_Enable = 0).
- **Second lambda:** LambdaCal.ST_AdapEnable exists / ST_AdapEnable ≠ 0 and O2HeatPostCal.I_LowLim ≠ 00 00.
- **OBDII:** OBD2Enabled or EOBD/LOBDEnabled exists / OBD2Enabled ≠ 0, else EOBD or LOBD ≠ 0.
- **Biopower:** never editable / E85Cal.ST_Enable ≠ 0.
- **Fast throttle:** any EngTipCal.* / Tipin = 0 and Tipou = 0.
- **Extra fast throttle:** any EngTipCal.* / Tipin, ActG2 and Tipou all 0. Mutually exclusive with fast throttle.
- **No TCS:** BioPower bin / E85Cal.ST_EthanolSensor = 0 or missing.
- **Disable emission limiting:** EU0AF01C with the byte at 0x13837 ∈ {02, 03} / byte = 03.
- **Start screen / adaption messages:**
  - EU0AF01C.55P, EU0AF01C.46T, ET03F01C.46S: words at 0x4968E, 0x496B4 and 0x49760, each 0000 or 0080; disabled = 0000.
  - ET02U01C*: bytes at 0x46F4D and 0x4701F, each 00 or 80; disabled = 00.

**OK, in this order:**
1. **TIS file** (no 0x90/0x92 footer fields) with a changed VIN or immobilizer code and no AutoFixFooter: offer to fix the footer. The fix makes a `.binarybackup` copy, then runs `init(file, true)`.
2. **Header setters** (in memory).
3. **Programming date:** written to 0x7FD00 if changed.
4. **Open SID:** write "No." or "Yes" at the indicator.
5. **ST_Loop:** 02 or 00.
6. **OBD:**
   - OBD2Enabled = 1 or 0.
   - If that symbol is missing, ask "Do you want to set the European OBD2 tests active?..." and set EOBD (Yes) or LOBD (No) to 1.
   - Disabling sets both to 0.
7. **Second lambda:** ST_AdapEnable = 1 and I_LowLim = 00 E6, or both 0.
8. **Tip in/out**, by transition:
   - F && !fast → Tipin = Tipou = 0, ActG2 = 1.
   - !F && fast → all 1.
   - E && !extra → all 0.
   - !E && !F && extra → all 1.
   - !E && F && extra → ActG2 = 1.
9. **Light-off:** IdleCal.ST_EnableLOffRpm and IgnLOffCal.ST_Enable = 1 or 0.
10. **BioPower:** E85Cal.ST_EthanolSensor = 1 or 0.
11. **`header.save(file)`.**
12. **SID patches** (no transaction entry):
    - EU0AF01C/46S: 0x4968E and 0x496B4 = 0000 (start screen disabled) or 0080; 0x49760 = 0000 or 0080.
    - ET02U01C: 0x46F4D and 0x4701F = 00 or 80.
13. **EU0AF01C emission byte:** 0x13837 = 03 if checked, else 02, unconditionally.
14. **`ChecksumT7.UpdateChecksum`.**

Every symbol write goes through `savedatatobinary`. It only writes 0 < address < 0x80000, adds a transaction entry when a project is open, and updates the checksum.

**Quirks:**
- Footer strings must keep their length: a shorter value makes `save()` stop half way.
- An unstamped file always gets a timestamp on OK.
- On a TIS file without the footer fix, VIN and immobilizer changes are lost.

**VIN decoder** (frmDecodeVIN, display only):
- Shows car model, engine type, turbo, makeyear, assembly plant, series, body and gearbox.
- Checksum line: "Valid", "WRONG! Expected: X but found: Y", or "Not verified".
