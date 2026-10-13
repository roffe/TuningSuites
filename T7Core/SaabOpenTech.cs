using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using TrionicCANLib.CAN;

namespace T7
{
    /// <summary>
    /// SaabOpenTech v0.41 (Tomi Liljemark, enhanced by Mattias Claesson; saabopentech/windows/main.c), the Saab I-bus maintenance
    /// terminal T7Suite ran as SaabOpenTech.exe for Realtime → Extra functions: KWP over CAN to the car's modules (TWICE, SID, MIU,
    /// ACC, the audio head unit, SPA and the Trionic). <see cref="Run"/> takes the program's arguments and returns what it printed,
    /// which T7Suite showed as its "Communication report". The bus is the caller's, opened on the I-bus: send puts a frame on it,
    /// next takes the next frame of any id, null when none came within the timeout. Unlike the program, a module that doesn't
    /// answer says so: its debug messages for timeouts are always printed, with the other modules' answers that came instead.
    /// The functions T7Suite's menu had are ported (S, U, I, T, M, P, Z); the program's trunk locking (L, R, V), brake light (B)
    /// and audio (A) functions were never in it.
    /// </summary>
    public sealed class SaabOpenTech
    {
        private enum Unit { Audio, Sid, Acc, Miu, Twice, Trionic, Spa }

        // init_table, unit_table, reply_table and init_reply_table, without the CDC ("needs more research")
        private static readonly byte[] InitTable = { 0x81, 0x65, 0x98, 0x61, 0x45, 0x11, 0x66 };
        private static readonly byte[] UnitTable = { 0x91, 0x96, 0x98, 0x9A, 0x9B, 0xA1, 0x9F };
        private static readonly uint[] ReplyTable = { 0x248, 0x24B, 0x24D, 0x24E, 0x24F, 0x258, 0x254 };
        private static readonly uint[] InitReplyTable = { 0x228, 0x22B, 0x22D, 0x22E, 0x22F, 0x238, 0x234 };

        private const string Banner = "SaabOpenTech v0.41 - Freeware Saab maintenance terminal\nby Tomi Liljemark & Mattias Claesson 2018-01-12\n\n";

        /// <summary>What the program printed when it couldn't open the adapter.</summary>
        public const string OpenFailed = Banner + "Opening CAN channel to Saab I-Bus (47,619 kBit/s)...Failed to open device\n";

        private readonly Func<uint, byte[], bool> m_send;
        private readonly Func<int, (uint Id, byte[] Data)?> m_next;
        private readonly Action<int> m_sleep;
        private readonly StringBuilder m_out = new StringBuilder();
        // the diagnostic answers passed over since the last wait that got its frame, for the message when one doesn't
        private readonly List<string> m_seen = new List<string>();
        // main's buf: one for the whole run, so a shorter answer leaves the end of the one before it, as there
        private readonly byte[] m_buf = new byte[256];

        public SaabOpenTech(Func<uint, byte[], bool> send, Func<int, (uint Id, byte[] Data)?> next, Action<int> sleep)
        {
            m_send = send;
            m_next = next;
            m_sleep = sleep;
        }

        /// <summary>
        /// main on an adapter set up for the T7 (Trionic7.setCANDevice): opened on the I-bus as the program opened its CANUSB, the
        /// function run on it and its report. The adapter's own open finds the bus; it is the caller's to close.
        /// </summary>
        // ponytail: an adapter that finds no I-bus traffic falls back to the P-bus as for the Trionic, where the other modules don't
        // answer ("no response"); the report's bus check can't tell the two buses apart
        public static string Run(ICANDevice device, string arguments, Action<int> sleep = null)
        {
            device.UseOnlyPBus = false;
            // the adapter's filter open, for the bus check and the modules' ids
            device.bypassCANfilters = true;
            if (device.open() != OpenResult.OK) return OpenFailed;
            // and the device's own: it hands its listeners only the Trionic's ids (Trionic7.FilterIdECU), null is every id; set
            // after the open, which goes through the list
            device.AcceptOnlyMessageIds = null;
            var frames = new FrameQueue();
            device.addListener(frames);
            try
            {
                return new SaabOpenTech((id, data) => device.sendMessage(Frame(id, data)), frames.Next, sleep ?? Thread.Sleep).Run(arguments);
            }
            finally
            {
                device.removeListener(frames);
            }
        }

        private static CANMessage Frame(uint id, byte[] data)
        {
            var msg = new CANMessage(id, 0, 8);
            msg.setCanData(data, 8);
            return msg;
        }

