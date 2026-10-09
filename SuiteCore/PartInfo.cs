using System;
using System.IO;
using System.Linq;

namespace CommonSuite
{
    /// <summary>Lookup partnumber's result; Binary is the stock file in Binaries when there is one.</summary>
    public sealed record PartInfo(string PartNumber, string CarModel, string EngineType, int Bhp, int Torque, bool TwoLiter, bool TwoPointThreeLiter,
        bool Turbo, bool FullPressureTurbo, string Binary)
    {
        /// <summary>&lt;name&gt;.bin in Binaries next to the executable (the T7Extras / T8Extras installers put the stock bins there).</summary>
        public static string StockBinary(string name)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, name + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault()
                : null;
        }
    }
}
