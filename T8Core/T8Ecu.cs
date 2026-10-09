using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;
using TrionicCANLib;
using TrionicCANLib.API;

namespace T8SuitePro
{
    /// <summary>A row of Get ECU information (frmECUInformation): a label and its one or two values.</summary>
    public sealed record EcuInfoRow(string Group, string Label, string Value, string Value2 = "");

    /// <summary>
    /// The connection to a T8 ECU over GMLAN, on EcuWorker's own thread (T8Suite ran everything on its GUI thread). Every action
    /// first closes whatever the last one left open: T8Suite built a new adapter each time without closing the last.
    /// </summary>
    public sealed class T8Ecu : EcuWorker<Trionic8>
    {
        public T8Ecu() : base(new Trionic8(), "T8 ECU")
        {
        }

        // the next session starts with the keep-alive running, also when the realtime panel was polling at the disconnect
        protected override void Close(Trionic8 t)
        {
            t.StallKeepAlive = false;
            base.Close(t);
        }

        // SetCanAdapter, after closing the last device; Trionic8 has no latency
        private void Setup(Trionic8 t, AppSettings settings, AccessLevel level)
        {
            if (IsConnected || t.isOpen()) Close(t);
            t.SecurityLevel = level;
            CanAdapters.Setup(t, settings, Latency.Default);
        }

        /// <summary>
        /// RealtimeCheckAndConnect: a session with security access at level FD and the library's keep-alive; the ECU's software
        /// version for the status bar, null when it didn't open. Throws a message when no adapter is configured.
        /// </summary>
        public Task<string> ConnectAsync(AppSettings settings) => RunAsync(t =>
        {
            if (IsConnected) return t.GetSoftwareVersion();
            Setup(t, settings, AccessLevel.AccessLevelFD);
            if (!t.openDevice(true))
            {
                t.Cleanup();
                return null;
            }
            IsConnected = true;
            return t.GetSoftwareVersion();
        });

        // ---- the flasher: Read ECU, Flash, Recover ----

        /// <summary>Read ECU: the flash into a file, with the Legion bootloader unless the settings say otherwise.</summary>
        public Task<(bool ok, string message)> ReadFlashAsync(AppSettings settings, string file) =>
            FlashAsync(settings, file, false, (t, a) =>
            {
                if (settings.UseLegionBootloader) t.ReadFlashLegT8(null, a);
                else t.ReadFlash(null, a);
            });

        /// <summary>Flash a file to the ECU; needRecovery when it was erased but not programmed.</summary>
        public async Task<(bool ok, bool needRecovery, string message)> WriteFlashAsync(AppSettings settings, string file)
        {
            var (ok, message) = await FlashAsync(settings, file, false, (t, a) =>
            {
                t.NeedRecovery = false;
                if (settings.UseLegionBootloader) t.WriteFlashLegT8(null, a);
                else t.WriteFlash(null, a);
            });
            return (ok, !ok && Trionic.NeedRecovery, message);
        }

        /// <summary>
        /// Recover ECU: the file written to an ECU in its recovery state, which answers on 0x011 / 0x311; the adapters filter those
        /// out unless told otherwise (as TrionicCANFlasher does; T8Suite didn't, so recovery couldn't reach the ECU).
        /// </summary>
        public Task<(bool ok, string message)> RecoverAsync(AppSettings settings, string file) =>
            FlashAsync(settings, file, true, (t, a) =>
            {
                if (settings.UseLegionBootloader) t.RecoverECU_Leg(null, a);
                else t.RecoverECU_Def(null, a);
            });

        /// <summary>
        /// A flasher session: the adapter at level 01, openDevice without security access, a second's wait as the flasher does,
        /// then the library's BackgroundWorker-shaped call on this thread, whose Result is the outcome. ReadFlash returns without
        /// one when security access is refused (T8Suite waited for it forever): that's a failure. The device is closed afterwards
        /// (T8Suite left it open). The message is the library's last.
        /// </summary>
        private async Task<(bool ok, string message)> FlashAsync(AppSettings settings, string file, bool recovery, Action<Trionic8, DoWorkEventArgs> run)
        {
            string last = "";
            void OnInfo(object s, ITrionic.CanInfoEventArgs e) => last = e.Info;
            IsFlashing = true;
            Info += OnInfo;
            try
            {
                return await RunAsync(t =>
                {
                    Setup(t, settings, AccessLevel.AccessLevel01);
                    if (recovery) t.SetCANFilterIds(Trionic8.FilterIdRecovery);
                    if (!t.openDevice(false)) return (false, "Unable to connect to Trionic 8 ECU");
                    Thread.Sleep(1000);
                    var args = new DoWorkEventArgs(file);
                    run(t, args);
                    return (args.Result is true, last);
                });
            }
            finally
            {
                Info -= OnInfo;
                await RunAsync(Close);
                IsFlashing = false;
            }
        }

