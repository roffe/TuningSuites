using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using NLog;

namespace CommonSuite
{
    /// <summary>
    /// Stands in for the HKCU\Software\MattiasC\&lt;suite&gt; registry key (and its subkeys like Channels, SymbolColors,
    /// LogFilters\n) the suites kept their settings in: same names, every value kept as the string the registry handed back,
    /// so the old parsing code works unchanged. The whole tree is one flat JSON object in &lt;AppData&gt;/MattiasC/&lt;suite&gt;/settings.json
    /// with "Subkey\Name" keys, case-insensitive like the registry. On Windows the registry tree is imported the first time
    /// that file is missing.
    /// </summary>
    public sealed class SettingsKey : IDisposable
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly string m_file;
        private readonly string m_prefix;
        private readonly Dictionary<string, string> m_values;
        private bool m_dirty;

        /// <summary>Where the suites keep their settings, &lt;AppData&gt;/MattiasC; tests point it at a temp folder.</summary>
        public static string BaseFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MattiasC");

        /// <summary>
        /// The program's settings: &lt;AppData&gt;/MattiasC/TxSuite/settings.json. An ECU's own settings are a section of it, "TxSuite/T7":
        /// its values "T7\name" in that file, its files (mymaps.xml, rtsymbols.txt, ...) in the TxSuite/T7 folder.
        /// </summary>
        public const string Program = "TxSuite";

        /// <summary>
        /// The settings folder of a suite name, or of a section ("TxSuite/T7": TxSuite/T7); a folder under its former name
        /// (<see cref="FormerFolders"/>) is moved there first.
        /// </summary>
        public static string Folder(string suite)
        {
            string folder = Path.Combine(BaseFolder, suite.Replace('/', Path.DirectorySeparatorChar));
            if (FormerFolders.TryGetValue(suite, out string former) && !Directory.Exists(folder) && Directory.Exists(Path.Combine(BaseFolder, former)))
            {
                try
                {
                    Directory.Move(Path.Combine(BaseFolder, former), folder);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // the settings then come from the registry again (Windows) or start over
                    logger.Warn(e, "Could not move the settings folder {0} to {1}", former, suite);
                }
            }
            return folder;
        }

