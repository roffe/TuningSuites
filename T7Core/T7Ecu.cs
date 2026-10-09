using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;
using NLog;
using TrionicCANLib.API;

namespace T7
{
    /// <summary>
    /// The connection to a T7 ECU. TrionicCANLib's KWP handler holds a thread-affine mutex and expects one thread per session,
    /// so every call of a session runs on this object's own thread, queued and awaited (the flasher's RunOnWorker, kept alive
    /// for as long as the suite stays connected). Events from the library arrive on its threads; the UI posts them over.
    /// </summary>
    public sealed class T7Ecu : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly BlockingCollection<Action> m_work = new();
        private readonly Thread m_thread;

        public Trionic7 Trionic { get; } = new();

        /// <summary>m_RealtimeConnectedToECU: a KWP session is open.</summary>
        public bool IsConnected { get; private set; }

        public event EventHandler<ITrionic.CanInfoEventArgs> Info;
        public event EventHandler<int> Progress;

        public T7Ecu()
        {
            Trionic.onCanInfo += (s, e) => Info?.Invoke(this, e);
            Trionic.onReadProgress += (s, e) => Progress?.Invoke(this, e.Percentage);
            Trionic.onWriteProgress += (s, e) => Progress?.Invoke(this, e.Percentage);
            m_thread = new Thread(() =>
            {
                foreach (Action work in m_work.GetConsumingEnumerable()) work();
            })
            {
                IsBackground = true,
                Name = "T7 ECU",
            };
            try { m_thread.Priority = ThreadPriority.AboveNormal; } catch (Exception) { }
            m_thread.Start();
        }

        /// <summary>Runs a call on the ECU thread.</summary>
        public Task<T> RunAsync<T>(Func<Trionic7, T> call)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_work.Add(() =>
            {
                try
                {
                    done.SetResult(call(Trionic));
                }
                catch (Exception e)
                {
                    logger.Debug(e);
                    done.SetException(e);
                }
            });
            return done.Task;
        }

        public Task RunAsync(Action<Trionic7> call) => RunAsync<bool>(t => { call(t); return true; });

        /// <summary>The adapter types the settings offer, in CANBusAdapter order (their descriptions are what AppSettings stores).</summary>
        public static string[] AdapterTypes => Enum.GetValues<CANBusAdapter>().Select(a => EnumHelper.GetDescription(a)).ToArray();

        public static CANBusAdapter? AdapterFromDescription(string description) =>
            Enum.GetValues<CANBusAdapter>().Cast<CANBusAdapter?>().FirstOrDefault(a => EnumHelper.GetDescription(a.Value) == description);

        /// <summary>
        /// frmMain.SetupCanAdapter + RealtimeCheckAndConnect: P-bus only, latency, forced baudrate for the serial adapters,
        /// adapter type and the chosen adapter, then a KWP session. Throws a message when no adapter is configured.
        /// SLCAN is set up too (T7Suite's if-chain had no SLCAN branch, so choosing it connected nothing).
        /// </summary>
        public Task<bool> ConnectAsync(AppSettings settings, Latency latency = Latency.Low) => RunAsync(t =>
        {
            if (IsConnected) return true;
            Setup(t, settings, latency);
            if (t.openDevice())
            {
                IsConnected = true;
                t.ResumeAlivePolling();
                return true;
            }
            t.Cleanup();
            return false;
        });

        private static void Setup(Trionic7 t, AppSettings settings, Latency latency)
        {
            if (AdapterFromDescription(settings.AdapterType) is not { } adapter)
                throw new InvalidOperationException("Check settings, no CAN adapter has been selected!");
            t.OnlyPBus = settings.OnlyPBus;
            t.Latency = latency;
            if (adapter is CANBusAdapter.ELM327 or CANBusAdapter.JUST4TRIONIC or CANBusAdapter.SLCAN) t.ForcedBaudrate = settings.Baudrate;
            t.setCANDevice(adapter);
            if (!string.IsNullOrEmpty(settings.Adapter)) t.SetSelectedAdapter(settings.Adapter);
            else if (adapter != CANBusAdapter.COMBI) throw new InvalidOperationException("Check settings, no CAN adapter has been selected!");
        }

        public Task DisconnectAsync() => RunAsync(t =>
        {
            t.SuspendAlivePolling();
            t.Cleanup();
            IsConnected = false;
        });

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
            bool opened = await OpenFlasherAsync(settings);
            if (!opened) return (false, "Failed to start KWP session");
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
            }
        }

        private Task<bool> OpenFlasherAsync(AppSettings settings) => RunAsync(t =>
        {
            if (IsConnected || t.isOpen())
            {
                t.SuspendAlivePolling();
                t.Cleanup();
                IsConnected = false;
            }
            Setup(t, settings, Latency.Default);
            if (t.openDevice()) return true;
            t.Cleanup();
            return false;
        });

        /// <summary>Get SRAM snapshot: the 64 KB of SRAM to a .RAM file, in a flasher session.</summary>
        public async Task<bool> SnapshotAsync(AppSettings settings, string file)
        {
            if (!await OpenFlasherAsync(settings)) return false;
            try
            {
                return await RunAsync(t => t.GetSRAMSnapshot(file));
            }
            finally
            {
                await RunAsync(t => t.Cleanup());
            }
        }

        // ---- SRAM maps and DTCs: on the realtime connection ----

        /// <summary>ReadMapFromSRAM: the symbol's SRAM bytes (the library never reports a failed read, it returns zeros).</summary>
        public Task<byte[]> ReadMapAsync(SymbolHelper sh) => RunAsync(t => t.ReadMapfromSRAM(sh.Start_address, sh.Length, true));

        /// <summary>WriteMapToSRAM: by symbol number below 0xF00000, else in 64-byte chunks at the SRAM address.</summary>
        public Task WriteMapAsync(SymbolHelper sh, byte[] data) => RunAsync(t =>
        {
            if (sh.Symbol_number < 0) return;
            if (sh.Start_address < 0xF00000) t.WriteSymbolToSRAM((uint)sh.Symbol_number, data);
            else t.WriteMapToSRAM(sh.SmartVarname, data, true, (uint)sh.Start_address, sh.Symbol_number);
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

        public void Dispose()
        {
            if (IsConnected)
            {
                try { DisconnectAsync().Wait(TimeSpan.FromSeconds(5)); } catch (Exception e) { logger.Debug(e); }
            }
            m_work.CompleteAdding();
        }
    }
}
