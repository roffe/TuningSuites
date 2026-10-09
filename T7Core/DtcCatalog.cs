using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// frmFaultcodes' descriptions: every DTC_*.xml next to the program, in name order, the first complete entry per code
    /// wins. T7Suite's loader stopped reading a file at its first incomplete entry (it looked that code up before adding it).
    /// </summary>
    public static class DtcCatalog
    {
        public static Dictionary<string, DTCDescription> Load(string dir = null)
        {
            dir ??= AppContext.BaseDirectory;
            var catalog = new Dictionary<string, DTCDescription>();
            if (!Directory.Exists(dir)) return catalog;
            string[] files = Directory.GetFiles(dir, "DTC_*.xml");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(file);
                    foreach (XmlNode node in doc.DocumentElement.SelectNodes("dtcdescription"))
                    {
                        var dtc = new DTCDescription(node);
                        if (dtc.IsComplete()) catalog.TryAdd(dtc.Code, dtc);
                    }
                }
                catch (XmlException)
                {
                    // ponytail: a broken file is skipped, like T7Suite did
                }
            }
            return catalog;
        }
    }
}
