using System;
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

        public int SymbolLength(string symbolname) => Find(symbolname)?.Length ?? 0;

        /// <summary>The symbol's address in the file, 0 when it isn't there.</summary>
        public abstract long SymbolAddress(string symbolname);

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

        /// <summary>The open-loop limits drawn on the map's load × rpm cells, null when the suite has none.</summary>
        public virtual byte[] OpenLoopTable(string mapname) => null;
    }
}
