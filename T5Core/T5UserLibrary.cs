using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>
    /// frmUserLibrary's "User library browser": T5 bins found under folders the user added, each with what T5Suite guessed about it.
    /// Kept in UserLib.json in the settings folder (T5Suite: UserLib.xml next to the program).
    /// </summary>
    public static class T5UserLibrary
    {
        public sealed record Row(string File, string Name, string EngineType, string Stage, string Injectors, string Mapsensor, int Torque, bool E85,
            bool T7Valve, string Partnumber, string SoftwareID, string Cpu, bool RamLocked);

        public static string Store => Path.Combine(SettingsKey.Folder(T5AppSettings.Suite), "UserLib.json");

        public static List<Row> Load(string store = null)
        {
            try
            {
                return File.Exists(store ??= Store) ? JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(store)) ?? [] : [];
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                return [];
            }
        }

        public static void Save(IEnumerable<Row> rows, string store = null)
        {
            store ??= Store;
            Directory.CreateDirectory(Path.GetDirectoryName(store)!);
            File.WriteAllText(store, JsonSerializer.Serialize(rows.ToList()));
        }

        /// <summary>One bin: its properties and guesses, the torque from the boost peak and turbo; null for anything that isn't a T5 bin.</summary>
        public static Row Scan(string file)
        {
            try
            {
                if (!T5Binary.IsValidFile(file)) return null;
                T5Binary bin = T5Binary.Open(file);
                Trionic5Properties p = bin.File.GetTrionicProperties();
                T5Reports.FileGuess g = T5Reports.Guess(bin);
                int torque = (int)new PressureToTorque().CalculateTorqueFromPressure(g.MaxBoost, p.TurboType);
                return new Row(file, Path.GetFileName(file), p.Enginetype.Trim(), g.Stage.ToString(), g.Injectors.ToString(), g.Sensor.ToString(), torque, g.E85,
                    !g.T5Valve, p.Partnumber.Trim(), p.SoftwareID.Trim(), p.CPUspeed, p.RAMlocked);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>"Add files": every 128 / 256 KB T5 bin under the folder, replacing the rows of files already listed.</summary>
        public static List<Row> AddFolder(IReadOnlyList<Row> rows, string folder)
        {
            var found = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => new FileInfo(f).Length is 0x20000 or 0x40000).Select(Scan).Where(r => r != null).ToList();
            var names = found.Select(r => r.File).ToHashSet();
            return rows.Where(r => !names.Contains(r.File)).Concat(found).ToList();
        }
    }
}