        // the frames the device hands its listeners, copied as they come: a device fills one CANMessage for every frame it reads
        private sealed class FrameQueue : ICANListener
        {
            private readonly BlockingCollection<(uint Id, ulong Data)> m_frames = new();

            public override void handleMessage(CANMessage a_canMessage) => m_frames.Add((a_canMessage.getID(), a_canMessage.getData()));

            public override bool messagePending() => m_frames.Count > 0;

            public override void dumpQueue() { }

            public override void FlushQueue()
            {
                while (m_frames.TryTake(out _)) { }
            }

            public (uint Id, byte[] Data)? Next(int timeout)
            {
                if (!m_frames.TryTake(out var frame, Math.Max(0, timeout))) return null;
                var data = new byte[8];
                for (int i = 0; i < 8; i++) data[i] = (byte)(frame.Data >> (8 * i));
                return (frame.Id, data);
            }
        }

        /// <summary>main with the device open: the bus check, a second's pause, the function ("S 1", "Z", ...), the close.</summary>
        public string Run(string arguments)
        {
            string[] args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            char function = char.ToUpperInvariant(args[0][0]);
            string param = args.Length > 1 ? args[1] : null;
            m_out.Append(Banner).Append("Opening CAN channel to Saab I-Bus (47,619 kBit/s)...OK\n");
            if (Wait(0, 1000) == null)
            {
                Print("Error: Timeout while listening to bus for any messages.\n\nCAN channel closed.\n");
                return m_out.ToString();
            }
            Print("Message received from bus, everything seems OK.\n\n");
            m_sleep(1000);
            switch (function)
            {
                case 'S': TwiceBit("seat belt warning beep", "Seat belt warning beep", 0x80, param); break;
                case 'U': TwiceBit("double unlocking with remote", "Double unlocking with remote", 0x20, param); break;
                case 'Z': AlarmLevel(param); break;
                case 'I': SystemInformation(); break;
                case 'T': SidDisplayTest(); break;
                case 'M': TrionicValues(); break;
                case 'P': SpaValues(); break;
            }
            Print("\nCAN channel closed.\n");
            return m_out.ToString();
        }

        private void Print(string s) => m_out.Append(s);

        // wait_for_msg: the next frame with the id (0: any) within the timeout, the others passed over; null when none came.
        // The diagnostic answers passed over (0x200-0x28F, the I-bus's own traffic starts at 0x290) are kept for Missed
        private byte[] Wait(uint id, int timeout)
        {
            long until = Environment.TickCount64 + timeout;
            while (m_next((int)Math.Max(0, until - Environment.TickCount64)) is { } frame)
            {
                if (id == 0 || frame.Id == id)
                {
                    m_seen.Clear();
                    return frame.Data;
                }
                string seen = $"0x{frame.Id:X3} {BitConverter.ToString(frame.Data).Replace('-', ' ')}";
                if (frame.Id is >= 0x200 and < 0x290 and not (0x220 or 0x240 or 0x266) && !m_seen.Contains(seen) && m_seen.Count < 8) m_seen.Add(seen);
            }
            return null;
        }

        // a wait without its frame: the program's debug message, then what answered instead
        private void Missed(string message)
        {
            Print(message + "\n");
            foreach (string seen in m_seen) Print($"  answered instead: {seen}\n");
            m_seen.Clear();
        }

        // S and U: a bit of TWICE's settings (0x21 0x02, byte 25), shown, set or cleared with 0x3B 0x3E, shown again
        private void TwiceBit(string title, string name, byte bit, string param)
        {
            Print($"\nTWICE - {title}\n\n");
            InitConnection(Unit.Twice);
            if (QueryData(Unit.Twice, 0x02) >= 26) Print($"{name} {((m_buf[25] & bit) != 0 ? "ON" : "OFF")}\n");
            if (param == null) return;
            m_sleep(500);
            byte value = param[0] == '1' ? (byte)(m_buf[25] | bit) : (byte)(m_buf[25] & ~bit);
            ChangeSetting(Unit.Twice, 0x3E, value);
            m_sleep(500);
            if (QueryData(Unit.Twice, 0x02) >= 26) Print($"{name} {((m_buf[25] & bit) != 0 ? "ON" : "OFF")}\n");
        }

