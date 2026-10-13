using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using TrionicCANLib.API;
using TrionicCANLib.CAN;

namespace T7CoreTest
{
    /// <summary>SaabOpenTech's functions on a scripted I-bus: the frames main.c sent, the answers put together, its report.</summary>
    [TestClass]
    public class SaabOpenTechTest
    {
        // the car's I-bus as SaabOpenTech sees it: every frame sent, and the modules' answers to them queued
        private sealed class Bus
        {
            public readonly List<(uint Id, byte[] Data)> Sent = new();
            private readonly Queue<(uint Id, byte[] Data)> m_incoming = new();
            public Func<uint, byte[], IEnumerable<(uint, byte[])>> Answer = (_, _) => [];
            public bool Silent;

            public bool Send(uint id, byte[] data)
            {
                Sent.Add((id, data.ToArray()));
                foreach (var frame in Answer(id, data)) m_incoming.Enqueue(frame);
                return true;
            }

            public (uint Id, byte[] Data)? Next(int timeout) => m_incoming.Count > 0 ? m_incoming.Dequeue() : null;

            public string Run(string arguments)
            {
                // some traffic for the bus check
                if (!Silent) m_incoming.Enqueue((0x290, new byte[8]));
                return new SaabOpenTech(Send, Next, _ => { }).Run(arguments);
            }

            public string[] SentOn(uint id) => Sent.Where(f => f.Id == id).Select(f => Convert.ToHexString(f.Data)).ToArray();
        }

        // a module's answer as rows on its reply id: the first [C0|rows-1, BF, payload+2, service+40, id, 3 bytes], then six bytes a row
        private static IEnumerable<(uint, byte[])> Answer(uint replyId, byte service, byte id, params byte[] payload)
        {
            int rows = payload.Length <= 3 ? 1 : 1 + (payload.Length - 3 + 5) / 6;
            var first = new byte[8] { (byte)(0xC0 | (rows - 1)), 0xBF, (byte)(payload.Length + 2), (byte)(service + 0x40), id, 0, 0, 0 };
            payload.Take(3).ToArray().CopyTo(first, 5);
            yield return (replyId, first);
            for (int r = 1, at = 3; r < rows; r++, at += 6)
            {
                var row = new byte[8] { (byte)(0x80 | (rows - 1 - r)), 0xBF, 0, 0, 0, 0, 0, 0 };
                payload.Skip(at).Take(6).ToArray().CopyTo(row, 2);
                yield return (replyId, row);
            }
        }

        [TestMethod]
        public void SeatbeltPingOnReadsSetsAndReadsAgain()
        {
            var settings = new byte[34];
            var bus = new Bus();
            bus.Answer = (id, d) => id switch
            {
                0x220 when d[3] == 0x45 => [(0x22F, new byte[8])],
                0x240 when d[1] == 0x9B && d[3] == 0x21 && d[4] == 0x02 => Answer(0x24F, 0x21, 0x02, settings),
                0x240 when d[1] == 0x9B && d[3] == 0x3B && d[4] == 0x3E => Set(d[5]),
                _ => [],
            };
            IEnumerable<(uint, byte[])> Set(byte value)
            {
                settings[25] = value;
                return [(0x24F, new byte[] { 0xC0, 0xBF, 0x01, 0x7B, 0, 0, 0, 0 })];
            }

            Assert.AreEqual(
                "SaabOpenTech v0.41 - Freeware Saab maintenance terminal\nby Tomi Liljemark & Mattias Claesson 2018-01-12\n\n" +
                "Opening CAN channel to Saab I-Bus (47,619 kBit/s)...OK\nMessage received from bus, everything seems OK.\n\n" +
                "\nTWICE - seat belt warning beep\n\nSeat belt warning beep OFF\nSeat belt warning beep ON\n\nCAN channel closed.\n",
                bus.Run("S 1"));
            CollectionAssert.AreEqual(new[] { "3F81004502400000" }, bus.SentOn(0x220));
            CollectionAssert.AreEqual(new[] { "409B022102000000", "409B033B3E800000", "409B022102000000" }, bus.SentOn(0x240));
            // each row acknowledged with its number: 34 bytes are seven rows (C6, 85 ... 80), the change's one answer
            CollectionAssert.AreEqual(new[] { "409B3F8600000000", "409B3F8500000000", "409B3F8400000000", "409B3F8300000000",
                "409B3F8200000000", "409B3F8100000000", "409B3F8000000000", "409B3F8000000000" }, bus.SentOn(0x266).Take(8).ToArray());

            settings[25] = 0xA0;
            StringAssert.Contains(bus.Run("S 0"), "Seat belt warning beep ON\nSeat belt warning beep OFF\n");
            Assert.AreEqual(0x20, settings[25], "only the beep's bit cleared");
        }

