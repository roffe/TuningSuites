using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace CommonSuite
{
    /// <summary>A My Maps entry.</summary>
    public record MapShortcut(string Group, string Caption, string Symbol);

    /// <summary>
    /// An entry of the Tuning menu (txlogger's menu for the ECU, its tool entries left out): a submenu (Children) or maps. Symbols
    /// are the map's names in priority order (txlogger's and what other software of the ECU calls it), each "a|b" for maps opened
    /// together; none: the caption is the symbol (txlogger's Name only entry).
    /// </summary>
    public record TuningItem(string Caption, string[] Symbols = null, TuningItem[] Children = null, bool Ecu = false)
    {
        /// <summary>The maps the entry opens in bin: those of its first name the bin has; none leaves the entry out.</summary>
        public string[] Maps(SuiteBinary bin) =>
            (Symbols ?? [Caption]).Select(names => names.Split('|').Where(s => bin.Find(s) != null).ToArray()).FirstOrDefault(m => m.Length > 0) ?? [];
    }

    /// <summary>My Maps (mymaps.xml in the suite's settings folder, the file both suites used).</summary>
    public static class MapMenus
    {
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
