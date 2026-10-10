using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CommonSuite
{
    /// <summary>
    /// Symbol name imports (XML / CSV / AS2 descriptors), the symbol list CSV export, S19 / tuning package exports and the map
    /// export that replaced Excel, as both suites' File actions did them.
    /// </summary>
    public static class SymbolFiles
    {
        /// <summary>Import XML descriptor: names matched on name and address, then the &lt;bin&gt;.xml sidecar is rewritten.</summary>
        public static bool ImportXml(SuiteBinary bin, string file)
        {
            bool ok = bin.ImportXmlSymbols(file);
            SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
            return ok;
        }

        /// <summary>Import CSV descriptor: "number;name;..." lines name every symbol with that number.</summary>
        public static void ImportCsv(SuiteBinary bin, string file)
        {
            foreach (string line in File.ReadAllLines(file))
            {
                string[] v = line.Split(';');
                if (v.Length < 2 || !int.TryParse(v[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) continue;
                foreach (SymbolHelper sh in bin.Symbols)
                    if (sh.Symbol_number == number) Name(bin, sh, v[1]);
            }
            SwapAndSave(bin);
        }

        /// <summary>
        /// Import AS2 descriptor: the Nth "*name" line names the Nth symbol with a length, in symbol table order as T8Suite
        /// counted. T7Suite counted in its list, which it had sorted by length.
        /// </summary>
        public static void ImportAs2(SuiteBinary bin, string file)
        {
            List<SymbolHelper> withLength = bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Length > 0).OrderBy(sh => sh.Symbol_number).ToList();
            int n = 0;
            foreach (string line in File.ReadAllLines(file))
            {
                if (!line.StartsWith('*')) continue;
                if (n < withLength.Count) Name(bin, withLength[n], line[1..]);
                n++;
            }
            SwapAndSave(bin);
        }

        private static void Name(SuiteBinary bin, SymbolHelper sh, string name)
        {
            sh.Userdescription = name;
            sh.Description = bin.Describe(name);
            sh.createAndUpdateCategory(name);
        }

        // "Symbolnumber N" symbols that got a name show the name as their Varname (T8Suite only did that on the next open)
        private static void SwapAndSave(SuiteBinary bin)
        {
            foreach (SymbolHelper sh in bin.Symbols)
            {
                if (sh.Userdescription != "" && sh.Varname.StartsWith("Symbolnumber "))
                    (sh.Varname, sh.Userdescription) = (sh.Userdescription, sh.Varname);
            }
            SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
        }

        /// <summary>Export symbollist as CSV: varname,address,sram,length,number,type(,userdescription: T7Suite's), no header.</summary>
        public static void ExportSymbolCsv(SuiteBinary bin, string file, bool userDescription)
        {
            var sb = new StringBuilder();
            foreach (SymbolHelper sh in bin.Symbols)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{sh.Varname.Replace(',', '.')},{sh.Flash_start_address},{sh.Start_address},{sh.Length},{sh.Symbol_number},{sh.Symbol_type}");
                if (userDescription) sb.Append(',').Append(sh.Userdescription);
                sb.Append("\r\n");
            }
            File.WriteAllText(file, sb.ToString());
        }

        /// <summary>Export to S19 (S0 header, S2 records of 32 bytes, S5, S8). False when the file doesn't have the suite's length.</summary>
        public static bool ExportS19(SuiteBinary bin, string target) => new Srecord().ConvertBinToSrec(bin.FileName, (ulong)bin.FileLength, target);

        /// <summary>Export as tuning package (.t7p / .t8p): the symbols' bytes; open T7 software maps calibration back into the file.</summary>
        public static void ExportPackage(SuiteBinary bin, IEnumerable<SymbolHelper> symbols, string target)
        {
            var collection = new SymbolCollection();
            foreach (SymbolHelper sh in symbols) collection.Add(sh);
            new PackageExporter { AddressOffset = bin.PackageAddressOffset, FileLength = bin.FileLength }.ExportPackage(collection, bin.FileName, target);
        }

        /// <summary>The fixed package's symbols this bin has, named as in the list (T7Suite copied them under that name).</summary>
        public static List<SymbolHelper> FixedPackage(SuiteBinary bin)
        {
            var list = new List<SymbolHelper>();
            foreach (string name in bin.FixedPackageSymbols)
            {
                if (bin.FindAny(name) is not { } sh) continue;
                list.Add(new SymbolHelper
                {
                    Varname = name, Userdescription = name, Flash_start_address = sh.Flash_start_address, Start_address = sh.Start_address,
                    Length = sh.Length, Symbol_number = sh.Symbol_number,
                });
            }
            return list;
        }

        /// <summary>A .t7p package's maps: "symbol=", "length=", "data=" hex bytes separated by commas. Broken entries are skipped.</summary>
        public static List<(string name, byte[] data)> ReadPackage(string file)
        {
            var maps = new List<(string, byte[])>();
            string name = "";
            int length = 0;
            foreach (string line in File.ReadLines(file))
            {
                if (line.StartsWith("symbol=")) name = line[7..];
                else if (line.StartsWith("length=")) int.TryParse(line[7..], out length);
                else if (line.StartsWith("data="))
                {
                    string[] bytes = line[5..].Split(',');
                    if (bytes.Length < length) continue;
                    try
                    {
                        maps.Add((name, bytes.Take(length).Select(b => Convert.ToByte(b, 16)).ToArray()));
                    }
                    catch (FormatException)
                    {
                        // ponytail: T7Suite logged and skipped a broken entry too
                    }
                }
            }
            return maps;
        }

        /// <summary>
        /// "Export map to Excel", as CSV: "Data for &lt;map&gt;", the X axis (with its factor) across, the Y axis down (reversed, raw)
        /// and the values with the map's factor, 2 decimals, data rows flipped like the sheet.
        /// </summary>
        public static void ExportMapCsv(SuiteBinary bin, SymbolHelper sh, string target)
        {
            string name = sh.SmartVarname;
            byte[] data = bin.ReadSymbol(sh) ?? [];
            int cols = bin.TableWidth(name);
            bool sixteen = bin.IsSixteenBitTable(name);
            double factor = bin.GetMapCorrectionFactor(name), offset = bin.GetMapCorrectionOffset(name);
            int size = sixteen ? 2 : 1, values = data.Length / size, rows = (values + cols - 1) / cols;
            int[] x = bin.GetXaxisValues(name), y = bin.GetYaxisValues(name);
            double xfactor = bin.AxisFactor(bin.AxisSymbols(name).xAxis);
            string F(double d) => Math.Round(d, 2).ToString(CultureInfo.InvariantCulture);
            // two decimals lose raw steps below a factor of 0.01 (T5's Insp_mat! is 1/256): enough for the import to read them back
            int decimals = factor is > 0 and < 0.01 ? (int)Math.Ceiling(-Math.Log10(factor)) + 1 : 2;
            string Cell(double d) => Math.Round(d, decimals).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder().Append("Data for ").Append(name).Append('\n');
            sb.Append(';').AppendJoin(';', Enumerable.Range(0, cols).Select(c => c < x.Length ? F(x[c] * xfactor) : c.ToString(CultureInfo.InvariantCulture))).Append('\n');
            for (int r = rows - 1; r >= 0; r--)
            {
                sb.Append(r < y.Length ? y[r].ToString(CultureInfo.InvariantCulture) : "");
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= values) break;
                    // the sheet's own sign rule: negative only when the high byte is 0xFF
                    int v = sixteen ? (data[i * 2] == 0xFF ? -(0x100 - data[i * 2 + 1]) : data[i * 2] << 8 | data[i * 2 + 1]) : data[i];
                    sb.Append(';').Append(Cell(v * factor + offset));
                }
                sb.Append('\n');
            }
            File.WriteAllText(target, sb.ToString());
        }

        /// <summary>
        /// T5Suite's "Import map from Excel", for the layout ExportMapCsv writes: rows bottom-up after the two header lines, the values
        /// scaled back (T5Suite took them raw, so an export / import round trip changed scaled maps). Cells that don't parse keep
        /// the map's value; more rows than the map has throw "Too much information in file, abort".
        /// </summary>
        public static byte[] ImportMapCsv(SuiteBinary bin, SymbolHelper sh, string file)
        {
            string name = sh.SmartVarname;
            byte[] data = bin.ReadSymbol(sh) ?? [];
            int cols = Math.Max(bin.TableWidth(name), 1), size = bin.IsSixteenBitTable(name) ? 2 : 1, values = data.Length / size, rows = (values + cols - 1) / cols;
            double factor = bin.GetMapCorrectionFactor(name) is var f && f != 0 ? f : 1, offset = bin.GetMapCorrectionOffset(name);
            List<string> lines = File.ReadAllLines(file).Skip(2).Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count > rows) throw new InvalidDataException("Too much information in file, abort");
            for (int i = 0; i < lines.Count; i++)
            {
                string[] cells = lines[i].Split(';');
                for (int c = 0; c < cols && c + 1 < cells.Length; c++)
                {
                    int v = (rows - 1 - i) * cols + c;
                    if (v >= values || !double.TryParse(cells[c + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) continue;
                    int raw = (int)Math.Round((d - offset) / factor);
                    if (size == 2)
                    {
                        data[v * 2] = (byte)(raw >> 8);
                        data[v * 2 + 1] = (byte)raw;
                    }
                    else data[v] = (byte)Math.Clamp(raw, 0, 255);
                }
            }
            return data;
        }
    }
}