        [TestMethod]
        public void SystemInformationAsksEveryModule()
        {
            var bus = new Bus();
            bus.Answer = (id, d) => id switch
            {
                0x220 when d[3] == 0x11 => [(0x238, new byte[8])],
                0x240 when d[1] == 0xA1 && d[3] == 0x1A => Answer(0x258, 0x1A, 0x80, [.. "5168646"u8, 0x00, .. "EF"u8]),
                _ => [],
            };
            string report = bus.Run("I");
            static string Silent(string label, int init, int reply) =>
                $"timeout waiting for initialization reply 0x{init:X3} (tried three times)\nTimeout waiting for 0x{reply:X3}.\n{label}no response\n";
            StringAssert.Contains(report, "\nSystem information\n\n" + Silent("MIU,     ", 0x22E, 0x24E) + Silent("ACC,     ", 0x22D, 0x24D) +
                Silent("AUDIO,   ", 0x228, 0x248) + Silent("SID,     ", 0x22B, 0x24B) + Silent("TWICE,   ", 0x22F, 0x24F) +
                "TRIONIC, len=10, answer='5168646_EF'\n" + Silent("SPA, ", 0x234, 0x254));
            // init three times, then the query, for each silent module
            Assert.AreEqual(6 * 3 + 1, bus.SentOn(0x220).Length);
        }

        [TestMethod]
        public void TrionicValuesAndFaultCodes()
        {
            var bus = new Bus();
            bus.Answer = (id, d) => id switch
            {
                0x220 => [(0x238, new byte[8])],
                0x240 when d[3] == 0x21 && d[4] == 0xAC => Answer(0x258, 0x21, 0xAC, 0, 1, 0, 2, 0, 3, 1, 0),
                0x240 when d[3] == 0x21 && d[4] == 0xAD => Answer(0x258, 0x21, 0xAD, 0, 0, 0, 0, 0, 7, 0, 0),
                // two-byte answers in one row: the program lost their second byte
                0x240 when d[3] == 0x21 && d[4] == 0xBD => Answer(0x258, 0x21, 0xBD, 0x02, 0xEE),
                0x240 when d[3] == 0x21 && d[4] == 0x59 => Answer(0x258, 0x21, 0x59, 0x30, 0x39),
                0x240 when d[3] == 0x18 => Answer(0x258, 0x18, 0x02, 0x01, 0x05, 0x60, 0x03, 0x40, 0x20),
                _ => [],
            };
            StringAssert.Contains(bus.Run("M"), "Trionic - Read values\n\n" +
                "Knocking, cylinder 1            1 times\nKnocking, cylinder 2            2 times\nKnocking, cylinder 3            3 times\n" +
                "Knocking, cylinder 4          256 times\n" +
                "Misfire, cylinder 1             0 times\nMisfire, cylinder 2             0 times\nMisfire, cylinder 3             7 times\n" +
                "Misfire, cylinder 4             0 times\n" +
                "Airmass / Combustion          750 mg/comb\nFuel tank pressure          12.345 kPa\n" +
                "\nTrionic - DTC data\nDTC: P0105 Status:60\nDTC: P0340 Status:20\n");
        }

        [TestMethod]
        public void SpaFaultCodesAndAlarmLevel()
        {
            var bus = new Bus();
            byte alarm = 0xE6;
            bus.Answer = (id, d) => id switch
            {
                0x220 when d[3] == 0x66 => [(0x234, new byte[8])],
                0x220 when d[3] == 0x45 => [(0x22F, new byte[8])],
                0x240 when d[1] == 0x9F && d[3] == 0x21 => Answer(0x254, 0x21, 0x01, 0x12, 0x34),
                0x240 when d[1] == 0x9F && d[3] == 0x18 => Answer(0x254, 0x18, 0x01, 0xA9, 0x35, 0x26),
                0x240 when d[1] == 0x9B && d[3] == 0x21 => Answer(0x24F, 0x21, 0x02, 0x00, 0x00, alarm, 0x00),
                0x240 when d[1] == 0x9B && d[3] == 0x3B && d[4] == 0x20 => Set(d[5]),
                _ => [],
            };
            IEnumerable<(uint, byte[])> Set(byte value)
            {
                alarm = value;
                return [(0x24F, new byte[] { 0xC0, 0xBF, 0x01, 0x7B, 0, 0, 0, 0 })];
            }
            StringAssert.Contains(bus.Run("P"), "SPA - Read values\n\n0x12 0x34 \n\nSPA - DTC data\nDTC: B2937 Status: 26\n");
            StringAssert.Contains(bus.Run("Z 2"), "\nTWICE - Alarm level\n\nAlarm level NO ALARM\nAlarm level GLASS BREAK AND TILT SENSORS\n");
            Assert.AreEqual(0xFF, alarm);
            CollectionAssert.Contains(bus.SentOn(0x240), "409B033B20FF0000");
        }