        // Z: TWICE's settings byte 2 (0xE6 none, 0xF7 glass break, else glass break and tilt), set with 0x3B 0x20
        private void AlarmLevel(string param)
        {
            Print("\nTWICE - Alarm level\n\n");
            InitConnection(Unit.Twice);
            if (QueryData(Unit.Twice, 0x02) >= 2) PrintAlarmLevel();
            if (param == null || !int.TryParse(param, out int level) || level is < 0 or > 2) return;
            m_sleep(500);
            ChangeSetting(Unit.Twice, 0x20, level == 0 ? (byte)0xE6 : level == 1 ? (byte)0xF7 : (byte)0xFF);
            m_sleep(500);
            if (QueryData(Unit.Twice, 0x02) >= 2) PrintAlarmLevel();
        }

        private void PrintAlarmLevel() =>
            Print("Alarm level " + (m_buf[2] == 0xE6 ? "NO ALARM\n" : m_buf[2] == 0xF7 ? "GLASS BREAK SENSOR\n" : "GLASS BREAK AND TILT SENSORS\n"));

        // I: every module's identification (0x1A 0x80), its zero bytes shown as '_'
        private void SystemInformation()
        {
            Print("\nSystem information\n\n");
            foreach (var (unit, label) in new[] { (Unit.Miu, "MIU,     "), (Unit.Acc, "ACC,     "), (Unit.Audio, "AUDIO,   "), (Unit.Sid, "SID,     "),
                         (Unit.Twice, "TWICE,   "), (Unit.Trionic, "TRIONIC, "), (Unit.Spa, "SPA, ") })
            {
                InitConnection(unit);
                int n = Query(unit, new byte[] { 0x40, 0x00, 0x02, 0x1A, 0x80, 0x00, 0x00, 0x00 });
                for (int k = 0; k < n; k++) if (m_buf[k] == 0) m_buf[k] = (byte)'_';
                Print(n != -1 ? $"{label}len={n}, answer='{Encoding.Latin1.GetString(m_buf, 0, n)}'\n" : $"{label}no response\n");
            }
        }

        // T: the SID's test modes, all pixels on for five seconds, off for one, then back
        private void SidDisplayTest()
        {
            Print("\nSID - Display test\n\n");
            InitConnection(Unit.Sid);
            Request(Unit.Sid, TestMode, 0x36, 0x06, 0x02);
            Print("All pixels turned ON\n");
            m_sleep(5000);
            Request(Unit.Sid, TestMode, 0x36, 0x06, 0x01);
            Print("All pixels turned OFF\n");
            m_sleep(1000);
            Request(Unit.Sid, TestMode, 0x36, 0x00);
            Print("Display test ended\n");
        }

        // M: the Trionic's knock and misfire counters, airmass, fuel tank pressure and DTCs
        private void TrionicValues()
        {
            Print("Trionic - Read values\n\n");
            InitConnection(Unit.Trionic);
            if (QueryData(Unit.Trionic, 0xAC) == 8)
                for (int c = 0; c < 4; c++) Print($"Knocking, cylinder {c + 1}        {Word(c * 2),5} times\n");
            int n = QueryData(Unit.Trionic, 0xAD);
            if (n >= 8)
                for (int c = 0; c < 4; c++) Print($"Misfire, cylinder {c + 1}         {Word(c * 2),5} times\n");
            else
                Print($"Received {n} bytes!\n");
            if (QueryData(Unit.Trionic, 0xBD) == 2) Print($"Airmass / Combustion        {Word(0),5} mg/comb\n");
            if (QueryData(Unit.Trionic, 0x59) == 2) Print($"Fuel tank pressure          {(Word(0) / 1000.0).ToString("F3", CultureInfo.InvariantCulture),5} kPa\n");
            Print("\nTrionic - DTC data\n");
            n = QueryDtc(Unit.Trionic);
            if (n == 3 && m_buf[0] == 0x00) Print("No DTC's\n");
            else
                for (int k = 0; k < n; k += 3) Print($"DTC: P{m_buf[k]:X2}{m_buf[k + 1]:X2} Status:{m_buf[k + 2]:X2}\n");
        }

        // P: SPA's values as bytes and its DTCs (0xA9 is B29; the second byte plus two, the third the status)
        private void SpaValues()
        {
            Print("SPA - Read values\n\n");
            InitConnection(Unit.Spa);
            int n = QueryData(Unit.Spa, 0x01);
            if (n != -1)
            {
                for (int k = 0; k < n; k++) Print($"0x{m_buf[k]:X2} ");
                Print("\n");
            }
            Print("\nSPA - DTC data\n");
            n = QueryDtc(Unit.Spa);
            if (n == 3 && m_buf[0] == 0x00) Print("No DTC's\n");
            else
                for (int k = 0; k < n; k += 3) Print($"DTC: B29{m_buf[k + 1] + 2:X2} Status: {m_buf[k + 2]:X2}\n");
        }

