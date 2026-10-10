using System.Linq;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>When a realtime pass may tune the fuel map (frmMain.CheckAutoTuneParameters and ctrlRealtime's enrichment filter).</summary>
    public static class T5Autotune
    {
        /// <summary>
        /// CheckAutoTuneParameters over Pgm_status: not while enriching after a fuel cut, purging, warming up, in afterstart, in closed
        /// loop, with the knock fuel map, in fuel cut or with the throttle closed (each per its setting), or idling unless idle autotune
        /// is allowed; idle tells the idle map is the one to tune.
        /// </summary>
        public static bool Allowed(long status, AppSettings s, out bool idle)
        {
            bool Bit(long mask) => (status & mask) != 0;
            idle = Bit(0x40000000) && s.AllowIdleAutoTune;
            if (Bit(0x2000000000)) return false;                            // enrichment after fuel cut
            if (Bit(0x40000000) && !s.AllowIdleAutoTune) return false;      // idle map
            if (Bit(0x20000000)) return false;                              // purge control active
            if (!Bit(0x10000000)) return false;                             // cooling water enrichment not finished
            if (!Bit(0x4000000)) return false;                              // afterstart enrichment not completed
            if (Bit(0x2000000)) return false;                               // lambda control active
            if (s.DiscardFuelcutMeasurements && (Bit(0xF000) || Bit(0x20))) return false; // fuel cut, per cylinder or all
            if (s.DiscardClosedThrottleMeasurements && Bit(0x400)) return false;           // throttle closed
            if (!Bit(0x10)) return false;                                   // engine not warm
            return !Bit(0x200);                                             // fuel knock map active
        }

        private static readonly string[] Enrichments = ["LoadAccCyl", "TPSAccCyl", "LoadRetCyl", "TPSRetCyl"];

        /// <summary>The enrichment filter: a cylinder's load / TPS enrichment or enleanment above it skips the pass.</summary>
        public static bool Enriching(RealtimeSample sample, int filter) =>
            Enrichments.Any(p => Enumerable.Range(1, 4).Any(c => sample[p + c] > filter));
    }
}
