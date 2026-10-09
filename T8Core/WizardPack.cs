using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CommonSuite;
using NLog;
using TrionicCANLib.Checksum;

namespace T8SuitePro
{
    /// <summary>
    /// A pack of the Tuning Wizard (Form1.addWizTuneFilePacks, FileTuningAction): a .t8x file with a signature and AES-encrypted
    /// lines, its header (packname, bintype OLD / NEW / BOTH, whitelist, blacklist, code, author, msg) and a tuning package.
    /// </summary>
    public sealed class WizardPack
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string FileName { get; private init; } = "";
        public string Name { get; private init; } = "";
        public string BinType { get; private init; } = "";
        public string[] Whitelist { get; private init; } = [];
        public string[] Blacklist { get; private init; } = [];
        public string Code { get; private init; } = "";
        public string Author { get; private init; } = "";
        public string Message { get; private init; } = "";

        /// <summary>The decrypted, trimmed lines.</summary>
        public List<string> Lines { get; private init; } = [];

        public override string ToString() => Code != "" ? Name + " [Protected]" : Name;

        /// <summary>Every *.t8x in the folder whose signature verifies against T8Pub.pem; the others are logged and left out.</summary>
        public static List<WizardPack> Load(string folder) =>
            Directory.Exists(folder) ? Directory.GetFiles(folder, "*.t8x").Order().Select(Read).Where(p => p != null).ToList() : [];

        /// <summary>The pack, null when its signature doesn't verify or it can't be read (T8Suite stopped starting up on the latter).</summary>
        public static WizardPack Read(string file)
        {
            try
            {
                using var sr = new StreamReader(file);
                string signature = "", s = sr.ReadLine();
                if (s != null && s.StartsWith("<SIGNATURE>"))
                    while ((s = sr.ReadLine()) != null && !s.StartsWith("</SIGNATURE>")) signature += s;
                var lines = new List<string>();
                while ((s = sr.ReadLine()) != null) lines.Add(Crypto.DecodeAES(s).Trim());
                // the MD5 of the decrypted lines, each with CRLF
                if (!Crypto.VerifyRSASignature(Crypto.CalculateMD5Hash(string.Concat(lines.Select(l => l + "\r\n"))), signature))
                {
                    logger.Debug("Signature check failed for file: " + file);
                    return null;
                }
                // the last line of a kind counts; lists lose their whitespace
                string Value(string key) => lines.LastOrDefault(l => l.StartsWith(key + "="))?.Replace(key + "=", "") ?? "";
                string[] List(string key) => lines.Any(l => l.StartsWith(key + "=")) ? Regex.Replace(Value(key), @"\s+", "").Split(',') : [];
                return new WizardPack
                {
                    FileName = file, Name = Value("packname"), BinType = Value("bintype"), Whitelist = List("whitelist"), Blacklist = List("blacklist"),
                    Code = Regex.Replace(Value("code"), @"\s+", ""), Author = Value("author"), Message = Value("msg"), Lines = lines,
                };
            }
            catch (Exception e) when (e is IOException or FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                logger.Debug("Failed to read tuning pack " + file + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// compatibelSoftware: old software (FA… FB…, FC01_O…) takes OLD or BOTH packs, newer NEW or BOTH; the version has to start
        /// with a whitelist entry when there is a whitelist (an empty "whitelist=" admits nothing) and with no blacklist entry; '*'
        /// ends an entry.
        /// </summary>
        public bool Compatible(string software)
        {
            if (software.Length < 2) return false;
            bool old = software[1] < 'C' || software.StartsWith("FC01_O");
            if (!(old ? BinType is "OLD" or "BOTH" : BinType is "NEW" or "BOTH")) return false;
            bool Matches(string entry) => entry != "" && software.StartsWith(entry.IndexOf('*') is var a and >= 0 ? entry[..a] : entry);
            return (Whitelist.Length == 0 || Whitelist.Any(Matches)) && !Blacklist.Any(Matches);
        }

        /// <summary>
        /// performTuningAction, then DateAndName: a copy "&lt;bin&gt;-yyyyMMddHHmmss-BACKUP-BEFORE-WIZARD-&lt;pack&gt;.bin" next to the bin,
        /// the package applied as Import tuning package does (transaction entries in a project, one checksum update, the WIZARD
        /// log), then the PI area's programmer name and release date. The lines of the wizard's result list.
        /// </summary>
        public List<string> Apply(T8Binary bin, TrionicTransactionLog log)
        {
            string backup = $"{Path.GetFileNameWithoutExtension(bin.FileName)}-{DateTime.Now:yyyyMMddHHmmss}-BACKUP-BEFORE-WIZARD-{Name}.bin";
            char[] invalid = [.. Path.GetInvalidFileNameChars(), .. Path.GetInvalidPathChars()];
            File.Copy(bin.FileName, Path.Combine(Path.GetDirectoryName(bin.FileName) ?? "", string.Concat(backup.Where(c => !invalid.Contains(c)))), true);
            TuningPackage package = TuningPackage.Parse(Lines, bin);
            List<PackageResult> results = package.Apply(bin, log);
            // symbols come first: "OK: " / "Fail: " and the name; patterns say their count themselves
            var lines = results.Select((r, i) => i < package.Symbols.Count ? (r.Success ? "OK: " : "Fail: ") + r.Map : r.Map).ToList();
            UpdatePiArea(bin);
            lines.Add("Update PI Area");
            return lines;
        }

        /// <summary>
        /// DateAndName ("Update PI Area"): in the PI area, the programmer name (container 0x1D) becomes "T8Suite" space padded when it
        /// has room, and the release date (0x0A) now when it has 19 characters. Encoded, no transaction entry; the PI area lies outside
        /// both checksum layers.
        /// </summary>
        public static void UpdatePiArea(T8Binary bin)
        {
            int start = ChecksumT8.GetChecksumAreaOffset(bin.FileName);
            int end = T8Header.GetEmptySpaceStartFrom(bin.FileName, start);
            byte[] pi = bin.Read(start, end - start + 1);
            static byte Decode(byte b) => (byte)((byte)(b + 0xD6) ^ 0x21);
            static byte[] Encode(byte[] plain) => plain.Select(b => (byte)((byte)(b ^ 0x21) - 0xD6)).ToArray();
            int namePos = 0, nameLen = 0, datePos = 0, dateLen = 0;
            for (int t = 0; t < pi.Length - 1;)
            {
                int len = Decode(pi[t]), id = Decode(pi[t + 1]);
                if (id == 0x1D)
                {
                    (namePos, nameLen) = (t + 2, len);
                    if (datePos != 0) break;
                }
                if (id == 0x0A)
                {
                    (datePos, dateLen) = (t + 2, len);
                    if (namePos != 0) break;
                }
                t += len + 2;
            }
            const string programmer = "T8Suite";
            if (namePos != 0 && nameLen >= programmer.Length) bin.WriteData(start + namePos, Encode(Encoding.ASCII.GetBytes(programmer.PadRight(nameLen))));
            byte[] now = Encoding.ASCII.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (datePos != 0 && dateLen == now.Length) bin.WriteData(start + datePos, Encode(now));
        }
    }
}
