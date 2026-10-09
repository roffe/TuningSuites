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

## Projects

**Settings and ribbon.**
- Settings: `ProjectFolder` (default MyDocuments/TxSuite/Projects), `RequestProjectNotes`, `LastOpenedType` (0 file, 1 project), `Lastprojectname` (registry name "LastProjectname"), `AutoLoadLastFile`.
- Ribbon "Projects": Create, Open, Close, Show transaction log, Roll back/undo, Roll forward/redo, Rebuild file, Edit project, Add note to project log, Show project logbook, Produce latest binary. Everything except Create and Open starts disabled.

**Disk layout** (P = ProjectFolder, N = project dir):
```
P/N/projectproperties.xml      DataTable "T5PROJECT" in <DocumentElement>, one row, string columns
                               CARMAKE CARMODEL CARMY CARVIN NAME BINFILE VERSION (BINFILE = absolute path of the copy)
P/N/<bin file name>            the working binary, copied in at create
P/N/TransActionLogV2.ttl       (a V1 TransActionLog.ttl is migrated, then deleted)
P/N/TransActionLogV2-yyyyMMddHHmmss.btl   copy made on purge
P/N/ProjectLogbook.log
P/N/Backups/<binbase>-backup-MMddyyyyHHmmss.BIN
P/N/Snapshots/SnapshotMMddyyyyHHmmss.RAM
P/Nrebuild.bin                 rebuild temp file (no separator: next to the project folder)
```
`MakeDirName` strips `\ / : * ? > < |`.

**Create.**
- frmProjectProperties, "Trionic project properties":
  - Car make (default "SAAB"), Car model, Car MY, Car VIN.
  - Project name, Version (default "1.00.000"), Binary file (*.bin).
- Prefilled from the open file: binary = current file, car model = car description, project name = "<partnumber> <software version>".
- On OK:
  1. mkdir P.
  2. If P/MakeDirName(name) already exists: "The chosen projectname already exists, please choose another one" and stop.
  3. mkdir P/MakeDirName(name) and copy the binary in.
  4. Write the xml.
  5. `OpenProject(name)`, with the raw name: stripped characters make it miss.

**OpenProject(name):**
1. Return if P/name is missing.
2. `LastOpenedType=1`, open the logbook, `OpenFile(BINFILE)`.
3. Open the transaction log (an empty one is created if missing).
4. More than 2000 entries offers a purge. The dialog says "reduce to 500"; the code keeps 1000.
5. Enable the project buttons, `CreateProjectBackupFile`, `UpdateRollbackForwardControls`.
6. Lastprojectname = name; title "T7SuitePro [Project: name]".

There is no pending-changes check, and a previous project isn't closed first.

