using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using TrionicCANLib.API;

namespace Trionic5Tools
{
    /// <summary>
    /// The connection to a T5 ECU: the byte terminal on the P-bus (no session, no security access, no keep-alive), on EcuWorker's
    /// thread, which replaces T5Suite's polling flags and locks. "Connected" means the adapter is open and the ECU answered its
    /// software version (ECUConnection / StartECUConnection).
    /// </summary>
    public sealed class T5Ecu : EcuWorker<Trionic5>
    {
        public T5Ecu() : base(new Trionic5(), "T5 ECU")
        {
        }

        /// <summary>SRAM 0x7FC0: T5Suite's sync date, year (2 bytes), month, day, hour, minute, second; the bin keeps one too.</summary>
        public const ushort SyncDateAddress = 0x7FC0;

        /// <summary>The ECU's software version, or null when it didn't answer (then the adapter is closed again).</summary>
        public Task<string> ConnectAsync(AppSettings settings, bool isT52) => RunAsync(t =>
        {
            if (IsConnected) return Version(t, isT52);
            if (t.isOpen()) Close(t);
            CanAdapters.Setup(t, settings, Latency.Default);
            if (!t.openDevice())
            {
                t.Cleanup();
                return null;
            }
            string sw = Version(t, isT52);
            if (sw == "")
            {
                t.Cleanup();
                return null;
            }
            IsConnected = true;
            return sw;
        });

        // trimmed, without the terminal's prompt, the last 12 characters (ECUConnection)
        private static string Version(Trionic5 t, bool isT52)
        {
            string sw = (isT52 ? t.getSWVersionT52(false) : t.getSWVersion(false)) ?? "";
            sw = sw.Replace(">", "").Trim();
            return sw.Length > 12 ? sw[^12..] : sw;
        }

        // ---- SRAM ----

        /// <summary>ReadSymbolData: the bytes at the symbol's 16-bit SRAM address (the library gives zeros on a timeout).</summary>
        public Task<byte[]> ReadAsync(int sramAddress, int length) =>
            RunAsync(t => IsConnected ? t.readRAM((ushort)sramAddress, (uint)length) : null);

        public Task<byte[]> ReadMapAsync(SymbolHelper sh) => ReadAsync((int)sh.Start_address, sh.Length);

        /// <summary>WriteSymbolData: the changed bytes, then the sync date stamped "now" in the ECU.</summary>
        public Task<bool> WriteMapAsync(SymbolHelper sh, byte[] data) => RunAsync(t =>
        {
            if (!IsConnected) return false;
            bool ok = t.writeRam((ushort)sh.Start_address, data);
            t.writeRam(SyncDateAddress, EncodeDate(DateTime.Now));
            return ok;
        });

        /// <summary>WriteSymbolDataForced: every byte, no date (synchronize, SRAM upload, clearing counters).</summary>
        public Task<bool> WriteForcedAsync(int sramAddress, byte[] data) =>
            RunAsync(t => IsConnected && t.writeRamForced((ushort)sramAddress, data));

        /// <summary>The ECU's sync date; 2000-01-01 when it has none (never stamped, or SRAM lost).</summary>
        public Task<DateTime> ReadSyncDateAsync() => RunAsync(t => IsConnected ? DecodeDate(t.readRAM(SyncDateAddress, 7)) : NoDate);

        public Task WriteSyncDateAsync(DateTime date) => RunAsync(t =>
        {
            if (IsConnected) t.writeRam(SyncDateAddress, EncodeDate(date));
        });

        public static readonly DateTime NoDate = new(2000, 1, 1);

        public static byte[] EncodeDate(DateTime d) =>
            [(byte)(d.Year >> 8), (byte)d.Year, (byte)d.Month, (byte)d.Day, (byte)d.Hour, (byte)d.Minute, (byte)d.Second];

        public static DateTime DecodeDate(byte[] b)
        {
            if (b is not { Length: >= 7 }) return NoDate;
            try
            {
                var d = new DateTime(b[0] << 8 | b[1], b[2], b[3], b[4], b[5], b[6]);
                return d.Year is >= 2000 and <= 2100 ? d : NoDate;
            }
            catch (ArgumentOutOfRangeException)
            {
                return NoDate;
            }
        }

