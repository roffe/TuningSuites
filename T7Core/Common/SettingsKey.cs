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

        public static string Folder(string suite) => Path.Combine(BaseFolder, suite);

        public static SettingsKey Open(string suite, string subkey = null) =>
            new SettingsKey(Path.Combine(Folder(suite), "settings.json"), @"Software\MattiasC\" + suite, subkey);

        internal SettingsKey(string file, string registryPath, string subkey = null)
        {
            m_file = file;
            m_prefix = string.IsNullOrEmpty(subkey) ? "" : subkey.TrimEnd('\\') + "\\";
            m_values = Load(file);
            if (m_values == null)
            {
                m_values = ImportRegistry(registryPath);
                m_dirty = m_values.Count > 0;
            }
        }

        public string[] GetValueNames() =>
            Below().Where(rest => !rest.Contains('\\')).ToArray();

        public string[] GetSubKeyNames() =>
            Below().Where(rest => rest.Contains('\\')).Select(rest => rest[..rest.IndexOf('\\')]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public object GetValue(string name) => m_values.TryGetValue(m_prefix + name, out string value) ? value : null;

        // the registry stored bools and doubles via ToString() in the user's culture, keep doing that so old values parse
        public void SetValue(string name, object value)
        {
            m_values[m_prefix + name] = Convert.ToString(value, CultureInfo.CurrentCulture);
            m_dirty = true;
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
