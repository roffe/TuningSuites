using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using TrionicCANLib.API;

namespace CommonSuite
{
    /// <summary>The CAN adapters the settings offer, and the library's setup for one: the same for every Trionic.</summary>
    public static class CanAdapters
    {
        /// <summary>The adapter types in CANBusAdapter order (their descriptions are what AppSettings stores).</summary>
        public static string[] Types => Enum.GetValues<CANBusAdapter>().Select(a => EnumHelper.GetDescription(a)).ToArray();

        public static CANBusAdapter? FromDescription(string description) =>
            Enum.GetValues<CANBusAdapter>().Cast<CANBusAdapter?>().FirstOrDefault(a => EnumHelper.GetDescription(a.Value) == description);

        /// <summary>The serial adapters, which take the settings' speed.</summary>
        public static bool NeedsBaudrate(CANBusAdapter? adapter) => adapter is CANBusAdapter.ELM327 or CANBusAdapter.JUST4TRIONIC or CANBusAdapter.SLCAN;

        /// <summary>
        /// SetupCanAdapter / SetCanAdapter: P-bus only, latency, forced baudrate for the serial adapters, the adapter type and the
        /// chosen adapter. Throws a message when no adapter is configured. SLCAN is set up too (the suites' if-chains had no SLCAN
        /// branch, so choosing it connected nothing).
        /// </summary>
        public static void Setup(ITrionic t, AppSettings settings, Latency latency)
        {
            if (FromDescription(settings.AdapterType) is not { } adapter)
                throw new InvalidOperationException("Check settings, no CAN adapter has been selected!");
            t.OnlyPBus = settings.OnlyPBus;
            t.Latency = latency;
            if (NeedsBaudrate(adapter)) t.ForcedBaudrate = settings.Baudrate;
            t.setCANDevice(adapter);
            if (!string.IsNullOrEmpty(settings.Adapter)) t.SetSelectedAdapter(settings.Adapter);
            else if (adapter != CANBusAdapter.COMBI) throw new InvalidOperationException("Check settings, no CAN adapter has been selected!");
        }
    }

    /// <summary>
    /// A Trionic library object on a thread of its own. TrionicCANLib is synchronous and thread-affine (KWP's mutex, one thread
    /// per session), so every call runs on this thread and comes back as a task; its events are passed on from there. The
    /// protocol is the suite's: T7Ecu and T8Ecu add their calls.
    /// </summary>
    public abstract class EcuWorker<T> : IDisposable where T : ITrionic
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly BlockingCollection<Action> m_work = new();

        public T Trionic { get; }

        /// <summary>m_RealtimeConnectedToECU: a realtime session is open.</summary>
        public bool IsConnected { get; protected set; }

        /// <summary>A read, flash or recovery is running; closing the app now would leave it half done.</summary>
        public bool IsFlashing { get; protected set; }

        public event EventHandler<ITrionic.CanInfoEventArgs> Info;
        public event EventHandler<int> Progress;

        protected EcuWorker(T trionic, string threadName)
        {
            Trionic = trionic;
            trionic.onCanInfo += (s, e) => Info?.Invoke(this, e);
            trionic.onReadProgress += (s, e) => Progress?.Invoke(this, e.Percentage);
            trionic.onWriteProgress += (s, e) => Progress?.Invoke(this, e.Percentage);
            var thread = new Thread(() =>
            {
                foreach (Action work in m_work.GetConsumingEnumerable()) work();
            })
            {
                IsBackground = true,
                Name = threadName,
            };
            try { thread.Priority = ThreadPriority.AboveNormal; } catch (Exception) { }
            thread.Start();
        }

        /// <summary>Runs a call on the ECU thread.</summary>
        public Task<TResult> RunAsync<TResult>(Func<T, TResult> call)
        {
            var done = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        public Task RunAsync(Action<T> call) => RunAsync<bool>(t => { call(t); return true; });

        /// <summary>Ends the session on the ECU thread: Cleanup closes the adapter.</summary>
        protected virtual void Close(T t)
        {
            t.Cleanup();
            IsConnected = false;
        }

        public Task DisconnectAsync() => RunAsync(Close);

        /// <summary>Closes whatever session is open so the library's adapter threads end and the process can exit.</summary>
        public void Dispose()
        {
            if (m_work.IsAddingCompleted) return;
            try { DisconnectAsync().Wait(TimeSpan.FromSeconds(5)); } catch (Exception e) { logger.Debug(e); }
            m_work.CompleteAdding();
        }
    }
}