        /// <summary>Download SRAM from ECU: the 32 KB SRAM image (file offset = SRAM address).</summary>
        public Task<bool> SnapshotAsync(string file) => RunAsync(t =>
        {
            if (!IsConnected) return false;
            t.GetSRAMSnapshot(file);
            return System.IO.File.Exists(file);
        });

        // ---- flash: a session of its own, the T5 CAN Flasher's way ----

        /// <summary>Download flash from ECU: MyBooty, the 128 / 256 KB dump with its checksum check, then the bootloader exits.</summary>
        public async Task<bool> ReadFlashAsync(AppSettings settings, string file)
        {
            IsFlashing = true;
            try
            {
                if (!await OpenFlasherAsync(settings)) return false;
                return await RunAsync(t =>
                {
                    var args = new DoWorkEventArgs(file);
                    t.DumpECU(null, args);
                    return args.Result is true;
                });
            }
            finally
            {
                await RunAsync(t => t.Cleanup());
                IsFlashing = false;
            }
        }

        /// <summary>Upload flash to ECU: the library detects the ECU and asks about T5.2 / T5.5 conversions through UserPrompt.</summary>
        public async Task<WriteFlashResult> WriteFlashAsync(AppSettings settings, string file)
        {
            IsFlashing = true;
            try
            {
                if (!await OpenFlasherAsync(settings)) return WriteFlashResult.Failed;
                return await RunAsync(t => t.WriteFlash(file));
            }
            finally
            {
                // a failed write keeps MyBooty running for a retry, as the flasher does; Cleanup only closes the adapter
                await RunAsync(t => t.Cleanup());
                IsFlashing = false;
            }
        }

        private Task<bool> OpenFlasherAsync(AppSettings settings) => RunAsync(t =>
        {
            if (IsConnected || t.isOpen()) Close(t);
            CanAdapters.Setup(t, settings, Latency.Default);
            if (t.openDevice())
            {
                System.Threading.Thread.Sleep(1000);
                return true;
            }
            t.Cleanup();
            return false;
        });

        // ---- error counters (T5 has no OBD-II DTCs) ----

        /// <summary>Read DTC codes: every 1-byte symbol named *_error* or *_fel* whose SRAM value isn't 0, with its count.</summary>
        public Task<List<(SymbolHelper Symbol, int Count)>> ReadErrorCountersAsync(IEnumerable<SymbolHelper> symbols) => RunAsync(t =>
        {
            var found = new List<(SymbolHelper, int)>();
            if (!IsConnected) return found;
            foreach (SymbolHelper sh in ErrorCounters(symbols))
            {
                byte[] v = t.readRAM((ushort)sh.Start_address, 1);
                if (v is { Length: > 0 } && v[0] > 0) found.Add((sh, v[0]));
            }
            return found;
        });

        /// <summary>The error counters' symbols: 1 byte, in SRAM, named *_error* or *_fel*.</summary>
        public static IEnumerable<SymbolHelper> ErrorCounters(IEnumerable<SymbolHelper> symbols) =>
            symbols.Where(sh => sh.Length == 1 && sh.Start_address > 0 && (sh.Varname.Contains("_error") || sh.Varname.Contains("_fel")));

        /// <summary>Clear knock counters: Knock_count_map and Knock_count_cyl1..4 zeroed (symbols the bin lacks are skipped).</summary>
        public Task ClearKnockCountersAsync(Func<string, SymbolHelper> find) => RunAsync(t =>
        {
            if (!IsConnected) return;
            if (find("Knock_count_map") is { Start_address: > 0 } map) t.writeRam((ushort)map.Start_address, new byte[map.Length]);
            for (int cyl = 1; cyl <= 4; cyl++)
                if (find("Knock_count_cyl" + cyl) is { Start_address: > 0 } c) t.writeRamForced((ushort)c.Start_address, new byte[2]);
        });
    }
}
