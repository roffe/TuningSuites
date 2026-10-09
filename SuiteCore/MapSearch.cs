using System.Collections.Generic;
using System.Text;

namespace CommonSuite
{
    /// <summary>frmSearchMaps' options.</summary>
    public record MapSearchOptions(bool SearchForNumericValues, decimal NumericValue, bool SearchForStringValues, string StringValue,
        bool IncludeSymbolNames, bool IncludeSymbolDescription, bool UseSpecificMapLength, int MapLength);

    /// <summary>Actions > Search map content (T7Suite frmMain 3756-3941, T8Suite Form1 5095).</summary>
    public static class MapSearch
    {
        /// <summary>
        /// Symbols whose name / description contain the value, or whose data holds the value (as raw*factor+offset) or the text.
        /// Both suites only scanned the first half of 16-bit tables and only matched text longer than the map; both fixed.
        /// </summary>
        public static List<SymbolHelper> Find(SuiteBinary bin, MapSearchOptions o)
        {
            var hits = new List<SymbolHelper>();
            string number = o.NumericValue.ToString(System.Globalization.CultureInfo.CurrentCulture);
            foreach (SymbolHelper sh in bin.Symbols)
            {
                if (o.UseSpecificMapLength && sh.Length != o.MapLength) continue;
                string name = bin.CompareName(sh);
                bool hit = false;
                foreach (var (include, text) in new[] { (o.IncludeSymbolNames, name), (o.IncludeSymbolDescription, sh.Description ?? "") })
                {
                    if (!include) continue;
                    if (o.SearchForNumericValues && text.Contains(number)) hit = true;
                    if (o.SearchForStringValues && o.StringValue != "" && text.Contains(o.StringValue)) hit = true;
                }
                if (!hit && sh.Flash_start_address < bin.FileLength && sh.Length > 0)
                {
                    byte[] data = bin.Read((int)sh.Flash_start_address, sh.Length);
                    if (o.SearchForNumericValues)
                    {
                        float factor = (float)bin.GetMapCorrectionFactor(name), offset = (float)bin.GetMapCorrectionOffset(name), target = (float)o.NumericValue;
                        bool sixteen = bin.IsSixteenBitTable(name);
                        for (int i = 0; i + (sixteen ? 1 : 0) < data.Length && !hit; i += sixteen ? 2 : 1)
                        {
                            float value = sixteen ? data[i] * 256 + data[i + 1] : data[i];
                            if (value * factor + offset == target) hit = true;
                        }
                    }
                    if (!hit && o.SearchForStringValues && o.StringValue != "" && Encoding.ASCII.GetString(data).Contains(o.StringValue)) hit = true;
                }
                if (hit) hits.Add(sh);
            }
            return hits;
        }
    }
}
