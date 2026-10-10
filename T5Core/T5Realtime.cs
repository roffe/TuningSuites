using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>T5Suite's realtime table on the shared panel. ponytail: rows, names and texts come with the realtime chunk.</summary>
    public sealed class T5Realtime : RealtimeRules
    {
        public static T5Realtime Rules { get; } = new();

        public override IReadOnlySet<string> Signed { get; } = new HashSet<string>();

        public override IReadOnlyDictionary<string, (string Prefix, int Size)> PerCylinder { get; } = new Dictionary<string, (string, int)>();

        public override DashboardSymbols Symbols { get; } = new("", "", "", "");

        public override string LogExtension => "t5l";

        public override string LogFilesName => "Trionic 5 logfiles";

        public override string LayoutExtension => "t5rtl";

        public override List<RealtimeSymbol> Dashboard(SuiteBinary bin, AppSettings settings) => [];

        public override RealtimeSymbol FromSymbol(SymbolHelper sh) => UserRow(sh, sh.SmartVarname, 0, sh.Length == 1 ? 255 : 65535, 1);

        public override IReadOnlyList<CellRule> CellRules { get; } = [];

        public override string AirDemand(int value) => value.ToString();
        public override string Lambda(int value) => value.ToString();
        public override string Fuelcut(int value) => value.ToString();
    }

    /// <summary>T5's passes: every row read from SRAM by address over the terminal (6 bytes per round trip).</summary>
    public sealed class T5RealtimeEngine(T5Ecu ecu) : RealtimeEngine
    {
        protected override bool Connected => ecu.IsConnected;

        protected override Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps) => ecu.RunAsync(t =>
        {
            // the session closed while this pass waited (see RealtimeEngine.PassAsync)
            if (!ecu.IsConnected) return null;
            return Realtime.Cycle(rows, row => row.SramAddress > 0 && row.Length > 0 ? t.readRAM((ushort)row.SramAddress, (uint)row.Length) : null,
                DateTime.Now, T5Realtime.Rules, fps);
        });
    }
}
