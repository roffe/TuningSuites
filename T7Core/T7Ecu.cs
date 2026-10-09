using System;
using System.Threading.Tasks;
using CommonSuite;
using TrionicCANLib.API;

namespace T7
{
    /// <summary>
    /// The connection to a T7 ECU over KWP2000, on EcuWorker's own thread (TrionicCANLib's KWP handler holds a thread-affine
    /// mutex and expects one thread per session), kept alive for as long as the suite stays connected.
    /// </summary>
    public sealed class T7Ecu : EcuWorker<Trionic7>
    {
        public T7Ecu() : base(new Trionic7(), "T7 ECU")
        {
        }

        /// <summary>
        /// frmMain.SetupCanAdapter + RealtimeCheckAndConnect: the adapter, then a KWP session. Throws a message when no adapter
        /// is configured.
        /// </summary>
        public Task<bool> ConnectAsync(AppSettings settings, Latency latency = Latency.Low) => RunAsync(t =>
        {
            if (IsConnected) return true;
            CanAdapters.Setup(t, settings, latency);
            if (t.openDevice())
            {
                IsConnected = true;
                t.ResumeAlivePolling();
                return true;
            }
            t.Cleanup();
            return false;
        });

        protected override void Close(Trionic7 t)
        {
            t.SuspendAlivePolling();
            base.Close(t);
        }

        // ---- flashing: a session of its own (FlasherConnect: Latency.Default, closes a realtime connection first) ----

        /// <summary>
        /// Read ECU: the flash to a file. The library reads on its own thread and reports the end through onCanInfo; the
        /// result is that message ("Finished download of FLASH" or why not). T7Suite said "Download done" either way.
        /// </summary>
        public Task<(bool ok, string message)> ReadFlashAsync(AppSettings settings, string file) =>
            FlashAsync(settings, ActivityType.FinishedDownloadingFlash, t => t.ReadFlash(file));

        /// <summary>Flash a file to the ECU, the same way; "Finished FLASH session" or the error.</summary>
        public Task<(bool ok, string message)> WriteFlashAsync(AppSettings settings, string file) =>
            FlashAsync(settings, ActivityType.FinishedFlashing, t => t.WriteFlash(file));

        // ponytail: ReadFlash / WriteFlash silently do nothing while the flasher is busy, the finish never comes; a full
        // write takes a few minutes, so 20 minutes means it was never started
        private static readonly TimeSpan FlashTimeout = TimeSpan.FromMinutes(20);

        private async Task<(bool ok, string message)> FlashAsync(AppSettings settings, ActivityType finished, Action<Trionic7> start)
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnInfo(object s, ITrionic.CanInfoEventArgs e)
            {
                if (e.Type == finished) done.TrySetResult(e.Info);
            }
            IsFlashing = true;
            bool opened;
            try { opened = await OpenFlasherAsync(settings); }
            catch { IsFlashing = false; throw; }
            if (!opened) { IsFlashing = false; return (false, "Failed to start KWP session"); }
            Info += OnInfo;
            try
            {
                await RunAsync(start);
                string message = await done.Task.WaitAsync(FlashTimeout);
                return (message.StartsWith("Finished"), message);
            }
            catch (TimeoutException)
            {
                return (false, "The ECU did not finish, the flasher may have been busy");
            }
            finally
            {
                Info -= OnInfo;
                await RunAsync(t => t.Cleanup());
                IsFlashing = false;
            }
        }

        private Task<bool> OpenFlasherAsync(AppSettings settings) => RunAsync(t =>
        {
            if (IsConnected || t.isOpen()) Close(t);
            CanAdapters.Setup(t, settings, Latency.Default);
            if (t.openDevice()) return true;
            t.Cleanup();
            return false;
        });

        /// <summary>Get SRAM snapshot: the 64 KB of SRAM to a .RAM file, in a flasher session.</summary>
        public async Task<bool> SnapshotAsync(AppSettings settings, string file)
        {
            IsFlashing = true;
            try
            {
                if (!await OpenFlasherAsync(settings)) return false;
                return await RunAsync(t => t.GetSRAMSnapshot(file));
            }
            finally
            {
                await RunAsync(t => t.Cleanup());
                IsFlashing = false;
            }
        }

        // ---- SRAM maps and DTCs: on the realtime connection ----

        /// <summary>ReadMapFromSRAM: the symbol's SRAM bytes (the library never reports a failed read, it returns zeros).</summary>
        public Task<byte[]> ReadMapAsync(SymbolHelper sh) => RunAsync(t => t.ReadMapfromSRAM(sh.Start_address, sh.Length, true));

        /// <summary>
        /// WriteMapToSRAM: by symbol number below 0xF00000, else in 64-byte chunks at the SRAM address. False when the ECU
        /// refused a write (a closed binary does); T7Suite ignored the answer and said nothing. The chunks are written here
        /// because the library's chunked writer only logs a refusal.
        /// </summary>
        public Task<bool> WriteMapAsync(SymbolHelper sh, byte[] data) => RunAsync(t =>
        {
            if (sh.Symbol_number < 0) return false;
            if (sh.Start_address < 0xF00000) return t.WriteSymbolToSRAM((uint)sh.Symbol_number, data);
            for (int i = 0; i < data.Length; i += 64)
            {
                if (!t.WriteMapToSRAM((uint)(sh.Start_address + i), data[i..Math.Min(i + 64, data.Length)])) return false;
            }
            return true;
        });

        /// <summary>Get fault codes: obdFaults read from SRAM, "Pxxxx" per byte pair until 00 00.</summary>
        public Task<string[]> ReadFaultCodesAsync(SymbolHelper obdFaults) => RunAsync(t =>
        {
            bool ok;
            byte[] data;
            if (obdFaults.Length <= 4)
            {
                // ReadValueFromSRAM answers with the data from byte 1
                byte[] reply = t.ReadValueFromSRAM(obdFaults.Start_address, obdFaults.Length, out ok);
                data = ok && reply.Length > 1 ? reply[1..] : [];
            }
            else
            {
                data = t.ReadSymbolNumber((uint)obdFaults.Symbol_number, out ok);
            }
            return ok ? FaultCodes(data) : [];
        });

        internal static string[] FaultCodes(byte[] data)
        {
            var codes = new System.Collections.Generic.List<string>();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0 && data[i + 1] == 0) break;
                codes.Add($"P{data[i]:X2}{data[i + 1]:X2}");
            }
            return codes.ToArray();
        }

        /// <summary>Clear one code ("Pxxxx", the digits as hex) / all codes and the knock counters.</summary>
        public Task ClearFaultCodeAsync(string code) => RunAsync(t => { t.ClearDTCCode(Convert.ToInt32(code[1..], 16)); });

        public Task ClearAllFaultCodesAsync() => RunAsync(t =>
        {
            t.ReadDTC();
            t.ClearDTCCodes();
        });
    }
}
