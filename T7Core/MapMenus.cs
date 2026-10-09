using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace T7
{
    public record MapShortcut(string Group, string Caption, string Symbol);

    /// <summary>
    /// The Tuning page's map buttons in its group and button order, with DynamicTuningMenu's rules (BioPower names and E85 maps,
    /// B308 second maps, gas maps, boost control and cab gear limit only when the bin has them), and My Maps (mymaps.xml).
    /// </summary>
    public static class MapMenus
    {
        public static List<MapShortcut> QuickMaps(T7Binary bin)
        {
            bool bio = bin.IsBioPower, b308 = bin.Has("IgnNormCal2.Map");
            var m = new List<MapShortcut>
            {
                new("Fuel", bio ? "Petrol VE Map" : "VE map", "BFuelCal.Map"),
                new("Fuel", bio ? "E85 VE Map" : "Startup VE map", bio ? "BFuelCal.E85Map" : "BFuelCal.StartMap"),
                new("Fuel", "Injector constant", "InjCorrCal.InjectorConst"),
            };
            if (b308)
            {
                m.Add(new("Fuel", "VE map2", "BFuelCal2.Map"));
                m.Add(new("Fuel", "Startup VE map2", "BFuelCal2.StartMap"));
            }
            if (bin.Has("MyrtilosCal.Fuel_GasMap")) m.Add(new("Fuel", "Gas VE map", "MyrtilosCal.Fuel_GasMap"));
            else if (bin.Has("BFuelCal.GasMap")) m.Add(new("Fuel", "Gas VE map", "BFuelCal.GasMap"));

            m.Add(new("Ignition", "Ignition map", "IgnNormCal.Map"));
            if (b308) m.Add(new("Ignition", "Ignition map2", "IgnNormCal2.Map"));
            if (bio) m.Add(new("Ignition", "Ignition for E85", "IgnE85Cal.fi_AbsMap"));
            if (bin.Has("IgnNormCal.GasMap")) m.Add(new("Ignition", "Ignition for gas", "IgnNormCal.GasMap"));
            m.Add(new("Ignition", "Knock pull map", "IgnKnkCal.IndexMap"));
            m.Add(new("Ignition", "Max knock pull", "KnkFuelCal.fi_MapMaxOff"));

            m.Add(new("Airmass request", "Pedal request map", "PedalMapCal.m_RequestMap"));
            m.Add(new("Airmass request", "Air/torque calibration", "TorqueCal.m_AirTorqMap"));
            m.Add(new("Airmass request", "Nom. torque map", "TorqueCal.M_NominalMap"));
            m.Add(new("Airmass request", "Pedal request airmass (Y)", "TorqueCal.m_PedYSP"));
            m.Add(new("Airmass request", "Air/torque (X)", "TorqueCal.M_EngXSP"));
            m.Add(new("Airmass request", "Nom. torque map (X)", "TorqueCal.m_AirXSP"));

            if (bin.Has("BoostCal.RegMap"))
            {
                m.Add(new("Boost control", "Boost calibr.", "BoostCal.RegMap"));
                m.Add(new("Boost control", "P factors", "BoostCal.PMap"));
                m.Add(new("Boost control", "I factors", "BoostCal.IMap"));
                m.Add(new("Boost control", "D factors", "BoostCal.DMap"));
            }

            m.Add(new("Knock", "Knock enrichment", "KnkFuelCal.EnrichmentMap"));
            m.Add(new("Knock", "Knock sensitivity", "KnkDetCal.RefFactorMap"));

            m.Add(new("Limiters", "Airmass (M)", "BstKnkCal.MaxAirmass"));
            m.Add(new("Limiters", "Airmass (A)", "BstKnkCal.MaxAirmassAu"));
            m.Add(new("Limiters", "RPM limiter", "MaxSpdCal.n_EngLimAir"));
            m.Add(new("Limiters", "Engine trq (M)", "TorqueCal.M_EngMaxTab"));
            m.Add(new("Limiters", "Engine trq (A)", "TorqueCal.M_EngMaxAutTab"));
            m.Add(new("Limiters", "Fuel cut", "FCutCal.m_AirInletLimit"));
            if (bio) m.Add(new("Limiters", "Engine trq for E85", "TorqueCal.M_EngMaxE85Tab"));
            if (bio && bin.Has("TorqueCal.M_EngMaxE85TabAut")) m.Add(new("Limiters", "Engine trq for E85 (A)", "TorqueCal.M_EngMaxE85TabAut"));
            m.Add(new("Limiters", "Speed limiter", "MaxVehicCal.v_MaxSpeed"));
            m.Add(new("Limiters", "Gear trq (M)", "TorqueCal.M_ManGearLim"));
            m.Add(new("Limiters", "Gear trq (5th)", "TorqueCal.M_5GearLimTab"));
            if (bin.Has("TorqueCal.M_CabGearLim")) m.Add(new("Limiters", "Gear trq (cab)", "TorqueCal.M_CabGearLim"));
            m.Add(new("Limiters", "Overboost", "TorqueCal.M_OverBoostTab"));
            return m;
        }

        /// <summary>mymaps.xml: &lt;categories&gt;&lt;category title&gt;&lt;map title symbol/&gt;, empty when missing or unreadable.</summary>
        public static List<MapShortcut> LoadMyMaps(string file)
        {
            var maps = new List<MapShortcut>();
            if (!File.Exists(file)) return maps;
            try
            {
                var doc = new XmlDocument();
                doc.Load(file);
                foreach (XmlNode category in doc.SelectNodes("categories/category"))
                    foreach (XmlNode map in category.SelectNodes("map"))
                        maps.Add(new MapShortcut(category.Attributes["title"]?.Value ?? "", map.Attributes["title"]?.Value ?? "", map.Attributes["symbol"]?.Value ?? ""));
            }
            catch (XmlException)
            {
                // ponytail: T7Suite also ignored a broken file
            }
            return maps;
        }

        /// <summary>Writes mymaps.xml with the maps grouped by category, categories sorted.</summary>
        public static void SaveMyMaps(string file, IEnumerable<MapShortcut> maps)
        {
            var doc = new XmlDocument();
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", null, null));
            XmlElement root = doc.CreateElement("categories");
            doc.AppendChild(root);
            foreach (var group in maps.GroupBy(m => m.Group).OrderBy(g => g.Key, System.StringComparer.Ordinal))
            {
                XmlElement category = doc.CreateElement("category");
                category.SetAttribute("title", group.Key);
                foreach (MapShortcut m in group)
                {
                    XmlElement map = doc.CreateElement("map");
                    map.SetAttribute("title", m.Caption);
                    map.SetAttribute("symbol", m.Symbol);
                    category.AppendChild(map);
                }
                root.AppendChild(category);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            doc.Save(file);
        }

        /// <summary>"Add to MyMaps": the symbol under "Directly added", titled by its name.</summary>
        public static void AddToMyMaps(string file, string symbol)
        {
            List<MapShortcut> maps = LoadMyMaps(file);
            maps.Add(new MapShortcut("Directly added", symbol, symbol));
            SaveMyMaps(file, maps);
        }
    }
}