        // ---- on the realtime connection ----

        /// <summary>ReadMapFromSRAM: the symbol's SRAM bytes in 0x40-byte blocks, null when the read failed.</summary>
        public Task<byte[]> ReadMapAsync(SymbolHelper sh) => RunAsync(t => t.readMemoryNew((int)sh.Start_address, sh.Length, 0x40, true));

        /// <summary>WriteMapToSRAM: the bytes to the symbol's SRAM address in 0x40-byte blocks.</summary>
        public Task<bool> WriteMapAsync(SymbolHelper sh, byte[] data) => RunAsync(t => t.writeMemoryNew((int)sh.Start_address, data, 0x40, true));

        /// <summary>
        /// Get ECU information: frmECUInformation's fields, each a ReadDataByIdentifier. On the realtime connection when there is
        /// one, else a session of its own that is closed afterwards (T8Suite left it open).
        /// </summary>
        public Task<List<EcuInfoRow>> ReadInfoAsync(AppSettings settings) => RunAsync(t =>
        {
            bool own = !IsConnected;
            if (own)
            {
                Setup(t, settings, AccessLevel.AccessLevelFD);
                if (!t.openDevice(true))
                {
                    t.Cleanup();
                    return null;
                }
            }
            try
            {
                const string ecu = "ECU related data", cal = "Calibration data";
                return new List<EcuInfoRow>
                {
                    new(ecu, "ECU description", t.GetECUDescription(), t.GetECUHardware()),
                    new(ecu, "Hardware type", t.RequestECUInfoAsString(0x97), t.RequestECUInfoAsString(0x92)),
                    new(ecu, "Build date", t.GetBuildDate()),
                    new(ecu, "Serial number", t.GetSerialNumber()),
                    new(ecu, "SAAB partnumber", t.GetSaabPartnumber()),
                    new(ecu, "Basemodel partnumber", t.GetInt64FromIdAsString(0xCC), t.GetInt64FromIdAsString(0xCB)),
                    new(cal, "Calibration set", t.GetCalibrationSet()),
                    new(cal, "Codefile version", t.GetCodefileVersion()),
                    new(cal, "Software version", t.GetSoftwareVersion()),
                    new(cal, "Software version file", t.RequestECUInfoAsString(0x0F)),
                    new(cal, "Software IDs", t.RequestECUInfoAsString(0xC1), t.RequestECUInfoAsString(0xC2)),
                    new(cal, "", t.RequestECUInfoAsString(0xC3), t.RequestECUInfoAsString(0xC4)),
                    new(cal, "", t.RequestECUInfoAsString(0xC5), t.RequestECUInfoAsString(0xC6)),
                    new(cal, "VIN number", t.GetVehicleVIN()),
                    new(cal, "Engine type", t.RequestECUInfoAsString(0x0C)),
                    new(cal, "Speedlimit", t.GetTopSpeed() + " km/h"),
                };
            }
            finally
            {
                if (own) t.Cleanup();
            }
        });

        // ---- fault codes: a session at level 01 without security access, closed straight after ----

        /// <summary>Get fault codes (OBDII): ReadDTC's codes, null when the ECU didn't answer (T8Suite showed an empty list).</summary>
        public Task<string[]> ReadFaultCodesAsync(AppSettings settings) => DtcSession(settings, t => FaultCodes(t.ReadDTC()));

        /// <summary>
        /// Clear: every code (the library clears them all; T8Suite's single-code clear was a TODO), on a session of its own (T8Suite
        /// used the one it had closed, so Clear did nothing). False when the ECU didn't confirm.
        /// </summary>
        public async Task<bool> ClearFaultCodesAsync(AppSettings settings) => await DtcSession(settings, t => t.ClearDTCCodes() ? "" : null) != null;

        /// <summary>Clear DTC and knock counters: the codes read first, as T8Suite did, then cleared.</summary>
        public async Task<bool> ClearDtcAndKnockCountersAsync(AppSettings settings) =>
            await DtcSession(settings, t =>
            {
                t.ReadDTC();
                return t.ClearDTCCodes() ? "" : null;
            }) != null;

        private Task<TResult> DtcSession<TResult>(AppSettings settings, Func<Trionic8, TResult> call) where TResult : class => RunAsync(t =>
        {
            Setup(t, settings, AccessLevel.AccessLevel01);
            try
            {
                return t.openDevice(false) ? call(t) : null;
            }
            finally
            {
                t.Cleanup();
            }
        });

        /// <summary>ReadDTC's "DTC: P0123 StatusByte: XX" lines as codes; the closing "No more errors!" isn't one.</summary>
        internal static string[] FaultCodes(string[] lines) =>
            lines?.Where(l => l.StartsWith("DTC: ") && l.Length >= 10).Select(l => l.Substring(5, 5)).Distinct().ToArray() ?? [];
    }
}