        /// <summary>
        /// The registry keys a suite kept its settings under besides HKCU\Software\MattiasC\&lt;suite&gt;, imported in this order, so the
        /// later ones win (T5SuitePro: T5Suite 2.0's MattiasC\T5Suite2 for log filters, channels and symbol colours, then its own
        /// Software\T5Suite2).
        /// </summary>
        public static Dictionary<string, string[]> RegistryPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A settings folder's name before the suite was renamed (T5SuitePro: T5Suite2).</summary>
        public static Dictionary<string, string> FormerFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A suite's settings (or a subkey of them); "TxSuite/T7" opens the T7 section of TxSuite's.</summary>
        public static SettingsKey Open(string suite, string subkey = null)
        {
            var (file, section) = Split(suite);
            string prefix = section == null ? subkey : string.IsNullOrEmpty(subkey) ? section : section + "\\" + subkey;
            return new SettingsKey(Path.Combine(Folder(file), "settings.json"), [@"Software\MattiasC\" + file, .. RegistryPaths.GetValueOrDefault(file) ?? []], prefix);
        }

        // "TxSuite/T7" → ("TxSuite", "T7"); a plain name has no section
        private static (string file, string section) Split(string suite)
        {
            int slash = suite.IndexOf('/');
            return slash < 0 ? (suite, null) : (suite[..slash], suite[(slash + 1)..].Replace('/', '\\'));
        }

        // the Recent list's subkey (numbered entries)
        private const string Mru = "MRUList\\";

        /// <summary>
        /// Once per section: an old suite's settings (T7SuitePro, ...; on Windows their registry keys when the suite never ran here) into
        /// a section of the program's ("TxSuite/T7"). The shared names go to the top of the program's settings unless they are there
        /// already (the first suite imported gives them), its Recent files after the program's, every other value and subkey into the
        /// section, and the suite's files (My Maps, the realtime layout, ...) into the section's folder.
        /// </summary>
        public static void ImportOnce(string oldSuite, string section, ICollection<string> shared)
        {
            var (file, name) = Split(section);
            using var target = Open(file);
            string marker = "Imported\\" + name;
            if (target.GetValue(marker) != null) return;
            using (var old = Open(oldSuite))
            {
                foreach (var (key, value) in old.m_values)
                {
                    if (!key.Contains('\\') && shared.Contains(key))
                    {
                        if (target.GetValue(key) == null) target.SetValue(key, value);
                    }
                    else if (!key.StartsWith(Mru, StringComparison.OrdinalIgnoreCase)) target.m_values[name + "\\" + key] = value;
                }
                // its Recent after the program's, in its order, each file once
                var recent = target.Below().Where(k => k.StartsWith(Mru, StringComparison.OrdinalIgnoreCase)).Select(k => target.m_values[k]).ToList();
                foreach (string path in old.m_values.Where(v => v.Key.StartsWith(Mru, StringComparison.OrdinalIgnoreCase))
                             .OrderBy(v => int.TryParse(v.Key[Mru.Length..], out int i) ? i : int.MaxValue).Select(v => v.Value))
                {
                    if (recent.Contains(path)) continue;
                    target.m_values[Mru + recent.Count] = path;
                    recent.Add(path);
                }
                // read only: a registry import isn't written into the old suite's folder
                old.m_dirty = false;
            }
            target.SetValue(marker, "1");
            string from = Folder(oldSuite), to = Folder(section);
            if (!Directory.Exists(from)) return;
            try
            {
                Directory.CreateDirectory(to);
                foreach (string f in Directory.GetFiles(from).Where(f => !Path.GetFileName(f).StartsWith("settings.json", StringComparison.OrdinalIgnoreCase)))
                    if (!File.Exists(Path.Combine(to, Path.GetFileName(f)))) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.Warn(e, "Could not copy the settings files of {0} to {1}", oldSuite, section);
            }
        }

        internal SettingsKey(string file, string[] registryPaths, string subkey = null)
        {
            m_file = file;
            m_prefix = string.IsNullOrEmpty(subkey) ? "" : subkey.TrimEnd('\\') + "\\";
            m_values = Load(file);
            if (m_values == null)
            {
                m_values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in registryPaths ?? [])
                    foreach (var (name, value) in ImportRegistry(path)) m_values[name] = value;
                m_dirty = m_values.Count > 0;
            }
        }

        public string[] GetValueNames() =>
            Below().Where(rest => !rest.Contains('\\')).ToArray();

        /// <summary>The value names of a subkey of this key ("T7": the T7 section's own names).</summary>
        public string[] GetValueNames(string subkey) =>
            Below().Where(rest => rest.StartsWith(subkey + "\\", StringComparison.OrdinalIgnoreCase)).Select(rest => rest[(subkey.Length + 1)..])
                .Where(rest => !rest.Contains('\\')).ToArray();

        public string[] GetSubKeyNames() =>
            Below().Where(rest => rest.Contains('\\')).Select(rest => rest[..rest.IndexOf('\\')]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public object GetValue(string name) => m_values.TryGetValue(m_prefix + name, out string value) ? value : null;

        // the registry stored bools and doubles via ToString() in the user's culture, keep doing that so old values parse
        public void SetValue(string name, object value)
        {
            m_values[m_prefix + name] = Convert.ToString(value, CultureInfo.CurrentCulture);
            m_dirty = true;
        }

        public void DeleteValue(string name)
        {
            if (m_values.Remove(m_prefix + name)) m_dirty = true;
        }

        public void Dispose()
        {
            if (!m_dirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m_file));
                string tmp = m_file + ".tmp";
                var sorted = new SortedDictionary<string, string>(m_values, StringComparer.OrdinalIgnoreCase);
                File.WriteAllText(tmp, JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, m_file, true);
                m_dirty = false;
            }
            catch (Exception E)
            {
                logger.Debug("error saving settings: " + E.Message);
            }
        }

        private IEnumerable<string> Below() =>
            m_values.Keys.Where(k => k.StartsWith(m_prefix, StringComparison.OrdinalIgnoreCase)).Select(k => k[m_prefix.Length..]);

        private static Dictionary<string, string> Load(string file)
        {
            if (!File.Exists(file)) return null;
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                return new Dictionary<string, string>(values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception E)
            {
                // ponytail: a corrupt file falls back to defaults rather than blocking startup
                logger.Debug("error reading settings: " + E.Message);
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The values (and subkey values as "Sub\\Name") under HKCU\&lt;registryPath&gt;, empty off Windows. Also for keys T7Suite
        /// kept outside MattiasC, like its MRU list.
        /// </summary>
        public static Dictionary<string, string> ImportRegistry(string registryPath)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!OperatingSystem.IsWindows() || registryPath == null) return values;
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath);
                if (key != null)
                {
                    Import(key, "", values);
                    logger.Info($"imported {values.Count} settings from HKCU\\{registryPath}");
                }
            }
            catch (Exception E)
            {
                logger.Debug("error importing registry settings: " + E.Message);
            }
            return values;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void Import(RegistryKey key, string prefix, Dictionary<string, string> values)
        {
            foreach (string name in key.GetValueNames())
            {
                values[prefix + name] = Convert.ToString(key.GetValue(name), CultureInfo.CurrentCulture);
            }
            foreach (string sub in key.GetSubKeyNames())
            {
                using RegistryKey child = key.OpenSubKey(sub);
                if (child != null) Import(child, prefix + sub + "\\", values);
            }
        }
    }
}
