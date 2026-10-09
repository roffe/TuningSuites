using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// Symbol name imports (XML / CSV / AS2 descriptors), the symbol list CSV export, S19 / IDC / tuning package exports and
    /// the map export that replaced Excel, as frmMain's File actions did them.
    /// </summary>
    public static class SymbolFiles
    {
        /// <summary>Import XML descriptor: names matched on name and address, then the &lt;bin&gt;.xml sidecar is rewritten.</summary>
        public static bool ImportXml(T7Binary bin, string file)
        {
            bool ok = Trionic7File.TryToLoadAdditionalXMLSymbols(file, bin.Symbols, bin.Language);
            SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
            return ok;
        }

        /// <summary>Import CSV descriptor: "number;name;..." lines name every symbol with that number.</summary>
        public static void ImportCsv(T7Binary bin, string file)
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
        /// Import AS2 descriptor: the Nth "*name" line names the Nth symbol with a length. T7Suite counted in its list, which it
        /// had sorted by length; here it counts in symbol table order.
        /// </summary>
        public static void ImportAs2(T7Binary bin, string file)
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

        private static void Name(T7Binary bin, SymbolHelper sh, string name)
        {
            sh.Userdescription = name;
            sh.Description = SymbolTranslator.ToHelpText(name, bin.Language);
            sh.createAndUpdateCategory(name);
        }

        // "Symbolnumber N" symbols that got a name show the name as their Varname
        private static void SwapAndSave(T7Binary bin)
        {
            foreach (SymbolHelper sh in bin.Symbols)
            {
                if (sh.Userdescription != "" && sh.Varname == $"Symbolnumber {sh.Symbol_number}")
                    (sh.Varname, sh.Userdescription) = (sh.Userdescription, sh.Varname);
            }
            SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
        }

        /// <summary>Export symbollist as CSV: varname,address,sram,length,number,type,userdescription, no header.</summary>
        public static void ExportSymbolCsv(T7Binary bin, string file)
        {
            var sb = new StringBuilder();
            foreach (SymbolHelper sh in bin.Symbols)
                sb.Append(CultureInfo.InvariantCulture, $"{sh.Varname.Replace(',', '.')},{sh.Flash_start_address},{sh.Start_address},{sh.Length},{sh.Symbol_number},{sh.Symbol_type},{sh.Userdescription}").Append("\r\n");
            File.WriteAllText(file, sb.ToString());
        }

        /// <summary>Export to S19 (S0 header, S2 records of 32 bytes, S5, S8). False when the file isn't 0x80000 bytes.</summary>
        public static bool ExportS19(T7Binary bin, string target) => new Srecord().ConvertBinToSrec(bin.FileName, 0x80000, target);

        /// <summary>Generate Idc file: &lt;bin&gt;-autogen.idc next to the bin.</summary>
        public static string ExportIdc(T7Binary bin)
        {
            IdaProIdcFile.create(bin.FileName, bin.Symbols);
            return Path.Combine(Path.GetDirectoryName(bin.FileName), Path.GetFileNameWithoutExtension(bin.FileName) + "-autogen.idc");
        }

        /// <summary>Export as tuning package (.t7p): the symbols' bytes; open software maps calibration back into the file.</summary>
        public static void ExportPackage(T7Binary bin, IEnumerable<SymbolHelper> symbols, string target)
        {
            var collection = new SymbolCollection();
            foreach (SymbolHelper sh in symbols) collection.Add(sh);
            var exporter = new PackageExporter();
            if (bin.IsSoftwareOpen) exporter.AddressOffset = bin.SramOffset > 0 ? bin.SramOffset : 0xEFFC04;
            exporter.ExportPackage(collection, bin.FileName, target);
        }

        /// <summary>Export fixed tuning package: the maps a stage tune touches, those the bin has.</summary>
        public static readonly string[] FixedPackageSymbols =
        [
            "LimEngCal.TurboSpeedTab", "LimEngCal.p_AirSP", "AirCtrlCal.m_MaxAirTab", "TempLimPosCal.Airmass", "BoostCal.RegMap",
            "BoostCal.SetLoadXSP", "BoostCal.n_EngSP", "PedalMapCal.m_RequestMap", "PedalMapCal.n_EngineMap", "PedalMapCal.X_PedalMap",
            "BstKnkCal.MaxAirmass", "BstKnkCal.OffsetXSP", "BstKnkCal.n_EngYSP", "BstKnkCal.MaxAirmassAu", "TorqueCal.M_EngMaxAutTab",
            "TorqueCal.M_EngMaxTab", "TorqueCal.M_EngMaxE85Tab", "TorqueCal.M_ManGearLim", "TorqueCal.M_CabGearLim", "TorqueCal.n_Eng5GearSP",
            "TorqueCal.M_5GearLimTab", "TorqueCal.M_NominalMap", "TorqueCal.m_AirXSP", "TorqueCal.n_EngYSP", "TorqueCal.m_AirTorqMap",
            "TorqueCal.M_EngXSP", "TorqueCal.m_PedYSP", "FCutCal.m_AirInletLimit", "BoosDiagCal.m_FaultDiff", "BoosDiagCal.ErrMaxMReq",
            "BFuelCal.Map", "BFuelCal.StartMap", "BFuelCal.E85Map", "MyrtilosCal.Fuel_GasMap", "BFuelCal.GasMap", "BFuelCal.AirXSP",
            "BFuelCal.RpmYSP", "InjCorrCal.BattCorrSP", "InjCorrCal.BattCorrTab", "InjCorrCal.InjectorConst", "IgnNormCal.Map",
            "IgnE85Cal.fi_AbsMap", "IgnNormCal.GasMap", "IgnNormCal.m_AirXSP", "IgnNormCal.n_EngYSP", "IgnKnkCal.IndexMap",
            "KnkFuelCal.fi_MapMaxOff", "KnkFuelCal.m_AirXSP", "BoostCal.PMap", "BoostCal.IMap", "BoostCal.DMap", "BoostCal.PIDXSP",
            "BoostCal.PIDYSP", "TorqueCal.M_OverBoostTab", "TorqueCal.n_EngYSP", "KnkFuelCal.EnrichmentMap", "IgnKnkCal.m_AirXSP",
            "IgnKnkCal.n_EngYSP", "KnkDetCal.RefFactorMap", "KnkDetCal.m_AirXSP", "KnkDetCal.n_EngYSP", "MaxSpdCal.T_EngineSP",
            "MaxSpdCal.n_EngLimAir", "MaxVehicCal.v_MaxSpeed",
        ];

        /// <summary>The fixed package's symbols this bin has, named as in the list (T7Suite copied them under that name).</summary>
        public static List<SymbolHelper> FixedPackage(T7Binary bin)
        {
            var list = new List<SymbolHelper>();
            foreach (string name in FixedPackageSymbols)
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

        /// <summary>
        /// "Export map to Excel", as CSV: "Data for &lt;map&gt;", the X axis (with its factor) across, the Y axis down (reversed, raw)
        /// and the values with the map's factor, 2 decimals, data rows flipped like the sheet.
        /// </summary>
        public static void ExportMapCsv(T7Binary bin, SymbolHelper sh, string target)
        {
            string name = sh.SmartVarname;
            byte[] data = bin.ReadSymbol(sh) ?? [];
            int cols = bin.TableWidth(name);
            bool sixteen = bin.IsSixteenBitTable(name);
            double factor = bin.GetMapCorrectionFactor(name), offset = T7Binary.GetMapCorrectionOffset(name);
            int size = sixteen ? 2 : 1, values = data.Length / size, rows = (values + cols - 1) / cols;
            int[] x = bin.GetXaxisValues(name), y = bin.GetYaxisValues(name);
            string F(double d) => Math.Round(d, 2).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder().Append("Data for ").Append(name).Append('\n');
            sb.Append(';').AppendJoin(';', Enumerable.Range(0, cols).Select(c => c < x.Length ? x[c].ToString(CultureInfo.InvariantCulture) : c.ToString(CultureInfo.InvariantCulture))).Append('\n');
            for (int r = rows - 1; r >= 0; r--)
            {
                sb.Append(r < y.Length ? y[r].ToString(CultureInfo.InvariantCulture) : "");
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= values) break;
                    // the sheet's own sign rule: negative only when the high byte is 0xFF
                    int v = sixteen ? (data[i * 2] == 0xFF ? -(0x100 - data[i * 2 + 1]) : data[i * 2] << 8 | data[i * 2 + 1]) : data[i];
                    sb.Append(';').Append(F(v * factor + offset));
                }
                sb.Append('\n');
            }
            File.WriteAllText(target, sb.ToString());
        }
    }
}
