using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// Actions → SID information (frmMain 3948-4076): the SID display's tables in the binary. The 33-entry "All" table (mode 99,
    /// "Generic") and, on files with the "AdpN" table, 84 entries in groups of 12 (Adaptation, Exhaust, TCM FUEL/DTI,
    /// TCM GSI/CSLU, TCM TRQ/SPEED, ESP, Purge). The first entry of each table is read-only.
    /// </summary>
    public static class SidInfo
    {
        /// <summary>The rows, or null when the file has no SID tables ("File not compatible!").</summary>
        public static List<SIDIHelper> Read(T7Binary bin)
        {
            var edit = new T7SidEdit();
            if (!edit.init(bin.FileName)) return null;
            var rows = new List<SIDIHelper>();
            Add(rows, edit.getDataArrayAll(), bin, _ => 99);
            if (edit.getFileType() == 1) Add(rows, edit.getDataArrayNew(), bin, i => i / 12);
            return rows;
        }

        private static void Add(List<SIDIHelper> rows, string[] table, T7Binary bin, Func<int, int> mode)
        {
            var translator = new SIDTranslator();
            for (int i = 0, n = 0; i + 2 < table.Length; i += 3, n++)
            {
                var h = new SIDIHelper
                {
                    Symbol = table[i].Replace('\0', ' '),
                    AddressSRAM = Convert.ToInt32(table[i + 1], 16),
                    Value = table[i + 2],
                    IsReadOnly = n == 0,
                    Mode = mode(n),
                };
                translator.GetSidDescription(h);
                h.FoundT7Symbol = Matched(bin, h.AddressSRAM);
                // frmSIDInformation: an entry without a known T7 symbol takes the matched one and its help text
                if (h.T7Symbol == "" && h.FoundT7Symbol != "")
                {
                    h.T7Symbol = h.FoundT7Symbol;
                    h.Info = SymbolTranslator.ToHelpText(h.T7Symbol, bin.Language);
                }
                rows.Add(h);
            }
        }

        /// <summary>The binary's symbol at that address (its user description for "Symbolnumber" names), or "".</summary>
        public static string Matched(T7Binary bin, int address)
        {
            SymbolHelper sh = bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(s => s.Flash_start_address == address);
            if (sh == null) return "";
            return sh.Varname.StartsWith("Symbolnumber") && sh.Userdescription != "" ? sh.Userdescription : sh.Varname;
        }

        /// <summary>The symbols the Symbol column offers (Flash_start_address ≥ 0xF0000).</summary>
        public static List<SymbolHelper> Choices(T7Binary bin) =>
            bin.Symbols.Cast<SymbolHelper>().Where(s => s.Flash_start_address >= 0xF0000).OrderBy(s => s.SmartVarname).ToList();

        /// <summary>
        /// Picking a symbol for a row: the catalogue's code for it (the last match) or its first 4 characters, its address,
        /// description and matched name, the ID by length (1 → 04 unsigned byte, 2 → 01 signed integer, else 00).
        /// </summary>
        public static void Assign(SIDIHelper row, SymbolHelper sh)
        {
            string name = sh.SmartVarname;
            string code = new SIDInformationTable().GetSIDInformation().Cast<SIDIHelper>().LastOrDefault(c => c.T7Symbol == name)?.Symbol
                ?? (name.Length > 4 ? name[..4] : name);
            row.Symbol = code;
            row.T7Symbol = name;
            row.AddressSRAM = (int)sh.Flash_start_address;
            row.Info = sh.Description ?? "";
            row.FoundT7Symbol = sh.Varname;
            row.Value = sh.Length switch { 1 => "04", 2 => "01", _ => "00" };
        }

        /// <summary>Ok: the tables back into the binary in row order, then the checksum. Short names longer than 4 are cut.</summary>
        public static void Write(T7Binary bin, IReadOnlyList<SIDIHelper> rows, bool autoFixFooter)
        {
            var edit = new T7SidEdit();
            if (!edit.init(bin.FileName)) throw new InvalidOperationException("File not compatible!");
            string[] Table(IEnumerable<SIDIHelper> r) => r.SelectMany(h => new[] { h.Symbol.Length > 4 ? h.Symbol[..4] : h.Symbol, h.AddressSRAM.ToString("X6"), h.Value }).ToArray();
            edit.setDataArrayAll(Table(rows.Where(r => r.Mode == 99)));
            if (edit.getFileType() == 1) edit.setDataArrayNew(Table(rows.Where(r => r.Mode != 99)));
            edit.saveFile();
            bin.UpdateChecksum(autoFixFooter);
        }

        /// <summary>
        /// Export SIDi settings (.sid): FoundT7Symbol|Info|Mode|ModeDescr|Symbol|T7Symbol|Value|AddressSRAM|oriAddress|IsReadOnly,
        /// oriAddress being the T7 symbol's SRAM address so an import into another binary can follow the symbol.
        /// </summary>
        public static void Export(T7Binary bin, IEnumerable<SIDIHelper> rows, string file)
        {
            File.WriteAllLines(file, rows.Select(h =>
            {
                long ori = bin.FindAny(h.T7Symbol)?.Start_address ?? 0;
                return string.Join('|', h.FoundT7Symbol, h.Info, h.Mode.ToString(CultureInfo.InvariantCulture), h.ModeDescr, h.Symbol, h.T7Symbol, h.Value,
                    h.AddressSRAM.ToString(CultureInfo.InvariantCulture), ori.ToString(CultureInfo.InvariantCulture), h.IsReadOnly ? "True" : "False");
            }));
        }

        /// <summary>
        /// Import SIDi settings: the same number of rows; each address follows its T7 symbol ("Name+N" adds N) in this binary,
        /// shifted by how far the entry sat from the symbol in the file it came from. Null when the row count differs.
        /// </summary>
        public static List<SIDIHelper> Import(T7Binary bin, string file, int rowCount)
        {
            string[] lines = File.ReadAllLines(file).Where(l => l.Length > 0).ToArray();
            if (lines.Length != rowCount) return null;
            var rows = new List<SIDIHelper>();
            foreach (string line in lines)
            {
                string[] f = line.Split('|');
                if (f.Length < 10) return null;
                var h = new SIDIHelper
                {
                    FoundT7Symbol = f[0], Info = f[1], Mode = int.Parse(f[2], CultureInfo.InvariantCulture), Symbol = f[4], T7Symbol = f[5], Value = f[6],
                    AddressSRAM = int.Parse(f[7], CultureInfo.InvariantCulture), IsReadOnly = f[9] == "True",
                };
                long ori = long.Parse(f[8], CultureInfo.InvariantCulture);
                if (h.T7Symbol == h.Symbol) h.T7Symbol = h.FoundT7Symbol;
                long diff = ori != 0 ? h.AddressSRAM - ori : 0;
                string name = h.T7Symbol;
                int extra = 0;
                int plus = name.IndexOf('+');
                if (plus > 0 && int.TryParse(name[(plus + 1)..], out extra)) name = name[..plus];
                if (bin.FindAny(name) is { } sh && sh.Start_address > 0) h.AddressSRAM = (int)(sh.Start_address + diff + extra);
                rows.Add(h);
            }
            return rows;
        }
    }
}
