using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;

namespace T7
{
    /// <summary>A row of the axis browser: a map, its description and its two axes.</summary>
    public sealed record AxisInfo(string Symbol, string Description, string XAxis, string XDescription, string YAxis, string YDescription);

    /// <summary>Lookup partnumber's result; Binary is the stock file in Binaries when there is one.</summary>
    public sealed record PartInfo(string PartNumber, string CarModel, string EngineType, int Bhp, int Torque, bool TwoLiter, bool TwoPointThreeLiter,
        bool Turbo, bool FullPressureTurbo, string Binary);

    /// <summary>The remaining File / Actions / Information tools of frmMain: the address table copy, the axis browser.</summary>
    public static class BinaryTools
    {
        /// <summary>GetStartOfAddressTableOffset: from 0x30000, the 0x20 that ends eight zero bytes; 0 when there is none.</summary>
        public static int AddressTableOffset(byte[] data)
        {
            int end = Math.Min(data.Length, 0x80000);
            for (int i = 0x30000 + 8; i < end; i++)
            {
                if (data[i] != 0x20) continue;
                bool zeros = true;
                for (int k = 1; k <= 8 && zeros; k++) zeros = data[i - k] == 0;
                if (zeros) return i;
            }
            return 0;
        }

        /// <summary>
        /// Copy address table to another binary: the 10-byte records from 7 bytes before the table's start while their last two
        /// bytes are zero, into the same place of the target, then the target's checksum (T7Suite left it stale). The number of
        /// records copied.
        /// </summary>
        public static int CopyAddressTable(string source, string target, bool autoFixFooter)
        {
            byte[] from = File.ReadAllBytes(source), to = File.ReadAllBytes(target);
            int a = AddressTableOffset(from), b = AddressTableOffset(to);
            if (a == 0 || b == 0) throw new InvalidOperationException("No address table found");
            int records = 0;
            for (int s = a - 7, d = b - 7; s + 10 <= from.Length && d + 10 <= to.Length; s += 10, d += 10)
            {
                if (from[s + 8] != 0 || from[s + 9] != 0) break;
                Array.Copy(from, s, to, d, 10);
                records++;
            }
            File.WriteAllBytes(target, to);
            T7Binary.OpenRaw(target).UpdateChecksum(autoFixFooter);
            return records;
        }

        /// <summary>Browse axis information: every symbol with an x or y axis (one symbol when given).</summary>
        public static List<AxisInfo> Axes(T7Binary bin, string only = null)
        {
            var rows = new List<AxisInfo>();
            foreach (string name in bin.Symbols.Cast<SymbolHelper>().Select(s => s.SmartVarname).Distinct().OrderBy(n => n))
            {
                if (only != null && name != only) continue;
                var (x, y, xd, yd, _) = bin.AxisSymbols(name);
                if (x == "" && y == "") continue;
                rows.Add(new AxisInfo(name, SymbolTranslator.ToHelpText(name, bin.Language), x, xd, y, yd));
            }
            return rows;
        }
    
        /// <summary>frmPartnumberLookup: what the part number is, or null when it isn't known.</summary>
        public static PartInfo LookupPartNumber(string partNumber)
        {
            partNumber = partNumber.Trim();
            ECUInformation ecu = new PartNumberConverter().GetECUInfo(partNumber, "");
            if (!ecu.Valid) return null;
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            string bin = Directory.Exists(dir)
                ? Directory.GetFiles(dir, partNumber + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()
                : null;
            return new PartInfo(partNumber, ecu.Carmodel.ToString().Replace('_', ' '), ecu.Enginetype.ToString().Replace('_', ' '), ecu.Bhp, ecu.Torque,
                !ecu.Is2point3liter, ecu.Is2point3liter, ecu.Isturbo, ecu.Isfpt, bin);
        }
    }
}
