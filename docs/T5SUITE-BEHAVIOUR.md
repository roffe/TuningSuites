# T5Suite behaviour reference

How the old T5Suite 2.0 (T5Suite2.0/, Trionic5Tools/, Trionic5Controls/, T5CANLib/) behaves, read from its code, where it differs from T7Suite ([T7SUITE-BEHAVIOUR.md](T7SUITE-BEHAVIOUR.md)) and T8Suite ([T8SUITE-BEHAVIOUR.md](T8SUITE-BEHAVIOUR.md)). Line numbers are `T5Suite2.0/frmMain.cs` unless another file is named. Check here before re-reading frmMain (13.3k lines).

The old code is in [OldSuites/](../OldSuites); the paths below are relative to it.

## The T5 binary and opening a file

How T5Suite 2.0 reads a T5.2 / T5.5 binary, read from `Trionic5Tools/Trionic5File.cs` (the file logic; line numbers without a file name are this file), `Trionic5FileInformation.cs`, `SymbolTranslator.cs`, `SymbolAxesTranslator.cs`, `T5Suite2.0/SrecordT5.cs` and `T5Suite2.0/frmMain.cs` ("frmMain"). Checked against the 85 stock bins in `T5Binaries/` with a re-implementation of the parser: all of them parse, find `END$`, find the address table and have a correct checksum.

Unlike T7 and T8, T5Suite keeps no symbol table XML, no repository and no sidecar: everything comes from the bin on every open. Bytes are always read and written straight from the file on disk, one `FileStream` per call, never cached.

### Opening a file

**Entry points.** They all end in `OpenWorkingFile(string)` (frmMain 903).
- Ribbon "Open file" (`btnOpenFile_ItemClick`, 1878) calls `OpenWorkingFile()` (877). It has one filter, `Trionic 5 files|*.bin;*.s19`. On OK it runs `CloseProject()`, converts an `.s19` (see S19 below), calls `OpenWorkingFile(f)`, then sets `LastOpenedType = 0` and calls `SetDefaultFilters()`.
- Part number lookup (`btnBrowseLibrary`, 5550): `CloseProject(); OpenWorkingFile(f)`. Its compare branch is part of compare.
- The firmware-info dialog's "Open file" (5729) and `OpenProject` (7271, BINFILE from `projectproperties.xml`) call it without `CloseProject`. So do the user library (`btnScanForBinaries`, 11643) and the PE-micro BDM read (9908/9912), which opens `FROM_ECU<ts>.S19` **as a bin, without converting it** (old bug).
- Command line (ctor 369): `args[0]` ending in `.BIN` is stored **upper-cased** (`args[0].ToUpper()`). It is opened in Load (3630) when it exists. `.s19` is ignored.
- Auto-load (3637): only with no command line and `AutoLoadLastFile` set. `LastOpenedType == 0` opens `Lastfilename` if it exists, otherwise `OpenProject(Lastprojectname)`. Both need `_immoValid`, which is always true because the HWID licence check is commented out (frmMain 295-311). `HWID.cs` (WMI CPU id plus volume serial) is dead.
- There is no MRU list, no drag and drop and no dirty check. Same as T8Suite.

**`OpenWorkingFile(string)` (frmMain 903)**:
```
if !File.Exists(f): return false                       (silently; an S19 that failed to convert lands here with "")
m_trionicFile = new Trionic5File(); LibraryPath = <StartupPath>\Binaries; SetAutoUpdateChecksum(AutoChecksum)   (default true)
btnReadOnly: "File is READ ONLY" / "File access OK"     (same as T7)
SelectFile(f); m_trionicFileInformation = ParseFile()   (symbol table + address table, below; progress 0..95, status "Decoding file" → "Idle")
props = GetTrionicProperties()                          (firmware info: footer fields, sw version, flags)
barECUType "T5.5" (length 0x40000) / "T5.2" (anything else); EnableT55Maps(IsTrionic55)
barECUSpeed props.CPUspeed ("16 Mhz" / "20 Mhz"); barECULocked "RAM locked" / "RAM unlocked"
btnCompareToOriginalFile.Enabled = <StartupPath>\Binaries\<Partnumber>-<SoftwareID>.bin exists (case-insensitive name match)
_ecuConnection.MapSensorType = GetMapSensorType(AutoDetectMapsensorType)
grid = SymbolCollection (in table order); status "File: <name>"; title "T5Suite Professional 2.0 [<name>]"
enable the tuning buttons; btnChangeRegkonMatRange = Has2DRegKonMat() (Reg_kon_mat! length ≠ 0x80)
Lastfilename = f; AFR maps saved, recreated when AlwaysCreateAFRMaps
realtime list = symbols with Start_address > 0 and 1 ≤ Length ≤ 4; LoadUserDefinedRealtimeSymbols; LoadKnockMaps
```
- **No validation at all.** Nothing checks the size, a signature or the checksum. A non-T5 file parses to an empty symbol list and gets "T5.2" in the status bar, with no message. T7 had "File is not a Trionic 7 binary file!" and T8 had its length and signature checks. The only size test is the T5.5 test, `length == 0x40000`.
- **The checksum is not verified at open**, as in T7.
- `EnableT55Maps(false)` (1060) disables 6 buttons for T5.2. Three of them (Boost adaption wizard, Hardcoded RPM limit, Change Reg_kon_mat range) are re-enabled a few lines later by the generic enable block. Only 1st / 2nd gear limiter (manual) and Knock limit map stay disabled.
- The compare button is enabled on `<partno>-<swid>.bin`, but `btnCompareToOriginalFile` (10095) opens `<partno>.bin`. Old bug: with the stock naming (`4300331.BIN`) the button stays disabled; with `4302642-A5EPK67L.10C.BIN` it is enabled but opens a file that may not exist.
- `CloseProject` (8161) clears the grid, the captions ("--", "No file"), the title "T5Suite Professional 2.0" and `Lastfilename`, and disables the file buttons. Open viewers stay open, as in T7.
- `SetDefaultFilters` (1925) puts the grid filter "Only symbols within binary" on: `Flash_start_address > 0`, or `<> '000000'` when ShowAddressesInHex.

### File sizes and type

| Length | Type | Flash in the ECU's address space | File offset of ECU address `a` |
|---|---|---|---|
| 0x20000 (128 KB) | T5.2 (`Trionic52File`) | 0x60000-0x7FFFF | `a − 0x60000` |
| 0x40000 (256 KB) | T5.5 (`Trionic55File`) | 0x40000-0x7FFFF | `a − 0x40000` |

`DetermineFileType` (2505) also maps 0x80000 to T7 and 0x100000 to T8, otherwise Unknown. Its callers are the tuning wizard, Tune to stage X, Free tune and the bin examiner (frmMain 6705, 9115, 10529, 11158). `IsTrionic55` in the properties is `length == 0x40000`.

Stock set: 81 × 0x40000 and 4 × 0x20000. The T5.2 bins are 4300810, 9136474, 9136490 and 9136516, the MY93 9000 2.3T (`PartNumberConverter.cs` header comment). Bytes 0-3 are `FF FF F7 FC` (the reset SP) in 84 bins; 4302972 has `00 FF 71 FC`. Nothing tests them.

The code converts ECU addresses to file offsets by wrapping modulo the file length rather than subtracting:
- `readdatafromfile` (4835), `readbytefromfile` (3040) and `writebyteinfile` (3065) do `while (address > fileLength) address -= fileLength`, so they accept an ECU address or a file offset alike.
- `GetSymbolAddress` (3106) does `Flash_start_address − Filelength`, then the same loop. It keeps the **last** symbol of that name; `GetSymbolLength` (3090) keeps the first.
- An address that is an exact multiple of the length (0x60000 in a T5.2) maps to `length`, past the end. No stock symbol does that.
- A symbol with no flash address (0) gives a negative offset. The read throws, is caught and returns zeros; writes skip `address <= 0`.

### Symbol table (`ParseFile`, 6317)

The names come from a plain-text table in flash. A byte-level state machine finds it:
```
1. find 00 0A 28 79 00          ("adda #10,sp" tail + "movea.l (abs).l,a4"); restart on any mismatch WITHOUT re-testing the byte
2. skip to the next 4E 75       (rts); the table starts right after it, with no CR LF before the first record
3. records: bytes up to the next 0D 0A, at most 32 bytes:
     u16 BE SRAM address | u16 BE length | ASCII name (often with a trailing 00)
   a record that reaches 32 bytes without CR LF is still added; if it begins "END$" parsing stops, else wait for the next CR LF
```
- The end is the `END$` record, which is followed by code, not CR LF. Stock tables have 540-997 records, all ≤ 28 bytes, so the 32-byte cap only fires on `END$`.
- Table start in the stock bins: T5.5 between 0xDB84 and 0x12D40, T5.2 at 0x9410 / 0x94B2 / 0x93DA.
- `TryToAddSymbolToCollection` (5742) re-parses a hex string of the record:
  - the record is dropped when the name has more than 2 bytes < 0x0A, or is empty once NULs are removed;
  - otherwise `Varname = name`, `Start_address` = the SRAM address, `Length` = the length.
  - `Symbol_number` is not set (0).
- The index of the first record ending past file offset 0xA000 is kept as `m_symboltablestartaddress`. Its only use is a bound on the address table read.
- **Masked names** (`FixMaskedSymbols`, 6301): a name that is blank after `Trim()` gets the name that follows the previous symbol's name in `<StartupPath>\symbolindex.xml`.
  - If `symbolindex.xml` doesn't exist, it is **written from the first file ever opened** (one SYMBOLNAME column).
  - The shipped one has 809 rows, and its first two are binary garbage.
  - No stock bin has a blank name, so this never fires on them.
- Duplicate records exist: `AMOS_text` twice in all 85 bins, plus `Ap_max_on_time!` twice in 23 of them. Both copies have the same address and length, so the first-match / last-match difference above doesn't show.
- Afterwards (`ParseTrionicFile`, 2684):
  - `Helptext`, `XdfCategory` and `XdfSubcategory` come from `SymbolTranslator`;
  - every `Knock_count_cyl*` is forced to `Length = 2` (2701). Stock cyl2/3/4 have 4/6/8 in 23 bins, 2 elsewhere;
  - the temperature tables are loaded (below).

**Names.** A trailing `!` marks a calibration constant in flash; names without it are RAM variables. In stock bins every `!` name has a flash address. The only exceptions in the other direction are `Adapt_ref` and `Adapt_korr` in the four T5.2 bins: flash maps without `!`, which is why the factor and width code tests `== "Adapt_korr"` as well.

### Address table: SRAM → flash (`ReadAddressLookupTableFromFile`, 5813)

```
find the 22 bytes 4E 75 48 E7 01 30 26 6F 00 16 3E 2F 00 14 24 6F 00 10 60 00 00 0A    (same no-backtrack matcher)
then the next 48 79 00 04 (T5.5) / 48 79 00 06 (any other length)   ("pea $0004xxxx" / "pea $0006xxxx")
from that 48 79 + 2, up to <symbol count> entries:
    u32 BE flash address | 8 bytes skipped | u16 BE SRAM address
    then scan at most 16 bytes for the next 48 79; stop when none is found
```
- Each symbol takes `Flash_start_address` from the first entry whose SRAM address equals its `Start_address`. Symbols with no match keep 0: RAM-only, about half of all records.
- Stock bins have 298-513 entries. Every flash address + length lies inside the file.
- Old bug: the guard `readaddress >= startofsymboltable` never advances `readaddress`, so it only tests the table's first entry.

So a T5 symbol has `Start_address` (16-bit SRAM address, used online and for SRAM dumps) and `Flash_start_address` (24-bit ECU flash address, or 0). The map viewer reads the bin at `Flash_start_address` through the wrap above. A double-click on a symbol with flash 0, offline and without an SRAM file, gives "Symbol resides in SRAM and you are in offline mode. T5Suite is unable to fetch this symboldata in offline mode" (frmMain 1961).

### Footer (identifiers)

The last 0x100 bytes hold fields written **backwards**, in this order from the end: data (reversed), then id byte, then length byte. The checksum takes the last 4 bytes. The 4300331 example is at file offsets 0x3FF96-0x3FFFF:

| Id | Meaning | Example | Read by | Written by |
|---|---|---|---|---|
| 01 | Partnumber (7) | `4300331` | `GetPartnumber` / `readpartnumber` (2641/2657) | `writepartnumber` 1806 |
| 02 | Software id (7) | `4302220` | nothing (its reader is commented out) | `writesoftwareid` 1857 |
| 03 | Dataname (12) | `A53OF4LL.12A` | `readdataname` 1552 → props.Dataname | `writedataname` 1720 |
| 04 | Engine type (30, space padded) | `B204S 9000 C3` | `readenginetype` 1518 | `writeenginetype` 1527 (pads with spaces) |
| 05 | Immobilizer / VSS code (6) | `040386` | `readimmocode` 1416, only when `Pgm_mod!` > 5 bytes (else "-----") | `writeimmocode` 1436 |
| 06 | `LX01` (4) | | nothing | nothing |
| FC | ROM end, hex text | `07FFFF` | | |
| FD | ROM start, hex text | `040000` (T5.2: `060000`) | | |
| FE | Checksum end address, hex text | `06805B` | `ReadEndMarker` 4873 | |
| — | Checksum, u32 BE | at length − 4 | | |