        private int Word(int at) => m_buf[at] << 8 | m_buf[at + 1];

        // ---- the transport ----

        // init_connection: 0x220 [3F 81 00 init 02 40], answered on the unit's init reply id; tried three times
        private int InitConnection(Unit unit)
        {
            byte[] init = { 0x3F, 0x81, 0x00, InitTable[(int)unit], 0x02, 0x40, 0x00, 0x00 };
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!m_send(0x220, init))
                {
                    Missed("send 0x220 failed");
                    return -1;
                }
                if (Wait(InitReplyTable[(int)unit], 500) != null) return 0;
            }
            Missed($"timeout waiting for initialization reply 0x{InitReplyTable[(int)unit]:X3} (tried three times)");
            return -1;
        }

        private void ChangeSetting(Unit unit, params byte[] setting) => Request(unit, 0x3B, setting);

        private const byte TestMode = 0x30;

        // test_mode (0x30) and change_setting (0x3B): 0x240 [40 unit len+1 service bytes], then its answer and the one after,
        // each acknowledged; what they returned was never looked at
        private void Request(Unit unit, byte service, params byte[] bytes)
        {
            var msg = new byte[8];
            msg[0] = 0x40;
            msg[1] = UnitTable[(int)unit];
            msg[2] = (byte)(bytes.Length + 1);
            msg[3] = service;
            bytes.CopyTo(msg, 4);
            if (!m_send(0x240, msg))
            {
                Missed("send 0x240 failed");
                return;
            }
            if (Wait(ReplyTable[(int)unit], 1000) is { } first) Ack(unit, first);
            else Missed(service == TestMode ? "timeout waiting for first reply" : "timeout waiting for reply");
            if (Wait(ReplyTable[(int)unit], 300) is { } second) Ack(unit, second);
            // change_setting's second answer may not come, and it said nothing then
            else if (service == TestMode) Missed("timeout waiting for second reply");
            else m_seen.Clear();
        }

        // query_data: 0x21 with the data id (none for 0, one byte shorter)
        private int QueryData(Unit unit, byte id) => Query(unit, new byte[] { 0x40, 0x00, (byte)(id != 0 ? 0x02 : 0x01), 0x21, id, 0x00, 0x00, 0x00 });

        // query_dtc: 0x18 00 FF
        private int QueryDtc(Unit unit) => Query(unit, new byte[] { 0x40, 0x00, 0x04, 0x18, 0x00, 0xFF, 0x00, 0x00 });

        /// <summary>
        /// The answer's payload into m_buf, row by row until the last (0x80, or 0xC0 for one row), each row acknowledged on 0x266;
        /// its length, -1 when a row didn't come within two seconds. The first row (0x40 set) holds the length (two bytes more
        /// than the payload) and three payload bytes, the others six.
        /// </summary>
        private int Query(Unit unit, byte[] query)
        {
            query[1] = UnitTable[(int)unit];
            if (!m_send(0x240, query))
            {
                Missed("send 0x240 failed");
                return -1;
            }
            int n = 0, length = 0;
            byte row;
            do
            {
                if (Wait(ReplyTable[(int)unit], 2000) is not { } data)
                {
                    Missed($"Timeout waiting for 0x{ReplyTable[(int)unit]:X3}.");
                    return -1;
                }
                row = data[0];
                if ((row & 0x40) != 0)
                {
                    // the program counted down before each byte, which dropped the last byte of a one-row answer of three bytes
                    // or fewer: airmass and fuel tank pressure (two) never showed
                    length = data[2] > 2 ? data[2] - 2 : 0;
                    for (int i = 5; i < 8 && length > 0; i++, length--) Add(ref n, data[i]);
                }
                else
                {
                    for (int i = 2; i < 8; i++)
                    {
                        Add(ref n, data[i]);
                        if (--length == 0) break;
                    }
                }
                Ack(unit, data);
            }
            while (row != 0x80 && row != 0xC0);
            m_buf[n] = 0;
            return n;
        }

        // ponytail: an answer past the 255 bytes the program's buffer held is cut there (it wrote past its end)
        private void Add(ref int n, byte b)
        {
            if (n < m_buf.Length - 1) m_buf[n++] = b;
        }

        // 0x266 [40 unit 3F row]: the row number without the first-row bit
        private void Ack(Unit unit, byte[] data) =>
            m_send(0x266, new byte[] { 0x40, UnitTable[(int)unit], 0x3F, (byte)(data[0] & 0xBF), 0x00, 0x00, 0x00, 0x00 });
    }
}