**Open dialog.**
- Lists every P/* with a projectproperties.xml: Projectname, NumberBackups (count of Backups/*.bin), NumberTransactions, DateTimeModified (the BINFILE's LastAccessTime), Version.
- No projects: "No projects were found, please create one first!".
- frmProjectSelection: "Select a project to open".

**Close.**
- Clears the current file, the symbol grid, the status ("No file"), Lastfilename and the project buttons; title "T7SuitePro".
- The Close button also clears Lastprojectname. FormClosing keeps it.
- Bug: the transaction log object stays, so later plain-file writes still append to the old project's log.
- Opening a plain file always closes the project first.

**Edit.**
- The dialog is filled from the xml.
- A rename moves the folder (raw name), and BINFILE is re-pointed into the moved folder before the project reopens, which makes a new backup.
- Writes the xml and logs PropertiesEdited (info: Version).
- Without a rename, a changed binary path is only stored.

**Produce latest binary:** save dialog, then copy the current file there.

## Transaction log

**When entries are added.**
- `savedatatobinary(..., true)` adds an entry when a project log exists: Now, address, length, before, after, not rolled back, note.
- It then runs SignalTransactionLogChanged:
  - updates the rollback/forward buttons;
  - writes logbook TransactionExecuted with "<symbol at address, or the address in decimal> <note>".
- Map saves ask "Remark for change" (frmChangeNote) first when RequestProjectNotes is set and a project is open.

**Buttons.**
- Show log: enabled when there are entries.
- Rollback: enabled when any entry is not rolled back.
- Rollforward: enabled when any entry is rolled back.

**RollBack / RollForward(e):**
1. Address wrapped to the file size.
2. Write DataBefore (rollback) or DataAfter (forward) without a new entry.
3. `VerifyChecksum(false)`, which offers to fix the checksum with AutoChecksum.
4. Set the flag.
5. Logbook TransactionRolledback/Rolledforward: "<symbol> <note> <number>".

- Any entry can be toggled on its own, with no ordering check.
- The ribbon's Roll back takes the last entry that is not rolled back; Roll forward takes the first one that is.
- Open viewers are not refreshed.

**frmTransactionLog** ("Transaction log", modeless).
- Columns:

  | Column | Format | Editable |
  |---|---|---|
  | Timestamp | dd/MM/yyyy HH:mm:ss, sorted descending | no |
  | Symbol | | no |
  | Note | | yes (SetEntryNote, whole file rewritten) |
  | Address | decimal | no |
  | Length | | no |
  | Rolled back | | no |

- Roll back / Roll forward act on the focused row (buttons and context menu). Details shows "Still needs to be implemented".

**Rebuild file** (frmRebuildFileParameters: "Rebuild upto" date, "Store result as current project file", checked by default):
1. Source = the newest backup with LastAccessTime ≤ the date, else the current file.
2. Copy it to the temp file and make a backup.
3. Apply DataAfter of every entry with source time ≤ entry time ≤ date (rolled back or not).
4. Then either replace the current file, or save the result elsewhere.
5. Logbook ProjectFileRecreated.

There is no checksum update of the result.

**Add note:** logbook Note only, nothing in the transaction file.

**Format and purge.**
- TTL V2 format: see `T7Core/Common/TrionicTransactionLog.cs` and `T7CoreTest/TransactionLogTest.cs`.
- Purge: copy to .btl, keep the last 1000 entries, renumber from 0.

## Project logbook

**Line format:** `ddMMyyyyHHmmss|<type text>|<info, '|' replaced by ' '>`.

| Type | Text |
|---|---|
| Note | A project note was inserted |
| TransactionExecuted | A transaction was executed |
| TransactionRolledback | A transaction was rolled back |
| TransactionRolledforward | A transaction rolled forward |
| BackupfileCreated | A backup file was created (info: backup path) |
| ProjectFileRecreated | A project file was recreated |
| PropertiesEdited | Project properties were edited |

**frmProjectLogbook:** columns Timestamp (sorted descending), Type and Description. Bad lines are skipped.

**Backups.**
- CreateProjectBackupFile runs on every project open and at the start of a rebuild. There is no overwrite, so two backups in the same second throw. There is no retention.
- File > Create backup file verifies the checksum first:
  - with a project open: the same as above;
  - without one: `<dir>/<base>yyyyMMddHHmmss.binarybackup`, then "Backup created: <path>".

## Compare

**Entry points.**
- "Compare symbols with other binary": pick a file.
- "Compare to original file": `StartupPath\Binaries\<partnumber>.bin`. It is enabled when that file exists, and runs only if exactly one matches.
- "Compare binary with other binary" (frmBinCompare): a raw 16-byte line diff of the two files, without symbols.

**CompareToFile.**
- The other file is parsed like an open, with side effects in T7Suite: MRU entry, status reset.
- Its SRAM offset: the header's, else ExtractFile's, else 0xEFFC04. Its calibration symbols are mapped back like open software.
- Name used everywhere: `Varname`, or `Userdescription` when Varname starts with "Symbolnumber". Matching is by that name only.

**Pass 1, differing symbols.**
- For each compare-file symbol, take the first current symbol with the same name, skipping SymbolNames and LocalID. Both addresses must be inside the file.
- Compare current bytes (address and length by SmartVarname) with the other file's bytes.
- Different lengths: listed, with stats 0.
- Otherwise:
  - abs = number of differing bytes (halved, integer division, for 16-bit tables)
  - perc = abs*100/length in bytes, so a fully different 16-bit map shows 50
  - avg = mean(current bytes) − mean(other bytes)
- Row fields:
  - SYMBOLNAME = the raw compare Varname
  - addresses and lengths
  - Description = help text
  - CATEGORYNAME = prefix before the first '.'
  - SymbolNumber1 / SymbolNumber2 (current / compare)
  - Userdescription

**Passes 2 and 3.**
- Pass 2: calibration symbols only in the compare file go under "Missing in original" (MissingInOriFile).
- Pass 3: calibration symbols only in the current file go under "Missing in compare" (MissingInCompareFile).
- Calibration: the name contains `Cal.` or `Cal1.`–`Cal4.`, or starts with `X_Acc` or `DisplAdap.`.

**CompareResults grid.**
- Panel "Compare results: <file>", docked left, width 700.
- Visible columns: Symbol, Description, Length (bytes), Percentage of values different (F1), Number of values different, Average difference (F1), Symbolnumber #1, Symbolnumber #2, User description, Missing in original file, Missing in compare file.
- Grouped by category, with a count.
- Rows coloured Salmon (missing in original) or CornflowerBlue (missing in compare).
- AutoFilterRow; X6 in hex mode.

**Double-click / Enter:**
- If the symbol exists in the current file, open its normal viewer (StartTableViewer(name, number1)).
- Plus a compare viewer: the compare file's data with its own axes, read-only (IsCompareViewer), titled "Symbol: <name> [<compare file>]".

**Context menu.**
- **Show differences map:**
  - Content = |compare − current| per value (unsigned 16-bit or byte); lengths must match, else "Map lengths don't match...".
  - Axes from the current file, factor applied (no offset).
  - The 3D view can overlay both files' surfaces.
  - Title "Symbol difference: <name> [<compare file>]".
- **Export to Excel:** writes diffexport.xls.
- **Export as tuning package:** ori or compared file, all rows; .t7p via PackageExporter.

**Transfer maps** (wizard "Transfer maps to different binary wizard"):
1. Offered: symbols in the file whose name contains '.', excluding MapChkCal.ST_Enable, SymbolNames and LocalID. A checked list, with the last selection remembered in the TransferSettings registry key.
2. Back up the target to `<name><yyyyMMddHHmmss>beforetransferringmaps.bin`.
3. Target symbols are matched by name (Varname/Userdescription cross-matching), address inside the file, length < 0x1000.
4. Copy the source bytes when the lengths match, with a transaction entry in the current project.
5. Update the target's checksum.
6. Optional summary report listing each transfer, length mismatches and failures.

**SRAM compare** (needs .ram dumps):
- "Compare binary to SRAM snapshot": compares each calibration symbol's flash bytes with the RAM bytes at its SRAM address.
- "Compare SRAM snapshots": compares two dumps value by value.

## Settings dialog

**Window.** "Settings", opened from File > Options > Settings. There are no tabs: three groups and a button row.
- Every value is loaded from AppSettings on open and written back on OK, each setter persisting at once.
- After OK: AFR/lambda gauge setup, docking (FancyDocking, HideSymbolTable), and display options (ShowAddressesInHex → X6 format on Address/SRAM address/Length). Viewer settings apply to viewers opened afterwards.
- No CAN adapter re-setup: that happens at connect.

**User interface settings** (all offline):

| Caption | Setting | Default | Notes |
|---|---|---|---|
| Auto size new mapwindows | AutoSizeNewWindows | true | |
| Use red and white maps | ShowRedWhite | false | |
| Show graphs in mapviewer | ShowGraphs | true | |
| Hide symbol window | HideSymbolTable | false | |
| Auto size columns in mapviewer | AutoSizeColumnsInWindows | true | |
| Don't display colors in mapviewer | DisableMapviewerColors | false | |
| Auto dock maps from same file | AutoDockSameFile | false | |
| Auto dock maps with same name | AutoDockSameSymbol | true | |
| Show mapviewers in seperate windows | ShowViewerInWindows | | disabled, unused |
| New panels are floating | NewPanelsFloating | false | |
| Always re-create repository items | | | disabled, unused |
| Auto load last file on startup | AutoLoadLastFile | true | |
| Default view type for maps | DefaultViewType | Easy | Hexadecimal view / Decimal view / Easy view |
| Synchronize mapviewers | SynchronizeMapviewers | true | |
| Default view size for maps | DefaultViewSize | 0 | 0 High resolution (1600*1200), 1 Normal (1280*1024), 2 Low (1024*768) |
| Fancy docking | FancyDocking | true | |
| Use T7Suite AFR maps | AutoCreateAFRMaps | true | realtime |
| Show table upside down | ShowTablesUpsideDown | | no effect: tables are always upside down |
| Write timestamp marker in binary | WriteTimestampInBinary | true | |
| Use new mapviewer | UseNewMapViewer | true | |
| (no label) | StandardFill | 0 | No / Square / Triangle closed loop indicator |

**General settings** (offline):

| Caption | Setting | Default | Notes |
|---|---|---|---|
| Auto update checksum | AutoChecksum | true | |
| Show addresses and lengths in Hex | ShowAddressesInHex | true | |
| Auto fix footer | AutoFixFooter | false | |
| Enable CAN logging | EnableCanLog | | unused |
| Request project notes | RequestProjectNotes | false | |
| Project folder | ProjectFolder | | folder browser; empty → StartupPath\Projects |

**Realtime settings** (ECU, later):
- CANBus adapter type, with its descriptions; Adapter (names from `ITrionic.GetAdapterNames`); Configuration for Combi / ELM327 / Just4Trionic.
- Only P-bus connection.
- Auto update SRAM viewers every 5–60 seconds (default 20).
- Reset realtime symbol on tabpage switch; Interpolate timescale for LogWorks; Measure AFR in lambda.
- Wideband:
  - "Use wideband O2 (pin 16) with symbol": DisplProt.AD_Scanner / LambdaScanner, plus a voltage/AFR configuration.
  - "Use wideband O2 on com port": device PLX/LM1/LC1/LM2/ZT2/AEM/STAG/LambdaShield, plus a com port.
  - The two wideband options exclude each other.
- Notifications.
- Autotune settings and Autologging settings (sub-dialogs).

## Symbol import / export

**Import XML descriptor** (File actions):
- The table name comes from the file's 3rd line (`_x0020_` → space, `_x003N_` → N). Columns SYMBOLNAME, SYMBOLNUMBER, FLASHADDRESS, DESCRIPTION (`DataTable.WriteXml`).
- Matched on SYMBOLNAME = Varname and FLASHADDRESS = address:
  - Varname "Symbolnumber N": Userdescription = old Varname, Varname = DESCRIPTION.
  - Otherwise: Userdescription = DESCRIPTION.
- Then Description and Category are recomputed and `<bin>.xml` is saved.

**Import CSV descriptor:**
- Lines `number;name;...`, separated by ';'. Every symbol with that number gets Userdescription = name.
- Then Varname and Userdescription are swapped where Varname == "Symbolnumber <number>".
- Then save.

**Import AS2 descriptor:**
- Lines starting with '*'. The Nth such line names the Nth symbol with Length > 0.
- Then the same swap and save.

**Sidecar `<bin>.xml`** (always written: on open, on user-description edit, after import). One row per symbol with a user description:
- (Userdescription, number, address, Varname) when Userdescription == "Symbolnumber <number>";
- else (Varname, number, address, Userdescription).

**Export symbollist as CSV** (symbol menu):
- `{Varname with ','→'.'},{address},{SRAM address},{length},{number},{type},{userdescription}`: decimal, no header.
- Then "Export done". It isn't readable by Import CSV.

## Exports

**Save as.** Copies the bin (not the .xml), then asks "Do you want to open the newly saved file?", which opens it as a plain file.

**Export to S19.**
- `Srecord.ConvertBinToSrec(file, 0x80000, target)`, with no messages.
- Output: S0 header; S2 records with 32 bytes and 24-bit addresses; S5; S8.

**Generate Idc file.** `IdaProIdcFile.create`: writes `<bin>-autogen.idc` with no dialog.

**XDF.** XDFWriter is unused in T7, so there is no menu.

**Tuning packages** (.t7p, PackageExporter):
- Per symbol: `symbol=<name>`, `length=<n>`, `data=XX,XX,...,`.
- "Export as tuning package" exports the selected symbols. "Export fixed tuning package" exports a fixed list of 64 maps.

**Excel exports**, which become CSV in the port:
- **Symbol grid "Export to excel"** (and to PDF): the grid as shown.
- **Actions > "Export map to Excel"**:
  - A1 = "Data for <map>".
  - Row 2: X-axis values with the axis factor, 2 decimals.
  - Column A from A3: Y-axis values reversed, raw.
  - Cells from B3: value×factor+offset, 2 decimals, data rows flipped.
  - Saved as `<bin>~<map>.xls`.
  - 16-bit cells are negative only when the high byte is 0xFF.