- **Lookup** (`ReadMarkerAddress`, 4936):
  - scan the 0xFF bytes from length − 0x100 for the first byte equal to the id whose next byte is < 0x30;
  - that byte's position is `pos`, the next byte is `len`, and the value is bytes `[pos − len, pos)` reversed;
  - a missing id (05 absent in 4300851; 06 absent in 9136474) returns a false hit (another field's length byte) or "". Values are not trimmed.
- **Writers** (all skip a file in the library folder, `FileInLibrary` 1424):
  - they overwrite in place, reversed, with no length change;
  - 01, 02, 03 and 05 don't pad a shorter value. Their loop skips the leading positions **without advancing the stream**, so a short value lands at the start of the field and the field's tail keeps the old bytes (old bug). Only 04 pads with spaces.
- **Car model** (`readcarmodel`, 1474): 4 bytes at length − 0x30, reversed. That is a slice of the engine-type field, so the stock bins give "9000" ×45, "-900" ×32, "900 " ×7 and "04EM". `writecarmodel` writes `model.Length` bytes there unpadded.
- **Software version** (`GetSoftwareVersion`, 2724), the "SoftwareID" in props and the compare-button name:
  - not a footer field: it scans the file from 0x1B00 for `<alnum><digit><digit>.` followed by `$`, and returns the 12 bytes before the `$`;
  - in 4300331 it finds `A53OF4LL.12A$` at 0x29B8; it is usually equal to footer 03;
  - the byte after the `$` is the **RAM lock flag** (`readramlockedflag` 1652, scanning to 0x10000): 00 = unlocked, anything else (or not found) = locked;
  - T5.2 is locked when that byte ≠ 00 or `Write_protect!` ≠ 00; the stock T5.2 bins have FF;
  - stock: 78 locked and 3 unlocked T5.5 bins, and 4 T5.2 bins with FF.
- **CPU speed** (`DetermineFrequency` 2069): "20 Mhz" when the 32-byte sequence `02 39 00 BF 00 FF FA 04 00 39 00 80 00 FF FA 04 02 39 00 C0 00 FF FA 04 00 39 00 13 00 FF FA 04` occurs, else "16 Mhz" (34 / 51 in the stock set). The matcher doesn't backtrack either.

**T5Suite's own markers.** These sit in the erased area below the footer, all FF in every stock bin, and are written by the tuning / firmware dialogs:

| Offset from end | Content | FF means |
|---|---|---|
| −0x200 | tuning stage | 0 |
| −0x1FF | map sensor: 01 = 3.0, 02 = 3.5, 03 = 4.0, 04 = 5.0 bar | 2.5 bar |
| −0x1FE | injector type | 0 |
| −0x1FD | turbo type | 0 |
| −0x1FC | memory sync counter, u64 BE | 0 |
| −0x1F0 / −0x1EE | manual rpm low / high, u16 | 2750 / 4000 |
| −0x1EC / −0x1EA | auto rpm low / high, u16 | 2750 / 4500 |
| −0x1E8 | max boost error, u16 | 4 |
| −0x1E4 | regulation divisor (Reg_kon_mat step), u16 | 10 |
| −0x1E0 | sync timestamp, 7 bytes: year u16, month, day, h, m, s | 2000-01-01 00:00:00 |

- `GetSymbolNameByAddress(length − 0x1E0)` returns "Sync timestamp" (`Trionic5FileInformation.cs` 50), for the transaction log.
- The markers lie past the checksum range, so writing them never invalidates the checksum.
- `GetMapSensorType(autodetect)` (2371) takes the marker. Only when it says 2.5 bar and AutoDetectMapsensorType is on does it guess with `DetermineMapSensorType` (759).

### Checksum (`verifychecksum` 4989 / `updatechecksum` 5022)

```
end  = hex(footer FE, the first FE byte in the last 0x100 bytes, its 6 preceding bytes reversed)
       − (length == 0x40000 ? 0x40000 : 0x60000)                  e.g. 0x6805B − 0x40000 = 0x2805B
sum  = Σ byte[0 ..= end]  as u32 (wraps)                          (loop: indexoffirstmarking = end − 3, tel < end − 3 + 4)
ok   = sum == u32 BE at length − 4
```
- `end ≤ 3`, i.e. no FE field (`ReadEndMarker` returns 0, or −0x40000 / −0x60000), means neither function does anything and verify returns false.
- The summed range is code and calibration only. The footer, the T5Suite markers and the erased gap are outside it. Stock `end` runs from 0x1EE71 (T5.2) to 0x36E99.
- Update rewrites the 4 bytes only when they differ, and does nothing for a library file.
- **When:**
  - Not on open.
  - Every write through `WriteData` / `WriteDataNoLog` / `WriteDataNoCounterIncrease` (2562-2627) updates it when AutoChecksum is on (the default).
  - Ribbon "Verify checksum" (frmMain 3832): valid → `frmInfoBox("Checksum is valid")`. Invalid → `frmChecksumWarning`, title "Checksum information", text "The checksum in the current file is invalid. Do you want to correct it?", buttons Yes / No; Yes updates.
  - Always fixed, whatever AutoChecksum says: Save as (11553, when invalid), the PE-micro BDM program buttons (9961…), the boost adaption wizard (7090), Change Reg_kon_mat range (10126), the map sensor wizard (10769), Hardcoded RPM limit (12873) and Excel import (13272).
- **Map save** (`mv_onSymbolSave`, frmMain 1822):
  1. "Remark for change" when RequestProjectNotes is on in a project.
  2. Write to the ECU when online.
  3. `WriteData(data, Flash_start_address, note)` (2603). It writes byte by byte, then `SetMemorySyncDate(now)`: 7 bytes at length − 0x1E0, through `WriteDataNoCounterIncrease`. When a project log is set, that write gets **its own transaction entry, logged before the map's**.
  4. The map's transaction entry, then the checksum when AutoChecksum is on.
  - T7 and T8 always correct the checksum on a map save; T5 follows AutoChecksum.
- Writes to a file whose folder is `<StartupPath>\Binaries` are silently dropped (`writebyteinfile` → `FileInLibrary`). That folder is the stock library, so it is write-protected.

### S19 (`SrecordT5.cs`)

**Open** (`ConvertSrecToBin`, 13):
- writes `<dir>\<name>.bin` (FileMode.Create, overwriting without asking) and opens that;
- **addresses are ignored**: data bytes are appended in line order;
- three line shapes are read, and everything else (S0, S3, S5, S7-9, short S1/S2 lines) is skipped:

| Line | Length | Data |
|---|---|---|
| S2 | > 75 chars | 32 data bytes from column 10 |
| S2 | 44-75 chars | 16 data bytes from column 10 |
| S1 | 42-44 chars | 16 data bytes from column 8 |

- there is no padding and no size check, unlike `CommonSuite.Srecord(…, size, pad)` that T7 and T8 use;
- a failure is logged only, and the caller then opens "", which does nothing after `CloseProject`.

**Save** (`ConvertBinToSrec`, 85) from Save as (11553):
- filter `Binary file|*.bin|Motorola S record format|*.S19`;
- accepts only 0x20000 / 0x40000 files (returns false: "Failed to convert file to S19 format");
- writes:
  ```
  S00600004844521B                                   ("HDR")
  S224 <addr 3 bytes from 000000> <32 data bytes> <cs>   one per 32 bytes; cs = 0xFF − (count+addr bytes+data) & 0xFF
  S503 <record count, 4 hex> <cs>                    (T5.5: S5032000DC, T5.2: S5031000EC)
  S804000000FB
  ```
  The addresses start at 0, not at the flash base.
- The other branch of Save as copies the file (`File.Copy`, when the name differs).
- Save as **does not switch to the new file** and doesn't offer to: work continues on the old one. T7 asked "Do you want to open the newly saved file?".
- `ConvertBinToSrec(f)` (77) writes `<dir>\<name>.s19` for the PE-micro BDM batch flow.

### Per-map geometry, factor, offset and axes

All of it is name based (`StartsWith` chains, first match wins). The bin holds no metadata. The map viewer (`Trionic5Controls/MapViewerEx.LoadSymbol`, 7882) gets each piece through `IECUFile`:

| Piece | Method | Rule |
|---|---|---|
| Width (columns) | `GetTableMatrixWitdhByName` 3120 | per name; the axis symbol length (÷2 when the axis is 16-bit) or a constant; default 1 |
| Rows | same | **not used**; the viewer derives rows from the length. Unreliable (e.g. Ign_map_0 rows = Y-axis length in bytes) |
| 16-bit | `isSixteenBitTable` (`Trionic5FileInformation.cs` 244) | ≈75 prefixes; `Ign_angle_byte` → false before `Ign_angle` → true; `Reg_kon_mat*` 16-bit unless its length is 0x80 |
| Factor | `GetMapCorrectionFactor` 2877 | default 1 |
| Offset | `GetMapCorrectionOffset` 2787 | default 0; display = raw × factor + offset (not in the compare viewer) |
| X / Y axis values | `GetXaxisValues` 3506 / `GetYaxisValues` 3836 | axis symbol, 8/16-bit, multiplier, special conversions (below) |
| Axis captions | `GetAxisDescriptions` 5150 | defaults "x-axis" / "y-axis" / "z-axis"; e.g. Ign_map_0/2/3/4/6/7 → MAP / RPM / Degrees |
| Axis symbol names (axis editor, axis browser) | `SymbolAxesTranslator` | a separate hand-kept table that can disagree with the value tables above |

`GetXYAxisAddresses` (4555) has no callers.

Main maps (rules quoted from 3120 / 3506 / 3836 / 2787 / 2877):

| Map | Width | X axis | Y axis | Factor / offset |
|---|---|---|---|---|
| Insp_mat!, Del_mat!, Purge_tab!, Adapt_ref*, Adapt_ind_mat*, Adapt_korr(!), Adapt_ggr* | len(Fuel_map_xaxis!) | Fuel_map_xaxis! 8-bit | Fuel_map_yaxis! 16-bit ×10 (rpm); Purge_tab! 16 hard-coded rpm (500, 830, 1160, … 5400) | Insp_mat 1/256 +0.5; Del_mat 3; Adapt_korr / Adapt_ref 1/512 +0.75 |
| Fuel_knock_mat! | len(Fuel_knock_xaxis!) | Fuel_knock_xaxis! | Fuel_map_yaxis! | 1/256 +0.5 |
| Inj_map_0! (LOLA) | len(Inj_map_0_x_axis!)/2 | own, 16-bit | own, 16-bit | 1 / 0 |
| Ign_map_0!, Ign_map_4!, Knock_count_map, Detect/Mis200/Mis1000/Misfire_map!, Knock_ref_matrix! | len(Ign_map_0_x_axis!)/2 | Ign_map_0_x_axis! 16-bit | Ign_map_0_y_axis! 16-bit | Ign maps 0.1 |
| Ign_map_1/2/3/5/6/7/8! | len(own x)/2 | own x 16-bit | own y | 0.1 |
| Tryck_mat!, Tryck_mat_a! | len(Trans_x_st!) | Pwm_ind_trot! 8-bit (not the width's symbol) | Pwm_ind_rpm! 16-bit ×10 | 0.01, −1 (bar) |
| Reg_kon_mat(_a)! | 8 when length 0x80 (MY94-, per gear), else 1 | 0x80: 8 bytes at Pwm_ind_trot! + 32 | 0x80: Pwm_ind_rpm! 16-bit ×10; else 31 synthetic rows `2500 + v·divisor·10` | 0x80: 1, else 0.1 |
| P/I/D_fors(_a)! | 4 | | | |
| Idle_fuel_korr! | len(Idle_st_last!) | Idle_st_last! 8-bit | Idle_st_rpm! 16-bit ×10 | 1/256 +0.5 |
| Lambdamatris(_diag)! | 3 | | | |
| Before_start!, Startvev_fak!, Start_dead_tab!, Ramp_fak!, Eftersta_fak*, Eft_dec_*, Eft_fak_* | 1 | | Temp_steg! → coolant °C | Startvev_fak 1/8; Eftersta_fak 1/128 +1 |
| Luft_kompfak!, Idle_ac_tab! | 1 | | Lufttemp_steg! → air °C | Luft_kompfak 1/512 +0.75 |

- **Axis value rules:**
  - Y values that are 16-bit and > 0x8000 become negative; X values never do.
  - 8-bit Y values are made signed (`> 0x80`, so 0x80 stays 128) only for Iv_start_time_tab!, Idle_temp_off!, Idle_rpm_tab!, Start_tab! and I_last_temp!.
  - A map without axes gets 0..n−1.
- **Temperatures** (`LookupCoolantTemperature` 5579 / `LookupAirTemperature` 5663): find the nearest `Kyltemp_steg!` / `Lufttemp_steg!` entry to the AD value, interpolate linearly into `Kyltemp_tab!` / `Lufttemp_tab!`, subtract 40.
  - The tables are read once at open (`TryToLoadTemperatureConversionTables` 5554).
  - An index past the end throws and is caught, so that point shows −1 (old bug, visible at axis ends).
  - Fahrenheit conversion is commented out.
- **Factor quirks**: Gear_st! 0.1, Hot_tab! / After_fcut_tab! 1/1024 +1, rpm constants (Rpm_max!, Open_varv!, …) ×10, pressure constants 0.01 / −1. `Pressure map … scaled for 3 bar mapsensor` (factor 0.012) are names of a commented-out feature. The map-sensor type doesn't change any map factor; it only scales realtime values (`Trionic5SymbolConverter`: ×1.2 / 1.4 / 1.6 / 2.0 on pressures; P/I/D_fak sign uses 65535, not 65536).

### Descriptions and categories (`SymbolTranslator.cs`)

- One `switch` (`TranslateSymbolToHelpText`, 10), with 895 `case` labels on the name without its trailing `!`. It sets `Helptext` = description (English only) plus `XdfCategory` and `XdfSubcategory`.
- Categories: Fuel, Ignition, Boost_control, Knocking, Idle, Sensor, Diagnostics, Adaption, Runtime, Misc, Undocumented. Subcategories: Basic / Advanced / … / Axis.
- Fallback (4468): a name containing `_frame` with no description gets Diagnostics / Advanced and the name as text. Everything else not listed gets "" / Undocumented / Undocumented.
- T7 has a 34.5k-line NL/EN translator; T8 forwards to SymbolDictionary. T5's is smaller and English only.

`PartNumberConverter.GetECUInfo(partnumber, enginetype)` covers 126 part numbers: software id, engine, car, bhp, torque, stage boosts, turbo, model years, region, T5.2 flag. Engine type ending "T5S1/2/3" marks a T5Suite-tuned file. It isn't used at open, only by the compressor map (frmMain 11768) and the part number list (`PartnumberCollection.GeneratePartNumberCollection` → `Trionic5Controls/frmPartNumberList`).

## Symbol list and map viewers

Paths: `frmMain` = `T5Suite2.0/frmMain.cs`, `Designer` = `T5Suite2.0/frmMain.Designer.cs` (Windows-1252), `MVE` = `Trionic5Controls/MapViewerEx.cs` (+ `MapViewerEx.designer.cs`), `T5File` = `Trionic5Tools/Trionic5File.cs` (already lifted as `T5Core/Trionic5File.cs`), `T5Info` = `Trionic5Tools/Trionic5FileInformation.cs`.

### Symbol list

**Columns** (Designer 2398-2483). No saved layout file, no user description and no `<bin>.xml`.

| Caption | Field | Shown | Notes |
|---|---|---|---|
| Description | `Helptext` | yes, first (width 290) | from `SymbolTranslator.TranslateSymbolToHelpText` at parse (T5File 2684). Unknown symbols get "", `*_frame` symbols get their own name |
| Symbol | `Varname` | yes, second (width 89) | T7: "Symbol name", first |
| Flash address | `Flash_start_address` | hidden | X6 with ShowAddressesInHex (default **on**), else decimal (`SetFilterMode`, frmMain 2606). This is an ECU flash address (0x40000-0x7FFFF on T5.5); 0 means the symbol is only in SRAM |
| Length | `Length` | hidden | always decimal (T7 used X6) |
| SRAM address | `Start_address` | hidden | X6 / decimal like Flash address |
| Category | `XdfCategory` | grouped | `XDFCategories` enum |
| Subcategory | `XdfSubcategory` | grouped | `XDFSubCategory` enum |

**Grouping and sorting.** There are **two group levels** (GroupCount 2), sorted:
1. Category, **descending**. Likely by enum value, so Adaption, Knocking, Diagnostics, Runtime, Sensor, Misc, Correction, Idle, Boost_control, Ignition, Fuel, with Undocumented last.
2. Subcategory, ascending.
3. Rows by Description, ascending.

T7 had one level and source order inside it.

- Group rows read `Category: Fuel (n)` and `Subcategory: Basic (n)`: format `"{0}: [#image]{1} {2}"`, count summary `" ({0})"`, enum names as they are (underscores kept).
- After an open, `OpenGridViewGroups(gridSymbols, 0)` (frmMain 2976) expands only the category groups. The subcategory groups stay collapsed.
- The find panel is always visible, with no close button. Incremental search, multi-select, read-only. The focused row is bold Tahoma 8.25 in MediumBlue.

**Colours** (`gridViewSymbols_CustomDrawCell`, frmMain 5500). By **category**, not by name prefix. Only the **Description cell's background** is filled:

| XdfCategory | Colour |
|---|---|
| Fuel | LightSteelBlue |
| Ignition | LightGreen |
| Boost_control | OrangeRed |
| Misc | LightGray |
| Sensor | Yellow |
| Correction | LightPink |
| Idle | BurlyWood |
| others | none |

**Filter "Only symbols within binary"** (`SetDefaultFilters`, 1925): `([Flash_start_address] <> '000000')` in hex mode, `([Flash_start_address] > 0)` in decimal mode. It hides SRAM-only symbols.
- Applied after Open file (898) and after every settings save (`SetFilterMode` → `SetDefaultFilters`). The project / library / recent open paths don't apply it themselves.
- **Disabled** (`ActiveFilterEnabled = false`) when the ECU connects (2061) and when an SRAM snapshot is opened (4641). Disconnecting doesn't re-enable it (2148 commented out).

**Description panel.** A label under the grid (fixed 114 px split panel) shows the focused row's description, or "No additional help available" (`TryShowHelpForSymbol` 2871 / `ShowContextSensitiveHelpOnSymbol` 2902).
- It is visible only with ShowAdditionalSymbolInformation, default **off** (`SetAdditionalHelpPanelSize` 2548).
- `StartTableViewer` also refreshes it when that setting is on.

**Advanced mode** (`SetModeAndFilters`, 3010). Once the immo check passed, EnableAdvancedMode (default **off**) controls the symbol list:
- Off: the symbol list dock is set to AutoHide and hidden immediately, so it collapses to an edge tab.
- On: the list is shown, but only in offline mode.

T7 always showed the list. The HideSymbolTable setting exists in the settings dialog but nothing reads it (frmMain 2653 / 2736 only).

**Double-click** (`gridViewSymbols_DoubleClick`, 1943; `gridSymbols_DoubleClick` 1884 is empty):
```
group row → ignored
!_ecuConnection.Opened && Flash_start_address == 0 && no SRAM snapshot loaded
    → frmInfoBox "Symbol resides in SRAM and you are in offline mode. T5Suite is unable to fetch this symboldata in offline mode"
else StartTableViewer(Varname)
```
- **Enter** (`gridViewSymbols_KeyDown`, 4730) does the same, but tests `!Varname.Contains("!")` instead of `Flash_start_address == 0`. In T5 a trailing "!" marks a flash (calibration) symbol, so the two tests differ for a symbol without "!" that does have a flash address.
- Exceptions are only logged.
- T7 differences: no auto-connect for SRAM symbols (T7/T8 connect on demand), no open-software branch, and nothing hides the list afterwards.

**Context menu** (Designer 2363, in this order). Opening (8985) only sets the first item's Enabled state, and only when the right-click lands on a data row; on a group row the previous state stays.
- **"View from SRAM file"**: enabled when a snapshot is loaded. Runs `StartTableViewerSRAMFile(name, snapshot)` (9018, see SRAM below). It hit-tests `Cursor.Position` at click time, i.e. where the menu item is. If the item isn't over a data row, nothing happens (old bug, likely).
- **"Show axis information"** (10049): opens Trionic5Controls `AxisBrowser` for the first selected row in a new panel titled `Axis browser: <bin file name>`. It is tabbed onto an existing "Axis browser: " panel, else docked left at 700 px wide. Its open request calls `StartTableViewer`.
- **"Add to realtime user maps"** (11587): `ctrlRealtime1.AddToRealtimeUserMaps(Varname, Helptext)`. This belongs to the realtime area.

T7's other items don't exist here: Read from ECU / binary, Add to MyMaps, tuning packages, CSV export.

**Shortcuts:** none of T7's grid / panel keys (Ctrl+F, F3, F9). Only Enter in the grid. The ribbon has Shift+F1 (realtime), F6 (log marker), Ctrl+Z / Ctrl+Shift+Z (transaction roll back / forward) and Shift+F5 (autotune).

### Map viewers

**Hosting** (`StartTableViewer(name)`, 1279):
1. Checks:
   - No file: "You should open a binary file first".
   - Unknown name: `"<name> is not present in the current file"` (frmInfoBox).
2. Settings applied before opening:
   - ShowAdditionalSymbolInformation → updates the description label.
   - AutoHighlightSelectedMap (default off) → selects the symbol's row and scrolls to it. If the filter hides the row, it clears the filter and retries.
3. **Special symbols:**
   - `Pgm_mod!` (`GetProgramModeSymbol`, T5Info 141) opens the **frmEasyFirmwareInfo** dialog instead of a viewer (1373).
     - Primary source: "ECU <bin name>" read from SRAM when online, else "BIN <bin name>" from the file.
     - Secondary source: "SRAM <snapshot name>" when a snapshot is loaded, else `DisableSecondarySource()`.
   - `Pgm_status` (1398): an empty TODO branch. **Nothing opens.**
4. A panel titled **`<bin file name> [<symbol>]`** already exists (`PanelExists` 2926): `BringPanelToForeGround` shows it. T7's title was `Symbol: <name> [<file>]`.
5. Otherwise a new DockPanel:
   - Docked **right** always. NewPanelsFloating is ignored for map viewers; it only applies to the hex viewer etc. (4342).
   - Tag = bin path. Width = `DetermineWidth()` = 600 for MapViewerEx (MVE 7934). T7 sized it from the x-axis.
   - AutoDockSameSymbol (default on): tab onto a visible panel whose title contains `[<symbol>]`.
   - Else AutoDockSameFile (default off): tab onto a visible panel with the same Tag.

**Viewer creation** (`MapViewerFactory.Get(settings, file)`, `Trionic5Controls/MapViewerFactory.cs`):
- "Mapviewer to use": Fancy (MapViewerEx, default) / Normal (MapViewer) / Simple (SimpleMapViewer).
- AutoSizeColumns = AutoSizeColumnsInWindows (default on; off gives 40 px columns). DisableColors, GraphVisible = ShowGraphs (default on), IsRedWhite = ShowRedWhite.
- `SetViewSize(DefaultViewSize)`, but `LoadSymbol` then forces `ViewSize.NormalView` (MVE 7855 / 7907), so **DefaultViewSize is ignored**.
- `AutoUpdateChecksum = AutoChecksum`.
- **View type from the MAP sensor** of the file: `GetMapSensorType(AutoDetectMapsensorType)`.

  | Sensor | DefaultViewType = Decimal | any other default |
  |---|---|---|
  | 2.5 bar | Decimal | Easy |
  | 3.0 / 3.5 / 4.0 / 5.0 bar | Decimal3Bar / Decimal35Bar / Decimal4Bar / Decimal5Bar | Easy3Bar / Easy35Bar / Easy4Bar / Easy5Bar |

  A Hex or ASCII default is therefore never used.
  - The sensor comes from the marker byte at `file length − 0x1FF` (`ReadThreeBarConversionMarker`, T5File 2413): 0xFF/0 → 2.5, 1 → 3.0, 2 → 3.5, 3 → 4.0, 4 → 5.0.
  - Only with AutoDetectMapsensorType (default off) and a 2.5 marker, `DetermineMapSensorType` (T5File 759) guesses:
    - last byte of `Fuel_map_xaxis!` is 240 or 224 → stock;
    - otherwise by `Tryck_mat!` byte 0x78: 16 → 3.0, 14 → 3.5, 12 → 4.0, 10 → 5.0.

**Viewer setup.** The viewer loads itself: `mv.LoadSymbol(name, file)`, MVE 7882. It opens a second `Trionic5File` on the same path and reads `Flash_start_address`/`Length`. Every call goes through T5File:

| Property | Source |
|---|---|
| Data | file at the flash address. readdatafromfile wraps an address beyond the file length by subtracting the length (T5File 4835). For an SRAM-only symbol (flash 0) it reads the file's first bytes, which are replaced when online |
| Factor / offset | `GetMapCorrectionFactor` (T5File 2877) / `GetMapCorrectionOffset` (2787): hard-coded per name prefix. Key ones below |
| X / Y axis values | `GetXaxisValues` / `GetYaxisValues` (3506 / 3836): hard-coded axis symbol per map, 8- or 16-bit BE **unsigned**, × multiplier (10 for some rpm axes), no sign. No axis gives 0..cols−1 |
| Axis captions | `GetAxisDescriptions` (5150): **descriptions, not units**; default "x-axis" / "y-axis" / "z-axis". Common ones are "MAP", "RPM", "Throttle position", "Pressure error (bar)", z "Degrees", "Injection time". The captions **drive behaviour** (see MAP below) |
| Width | `GetMapMatrixWitdhByName` → `GetTableMatrixWitdhByName` (3120), the `columns` out value |
| 16-bit | `IsTableSixteenBits` → `T5Info.isSixteenBitTable` |
| UpsideDown | always |
| Map_address / Map_sramaddress | never set (stay 0). The host finds addresses by name |

When online, `StartTableViewer` then replaces the content with `_ecuConnection.ReadSymbolData(name, Start_address, Length)` and sets `OnlineMode = true`.
- This applies to **every** map, calibration ones included: T5 runs its calibration from SRAM.
- Else, with an SRAM snapshot loaded and a name **without "!"**, the content comes from the snapshot at `Start_address`, again with OnlineMode.
- Then `TryToAddOpenLoopTables` (3932) and `InitEditValues`.

**Key factors / offsets** (Easy = `raw*factor + offset`, same formula as T7):

| Map | Factor | Offset | Easy shows |
|---|---|---|---|
| `Ign_map_0!`..`Ign_map_8!`, `Knock_lim*`, `Knock_ang_dec!` | 0.1 | 0 | degrees. `Ign_map_0!`/`Ign_map_4!` F1 + "°" |
| `Insp_mat!` (main fuel), `Fuel_knock_mat!`, `Idle_fuel_korr!` | 1/256 | 0.5 | correction factor around 1.00 |
| `Inj_map_0!` (LOLA injection map) | 1 | 0 | raw |
| `Tryck_mat!`, `Tryck_mat_a!`, `Regl_tryck*`, `Tryck_vakt_tab!`, `Idle_tryck!`, `Limp_tryck_konst!`, `Knock_press*`, `Turbo_knock_*`, `Open_loop*`, `Max_regl_temp_*` | 0.01 | −1 | **bar relative** (raw 100 = 0.00). No kPa anywhere |
| `Reg_kon_mat*` | 1 if length 0x80, else 0.1 | 0 | F0 + "%" (also when the factor is 1) |
| `Adapt_korr*`, `Adapt_ref*`, `Adapt_injfaktor*`, `Adapt_inj_imat!`, `Cyl_komp!`, `Lambdaint!`, `Luft_kompfak!` | 1/512 | 0.75 | |
| `Rpm_max!`, `Kadapt_rpm_*`, … | 10 | 0 | rpm |
| `Del_mat!` | 3 | 0 | |
| `Lamd_tid!` | 10 | 0 | |
| `Batt_korr_tab!` / `Start_insp!` | 0.004 | 0 | |

The full lists are at T5File 2787-3030. Both use `StartsWith`, first match wins, so order matters (e.g. `P_Manifold10` 0.001 before `P_Manifold` 0.01).

**Cell decoding** (MVE `ShowTable` 746). It differs from T7 (T7: 16-bit 0xF001-0xFFFF negative, 8-bit unsigned):
```
16-bit BE b: b > 32000 → b − 65536                 (every 16-bit map)
   FeedbackvsTargetAFR / IdleFeedbackvsTargetAFR: then b > 200 → b − 256
8-bit b: I_kyl_st!, I_luft_st!, Last_temp_st!: b > 128 → b − 256   (128 stays +128); others unsigned
then MAP-sensor scaling (below), then the view's text: Hex X4/X2, ASCII char, else the integer
```

**MAP-sensor views** (the 3-bar family). Maps in `MapIsScalableFor3Bar` (MVE 712):
- prefixes: Tryck_mat, Regl_tryck, Tryck_vakt_tab, Idle_tryck, Limp_tryck_konst, Knock_press(_tab/_lim), Turbo_knock_tab/_press, Open_loop(_knock), Sond_heat_tab;
- names: Reg_last!, Idle_st_last!, Lam_minlast!, Lam_laststeg!, Grund_last!, Max_ratio_aut!, Diag_speed_load!, Kadapt_load_high!/low!, Iv_min_load!, Shift_load!, Shift_up_load_hyst!, Fload_tab!.

In the DecimalNBar / EasyNBar views these have their **raw cell value scaled** by k = 1.2 / 1.4 / 1.6 / 2.0 (3.0 / 3.5 / 4.0 / 5.0 bar) with integer arithmetic, `b = b*120/100`.
- Easy then shows `(scaled raw)*factor + offset`.
- On save the cell goes back:
  - 16-bit: `v*100/120`, truncated, which is lossy. Example: 101 → 121 → 100.
  - 8-bit: `Math.Ceiling(v*100/120)` (GetDataFromGridView, MVE 2754).

**Axis headers** (MVE 4098 X, 4014 Y):
- **X header**:
  - caption "MAP" or "Pressure error (bar)":
    - Decimal* views: `(int)(raw*k)`.
    - Easy* views: `raw*k*0.01`, minus 1 for "MAP" only, formatted F2 (bar).
  - Hex view: `HexadecimalFormatXAxis` gives X2 when every (scaled) value is ≤ 255, else X4.
- **Y header** (row indicator):
  - Y caption "MAP": `(int)(raw*k)` in Decimal* **and** Easy* views. There's no bar conversion for Y, which is inconsistent with X.
  - Hex view: X4.
- 3D axis labels, both axes: a "MAP" / "Pressure error (bar)" caption is always converted to bar (F2, ×k only in Easy*N views), whatever the view type.
- The 2D slider label: `<X caption> [(int)(raw*k)]`.

**Easy display text** (MVE 2292): same as T7 (F2; `Ign_map_0!`/`Ign_map_4!` F1 + "°"; `Reg_kon_mat*` F0 + "%"; raw integer when factor 1 and offset 0), plus:
- FeedbackAFR, FeedbackvsTargetAFR, IdleFeedbackAFR and IdleFeedbackvsTargetAFR show "" for 0 (the autotune maps).
- A compare viewer never adds the offset.

**Cell colours:**
- Same scale as T7: `b = raw*255/MaxValueInTable`. Normal (b, 255−b, 0); red-white: alpha red; DisableColors: none.
- The white→red tint (b/2) is used when OnlineMode is on **or** the map is one of TargetAFR / FeedbackAFR / FeedbackvsTargetAFR / IdleTargetAFR / IdleFeedbackAFR / IdleFeedbackvsTargetAFR / IgnitionLockMap. Setting `Map_name` to one of these turns OnlineMode on (MVE 528).
- Live cell: yellow (same).

**T5-only cell overlays** (MVE 2292-2670). The data comes from `TryToAddOpenLoopTables`, frmMain 3932. That runs only for the ignition map, the injection map (`Inj_map_0!` if it has a flash address, else `Insp_mat!`), `Fuel_knock_mat!`, TargetAFR / FeedbackAFR / FeedbackvsTargetAFR, Adapt_korr, Adapt_ref and Knock_count_map.
- **Open loop** (only when the `Lambdacontrol` property is set):
  - A 2-px **black** box when `Open_loop![dataRow] > xaxis[col]` (raw MAP), on `Insp_mat!`, `Inj_map_0!`, `Ign_map_0!`, the AFR maps and IgnitionLockMap.
  - `Fuel_knock_mat!` uses `Open_loop_knock!`.
  - The StandardFill setting (T7's black box or green triangle) exists in T5AppSettings but the viewer never reads it: always the box.
- **Padlock icon** (`db_lock16_h`, 10×10 at the right) on `Ign_map_0!` / `Knock_count_map`:
  - when `Turbo_press_tab[row] > xaxis[col]`: T5.5 `Knock_press_tab!`, T5.2 16 copies of `Knock_press!`;
  - or when the cell is locked in IgnitionMaps' lock map.
  - The AFR lock map gives the same icon on Insp_mat! / Inj_map_0! / TargetAFR / FeedbackAFR / FeedbackvsTargetAFR, the idle AFR lock map on Idle_fuel_korr! and the Idle*AFR maps.
- **Knock adaption area:**
  - Blue 1-px box where `Kadapt_load_low! ≤ x ≤ Kadapt_load_high!` and `Kadapt_rpm_low!*10 ≤ rpm ≤ Kadapt_rpm_high!*10`.
  - On the last column, a white box over `GetAutoRpmLow..High` (automatic) or `GetManualRpmLow..High`: the **boost adaption** rpm range.
- **Feedback\*** maps: AFR counter box colour (255−n, n, 0).
- **Edited-cell marker** (T7 had none): a yellow triangle in the top-right corner of every cell edited since `InitEditValues` (open, Refresh, Undo). Save doesn't clear it. The ECU marker (orange, top-left) is never set.

**Editing** (MVE 6650 `ValidatingEditor`, 7365 `ShownEditor`). Same as T7 (Easy edits start from F2, `(v−offset)/factor` via `Convert.ToInt32` = round half to even, "Value not valid..."), except the ranges:

| | Hex | Decimal / Easy | 3.0 / 3.5 / 4.0 / 5.0 bar views, scalable maps |
|---|---|---|---|
| 16-bit | ≤ 0xFFFF | **\|v\| ≤ 78643** (bug: above 0xFFFF it keeps the low 16 bits on save) | ≤ 78643 / 91749 / 104856 / 131070 |
| 8-bit | ≤ 0xFF | ≤ 255 | ≤ 306 / 357 / 408 / 510 |

Negative values are refused only for `Insp_mat!`, `Inj_map_0!` and `Fuel_knock_mat!` (`MapSupportsNegativeValues`, 6641).

**Keys** (MVE 3546, not in compare viewers):
- Numpad + / − : ±1.
- PgUp / PgDn: ±10 (±0x10 in hex).
- Home: max (0xFFFF, or 255 / 306 / 357 / 408 / 510).
- End: 0.

Clamps: 8-bit at 0 and at the view's max; 16-bit at 0xFFFF going up, **no lower clamp** going down. Same set as T7.

**Math toolbar** (MVE 4835): "Addition / Multiply / Divide / Fill", value default "2", "Execute". Same as T7 except:
- Easy views **round** (`Math.Round`) the converted-back result; T7 truncated.
- The clamps follow the view's sensor range (16-bit 3-bar: 78642).

**Other viewer behaviour:**
- **Smooth selection**: same as T7, "Smoothing cannot be done in Hex view!".
- **Select by value**: Enter in the view combo, same as T7 (within 0.009).
- **Clipboard**: same format as T7; "No selection, copy the entire map?" (Yes/No) when nothing is selected.
  - The view digit is `(int)SuiteViewType` (0..11). Paste reads only the **first character**, so a copy from the 5-bar views (10 / 11) misparses (old bug).

**Toolbar** (Designer 495-707):
- View combo labelled "Viewtype", 12 entries:
  "Hex view ", "Decimal view ", "Easy view", "ASCII", "Decimal view (3 bar sensor)", "Easy view (3 bar sensor)", "Decimal view (3.5 bar sensor)", "Easy view (3.5 bar sensor)", "Decimal view (4 bar sensor)", "Easy view (4 bar sensor)", "Decimal view (5 bar sensor)", "Easy view (5 bar sensor)".
- Changing it re-runs ShowTable **from `Map_content`**, so unsaved edits vanish while Save stays enabled (old bug).
- "Toggle graph/map", "Maximize window" (float at full screen work area / restore), "Axis lock mode" (Autoscale / Lock to peak in maps / Lock to map limit).
- Hidden: "Toggle graph section", "Toggle hexview", "Maximize graph", "Maximize table".
- Group caption: `Symbol data [<name>]`.

**Bottom buttons:** "Undo changes", "Save", "Refresh", "Close" ("Save to RAM" exists but is hidden). T7 had "Save to file" / "Read from file" / "Save to ECU" / "Read from ECU".
- **Undo changes** (2733): re-shows `Map_content` and resets the edit markers.
- **Save** (`saveToFile_Click` 2743): enabled after an edit. Raises `onSymbolSave` with the grid data, see Saving.
- **Refresh** (`btnReadFromRAM_Click` 7559): disables itself and raises `onReadFromSRAM`. The host (`mv_onReadFromSRAM`, frmMain 1767) reloads:
  - default: the bin at the flash address;
  - online: SRAM, via `ProhibitRead = true`, 100 ms sleep, `ReadSymbolDataNoProhibitRead(Start_address, Length)`, `ProhibitRead = false`;
  - viewer filename ends in "RAM": the snapshot instead.

  Then ShowTable + InitEditValues. There's **no prompt**: edits are dropped. ShowTable re-enables the button.
- **Close** is wired to `simpleButton1_Click_1` (7939): it closes **without asking**. The "Data was mutated, do you want to save these changes in you binary?" handler `simpleButton1_Click` (2099) is dead code. Closing the dock panel doesn't ask either. **Unsaved edits are silently lost** (T7 asked Yes/No/Cancel).

**Viewer context menu** (Designer 207, `contextMenuStrip1_Opening` 7456), in this order:
- "Copy selected cells", "Paste selected cells" → "At original position" / "At currently selected location".
- "Edit x-axis (<sym>)" / "Edit y-axis (<sym>)": from `SymbolAxesTranslator`; disabled "Edit x-axis" / "Edit y-axis" without one. The host (1615) opens the axis with `StartTableViewer`, so unlike T7 the items work.
- "Smooth selection".
- **"Export map" → "As preferred setting in T5Dashboard"**: visible for Tryck_mat!, Tryck_mat_a!, Ign_map_0!, Insp_mat!, Fuel_knock_mat!, Reg_kon_mat! and Knock_ref_matrix! (MVE 7719).
  - Dialog "Export map for T5Dashboard" ("Description for this map"), then a save dialog:

    | Map | Filter | Ext |
    |---|---|---|
    | Ign_map_0! | "Ignition map settings" | .ims |
    | Knock_ref_matrix! | "Knock sensitivity maps" | .krm |
    | Insp_mat! | "Fuel map settings" | .fms |
    | Fuel_knock_mat! | "Fuel knock map settings" | .kms |
    | Reg_kon_mat! | "Regulation map settings" | .rms |
    | others | "Boost map settings" | .bms |

  - The text file holds:
    1. the map name;
    2. the description;
    3. `00` (16-bit) or the max byte as X2;
    4. one line per data row in file order: `XX,` / `XXXX,` per value (trailing comma).
- **"Clear data"**: Feedback / IdleFeedback / IgnitionLockMap only. It raises Save with ClearData (autotune area).
- **"Lock cells" / "Unlock cells"**: on FeedbackAFR, FeedbackvsTargetAFR, TargetAFR, Insp_mat!, Inj_map_0!, Ign_map_0!, Knock_count_map, IdleFeedbackAFR, IdleFeedbackvsTargetAFR, IdleTargetAFR and Idle_fuel_korr!. They raise `onCellLocked` per selected cell. The host (`mv_onCellLocked`, frmMain 12712) updates IgnitionMaps / AFRMaps lock maps and re-shows.

**Saving** (`mv_onSymbolSave`, frmMain 1822). One "Save" writes SRAM **and** file:
```
note = RequestProjectNotes (default off) && project open ? frmChangeNote "Remark for change" : ""
online → _ecuConnection.WriteSymbolData(Start_address, Length, data)        no confirmation, no error box
m_trionicFile.WriteData(data, Flash_start_address, note)                     (T5File 2603)
    each byte via writebyteinfile (3065): opens the file per byte; silently skipped when the file sits in the
        library folder (<app>\Binaries, FileInLibrary 1424) or address <= 0; exceptions only logged → a READ-ONLY
        file fails silently (T7: "Failed to write to binary. Is it read-only? ...")
    SetMemorySyncDate(now): 7 bytes (year hi, year lo, month, day, hour, minute, second) at length − 0x1E0
        through WriteDataNoCounterIncrease → its own transaction entry + checksum (logged FIRST)
    transaction entry (now, flash address, length, before, after, note) when a project log is set
    AutoChecksum (default on, taken when the file opens, frmMain 912) → UpdateChecksum
viewer: Map_content = data, ShowTable (markers stay)
```
- **Old data-corrupting bugs:**
  - Saving an SRAM-only symbol (flash address 0, possible online or from a snapshot) writes its bytes 1..n−1 over the **start of the bin**: byte 0 is skipped by the `address <= 0` check.
  - On an 8-bit map, a negative cell (the signed `I_kyl_st!` family) or one above 255 throws in `Convert.ToByte` inside GetDataFromGridView's upside-down branch (MVE 2876, no inner try). The outer catch returns the buffer with **zeros from that cell on**, and those get written.
- `IsRAMViewer` and `DirectSRAMWriteOnSymbolChange` are never turned on, and `onWriteToSRAM` is never subscribed: there's no per-cell SRAM write and no separate "Save to ECU".
- There is no SRAM auto-update timer either (T7's AutoUpdateIfSRAM).

**SRAM snapshot viewers** (`StartTableViewerSRAMFile`, 1171):
- Snapshots load through "Select a SRAM snapshot", filter "SRAM snapshots|*.RAM". The status bar shows "Snapshot: <name>".
- The viewer:
  - title `SRAM: <snapshot file name> [<symbol>]`, Tag = snapshot path;
  - `LoadSymbol(name, file, snapshot)` reads `Start_address`/`Length` from the snapshot (MVE 7827) with `OnlineMode = true`;
  - ShowTable and the same events.
- Axis items open the axis from the same snapshot (`mv_onAxisEditorRequestedSRAM` 1259).
- **Save writes the snapshot's data into the bin** at the flash address (and SRAM when online), never into the .RAM file.
- Refresh re-reads the snapshot.
- Re-opening an existing one: `SRAMPanelExists` (2939) is true, but `BringPanelToForeGround` looks for the bin-style title, so it does nothing (old bug).
- AutoDockSameFile compares with the bin path, so a snapshot viewer never matches.

**Sync and live view:**
- Sync is gated by SynchronizeMapviewers (default on).
- `tabdet_onSelectionChanged` (5234): one selected cell → `SelectCell` on every other viewer with the same `Map_name`, as in T7.
- `mv_onSurfaceGraphViewChangedEx` (1552) mirrors the 3D view: Nevron depth, zoom, rotation, elevation.
- `mv_onSurfaceGraphViewChanged` (1489) does the same for the legacy MapViewer.
- While realtime runs, `UpdateMapViewers` (823) pushes Rpm / Tps / Boost / BoostTarget to every viewer. MVE `UpdateLiveView` (3244) picks the **nearest** axis index by caption, then paints the yellow live cell (`HighlightCell` 7406):
  - "MAP": `(raw*k − 100)/100` against boost (bar);
  - "Pressure error (bar)": `raw*k/100` against |boost − target|;
  - "RPM": raw;
  - "TPS" / "Throttle position" / "Relative throttle position": raw.
  - The Y test reads the X caption for the throttle names (bug).
  - A 1-D map uses index 0 for the missing axis.

**Graphs:**
- 3D: Nevron mesh with the T7 palettes, both normal and online, plus the overlays. "Toggle graph overlay" appears when `Map_original_content` is set.
- 2D: the slider over the X axis, labelled "<X caption> values".
- Same as T7 apart from the MAP label scaling above.

**Realtime "show map" requests** (`ctrlRealtime1_onMapDisplayRequested` 4657 → `StartTableViewerFloating` 1634, realtime area):
- A floating panel `<bin> [<symbol>]`, width `30 + (xlen+1)*35`, at least 400, height 500, centred on the main window, GraphVisible off. The data comes from SRAM when online.
- "FuelAdjustmentMap" maps to the injection map (Adapt_korr on T5.2) and is filled from AFRMaps' mutated fuel map.

**Close and file change:** `OnCloseMapViewer` (1889) just removes the panel. It also matches the titles `Symbol difference: <name> [<file>]` and `Symbol: <name> [<path>]` used by the compare viewers (compare area).

## Firmware information and settings

T5Suite has nothing like T7's "Firmware information" dialog: there is no VIN, programming date, SID option, Import / Undo, TIS footer fix or VIN decoder. It has a **firmware options editor**, two report generators and a saved-report viewer. Line numbers are `T5Suite2.0/frmMain.cs` unless another file is named. `T5File` means `Trionic5Tools/Trionic5File.cs`.

**Entry points.**

| Ribbon (page / group) | Caption | Handler |
|---|---|---|
| Actions / Basic actions | "Trionic options (firmware)" | `btnFirmwareOptions_ItemClick` 5745 |
| Actions / Basic actions | "Examine binary" | `btnBinExaminor_ItemClick` 11141 |
| Actions / Advanced tools | "Check for anomalies" | `btnAnomaliesChecker_ItemClick` 10850 |
| Actions / Advanced tools | "Open a saved report" | `btnOpenReport_ItemClick` 11086 |

- The first three do nothing, with no message, unless `m_trionicFile.Exists()`.
- A fourth entry point: double-clicking the symbol `Pgm_mod!` opens the read-only Pgm_mod viewer instead of a map viewer (see the end of this section).

**Status bar on open** (929–951) comes from the same `GetTrionicProperties()`:
- `barECUType`: "T5.5" when the file is 0x40000 bytes long, else "T5.2".
- `barECUSpeed`: CPU speed.
- `barECULocked`: "RAM locked" or "RAM unlocked".
- "Compare to original" is enabled when `<Partnumber>-<SoftwareID>` is in the library.
- Nothing refreshes these after the options dialog's OK (bug).

### Reading the properties (`GetTrionicProperties`, T5File 445)

| Field | Where | Decode |
|---|---|---|
| Car model | len − 0x30, 4 bytes | ASCII reversed (`readcarmodel` 1474). Never written: `writecarmodel` is commented out (579). |
| Partnumber | footer id **0x01** | footer string (below) |
| Software ID | flash scan, *not* the footer | 12 ASCII chars `XXXXXXXX.NNA` before the `$` (`GetSoftwareVersion` 2724, below) |
| Dataname | footer id **0x03** | footer string |
| Engine type | footer id **0x04** | footer string |
| VSS code (immobiliser) | footer id **0x05** | footer string; only when `Pgm_mod!` is longer than 5 bytes, else "-----" |
| CPU speed | the 32-byte sequence `02 39 00 BF 00 FF FA 04 00 39 00 80 00 FF FA 04 02 39 00 C0 00 FF FA 04 00 39 00 13 00 FF FA 04` anywhere in the file | found: "20 Mhz", else "16 Mhz" (2034–2077) |
| RAM locked | the byte after `.NNA$`, scanning 0x1B00..0x10000 | 00 = unlocked; any other value, or no match, = locked. On a 0x20000 (T5.2) file, `Write_protect!` ≠ 0 also means locked (1652) |
| SecondO2Enable | `Diag_mod!` byte 0, bit 0x80 | (1459). A missing symbol reads file byte 0. |
| HasVSSOptions | `Pgm_mod!` length > 5 | |
| ExtendedProgramModeOptions | `Pgm_mod!` length > 4 | |
| IsTrionic55 | file length == 0x40000 | |
| Pgm_mod flags | table below | |
| TuningStage | byte at len − 0x200 | FF → 0; enum Stock, Stage1..Stage8, StageX |
| MapSensorType | byte at len − 0x1FF | FF → 2.5 bar, 01 = 3.0, 02 = 3.5, 03 = 4.0, 04 = 5.0. The marker only: no auto-detect here. |
| InjectorType | byte at len − 0x1FE | FF → 0; enum Stock, GreenGiants, Siemens630Dekas, Siemens875Dekas, Siemens1000cc |
| TurboType | byte at len − 0x1FD | FF → 0; `CommonSuite.TurboType`: Stock, GT17, TD0415T, TD0419T, GT28BB, GT28RS, GT3071R, HX35w, HX40w, S400SX371 |
| SyncDateTime | 7 bytes at len − 0x1E0 | year u16 BE, month, day, h, m, s. An invalid date (e.g. FF…) gives 2000-01-01 00:00:00 (2265). |
| HardcodedRPMLimit | u16 BE | at a code-pattern offset (below) |

**Footer strings** (`ReadMarkerAddress`, T5File 4936):
- Read 0xFF bytes from len − 0x100. The first index t with `b[t] == id && b[t+1] < 0x30` is the marker, and `b[t+1]` is the length L.
- The value is the L bytes just before the marker, reversed.
- Not found: "" (length 0).

**Footer string writers** (`writeenginetype` 1527, `writepartnumber` 1806, `writesoftwareid` 1857, `writeimmocode` 1436, `writedataname` 1720):
- They write the reversed string over the L bytes and skip characters beyond the string's length.
- Bug: a string of n < L characters ends up as *the old value's first L−n characters + the new string*. Only engine type pads with spaces first.
- A longer string is cut to its first L characters.
- They write directly with FileStream: no transaction entry.

**Software ID / RAM lock scan:**
- Scan for `$` preceded by `.`, digit, digit, `IsAlpha`. `IsAlpha` (2708) also accepts digits.
- The flag is the byte after the `$`.
- The software ID is the 12 bytes starting 13 bytes before the flag, read as ASCII.
- The Software ID scan runs to the end of the file; the RAM-lock scan stops at 0x10000.

**Hardcoded RPM limit** (1909–2032):
- Pattern 1 is `33 F9 ?? ?? ?? 00 00 00 ?? ?? 22 11`, where FF is a wildcard. The naive matcher restarts at 0 on a mismatch. The position is the end of the match, then:
  - +36 if bytes +4/+5 are `10 01`;
  - else +54 if bytes +22..+25 are `10 01 00 00`;
  - else +50.
- Pattern 2 is `48 E7 30 70 3F 3C 00 01 4E B9 00 04`; the position is the end of the match + 26, or −2 more if those two bytes are 00 00.
- Get reads pattern 1 only. When pattern 1 isn't found, it reads garbage at file offset 36/50/54 (bug).
- Set writes both positions only when both are > 0.

### Pgm_mod! flags

Read with `GetPgmStatusValue` (901) and written with `SetPgmStatusValue` (1074). The address is `Flash_start_address`, and the read wraps it by subtracting the file length.

| Byte | Mask | Property | Easy-dialog caption (group) |
|---|---|---|---|
| 0 | 01 | Afterstartenrichment | "Enrichment after start" (Enrichment) |
| 0 | 02 | WOTenrichment ¹ | "WOT enrichment" (Enrichment) |
| 0 | 04 | Interpolationofdelay | grid only |
| 0 | 08 | Temperaturecompensation ¹ | "Temperature correction" (Misc) |
| 0 | 10 | Lambdacontrol | "Lambda control" (Lambda control) |
| 0 | 20 | Adaptivity | "Adaptivity" (Adaption) |
| 0 | 40 | Idlecontrol | "Idle control" (Idle) |
| 0 | 80 | Enrichmentduringstart ² | "Enrichment during start" (Enrichment) |
| 1 | 01 | ConstantinjectiontimeE51 | "Constant injection (E51)" (Fuelling) |
| 1 | 02 | Lambdacontrolduringtransients | "Lambda control on transients" |
| 1 | 04 | Fuelcut | "Fuelcut in engine brake" (Fuelling) |
| 1 | 08 | Constantinjtimeduringidle | "Constant injection time" (Idle) |
| 1 | 10 | Accelerationsenrichment | "Acceleration enrichment" |
| 1 | 20 | Decelerationsenleanment | "Deceleration enleanment" |
| 1 | 40 | Car104 | never read or written |
| 1 | 80 | Adaptivitywithclosedthrottle | "Adaptivity with closed throttle" |
| 2 | 01 | Factortolambdawhenthrottleopening | "Correction for TPS opening" |
| 2 | 02 | Usesseparateinjmapduringidle ¹ | "Use idle injection map" (Idle) |
| 2 | 04 | FactortolambdawhenACisengaged | "Correction for engaging A/C" |
| 2 | 08 | ThrottleAccRetadjustsimultMY95 ¹ | grid only |
| 2 | 10 | Fueladjustingduringidle | "Fuel adjustment during idle" (Adaption) |
| 2 | 20 | Purge ¹ | "Purge control" (Misc) |
| 2 | 40 | Adaptionofidlecontrol | "Adaption of idle control" |
| 2 | 80 | Lambdacontrolduringidle | "Lambda control during idle" |
| 3 | 01 | Heatedplates | "Heatplates" (Car specifics) |
| 3 | 02 | AutomaticTransmission | "Automatic transmission" (Car specifics) |
| 3 | 04 | Loadcontrol ¹ | "Load control" (Fuelling) |
| 3 | 08 | ETS | "ETS/TCS" (Car specifics) |
| 3 | 10 | APCcontrol | "Boost control" (Misc) |
| 3 | 20 | Higheridleduringstart | "Higher idle during start" |
| 3 | 40 | Globaladaption | "Global adaption" |
| 3 | 80 | Tempcompwithactivelambdacontrol ¹ | "Temp. corr. in closed loop" (Misc) |
| 4 ³ | 01 | Loadbufferduringidle | "Load buffering during idle" (Idle) |
| 4 | 02 | Constidleignangleduringgearoneandtwo | "Fixed idle ignition gear 1&2" (Misc) |
| 4 | 04 | NofuelcutR12 | "No fuelcut in R12" (Fuelling) |
| 4 | 08 | Airpumpcontrol | "Airpump control" (Misc) |
| 4 | 10 | Normalasperatedengine | "Normally aspirated engine" (Car specifics) |
| 4 | 20 | Knockregulatingdisabled | "Knock detection OFF" (Misc) |
| 4 | 40 | Constantangle | grid only |
| 4 | 80 | PurgevalveMY94 | "Purge valve MY94" (Misc) |
| 5 ⁴ | 80 | VSSactive | "VSS enabled" (ECU specifics) |
| 5 | 10 | Tank_diagnosticsactive | "Tank pressure diagnostics" (ECU specifics) |

1. Read only when Pgm_mod is longer than 4 bytes (T5File 527–533), but **written always** (611–617). On a 4-byte Pgm_mod these seven flags always show off, and ticking one sets the bit.
2. **Bug:** read and written with mask **0x40** (T5File 937, 1109), the Idle control bit. "Enrichment during start" mirrors Idle control, and toggling it flips idle control. The Pgm_mod viewer decodes 0x80 correctly.
3. Byte 4 needs length ≥ 5 ("extended"). Otherwise the easy dialog disables these controls (`DisableAdvancedControls`), and SetTrionicOptions skips them.
4. Byte 5 needs length 6 (`sh.Length == 6`), the VSS options.

### Dialogs

`btnFirmwareOptions` picks the dialog by the setting "Show easy options screen" (`UseEasyTrionicOptions`, **default true**, frmSettings `checkEdit27`).

**Easy dialog** (`frmEasyFirmwareSettings`, title "Trionic firmware settings", Ok / Cancel; `EditTrionicSettingsEasyStyle` 5575):
- **Car specifics:**
  - Car model and Engine type are disabled.
  - "Injectors": Stock, Green giants, Siemens 630cc/min, Siemens 875cc/min, Siemens 1000cc/min.
  - "Mapsensor": 2.5 / 3.0 / 3.5 / 4.0 / 5.0 bar sensor.
  - "Turbo": Stock, TD04-15T, TD04-19T, GT28BB, GT28RS, GT3071R, HX35w, HX40w.
  - "Tuning stage": Stock, Stage 1..8, Stage X.
  - "Synchronization timestamp" is disabled, shown as `dd/MM/yyyy HH:mm:ss`.
  - The four Car specifics checkboxes from the flag table.
- **ECU specifics:**
  - CPU speed, Dataname, Software ID and "Trionic 5.5" are disabled.
  - Partnumber is editable. Its glyph button opens `frmPartnumberLookup`; Open or Compare there closes this dialog with Abort, and frmMain then opens or compares that file (5722).
  - VSS code, "VSS enabled", "RAM locked" (red text while checked), "Tank pressure diagnostics".
  - Text boxes get `MaxLength = current length`.
  - Without VSS options only "VSS enabled" and VSS code are disabled, and VSS code stays empty.
- Groups Enrichment, Fuelling, Idle, Adaption, Misc and Lambda control. "Lambda control" includes "Enable second lambda sensor": `IsTrionic55 = false` disables it **and unchecks it**. Because that runs after the value was set, OK on a T5.2 clears `Diag_mod` bit 0x80 if it was set (bug).
- **Turbo bug:** the combo index is cast straight to `CommonSuite.TurboType`, whose index 1 is GT17. Everything after Stock is off by one, and HX40w (8) / S400SX371 (9) have no list entry.

**OK copies back** (5651):
- Partnumber, VSS code, VSS enabled, Tank diagnostics, every visible flag, RAM locked and second O2.
- The four combos. Turbo, injector, map sensor and stage changes only write their markers. `LiftBoostRequestForTurboType` is empty (Trionic5Tuner.cs 4655), and the stage / map-sensor conversions are commented out.
- Car model, Engine type, Dataname and Software ID are *not* copied back.
- Then `SetTrionicOptions(props)`.

**Grid dialog** (`frmFirmwareSettings`, title "Trionic options", a PropertyGrid on `Trionic5Properties`):
- Categories: Adaption, Airpump, Boost, Car, Diagnostics, ECU, Enrichment, Fuelling, Idle, Ignition, Knock, Lambda control, Misc, Purge, Temperature, VSS/Immo. HardcodedRPMLimit has no attribute.
- Every property is editable. Enums show all their values, so turbo is correct here.
- Edits to CPUspeed, Carmodel, IsTrionic55, HasVSSOptions, ExtendedProgramModeOptions and Car104 are ignored.
- SoftwareID is read from the flash string but written to footer **0x02** (bug: the change never shows).
- OK → `SetTrionicOptions`.

### OK: `SetTrionicOptions` (T5File 573)

Re-reads the originals, then writes only the fields that changed, in this order:

1. Footer strings: engine type, partnumber, software ID (0x02), VSS code, dataname. Raw FileStream, no log.
2. VSS active and tank diagnostics (Pgm_mod byte 5), auto gearbox and heatplates (byte 3), then every flag. Each **changed flag rewrites the whole Pgm_mod symbol** through `WriteData` (2603). `WriteData`:
   - rewrites the symbol byte by byte;
   - stamps the sync date with `DateTime.Now` (`SetMemorySyncDate` → `WriteDataNoCounterIncrease`, which logs its own 7-byte entry);
   - adds a transaction entry at the **flash** address (unwrapped) with an empty note;
   - updates the checksum when AutoChecksum is on.

   So with a project open, every changed flag adds 2 transaction entries.
3. RAM lock (`writeramlockedflag` 1561):
   - writes 01 / 00 at the flag, only if it differs;
   - on T5.2 also writes `Write_protect!` = 01 / 00;
   - then `updatechecksum`, unconditionally. No log.
4. Second O2: `Diag_mod!` byte 0 bit 0x80 via `WriteData` (logged and timestamped; a missing symbol hits address 0, which `writebyteinfile` skips).
5. Markers: injector, turbo, stage, then (if edited in the grid) sync date, then map sensor. `writebyteinfile`, no log.
6. RPM limit (grid only), no log.
7. `updatechecksum`, **always**, whatever AutoChecksum is.

- Transaction entries need an open project (`SetTransactionLog`, 7179).
- Every write path silently skips files whose folder is `<StartupPath>\Binaries` (`FileInLibrary` 1424, `writebyteinfile` 3065). `WriteData` still logs its entry.
- No reload and no status-bar refresh.
- Note: every map save also goes through `WriteData` (1839, 1860), so "Synchronization timestamp" is really "last write through WriteData".

### Check for anomalies (`CheckForAnomalies`, T5File 426)

**Report format:** a one-column DataTable "Description" shown with `TuningReport`:
- title "Anomaly report";
- the fixed subtitle "Changed parameters in binary or actions taken" (CommonSuite/Reports/TuningReport.Designer.cs 80);
- one line per row and a "Page" footer.

**Rows, in this order:**
```
Checking file <file name>
Checking injection map against fuel knock map
  per hit: Found anomaly! Fuel injection map value larger than or equal to knock map
           --> pressure = <p> bar, rpm = <rpm>
Checking injection constant value
  Found anomaly! Injector constant has an invalid value: <b>
Checking boost request maps agains boost limiters            (sic)
  per hit: Found anomaly! Boost request value higher than boost limiter (fuel cut value) in Tryck_mat
           --> row: <r> column: <c>                        (same pair for Tryck_mat_a)
Checking axis against maximum requested boost level
Maximum boost request: <F2> bar
  per axis: <Symbol!> does not support the maximum boost request value!
""
""
```

**Rules:**
- **Knock fuel:**
  - `Insp_mat!` is 16 rows × len/16 columns; `Fuel_knock_mat!` is 16 × len/16.
  - For each knock cell, take the `Fuel_map_xaxis!` column closest to `Fuel_knock_xaxis![col]`.
  - Anomaly if insp ≥ knock.
  - p = knock x × 0.01 − 1 (Tryck_mat factor and offset; no map-sensor correction).
  - rpm = `Fuel_map_yaxis!` u16 BE[row] × 10.
  - p is formatted in the current culture.
  - No fixes: `fixproblems = false`.
- **Injector constant:** `Inj_konst!` byte 0 ≤ 5 or > 25.
- **Boost limiter:**
  - rows = `Pwm_ind_rpm!` length / 2; cols = `Tryck_mat!` length / rows.
  - For each byte f of `Tryck_vakt_tab!` and each column, anomaly if `Tryck_mat[f*cols+c]` ≥ limit (and the same for `Tryck_mat_a!`).
  - No bounds check and no try/catch.
- **Axes:**
  - max = the highest byte of Tryck_mat! and Tryck_mat_a!.
  - The "Maximum boost request" line converts max with the map-sensor factor (`GetMapSensorType(true)`) and then /100 − 1.
  - Anomaly if no value of the axis is ≥ the raw max:
    - u16 BE: `Detect_map_x_axis!`, `Ign_map_0/2/3/6/7_x_axis!`, `Misfire_map_x_axis!` (skipped when shorter than 2);
    - 8-bit: `Fuel_knock_xaxis!`, `Fuel_map_xaxis!`.
  - A missing symbol has address 0 and length 1, so it reads file byte 0.
- `Trionic5Anomalies.cs` is a near copy used only by the tuning wizard (Trionic5Tuner.cs 3484):
  - it has no "Maximum boost request" line;
  - it wraps the axis check in try/catch;
  - its fix path writes through its own `savedatatobinary`.

### Examine binary (11141)

`TuningReport`, title "Examination report", via `ShowPreview`. Rows:
1. "", "Report for file: <name>", ""
2. "File type: Trionic 5.2" or "File type: Trionic 5.5"
3. "CPU speed: …", "Data name: …", "Engine type: …", "Partnumber: …", "Software ID: …", then "SRAM is locked" or "SRAM is unlocked".
4. **Stage** (`DetermineTuningStage` T5File 686):
   - b = max(Tryck_mat!, Tryck_mat_a!) × factor (3.0: 1.2, 3.5: 1.4, 4.0: 1.6, 5.0: 2.0), using `GetMapSensorType(true)`; then /100 − 1.
   - Thresholds: ≤1 Stock, ≤1.16 Stage 1, ≤1.26 Stage 2, ≤1.36 Stage 3, ≤1.5 Stage 4, ≤1.72 Stage 5, ≤1.81 Stage 6, ≤2.0 Stage 7, else Stage 8. Stage X is never detected.
   - Row "Stage: stock" or "Stage: N".
   - Stages 3+ add tab-indented "\tRequires: …" lines and a blank row:
     - 3: 3'' turboback exhaust, BCPR7ES plugs.
     - 4: + upgraded intercooler.
     - 5: + upgraded pressureplate, GT28 turbo or better.
     - 6/7: + upgraded fuelpump, wideband lambda, EGT gauge, GT3071r .64 / .86 turbo or better, BCPR8ES plugs.
     - 8: + tubular exhaust manifold, HX40 hybrid (super) turbo or better, BCPR8ES plugs.
5. "Boost request peak: <F2> bar"
6. "Mapsensor type: stock 2.5 bar sensor" / "3.0 bar sensor" / "3.5 bar sensor" / "4.0 bar sensor" / "5.0 bar sensor". Auto-detect (`DetermineMapSensorType` 759) only when the marker says 2.5:
   - stock when the last byte of `Fuel_map_xaxis!` is 240 or 224;
   - else `Tryck_mat!`[0x78] (needs ≥ 0x80 bytes): 10 → 5.0, 12 → 4.0, 14 → 3.5, 16 → 3.0.
7. **Fuel:**
   - inj = max byte of `Inj_map_0!` (or `Insp_mat!` without it) × `Inj_konst!`.
   - s = last byte of `Fuel_map_xaxis!` × the same factor, /100 − 1.
   - inj = inj × (int)(1.4/s × 100) / 100.
   - E85 if inj > 7500, or Inj_konst > 26, or `Eftersta_fak!` (15 bytes)[13] > 170. Then inj = inj × 10/14 and the row is "Probable fuel: E85".
   - Otherwise "Probable fuel: Premium quality petrol" when peak > 1.1, else "Probable fuel: Petrol".
8. **Injectors:** >5000 "Injectors: stock"; >3500 "Injectors: Green giants (413 cc/min)"; >2000 "Injectors: Siemens deka 630 cc/min"; >1565 "…875 cc/min"; else "…1000 cc/min".
9. **APC valve:** "APC valve type: Trionic 5" when:
   - T5.2: `Frek_230!` == 728 or `Frek_250!` == 935;
   - T5.5: 90 / 70.

   Otherwise "APC valve type: Trionic 7".

### Open a saved report (11086)

- OpenFileDialog "Reports|*.prnx". The file goes to DevExpress `PrintingSystem.LoadDocument` and opens in `PrintPreviewFormEx`.
- `.prnx` files are saved from any report preview's DevExpress save button. It is a proprietary DevExpress document format.

### Pgm_mod viewer (`frmEasyFirmwareInfo`, also titled "Trionic firmware settings")

- **Openers:**
  - `StartTableViewer` (1373), when the symbol is `Pgm_mod!`. Primary: "ECU <name>" from SRAM when online, else "BIN <name>". Secondary: "SRAM <name>" when an SRAM file is loaded, else the group is disabled.
  - The compare results (4993) and SRAM compare (6415): primary is the open bin, secondary the other file.
  - `Pgm_status` opens nothing (TODO).
- Two groups, "Primary source: …" and "Secondary source: …", with the same checkbox captions as the easy dialog.
- It decodes the full table above, with the correct 0x80 for enrichment during start; interpolation, MY95, Car104 and constant angle are not shown.
- It disables the byte-4 and byte-5 boxes for short arrays and paints each differing pair orange.
- Checkboxes can be toggled but nothing is saved; "Ok" closes.

### Not part of this area / dead
- `Trionic5Immo.cs` is the **software licence** check (TripleDES/MD5 over HWID, registry `HKCU\Software\T5SuitePro\ImmoID`), not the car immobiliser. It is disabled: `_immoValid = true` (309). Don't port.
- `IECUProperties` is only the abstract base of `Trionic5Properties`.
- `Trionic5Resume` is a one-column "Description" DataTable for tuner and transfer reports.
- `Trionic5FileInformation` has no display of its own (`GetProgramModeSymbol` = "Pgm_mod!", `GetProgramStatusSymbol` = "Pgm_status").
- `ProgramModeSettings` belongs to autotune.

## ECU connection (T5)

T5 does not use KWP2000 or GMLAN. The ECU runs a byte "terminal" on the 615 kbit P-bus (IDs 0x005 / 0x006 / 0x00C). There's no session, no security access and no keep-alive. "Connected" means the adapter is open and the version string came back non-empty. Old code: `T5Suite2.0/ECUConnection.cs` (EC) wrapping `T5CANLib/T5CAN.cs` (T5CAN). The library today is `Trionic/TrionicCANLib/Trionic5.cs` (TR5).

### Wire protocol (both libraries)

| What | Frame | Reply |
|---|---|---|
| Read 6 bytes | 0x005 `C7 A3 A2 A1 A0 00 00 00`. The address is the **last** of the 6 bytes, so the caller sends start+5 (T5CAN:672, TR5:1082) | 0x00C `C7 xx m[a] m[a-1] … m[a-5]`. It's reversed into `retData[0..5]` = m[a-5..a]. A timeout gives an empty message, i.e. **zeros, no error** |
| Terminal char (`s`, `S`, CR) | 0x005 `C4 <ch> FF FF FF FF FF FF` (sendCommandByte, T5CAN:823) | 0x00C `C6 xx <ch> …` per char, acked with 0x006 `C6` |
| Write 1 byte | `W` + 4 hex address digits + 2 hex data digits + CR. Each char is a frame 0x006 `C4 <ch>` (T5CAN:478/848) | each answered 0x00C `C6`. waitNoAck only logs a mismatch, so a write never fails |
| Bootloader (MyBooty S19, embedded) | A5 address/len, data frames, C0 erase, C1 jump, C2 exit/reset, C3, C8 checksum, C9 chip types | 0x00C, first byte echoed |

- **Speed:** a read gets 6 bytes per round trip. A write costs 8 round trips per byte.
- **Addresses:** SRAM addresses are 16 bit (`ushort`, so they wrap at 0xFFFF). Flash is 0x40000-0x7FFFF (T5.5) or 0x60000-0x7FFFF (T5.2), readable only while the bootloader runs.

### Adapters

The setting is "CAN USB device" (frmSettings.Designer:843): Lawicel, DIY, CombiAdapter, Just4Trionic, Kvaser. The default is `"Lawicel"` (T5AppSettings:1175). A stored `"Multiadapter"` is mapped to `"CombiAdapter"` (1183). The device is picked in EC.OpenECUConnection (763):

| Setting | T5CANLib device | TrionicCANLib `CANBusAdapter` |
|---|---|---|
| Lawicel (and anything unknown) | CANUSBDevice | LAWICEL "Lawicel CANUSB" |
| CombiAdapter / Multiadapter | LPCCANDevice_T5 | COMBI |
| Just4Trionic | Just4TrionicDevice | JUST4TRIONIC |
| Kvaser | KvaserCANDevice (canlib32.dll) | KVASER "Kvaser HS" |
| DIY | MctCanDevice: the Mictronics CAN-USB through **mct_can.dll** (Windows native) | none |
| none | none | ELM327, J2534, SLCAN are new for T5 |

- T5Suite had no adapter-name choice. There was no `SetSelectedAdapter`.
- **`_tcan` and the device are created once, on the first connect.** A later adapter change in Settings is stored in `CanusbDevice` but does nothing until a restart.
- **Lawicel only, first connect only:** `CheckCanwakeup` (frmMain:2195) runs `WakeupCANbus.exe` from the exe folder, hidden, with a 10 s timeout, then kills it. All it does is open a T5CAN on a CANUSBDevice and exit: a driver workaround.
- The CAN logging setting maps to `EnableCanLogging` → `_usbcandevice.EnableLogging(exe dir)` (frmMain:2811).

### Connecting

Buttons:
- "Online tuning" → Basic actions → **Connect ECU**.
- **Go online** (`btnSwitchMode`) and **Switch mode [ SHIFT + F1 ]** both connect first when needed, then start online (realtime) mode.

`StartECUConnection` (frmMain:1973):
```
btnSwitchMode "Connecting..."; Lawicel → CheckCanwakeup
EC.OpenECUConnection: _opened = true; create _tcan/device once; openDevice (adapter open only; T5CAN prints
  "Open called/failed/succeeded in T5CAN"); fails → _opened = false. Never talks to the ECU.
IsT52 = (file length == 0x20000)            // decides which version command is used
swversion = IsT52 ? getSWVersionT52 ("S"+CR) : getSWVersion ("s"+CR); trimmed, ">" removed, last 12 chars
"" → btnSwitchMode "Not connected", CloseECUConnection(true), offline, return false
btnSwitchMode "Connected: <sw>"; status bar " ECU: <sw>"; symbol grid filter cleared (SRAM symbols shown)
file sw ≠ ECU sw (or no file) → status "Getting symboltable...", EC.GetSymbolTable (result DISCARDED, see below),
  FillRealtimePool(Fuel), status "Monitoring..." / "Idle"
sync-date check (below) → frmSyncFileECU
```

- **Failure box:** "Failed to open canbus connection!" (2167, 2366).
- **No file loaded:** the else-branch reaches `m_trionicFile.GetMemorySyncDate()` with `m_trionicFile` null and throws.

**Connect ECU button** (2151):
- **Not opened:** connect. The caption becomes "Disconnect ECU", or goes back to "Connect ECU" on failure.
- **Opened:** if the caption is "Connect ECU" it becomes **"Disonnect ECU"** (sic) and the mode goes online. Otherwise `StopOnlineMode` runs and the caption becomes "Connect ECU". `UpdateOnlineOffLineTexts` (2413) writes "Disconnect" instead.
- **Disconnect never closes the device:**
  - `CloseECUConnection(false)` only stalls polling: "<GS-22032010> never close the connection" (EC:854).
  - The device closes only on app exit (frmMain:4181, then a 1 s sleep) or after an empty version read.
  - `T5CAN.Cleanup` sends C2 before closing (T5CAN:1073). `TR5.Cleanup` does not, and it nulls the device (TR5:1455).

**StopOnlineMode** (2418):
- Switches autotune off, hides the realtime dock and stops monitoring.
- When `KnockCounterSnapshot` is on (default off) on T5.5, it reads `Knock_count_map`. If that is 576 bytes, it saves it as hex text to `<project>\Snapshots\Knockmap<MMddyyyyHHmmss>.KNK`, or to `<bin dir>\Snapshots\…`.

**SetOnlineButtons(bool)** (2370) enables:
- Synchronize maps, Write log marker, Clear knock counters, the two flash actions, Download SRAM, Upload SRAM, Compare ECU with binary, Read DTC codes;
- the three CANBUS buttons on "ECU programming".

These are enabled only while connected.

### Symbol table from the ECU, and "symbols match file"

- **`EC.GetSymbolTable` (887):**
  - The ECU's `S` command dumps its symbol table as text until `END\r\n`, one char per CAN frame (slow).
  - Line format: `AAAALLLLname` (hex SRAM address, hex length, name). Lines starting with `>` and the `END` line are skipped.
  - The text is cached as `<exe dir>\<swversion>.symbollist`, or `bad_name.symbollist` if the write throws. Later connects read the cache.
- **frmMain throws the DataTable away** (2035/2051). The only effect is the cache file, which nothing reads.
- **There is no "symbols match file" check:**
  - A version mismatch is not reported.
  - Every SRAM read and write uses the **file's** `Start_address`.
  - A bin that doesn't match the ECU silently reads and writes the wrong SRAM.

### SRAM symbols

**Addresses.**
- `Start_address` is the 16-bit SRAM address from the bin's symbol table.
- `Flash_start_address` comes from the bin's SRAM→flash address lookup table, matched by SRAM address (Trionic5File:6541). That's the file-format section.
- `GetSymbolAddressSRAM` and `GetSymbolLength` return **0 for a missing symbol** (Trionic5FileInformation).

**Reading.**
- `ReadSymbolData(name, addr, len)` (EC:1078): sets `_prohibitRead`, then `readRAM((ushort)addr, len)`.
- When not opened it returns a **1-byte {0}**. Callers detect that only by its length.
- `…NoProhibitRead` (1098) leaves the flag alone.

**Writing.**

| Writer | Library call | Then | Used by |
|---|---|---|---|
| `WriteSymbolData` (EC:1160) | `writeRam`: reads the range first, sends only the changed bytes; progress events when > 16 bytes | 20 ms, `SetMemorySyncDate(Now)` in the ECU, 20 ms | viewer Save (1822), Clear knock counters (`Knock_count_map`) |
| `WriteSymbolDataForced` (EC:1141) | `writeRamForced`: every byte | 20 ms | SyncMaps → ECU, Upload SRAM, knock cylinder counters, DTC clear, `Pgm_mod!` buttons |

Both do nothing when not opened, and return nothing.

**ECU sync date (T5Suite's own marker).**
- Location: SRAM 0x7FC0..0x7FC6 = year hi, year lo, month, day, hour, minute, second (EC:298/397).
- It is read with 7 single-byte `readRAM` calls and retried once when the date is invalid.
- An unreadable or invalid date gives 2000-01-01 00:00:00.
- The file has the same 7-byte layout at `filelength − 0x1E0` (Trionic5File:2265/2305).
- Every `Trionic5File.WriteData` stamps the file with Now. `WriteDataNoCounterIncrease` and `WriteDataNoLog` don't.
- The 8-byte counter at 0x7FD0 (file: `len − 0x1FC`) is dead code (its calls are commented out).

**Map viewers.**
- **Open:** a viewer started while `Opened && _ECUmode == Online` reads its data from SRAM and sets `OnlineMode` (frmMain:1690).
- **Read from ECU** (`mv_onReadFromSRAM`, 1767): data from the file, or from SRAM when online, or from the .RAM file when the viewer's file ends in "RAM".
- **Save** (1822): one action. When online it does `WriteSymbolData` to SRAM, and it **always** does `WriteData` to the file (stamping both dates). T5 has no separate "Save to ECU".
- **Symbol list:**
  - Double-click (1961) and Enter (4747) show "Symbol resides in SRAM and you are in offline mode. T5Suite is unable to fetch this symboldata in offline mode" when offline with no .RAM loaded.
  - The two tests differ: double-click tests `Flash_start_address == 0`, Enter tests "name has no `!`".

### Synchronization

**Dialog `frmSyncFileECU`.**
- Title "Data synchronization", group "Synchronization information".
- Rows "Timestamp binary" / "Timestamp ECU", formatted `dd/MM/yyyy HH:mm:ss`.
- Info "Proposed sync: ECU to binary" or "Proposed sync: binary to ECU".
- Buttons: **Accept** = OK = the proposed direction; **Decline** = Cancel = nothing; **Reverse** = Retry = the other direction.

**On connect (2063), and again on Go online (2286) unless the connect already asked (`_syncAskedForECUConnect`):**
```
dt_ecu == 2000-01-01 → dt_ecu = now, written to ECU;  dt_file == 2000-01-01 → dt_file = now, written to file
dt_ecu > dt_file → propose ECU→binary: Accept: SyncMaps(ToFile), file date = dt_ecu; Reverse: SyncMaps(ToECU), ECU date = dt_file
dt_file > dt_ecu → propose binary→ECU: Accept: SyncMaps(ToECU), ECU date = dt_file; Reverse: ToFile, file date = dt_ecu
```
Old quirk: an ECU that was never stamped (or whose SRAM was lost) gets "now", so it is proposed as **newer** than the bin.

**Synchronize maps button** (7030):
- No dialog: the direction comes from the dates.
- Equal dates → "Synchronization not needed". Not connected → "No connection to ECU available".

**`SyncMaps(dir)` (6938):**
```
ProhibitRead; project → logbook SynchronizationStarted(dir)
for every symbol with Flash_start_address > 0 (and Start_address > 0, Length > 0):
  status "Sync: N%" + progress; stop if _ECUmode went offline
  ecu = readRAM(Start, Len); bin = file bytes at Flash_start_address
  same length and different: ToECU → writeRamForced(all bytes); ToFile → WriteDataNoCounterIncrease (no transaction log)
status "Synchronized"
```
That's every calibration symbol, read byte-for-byte over CAN: slow, but it's the only "is the ECU equal to my file" check.

### Online tuning page: other actions

The "Advanced actions" group also contains Write log marker, Configure realtime panel, Toggle autotune and Browse tunes, which belong to other sections.

| Caption | Behaviour |
|---|---|
| **Download SRAM from ECU** (also "CANBUS" on ECU programming) (3335) | **Not connected:** "A canbus connection is needed to create a SRAM snapshot".<br>**Project:** saved straight to `<ProjectFolder>\<project>\Snapshots\Snapshot<MMddyyyyHHmmss>.RAM`.<br>**No project:** Save dialog "SRAM snapshots\|*.ram" in the bin folder, named `Snapshot-<bin>-<MMddyyyyHHmmss>.RAM`.<br>**Dump:** `DumpSRAMContent` = `readRAM(0, 0x8000)`, a raw 32 KB image where file offset = SRAM address. Texts "Downloading adaption data..." / "Adaption data saved...". |
| **Upload SRAM to ECU** (10867) | **Needs** a file and a connection (silent otherwise).<br>**Open dialog:** "SRAM snapshots\|*.ram" in Snapshots or the bin folder.<br>**Question:** "Do you want to write to the current binary file as well?" ("Question", Yes/No).<br>**Status:** "Restoring ECU state...".<br>**Per symbol** with `Start_address > 0 && Flash_start_address > 0`: `Length` bytes at `Start_address` of the .RAM file → `writeRamForced`. Yes → also `WriteDataNoLog` to the bin.<br>**Then:** ECU date (and file date on Yes) = now; status "Idle". |
| **Compare ECU with binary** (10926) | `DumpSRAM("Snapshot-<bin>-<ts>.RAM")`, a **relative path** (the process's working directory), then `StartCompareToSRAMFile`. Silent when not connected. |
| **Clear knock counters** (11067) | Online only, with no confirmation or message.<br>`Knock_count_map` is zeroed through `WriteSymbolData`. `Knock_count_cyl1..4` get 2 zero bytes each through the forced writer. A missing symbol writes at SRAM 0. |
| **Read DTC codes** (11463) | **Needs** file and connection (silent otherwise).<br>**Read:** every 1-byte symbol whose name contains `_error` or `_fel` and whose value is > 0 → rows (Symbol, Value).<br>**Window:** modeless `frmDTCCodes` (Trionic5Controls): title "DTC codes", group "Active errors", buttons "Ok" / "Clear codes".<br>**Clear** (11496): writes 0 to every 1-byte symbol whose name **ends** with `_error` and whose `Start_address > 0`. A failure shows "Failed to clear errorcounter: <name>. <msg>". The list is re-read with `_error` only, so `_fel` rows drop off without being cleared. |
| **Download flash from ECU** / **Upload flash to ECU** ("Flash actions", and "CANBUS" on ECU programming) | See the next table. Both are silent no-ops when not connected. |

There's no OBD-II / KWP DTC on T5: "DTCs" are SRAM error counters.

### Flash over CAN

**Old flow (T5CANLib).** Both actions block the GUI thread with polling paused (`_prohibitRead`, 100 ms sleep).

| Step | Download (`DownloadFlashCANUSB` 3304 → EC.ReadFlash → `DumpECU(file)` T5CAN:1939) | Upload (`UploadFlashCANUSB` 2571 → EC.ProgramFlash → `UpgradeECU(file, BinaryFile, type)` T5CAN:2156) |
|---|---|---|
| Dialog | Save "Flash files\|*.bin". The type dialog is commented out. | Open "Flash files\|*.bin" (**any** file, not the current one), then frmECUTypeSelection |
| Bootloader | `UploadBootLoader`, result ignored | type check first; then `UploadBootLoader`, result ignored |
| Type | C9 chip types: `[4] == 0x04` → T5.5 (0x40000, 256 KB), else T5.2 (0x60000, 128 KB). A T5.5 holding a T5.2 bin is dumped as 256 KB. | from the dialog. Length must be 0x20000 for T5.2 and **0x40000 for everything else**. Autodetect/Unknown → InvalidECUType. Both are returned silently. |
| Work | 6-byte C7 reads with progress; the tail is fetched by reading back from the end | `VerifyChecksum` (info only) → EraseFlash → `ProgramFlashBin` (0x80-byte A5 blocks, 7 bytes per frame; the T5.2-into-T5.5 double copy is unreachable because of the length check) → VerifyChecksum |
| End | `ExitBootloader` (C2), then the file is written. No checksum check, and the file isn't opened. | `ExitBootloader` only on success or a checksum failure. An erase or program failure leaves MyBooty running. **The UpgradeResult is discarded**: the user sees only the last status text. |
| Texts | "Determining ECU type", "Downloading flash from ECU", "Finished downloading flash from ECU", "ECU is reset and new program is executing..." | "Erasing FLASH..." / "FLASH erased... " / "Could Not Erase FLASH !!! <bytes>"; "Start upload of new program..." / "Flash programming finished" / "FLASHing Failed after: 0x<n> Bytes !!!"; "FLASH Checksum OK: <8 hex>" / "Checksum FAIL !!! <bytes>" / "Could NOT Determine Checksum !!!" |

- **Captions and progress:** info texts go to the status bar. Progress goes to the task bar; DumpECU reports as *write* progress, so the bar is never hidden.

**frmECUTypeSelection** ("ECU type selection"):
- Items: "Trionic 5.2", "Trionic 5.5 [ 16 MHz AMD and Intel ]", "Trionic 5.5 [ 16 MHz Catalyst ]", "Trionic 5.5 [ 20 MHz ]", and the default "Select your ECU type first!!!".
- The index is used as `ECUType` (0..4), so OK on the default passes Autodetect, which fails silently.

**What `Trionic5` (TrionicCANLib) does instead.** This is what the T5 CAN Flasher uses.
- **`WriteFlash(file)` → `WriteFlashResult` Done/Cancelled/Failed (TR5:317):**
  - Detection: uploads MyBooty and checks the C9 reply ("!!! ERROR !!! The bootloader is not answering, this attempt did not touch the FLASH"). It reuses a bootloader still running from a failed attempt.
  - The type comes from `DetermineECU` (chip id + footer ROM offset → T52ECU / T55ECU / T55AST52 / Unknown). There is no type dialog.
  - Conversions ask through `UserPrompt.AskYesNo`: "Do you want to upload a T5.2 BIN file to your T5.5 ECU" / "Do you want to upload a T5.5 BIN file to your ECU that has been used as a T5.2?" ("ECU Conversion Question").
  - It reports "!!! SUCCESS !!!" plus `GetECUInfo`, or a FAILURE text plus the advice lines ending "Don't switch the ECU off before you retry !!!".
- **`DumpECU(null, DoWorkEventArgs(file))` (TR5:2300):**
  - `Result` is true/false. It reads 128 KB for T5.2 and T55AST52, otherwise 256 KB.
  - It runs the C8 checksum check and `ChecksumT5.ValidateDump`. "It seems this dump is broken. Not saving file.." is shown, but the file **is still saved** (the flag is commented out).
  - It writes a `.md5` next to the file, then sends C2.
- **`GetSRAMSnapshot(file)`:** the same 32 KB as DumpSRAMContent.
- **`GetECUInfo(true)`:** footer fields (Part Number, Software ID, SW Version, Engine Type, IMMO Code, ROM Start/End, Code End), then a reset.
- **`ResetECU()`:** a manual reset.
- **Open/close texts:** `openDevice()` says "Open called in Trionic 5" / "Open failed in Trionic 5". `getSymbolTable`, `getSWVersion(T52)`, `readRAM`, `writeRam` and `writeRamForced` are byte-identical to T5CAN except for `FlushQueue`.
- **Failure reporting:** `readRAM` still returns zeros on a timeout, and the writes still always return true.

### ECU programming page: BDM (not portable)

- **"P&&E Micro BDM"** group: Settings, Read ECU, Brute force erase, Program ECU [AMD] / [Intel] / [Atmel] (9866-10095).
  - All of it runs user-configured `.bat` files for P&E's Windows tools.
  - **Settings dialog** `frmPeMicroParameters`, "Set PE Micro interface parameters": batch file for reading, the result file when reading, and batch files for programming AMD/CSI, Intel and Atmel and for brute force erase.
  - **Read:** runs the batch and waits. It copies `FROM_ECU.S19` to `<bin dir>\FROM_ECU<yyyyMMddHHmmss>.S19` (or to the target file) and opens it.
  - **Program:** `UpdateChecksum`, bin→S19 (SrecordT5), copied to `<batch dir>\TO_ECU.S19`, then the batch is started without waiting.
  - **Brute force erase** checks that the *Intel* batch file exists before it starts the erase batch (bug).
  - "Batch file not found. Check parameters".
- **"DIY USB BDM"** group (8358/8522/9039): Download flash / Upload flash / Download SRAM from ECU through **usb_bdm.dll** (`BdmAdapter_Open/GetVersion/DumpECU/EraseECU/FlashECU/ReadSRAM`, P/Invoke, Windows only).
  - **Type dialog** `frmECUBDMTypeSelection`: "Trionic 5.2", "Trionic 5.5 ", "Trionic 5.5 (AMD 29F chips)", "Trionic 7", and the default "Select your ECU type first!!!", which cancels. The choice becomes `ecu_t`.
  - **Messages:** "Could not connect to the BDM adapter", "BDM adapter is not compatible", "Failed to dump ECU", "Failed to download firmware from ECU: <msg>" / "Failed to program ECU: <msg>".
- **CAN instead:** the CANBUS group covers read flash, write flash and the SRAM image. A BDM-only rescue (bricked ECU, brute-force erase) stays outside the suite: the T5 CAN Flasher's retry-with-running-bootloader covers the half-written case.

### frmMultiAdapterConfig

This is the Combi/Multiadapter extra-inputs dialog, opened from Settings.
- **Inputs:** ADC1-5, each with a "use" checkbox and a channel name; the button opens frmADCInputConfig (name, low/high voltage, low/high value). Thermo has a checkbox and a name.
- **Storage:** the values are written straight into `T5AppSettings` (Adc*channelname/lowvoltage/highvoltage/lowvalue/highvalue, Useadc*, Usethermo, Thermochannelname).
- **Use:** the realtime poller reads them only when the device is "Multiadapter"/"CombiAdapter" (EC:582). The conversion is `ConvertADCValue`, which is the realtime section's concern.

### Threading (old)

- **Polling:** a `System.Timers.Timer` at 10 ms (EC:553) polls `SymbolsToMonitor` with `readRAM` on the thread pool. `Knock_count_cyl*` are forced to length 2.
- **Pausing:** the GUI thread pauses it with unsynchronized flags (`_prohibitRead`, `_stallReading`) plus 50-100 ms sleeps. T5CAN serializes only `readRAM`, `writeRam` and `writeRamForced` (`lock(this)`). Version and symbol-table commands aren't locked.
- **Flash work:** flash reads and writes block the GUI thread.
- **Dead code:** the emulation-mode engine simulator (never enabled, frmMain:352) and the `_sramDumpFile` read/write paths.

## Offline tuning: map buttons, tuning wizards, compare, transfer and library

Paths: `frmMain.cs` = `T5Suite2.0/frmMain.cs`, `Designer` = `T5Suite2.0/frmMain.Designer.cs` (Windows-1252), `Tuner` = `Trionic5Tools/Trionic5Tuner.cs` (Windows-1252; already lifted to `T5Core/Trionic5Tuner.cs`), `T5File` = `Trionic5Tools/Trionic5File.cs`, `Controls` = `Trionic5Controls/`.

### How T5 writes: four paths (matters for every wizard)

| Path | Who uses it | Transaction entry (open project) | Checksum |
|---|---|---|---|
| `IECUFile.WriteData` (T5File 2603) | E85, Rpm_max!, Excel import, transfer, map viewer saves | yes | only if Settings "auto checksum" (`m_autoUpdateChecksum`); also stamps the sync date (len − 0x1E0) |
| `WriteDataNoLog` (T5File 2586) | injector wizard | no | only if auto checksum |
| `WriteDataNoCounterIncrease` (T5File 2562) | footer values (boost adaption, divisor) | yes | only if auto checksum |
| raw file I/O (`writebyteinfile` / `savedatatobinary`, Tuner 1162 / 1545; T5File `writebyteinfile`) | every Tuner routine (stage tune, map sensor, free tune, code patches), hardcoded RPM limit | **no** | none of its own; callers below update it explicitly or not at all |

Checksum (`updatechecksum`, T5File 5022): byte sum of 0 … (0xFE footer marker − 3 + 4), stored big-endian at len − 4, written without asking; skipped when the file sits in `<StartupPath>\Binaries` (`FileInLibrary`, T5File 1424). No wizard refreshes open map viewers.

**T5 footer markers** written by the wizards (all relative to file length; T5File 544-547, 2189-2245, 2431-2494, Tuner 1049-1145):

| Offset | Size | Meaning | "Unset" (0xFF / 0xFFFF) means |
|---|---|---|---|
| len − 0x200 | 1 | tuned-to-stage (TuningStage) | 0 |
| len − 0x1FF | 1 | map sensor: 01 3.0, 02 3.5, 03 4.0, 04 5.0 bar | 2.5 bar (stock) |
| len − 0x1FE | 1 | injector type | stock |
| len − 0x1FD | 1 | turbo type | stock |
| len − 0x1F0 / 0x1EE | 2 BE | boost adaption manual rpm low / high | 2750 / 4000 |
| len − 0x1EC / 0x1EA | 2 BE | boost adaption automatic rpm low / high | 2750 / 4500 |
| len − 0x1E8 | 2 BE | max boost error (× 0.01 bar) | 4 |
| len − 0x1E4 | 2 BE | boost bias axis step (divisor) | 10 |

### Manual tuning ribbon page

Page "Manual tuning" (Designer 1945-2030). Every button is `StartTableViewer(<symbol getter>)` (frmMain 3046-3115, 3781-3830, 9821-9850); a missing symbol gives "<symbol> is not present in the current file" (frmMain 1287). Symbols from `Trionic5FileInformation` (152-600). Captions never change; T5 has no DynamicTuningMenu.

| Group | Caption | Symbol |
|---|---|---|
| Injection [ Fuel ] | VE map - normal | `Inj_map_0!` if the bin has it, else `Insp_mat!` (584) |
| | VE map - knock | `Fuel_knock_mat!` |
| | Injector scaling | `Inj_konst!` |
| | Battery correction map | `Batt_korr_tab!` |
| Ignition | Ignition map - normal / - knock / - warmup | `Ign_map_0!` / `Ign_map_2!` / `Ign_map_4!` |
| Turbo control - manual gearbox | Boost request map | `Tryck_mat!` |
| | Boost control bias | `Reg_kon_mat!` |
| | Fuel cut in overboost | `Tryck_vakt_tab!` |
| | P / I / D factors (PID) | `P_fors!` / `I_fors!` / `D_fors!` |
| | Boost limit in 1st gear / 2nd gear | `Regl_tryck_fgm!` / `Regl_tryck_sgm!` |
| Turbo control - automatic gearbox | Boost request map, Boost control bias | `Tryck_mat_a!`, `Reg_kon_mat_a!` |
| | Boost limit in 1st gear | T5.2 (len 0x20000): `Regl_tryck_fga!`, else `Regl_tryck_fgaut!` (218) |
| | P / I / D factors (PID) | `P_fors_a!` / `I_fors_a!` / `D_fors_a!` |
| Knock detection | Knock sensitivity map | T5.2: `Knock_ref_tab!`, else `Knock_ref_matrix!` (537) |
| | Ignition retard limit | `Knock_lim_tab!` |
| | Boost reduction map | `Apc_knock_tab!` |
| Engine warmup | Afterstart enrichment (1) / (2) | `Eftersta_fak!` / `Eftersta_fak2!` |
| Idle control | Idle target RPM, Idle ignition, Idle ignition correction, Idle fuel map | `Idle_rpm_tab!`, `Ign_idle_angle!`, `Ign_map_1!`, `Idle_fuel_korr!` |

Enable rules after open (frmMain 930-988, `EnableT55Maps` 1060): T5.2 disables "Boost limit in 1st gear" and "Boost limit in 2nd gear" (manual) and "Ignition retard limit". `EnableT55Maps(false)` also disables "Change boost adaption ranges", "Change boost bias range" and "Change RPM limit", but the open code re-enables them a few lines later (bug: they stay enabled on T5.2). A user "My Maps" page (`LoadMyMaps`, 12892) is inserted as ribbon page 3 (mymaps.xml, same as T7/T8).

### Tuning wizards ribbon page

Page "Tuning wizards" (Designer 2036-2060). All buttons start disabled and are enabled on open (frmMain 970-988), disabled on close (8188-8200). "Change boost bias range" is enabled only when `Has2DRegKonMat()` (Reg_kon_mat! length ≠ 0x80, i.e. the 16-bit table). Settings "advanced mode" off hides the "Advanced tuning wizards" group (frmMain 3014-3034).

| Group | Caption | Visible | Handler |
|---|---|---|---|
| Basic tuning wizards | Tune me up ® | yes | `RunTuningWizard` 6689 |
| | Tune binary to stage II / III | no | 6787 / 6822 |
| Advanced tuning wizards | Tune binary to stage X | no | 9103 |
| | Convert to different MAP sensor ▸ "Convert to 2.5 / 3.0 / 3.5 / 4.0 / 5.0 bar MAP sensor" | yes | `RunMapSensorWizard` 10737 |
| | Convert to 3 bar MAP sensor | no (empty handler) | 6857 |
| | Convert to larger injectors | yes | 6862 |
| | Convert to E85 (ethanol) fuel | yes | 6903 |
| | Change boost adaption ranges | yes | 7060 |
| | Change boost bias range | yes | 10108 |
| | Free tune wizard | no | 10519 |
| | Change RPM limit | yes | 12791 |

#### Tune me up ® (frmTuningWizard, Controls/frmTuningWizard.cs)

`RunTuningWizard` (frmMain 6689): if the stage marker > 3, "This file has already been tuned to a higher stage, the tuning wizard will not be started". Wizard "Welcome to the tuning wizard" / "Staged tuning options", "Choose the desired tuning stage": "Stage 1 (+30 bhp, +40Nm)", "Stage 2 (+50 bhp, +60Nm)", "Stage 3 (+80 bhp, +80Nm)", "Stage 4 and higher" (preselected: current stage, 0 → 1). Group "Free tuning settings" (enabled only for stage 4): Mapsensor type, Injector type, Turbo type ("Stock", "MHI TD0415T", "MHI TD0419T", "Garrett GT28BB", "Garrett GT28RS", "Garrett GT3071R", "Holset HX35w", "Holset HX40w"), radio torque / boost, "Peak torque (Nm)" 100-650 (400), "Peak boost (bar)" max 2.2 (1.4), "BCV type" ("Trionic 5 valve" / "Trionic 7 valve"), "RPM limiter" 6000-8500 (= Rpm_max! × 10), "Knock time (ms)" 1000-20000 (= Knock_matrix_time!). Maxima per sensor / injector / turbo in `UpdateMaxima` (frmTuningWizard.cs 52-232; e.g. 2.5 bar + stock injectors 400 Nm / 1.30 bar). BCV preset: T5.2 `Frek_230! == 728 || Frek_250! == 935` → T5 valve; T5.5 `== 90 || == 70` → T5 valve; else T7 valve.

OK: stage 1-3 → `TuneFileToStage(stage, …, SilentMode = true)` (no confirmation, no already-tuned / map-sensor check); stage 4 → `FreeTuneBinary` (below). Result boxes: "Tuning of the binary file failed!", "Your binary file was already tuned!", "Your binary file was already tuned (3 bar sensor)!", "Tuning process cancelled by user"; success shows the report "Tuning report (stage N)" (a DevExpress report over `Trionic5Resume.ResumeTuning`, one "Description" column).

Hidden "Tune binary to stage II / III" call `TuneFileToStage(2|3, SilentMode = false)`: refused if the stage marker or the map sensor marker is set; else a task dialog "Tune me up™ to stage II wizard": "This wizard will tune your binary to a stage II equivalent.", "Boost request map, fuel injection and ignition tables will be altered" + newline + msg, footer "Happy driving!!!\nDilemma © 2009", "The author does not take responsibility for any damage done to your car or other objects in any form!", checkbox "Show me a summary after tuning" (ignored), buttons "Yes, tune me to stage II" / "No thanks!" (Tuner 79-280). msg from `PartNumberConverter.GetECUInfo(partnumber, enginetype)`: "Tuning your: <bhp> bhp <car> (<engine>)  2.3 liter | 2.0 liter " + " Aero binary" (forces turbo TD0415T) / " Full pressure turbo binary" / " Low pressure turbo, you'll have to modify hardware (solenoid valve, hoses etc.) to get this working!" (sets isLpt) / " non turbo car to stage, you'll have to modify hardware to get this working!"; unknown part: "Partnumber not recognized, tuning will continue anyway, please verify settings afterwards". In silent mode isLpt is always false.

**TuneToStage** (Tuner 3333), args from TuneFileToStage: maxBoost = ECUInfo.StageNboost (defaults 1.15 / 1.25 / 1.35 bar for an unknown part), 1st gear 0.72, 2nd gear 1.54, 1st gear AUT 0.62, auto gearbox 90 %:
```
backup <dir>\<name>yyyyMMddHHmmssbeforetuningtostage<N>.bin (overwrite)
Reg_kon_fgm! / Reg_kon_sgm! / Reg_kon_fga! (1 byte, if present) = stage 1: 30/45/30, stage ≥ 2: 45/45/45
Reg_kon_mat! all zero → fill 45 (length 0x80: bytes) else 450 as 16-bit;  same for Reg_kon_mat_a!
P_fors!+I_fors!+D_fors! all zero → fixed 28-byte defaults (Tuner 1285); same for the _a! set (1394), identical values
SetBoostRequestMaps(turbo): Tryck_mat! columns 6 and 7, rows 0..15 = maxBoost × factor[row] (table below);
   byte = (bar + 1) × 100, capped 254, written only if larger than the current byte (never lowered);
   Tryck_mat_a! same cell = value × 90 / 100; isLpt also writes columns 4 and 5 (0.35…0.10, 0.55…0.35)
Fuel_knock_mat!: last 3 columns + 4 (cap 255)
Tryck_vakt_tab!: every byte 254 (fuel cut 1.54 bar; the fuelCutLevel argument is unused)
Regl_tryck_fgm! = (0.72+1)×100, Regl_tryck_fgaut! = 162, Regl_tryck_sgm! = 254 (each byte, if the symbol exists;
   T5.2's Regl_tryck_fga! is never set)
Max_regl_temp_1! / Max_regl_temp_2!: every word 0x00FA
Trionic5Anomalies.CheckBinForAnomalies → report rows
stage marker (len − 0x200) = stage; checksum always updated (fresh Trionic5File, no library / auto check)
```
Boost request factors per row (low rpm → high rpm), column 6 (column 7 differs only slightly):

| Turbo | rows 0-15 |
|---|---|
| Stock | .85 .87 .89 .90 .95 1 1 1 1 1 1 .9 .8 .7 .65 .6 |
| TD0415T | .85 .87 .89 .90 .95 1 1 1 1 1 1 1 .95 .9 .8 .65 |
| TD0419T, GT28BB, GT28RS | .60 .60 .60 .65 .80 .95 1 1 1 1 1 1 .95 .9 .85 .70 |
| GT3071R, HX35w | .60×6 .65 .75 .95 .98 1 1 1 1 .95 .80, rows 0-8 capped at 0.7/0.7…/0.8/1.0/1.2 bar |
| HX40w, S400SX371 | as GT3071R but rows 14-15 .98 .93 |

Free tune / stage X / stage 4 (`FreeTuneBinary`, Tuner 4191): refuses with TuningFailedAlreadyTuned / TuningFailedThreebarSensor when either marker is set. peak boost = `PressureToTorque.CalculatePressureFromTorque(torque, turbo)` or the typed boost; request = ((boost+1)×100 / sensorFactor − 100)/100. Converts the map sensor if it differs (as below), on an injector change subtracts the injector-constant difference and writes the fixed battery correction table, writes turbo / injector / sensor markers (`SetTrionicOptions`), then `TuneToStage(stage by boost: <1.2 → 1 … ≥1.9 → 9, request, 0.52, 1.0, 0.52, …, isLpt = !(Aero or FPT))`, then `Frek_230!`/`Frek_250!` (T5.2: 0x02D8/0x03A7 T5 valve, 0x0276/0x076C T7; T5.5: 0x005A/0x0046, 0x0032/0x0020), `Rpm_max!`, `Knock_matrix_time!` by raw I/O. Non-stock sensor + 630/875/1000 cc injectors additionally regenerate `Ign_map_0_x_axis!` (18 steps −1…peak), `Ign_map_0!`/`Ign_map_4!`, `Fuel_map_xaxis!` (−0.8…peak), `Insp_mat!`, `Fuel_knock_xaxis!` (−0.3…peak), `Fuel_knock_mat!` (+5 %) from `TuningReferenceMaps` (bilinear `Handle_tables`) and rewrite the boost request at 100 %. The writes after TuneToStage get no checksum (stale unless auto checksum and a later `WriteData`). Hidden "Tune binary to stage X" (9103) / "Free tune wizard" (10519) use frmFreeTuneSettings ("Tune settings", same fields, "Tune!" / "Cancel"; turbo names "MHI TD04HL-15T" / "-19T") and show "Tuning process completed!", "Tuning process aborted, file is already tuned!" or "Tuning process aborted, file was converted to another mapsensor type before!" (stage X in a report "Tuning report.. stage <n>", free tune in an info box). `TuneFileToStage(99)` (frmTuningSettings) has no caller.

#### Convert to different MAP sensor (frmMapSensorWizard)

"Map sensor wizard" / "Welcome to the mapsensor wizard": "All boost related tables will be altered to make sure the correct values are calculated within the ECU based on the new mapsensor type." + "You are converting from a <from> to a <to>" (strings "2.5 bar mapsensor" … "5.0 bar mapsensor"), "If you used the wizard on a file that was already converted for use with a non-stock mapsensor, be sure to verify the binary file after the wizard completes." From-type = marker, or auto-detected when the marker is stock and Settings "auto detect map sensor" is on (T5File 2371).

**Bug:** OK always calls `ConvertFileToThreeBarMapSensor(info, from, MapSensorType.MapSensor30)` (frmMain 10767): every menu entry converts to 3.0 bar and writes marker 0x01; the report is still titled "<target> report". From 3.0 the call returns at once (same type) but the report and checksum still run.

`ConvertFileToThreeBarMapSensor` (Tuner 4009), factor = to/from (2.5→3.0 1.2, →3.5 1.4, →4.0 1.6, →5.0 2.0, reverse 0.8333 / 0.7143 / 0.625 / 0.5 …, Tuner 3899):
```
backup <dir>\<name>yyyyMMddHHmmssbeforetuningfrom<250|300|…>kpasensorto<…>kpasensor.bin
divide by factor (truncate; 16-bit where isSixteenBitTable): Tryck_mat!, Tryck_mat_a!, Tryck_vakt_tab!, Regl_tryck_fgaut!,
   Regl_tryck_fgm!, Regl_tryck_sgm!, Limp_tryck_konst!, Idle_tryck!, Knock_press_tab!, Turbo_knock_tab!, Iv_min_load!,
   Open_loop!, Open_loop_knock!, Open_loop_adapt!, Lacc_clear_tab!, Lner_detekt!, Lupp_detekt!, Sond_heat_tab!, Grund_last!, Grund_last_max!
divide x axes: Fuel_map_xaxis!, Fuel_knock_xaxis!, Temp_reduce_x_st!, Idle_st_last!, Reg_last!, Min_load_gadapt!, Max_load_gadapt!,
   Kadapt_load_low!, Kadapt_load_high!, Last_cyl_komp!, Overs_tab_xaxis! (8-bit); Ign_map_0/2/3/6/8_x_axis!, Misfire_map_x_axis!,
   Detect_map_x_axis! (16-bit)
   then fixed tops: Fuel_map_xaxis! (16 long) [11..15] = 161 173 186 210 235; Fuel_knock_xaxis! (12) [7..11] = same;
   Ign_map_0_x_axis! (36 bytes) words 10..17 = 120 133 146 160 187 200 217 233
TuneAndSmoothTable Insp_mat! (16 cols), Fuel_knock_mat! (12): last 3 columns copied 2 to the left, last two extrapolated
   (prev + |prev − prev2| capped 10, ≤ 255); TuneAndSmoothTableSixteen Ign_map_0!, Ign_map_4! (18 cols): columns shifted, last two = col 15
Inj_konst! = floor(Inj_konst! × factor)
marker len − 0x1FF = target (FF for 2.5)
```
Missing symbols only add "Couldn't find symbol: X" to the report. frmMain then updates the checksum and shows the report. No transaction entries.

#### Convert to larger injectors (frmInjectorWizard)

"Injector wizard": welcome, "Injector constant parameter" ("Choose your injector type": "Stock injectors", "Green giants", "Siemens deka 630 cc/min (60lb/h)", "Siemens deka 875 cc/min (80lb/h)", "Siemens 1000 cc/min"; "Current injector constant" / "Proposed injector constant"), "Battery correction map" (current / proposed, 5-15 volt, ms = raw × 0.004), "Cranking injection duration" ("Current crank factor" / "Proposed crank factor" = `Start_insp!` × 0.004). Changing the type: proposed constant = original − Δ (Tuner 400: stock→GG 1, →630 5, →875 8, →1000 10; GG→630 4, →875 7, →1000 9; 630→875 3, →1000 5; 875→1000 2; reverse negative); crank 9 (stock, GG), 6 (630), 4 (875), 3.5 (1000). Proposed battery correction (15 → 5 V, ms): stock / 875 / 1000: 0.59 0.77 0.78 0.94 1.28 1.50 1.85 2.32 3.73 3.73 3.73; GG: 0.894 1.003 1.15 1.308 1.521 1.768 2.102 2.545 3.216 4.142 5.45; 630: 0.33 0.433 0.548 0.673 0.802 0.974 1.208 1.524 2.023 2.74 3.6 (the 875/1000 rows reuse the stock values: old gap). Finish (frmMain 6862): `WriteDataNoLog` Batt_korr_tab! (11 × 16-bit BE, index 0 = 15 V), Inj_konst! (1 byte), Start_insp! (16-bit, crank / 0.004), injector marker via `SetTrionicOptions`. No backup, no transaction entry, checksum only with auto checksum.

#### Convert to E85 (frmE85Wizard)

"E85 wizard": "Please note that Trionic 5 does not support bi-fuelling. Once your binary has been converted to E85 it will no longer run on regular petrol. The wizard will alter ignition settings, cranking and afterstart fuelling and normal fuelling." / "…coldstart and warmup factors are estimated…wideband lambda sensor." OK (frmMain 6903): backup `<ProjectFolder>\<project>\Backups\<projectbin>-backup-MMddyyyyHHmmss.BIN` in a project, else `<dir>\<name>-backup-MMddyyyyHHmmss.BIN`; then `ConvertToE85` (Tuner 4558) via `WriteData`:
- `Ign_map_0!` and `Ign_map_4!` regenerated: for each cell of the bin's own axes (pressure × sensor factor, rpm), `TuningReferenceMaps.GetIgnitionAdvanceE85ForPressureRpm` × 10, 16-bit, rows from the top rpm down.
- `Inj_konst!`, `Eftersta_fak!`, `Eftersta_fak2!`: each byte × 1.4 with `Convert.ToByte` (rounds; throws on > 255, aborting mid-way).
- `Startvev_fak!` = 8 12 16 20 23 32 36 45 60 104 128 168 208 254 255.
Report "Convert to E85 report". No marker: running it twice multiplies again. Checksum only with auto checksum.

#### Change boost adaption ranges (frmBoostAdaptionWizard)

"Boost adaption range wizard": "Please note that this wizard will adjust the actual code in your binary! The boost adaption range should match the spool characteristics of your turbo."; page "Boost adaption range options": "Manual gearbox from … rpm upto …", "Automatic gearbox from … rpm upto …" (1000-9000, step 50), "Max. boost error … bar" (0.01-0.2, step 0.01); prefilled from the footer. OK (frmMain 7060, Tuner 857): search the code with the *current* values (rpm/10, big-endian, `XX` = any):
```
automatic   33 FC [lowA] 00 00 XX XX 33 FC [highA] 00 00
manual #1   33 FC [lowM] 00 00 XX XX 33 FC [highM] 00 00
manual #2   0C 79 [lowM] 00 00 XX XX XX XX XX XX 0C 79 [highM] 00 00
```
Each found pattern gets the new values; only if all three were found are the five footer values written (`WriteDataNoCounterIncrease`, transaction entries) and "Boost adaption ranges were set" shown; otherwise silence, with the found patterns already patched (footer and code then disagree). The max boost error is only stored in the footer, never patched. Checksum always updated.

#### Change boost bias range (frmBoostBiasWizard)

"Boost bias RPM range wizard": "Please note that this wizard will adjust the actual code in your binary!"; "Axis step range" 5-20 (footer, default 10), "Result rpm from" 2500 "rpm upto" 2500 + step × 300; "Hardcoded RPM limit" (unused). OK (10108, Tuner 776): find `FF FF FF 06 70 <current step> 4C`; set byte 5 = new step; write (2500 + 300 × step)/10 big-endian at match − 28; footer len − 0x1E4 = step; "Boost bias range has been changed". Not found: no message. Checksum always.

#### Change RPM limit (frmRpmLimiterWizard; frmHardcodedRPMLimit is an empty unused form)

T5.2: "Trionic 5.2 files are not supported by this wizard". T5.5 (12791): hardcoded limit = 16-bit raw rpm at `RPMLimitSequenceOne` (pattern `33 F9 00 ?? ?? 00 00 00 ?? ?? 22 11`, 0xFF = wildcard, then +36 / +54 / +50 depending on following bytes, T5File 1959); 0 → "This file is not supported by this wizard" (practically unreachable: the offset is added even when nothing matched). Wizard "Welcome to the RPM limiter wizard", page caption "Boost bias RPM range options" (copy-paste), "Software RPM limit" (Rpm_max! × 10) and "Hardcoded RPM limit", both 5000-10000 step 100. OK: the hardcoded value (raw rpm, BE) to sequence one and sequence two (`48 E7 30 70 3F 3C 00 01 4E B9 00 04` + 26, −2 if those bytes are 00 00) by raw I/O; `Rpm_max!` = rpm/10 via `WriteData`; checksum; "RPM limiters have been changed".

### Compare (Actions → Basic actions)

- **"Compare with another binary"** (4764): `Trionic 5 binary files|*.bin`, **multi-select**. One file → `CompareToFile`. Several → each is parsed and counted, then a dock "Compare list: <current>" (left, width 700; CompareResultSelector with visible columns "Filename" and "Number of symbols" = how many symbols differ; Partnumber / Software ID / Full filename hidden and the first two left empty) where picking a row runs `CompareToFile`.
- **"Compare to original file"** (10095): enabled when `<StartupPath>\Binaries\<partnumber>-<softwareid>.bin` exists (`CheckFileInLibrary`, 1041), but compares `Binaries\<partnumber>.bin`. Of the 85 shipped bins (`T5Binaries/`) only two are named `<pn>-<swid>`, and neither has a `<pn>.BIN` twin: the button is effectively always disabled (bug).
- **"Binary compare files"** (5774, frmBinCompare "Binary compare (byte-by-byte)", labels "File 1"/"File 2", "Close"): filter `Binary files|*.bin`; same 16-byte line diff as T7. No file: "No file is currently opened, you need to open a binary file first to compare it to another one!".

`CompareToFile` (5300) differs from T7/T8:
- Only one pass, over the compare file's symbols with flash address > 0, matched by `Varname` in the current file. A symbol missing from the current file reads as 0 bytes, so it is listed as a length mismatch with stats 0; symbols only in the current file are never listed (no "Missing in" passes, no calibration test).
- Stats (4825): diffabs = differing **bytes** (not halved for 16-bit), perc = integer `diffabs × 100 / length`, avg = **|**mean(current) − mean(compare)**|**.
- Panel "Compare results: <file>" docked left, width **400**. Grid: visible "Description" (384) then "Symbol " (trailing space); hidden SRAM address, Flash address, Length (bytes), Length (values) (all `{0:X4}`), Percentage of values different (F1), Number of values different, Average difference (F1), Category, Subcategory; sorted Category asc / Subcategory desc; AutoFilterRow. Context menu: only "Show differences map" (no Excel export, no package export). Enter / double-click as T7.
- Selecting `Pgm_mod!` opens frmEasyFirmwareInfo with both files' program-mode bytes side by side instead of map viewers.
- Difference map "Symbol difference: <name> [<file>]": axes and descriptions from the **compare** file, factor **and offset** applied; "Map lengths don't match...".

### Move data to another binary (frmTransferDataWizard)

"Data transfer wizard": "Welcome to the data transfer wizard", "This wizard will help you transferring data from the currect binary to the target binary file.", "Make sure the source and target binary files are for the same engine type and such to prevent problem in ignition advance and fuelling from occuring!", "Please review the target binary for correctness after the wizard finishes." Then the target (`Binary files|*.bin`), **no symbol selection** (4016):
```
backup <dir>\<target><yyyyMMddHHmmss>beforetransferringmaps.bin
for each target symbol with flash > 0, each current symbol with the same Varname:
   lengths differ → "Unable to transfer symbol X because source and target lengths don't match!"
   else copy via target.WriteData → "Transferred symbol X successfully" / "Failed to transfer symbol X: <error>"
report "Data transfer report", then "Data was transferred to the target binary"
```
Every same-named symbol is copied, RAM-only ones included when the source flash address is 0 (reads file offset 0, bug). The target has no transaction log; its checksum is updated only per write with auto checksum (no final update); each write also stamps the target's sync date.

### Merge / split (Actions → Binary tools)

- **"Merge binary files"** (frmBinmerger "Select files to merge": "First part of binary", "Second part of binary", Ok / Cancel; `Binary files|*.bin`): lengths must match ("File lengths don't match, unable to merge!"); output byte pairs **second[i], first[i]**; "Files merged successfully".
- **"Split binary file"** (6648): next to the open bin, even offsets → `chip2.bin`, odd offsets → `chip1.bin` (overwritten silently); "File split to chip1.bin and chip2.bin". Merge(first = chip1, second = chip2) restores the original.

### Excel, Idc

- **"Export map to Excel"** (12995): the first selected row of the symbol list (else "No symbol selected in the primary symbol list"); as T7 (A1 "Data for <map>", x axis row 2, y axis column A, `<bin>~<map>.xls`), plus T5: x axis × factor **+ offset** of its axis symbol, cells × factor + offset, a 3D chart with axis titles.
- **"Import map from Excel"** (13278): symbol from the text after the last `~` in the file name ("Found valid symbol for import: <map>. Are you sure you want to overwrite the map in the binary?", "Confirmation", Yes/No), else a symbol picker; values taken **raw** (no factor / offset reversal: an export → import round trip is wrong for scaled maps), rows bottom-up, "Too much information in file, abort"; `WriteData` + checksum update.
- **"Generate Idc file"** (File actions, 12987): `IdaProIdcFile.create(<last opened file>, …)` → `<bin>-autogen.idc`, ROM segment `0x80000 − length` … 0x80000, no message (already lifted to `T5Core/IdaProIdcFile.cs`).

### Library, part numbers, VIN (File → General / File actions)

- **"Browse the library"** (5550) and **"Lookup partnumber"** (4117) both open frmPartnumberLookup ("Partnumber lookup"); Lookup hides "Open this file" / "Compare to this file". Enter or the box button (frmPartNumberList) runs `PartNumberConverter.GetECUInfo(pn, "")`; unknown: "The entered partnumber was not recognized by T5Suite". Shows Carmodel, Engine type, Base boost, Max. boost (M), Max. boost (AUT), Power, Torque, Stage I/II/III boost ("<x> bar"), MYs ("from-upto"), Region, a "T5.2" label, checks "2.0L engine", "2.3L engine", "Aero", "Turbo engine", "Full pressure turbo", "High altitude file". Open / Compare are enabled when `Binaries\<pn>.BIN` exists. Open: close the project and open **the library file itself** (its checksum is never updated there); Compare: `CompareToFile`.
- **frmPartNumberList** ("Partnumber list"): the `PartnumberCollection` table grouped by Carmodel / Enginetype; a partnumber cell is YellowGreen when its bin is in `Binaries` (16 MHz), Orange when it is a 20 MHz bin (32-byte code sequence); double-click or Close picks it. Legend "Available in library: 16 Mhz" / "20 Mhz".
- **"User library"** (File actions, 11642, frmUserLibrary "User library browser"): "Add files" scans a folder recursively for 0x20000 / 0x40000 bins and caches a row each in `<StartupPath>\UserLib.xml` (Filename, Engine type, Stage from `DetermineTuningStage`, Injectors / E85 guessed from max injection × Inj_konst!, Eftersta_fak![13] > 170 or Inj_konst! > 26 → E85, Mapsensor, Torque, T7 BCV, Partnumber, SoftwareID, CPU, RAM locked); "Open selected", "Compare to selected" (only with a file open), "Clear library", "Close".
- **"Browse tunes in internet repository"** (Online page, frmBrowseTunes): downloads `http://trionic.mobixs.eu/t5tunes/t5tunes.xml`; the site is gone.
- **"VIN decoder"** (4111, frmDecodeVIN): T5's own `VINDecoder` (Trionic5Tools/VINDecoder.cs): only `YK1…` / `YS3…`; shows Body, Car model, Engine type, Make year, Plant, Series, Turbo ("---" first); no gearbox line and no check-digit line (T7 has both).
