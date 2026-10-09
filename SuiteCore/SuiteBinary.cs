using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrionicCANLib.Checksum;

namespace CommonSuite
{
    /// <summary>
    /// An opened binary as the suites' windows use it: its symbols, where a map sits in the file, how to read and write it, its
    /// axes and how to show it. T7Binary and T8Binary read their own formats; lookups by name go by SmartVarname, as the suites did.
    /// </summary>
    public abstract class SuiteBinary
    {
        protected SuiteBinary(string fileName, SymbolCollection symbols)
        {
            FileName = fileName;
            Symbols = symbols;
        }

        public string FileName { get; }
        public SymbolCollection Symbols { get; }

        /// <summary>Open (development) software: calibration also lives in SRAM.</summary>
        public bool IsSoftwareOpen { get; protected init; }

        /// <summary>The binary's size: writes stay inside it.</summary>
        public abstract int FileLength { get; }

        public SymbolHelper Find(string symbolname) => Symbols.Cast<SymbolHelper>().FirstOrDefault(sh => sh.SmartVarname == symbolname);

        public virtual bool Has(string symbolname) => Find(symbolname) != null;

        /// <summary>By name or user description, as the feature checks and tuning packages matched symbols.</summary>
        public SymbolHelper FindAny(string symbolname) =>
            Symbols.Cast<SymbolHelper>().FirstOrDefault(sh => sh.Varname == symbolname || sh.Userdescription == symbolname);

        /// <summary>The name compare and transfer match symbols by, "" leaves one out. T8Suite matched by SmartVarname.</summary>
        public virtual string CompareName(SymbolHelper sh) => sh.SmartVarname;

        /// <summary>Calibration symbols: compare lists the ones only one of the files has.</summary>
        public virtual bool IsCalibration(string name) =>
            name.Contains("Cal.") || name.Contains("Cal1.") || name.Contains("Cal2.") || name.Contains("Cal3.") || name.Contains("Cal4.")
            || name.StartsWith("X_Acc");

        /// <summary>The help text shown for a symbol name.</summary>
        public abstract string Describe(string symbolname);

        /// <summary>Import XML descriptor: names matched on name and flash address. False when the file holds no table.</summary>
        public abstract bool ImportXmlSymbols(string file);

        public int SymbolLength(string symbolname) => Find(symbolname)?.Length ?? 0;

        /// <summary>The symbol's address in the file, 0 when it isn't there.</summary>
        public abstract long SymbolAddress(string symbolname);

        /// <summary>Where compare, transfer and search read a symbol in the file.</summary>
        public virtual long AddressOf(SymbolHelper sh) => sh.Flash_start_address;

        /// <summary>Where a map's data sits in the file, -1 if it only lives in SRAM.</summary>
        public abstract int FileAddress(SymbolHelper sh);

        /// <summary>A map's content as the viewer reads it, null if it only lives in SRAM.</summary>
        public byte[] ReadSymbol(SymbolHelper sh) => FileAddress(sh) is var a and >= 0 ? Read(a, sh.Length) : null;

        public abstract byte[] Read(int address, int length);

        /// <summary>savedatatobinary: only inside the file, a transaction entry when a project log is given. Throws when the file can't be written.</summary>
        public void WriteData(int address, byte[] data, TrionicTransactionLog log = null, string note = "")
        {
            if (address <= 0 || address >= FileLength) return;
            byte[] before = Read(address, data.Length);
            using (var fs = new FileStream(FileName, FileMode.Open, FileAccess.Write))
            {
                fs.Position = address;
                fs.Write(data, 0, data.Length);
            }
            log?.AddToTransactionLog(new TransactionEntry(DateTime.Now, address, data.Length, before, data, 0, 0, note));
        }

        /// <summary>A map save (tabdet_onSymbolSave): the data, then the checksum.</summary>
        public void WriteSymbol(int address, byte[] data, TrionicTransactionLog log = null, string note = "")
        {
            WriteData(address, data, log, note);
            UpdateChecksum();
        }

        /// <summary>Corrects the file's checksum; throws when it doesn't verify afterwards.</summary>
        public abstract void UpdateChecksum();

        /// <summary>Checks the checksum without correcting it.</summary>
        public abstract ChecksumResult VerifyChecksum();

        /// <summary>Another file of this kind, to write bytes into without parsing its symbols (a project's rebuild).</summary>
        public abstract SuiteBinary RawFile(string fileName);

        /// <summary>A map's x and y axis symbols and the x, y and z captions.</summary>
        public abstract (string xAxis, string yAxis, string xDescr, string yDescr, string zDescr) AxisSymbols(string symbolname);

        public abstract int[] GetXaxisValues(string symbolname);
        public abstract int[] GetYaxisValues(string symbolname);

        /// <summary>The number of columns the viewer shows.</summary>
        public abstract int TableWidth(string symbolname);

        public abstract bool IsSixteenBitTable(string symbolname);

        public abstract double GetMapCorrectionFactor(string symbolname);

        public virtual double GetMapCorrectionOffset(string symbolname) => 0;

        /// <summary>The factor an axis' values still need in an export: T7's come with it, T8's are raw.</summary>
        public virtual double AxisFactor(string axisSymbol) => 1;

        /// <summary>Export fixed tuning package: the maps a stage tune touches.</summary>
        public abstract IReadOnlyList<string> FixedPackageSymbols { get; }

        /// <summary>A tuning package export reads open software's SRAM-addressed calibration this much lower in the file.</summary>
        public virtual int PackageAddressOffset => 0;

        /// <summary>Copy address table: where a file's address table starts (the two files' should match), 0 when it has none.</summary>
        public abstract int AddressTableStart(string file);

        /// <summary>Copy address table to another binary, then that file's checksum. Throws when either file has no table.</summary>
        public abstract void CopyAddressTable(string target);

        /// <summary>Generate Idc file: &lt;bin&gt;-autogen.idc next to the bin, for IDA Pro. Its path.</summary>
        public abstract string ExportIdc();

        /// <summary>The quick maps menu (the Tuning page's map buttons) for this binary.</summary>
        public virtual List<MapShortcut> QuickMaps() => [];

        /// <summary>The open-loop limits drawn on the map's load × rpm cells, null when the suite has none.</summary>
        public virtual byte[] OpenLoopTable(string mapname) => null;
    }
}