        // an adapter that reads as CANUSBDevice does: one thread hands every frame to the listeners in the same CANMessage, through
        // the device's own id filter; the I-bus's traffic every 10 ms between a TWICE's answers
        private sealed class Adapter : ICANDevice
        {
            private readonly BlockingCollection<(uint Id, ulong Data)> m_rx = new();
            private readonly CANMessage m_frame = new();
            private Thread m_reader;
            private volatile bool m_open;
            public readonly byte[] Settings = new byte[34];

            public override OpenResult open()
            {
                m_open = true;
                m_reader = new Thread(() =>
                {
                    while (m_open)
                    {
                        var (id, data) = m_rx.TryTake(out var frame, 10) ? frame : (0x460u, 0xFFFF00000000003FUL);
                        if (!acceptMessageId(id)) continue;
                        m_frame.setID(id);
                        m_frame.setData(data);
                        receivedMessage(m_frame);
                    }
                }) { IsBackground = true };
                m_reader.Start();
                return OpenResult.OK;
            }

            public override CloseResult close()
            {
                m_open = false;
                m_reader?.Join();
                return CloseResult.OK;
            }

            public override bool isOpen() => m_open;

            protected override bool sendMessageDevice(CANMessage msg)
            {
                byte[] d = BitConverter.GetBytes(msg.getData());
                uint id = msg.getID();
                if (id == 0x220 && d[3] == 0x45) Queue([(0x22F, new byte[8])]);
                else if (id == 0x240 && d[1] == 0x9B && d[3] == 0x21) Queue(Answer(0x24F, 0x21, 0x02, Settings).ToList());
                else if (id == 0x240 && d[1] == 0x9B && d[3] == 0x3B && d[4] == 0x3E)
                {
                    Settings[25] = d[5];
                    Queue([(0x24F, new byte[] { 0xC0, 0xBF, 0x01, 0x7B, 0, 0, 0, 0 })]);
                }
                return true;
            }

            private void Queue(IEnumerable<(uint Id, byte[] Data)> frames)
            {
                foreach (var (id, data) in frames) m_rx.Add((id, BitConverter.ToUInt64(data)));
            }

            public override uint waitForMessage(uint a_canID, uint timeout, out CANMessage canMsg)
            {
                canMsg = new CANMessage();
                return 0;
            }

            public override float GetThermoValue() => 0;
            public override float GetADCValue(uint channel) => 0;
            public override void SetSelectedAdapter(string adapter) { }
            public override int ForcedBaudrate { get; set; }
            public override bool bypassCANfilters { get; set; }
        }

        /// <summary>
        /// On an adapter set up as Trionic7.setCANDevice leaves it (the Trionic's ids only): every frame reaches the function, each
        /// one as it was. A bench's I-bus said "Timeout while listening to bus for any messages" with only the ECU's ids let through.
        /// </summary>
        [TestMethod]
        public void RunsOnAnAdapterSetUpForTheTrionic()
        {
            var adapter = new Adapter { AcceptOnlyMessageIds = [.. Trionic7.FilterIdECU] };
            try
            {
                StringAssert.Contains(SaabOpenTech.Run(adapter, "S 1", _ => { }), "Seat belt warning beep OFF\nSeat belt warning beep ON\n");
                Assert.AreEqual(0x80, adapter.Settings[25]);
            }
            finally
            {
                adapter.close();
            }
        }

        /// <summary>A module that doesn't answer says so, with the diagnostic answers that came instead (the program printed nothing).</summary>
        [TestMethod]
        public void AModuleThatDoesntAnswerSaysWhatCameInstead()
        {
            var bus = new Bus();
            bus.Answer = (id, d) => id switch
            {
                0x220 when d[3] == 0x45 => [(0x22F, new byte[8])],
                // answering on another module's id
                0x240 when d[1] == 0x9B => [(0x290, new byte[8]), (0x24E, new byte[] { 0xC0, 0xBF, 0x04, 0x61, 0x02, 0x00, 0xE6, 0x00 })],
                _ => [],
            };
            StringAssert.Contains(bus.Run("Z"), "\nTWICE - Alarm level\n\nTimeout waiting for 0x24F.\n  answered instead: 0x24E C0 BF 04 61 02 00 E6 00\n\nCAN channel closed.\n");
        }

        [TestMethod]
        public void SilentBusStops()
        {
            var bus = new Bus { Silent = true };
            Assert.AreEqual(
                "SaabOpenTech v0.41 - Freeware Saab maintenance terminal\nby Tomi Liljemark & Mattias Claesson 2018-01-12\n\n" +
                "Opening CAN channel to Saab I-Bus (47,619 kBit/s)...OK\nError: Timeout while listening to bus for any messages.\n\nCAN channel closed.\n",
                bus.Run("I"));
            Assert.AreEqual(0, bus.Sent.Count);
        }
    }
}
