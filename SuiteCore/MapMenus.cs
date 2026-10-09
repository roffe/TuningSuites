using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace CommonSuite
{
    /// <summary>A map button: the quick maps menu (the Tuning page's buttons) or My Maps.</summary>
    public record MapShortcut(string Group, string Caption, string Symbol);

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
