using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CommonSuite
{
    /// <summary>Lookup partnumber's result; Binary is the stock file in Binaries when there is one.</summary>
    public sealed record PartInfo(string PartNumber, string CarModel, string EngineType, int Bhp, int Torque, bool TwoLiter, bool TwoPointThreeLiter,
        bool Turbo, bool FullPressureTurbo, string Binary)
    {
        /// <summary>The suite's own extra rows (T5Suite: boosts, model years, region, ECU type, Aero, high altitude).</summary>
        public IReadOnlyList<PartDetail> Extra { get; init; } = [];

        /// <summary>Lookup partnumber shows power, torque and the engine boxes (T5Suite, T7Suite); T8Suite showed only the car and engine.</summary>
        public bool Details { get; init; } = true;

        /// <summary>The ECU's ("T5", "T7", "T8") stock bins: Binaries/&lt;ecu&gt; next to the executable, where TxExtras puts them.</summary>
        public static string StockFolder(string ecu) => Path.Combine(AppContext.BaseDirectory, "Binaries", ecu);

        /// <summary>&lt;name&gt;.bin in the ECU's stock bins, any letter case.</summary>
        public static string StockBinary(string ecu, string name)
        {
            string dir = StockFolder(ecu);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, name + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()
                : null;
        }

        /// <summary>The names of the ECU's stock bins without .bin, any letter case: the partnumber list's "Available in library".</summary>
        public static HashSet<string> StockBinaryNames(string ecu)
        {
            string dir = StockFolder(ecu);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                names.UnionWith(Directory.GetFiles(dir, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Select(Path.GetFileNameWithoutExtension));
            return names;
        }
    }

    public sealed record PartDetail(string Label, string Value);
}
