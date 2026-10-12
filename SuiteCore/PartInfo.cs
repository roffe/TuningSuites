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

        /// <summary>&lt;name&gt;.bin in Binaries next to the executable (the T5Extras / T7Extras / T8Extras installers put the stock bins there).</summary>
        public static string StockBinary(string name)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, name + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()
                : null;
        }

        /// <summary>The names of the stock bins in Binaries without .bin, any letter case: the partnumber list's "Available in library".</summary>
        public static HashSet<string> StockBinaryNames()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(dir))
                names.UnionWith(Directory.GetFiles(dir, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Select(Path.GetFileNameWithoutExtension));
            return names;
        }
    }

    public sealed record PartDetail(string Label, string Value);
}
