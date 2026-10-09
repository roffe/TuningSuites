using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
namespace CommonSuite
{
    /// <summary>projectproperties.xml: one DataTable "T5PROJECT" row, every column a string.</summary>
    public record ProjectProperties(string CarMake, string CarModel, string CarMY, string CarVIN, string Name, string BinFile, string Version)
    {
        private static readonly string[] Columns = ["CARMAKE", "CARMODEL", "CARMY", "CARVIN", "NAME", "BINFILE", "VERSION"];

        public static ProjectProperties Read(string file)
        {
            using var dt = NewTable();
            dt.ReadXml(file);
            if (dt.Rows.Count == 0) return null;
            DataRow r = dt.Rows[0];
            string S(string c) => r[c]?.ToString() ?? "";
            return new ProjectProperties(S("CARMAKE"), S("CARMODEL"), S("CARMY"), S("CARVIN"), S("NAME"), S("BINFILE"), S("VERSION"));
        }

        public void Write(string file)
        {
            using var dt = NewTable();
            dt.Rows.Add(CarMake, CarModel, CarMY, CarVIN, Name, BinFile, Version);
            dt.WriteXml(file);
        }

        private static DataTable NewTable()
        {
            var dt = new DataTable("T5PROJECT");
            foreach (string c in Columns) dt.Columns.Add(c);
            return dt;
        }
    }

    /// <summary>A row of the "Select a project to open" list.</summary>
    public record ProjectSummary(string Projectname, string NumberBackups, string NumberTransactions, string DateTimeModified, string Version);

    /// <summary>
    /// A suite project (T7Suite's frmMain 1170-1823, T8Suite's Form1 8952-9618 work the same): &lt;ProjectFolder&gt;/&lt;name&gt;/ with
    /// projectproperties.xml, a copy of the binary, TransActionLogV2.ttl, ProjectLogbook.log and Backups/.
    /// </summary>
    public class SuiteProject
    {
        public string ProjectFolder { get; }
        public string Name { get; private set; }
        public string Dir => Path.Combine(ProjectFolder, Name);
        public ProjectProperties Properties { get; private set; }
        public TrionicTransactionLog TransactionLog { get; } = new();
        public TrionicProjectLog Logbook { get; } = new();

        private SuiteProject(string projectFolder, string name)
        {
            ProjectFolder = projectFolder;
            Name = name;
        }

        /// <summary>MakeDirName: drops the characters a folder name can't hold.</summary>
        public static string MakeDirName(string name) =>
            new string(name.Where(c => c is not ('\\' or '/' or ':' or '*' or '?' or '>' or '<' or '|')).ToArray());

        public static string PropertiesFile(string dir) => Path.Combine(dir, "projectproperties.xml");

        /// <summary>The open dialog's list: every folder with a projectproperties.xml.</summary>
        public static List<ProjectSummary> List(string projectFolder)
        {
            Directory.CreateDirectory(projectFolder);
            var list = new List<ProjectSummary>();
            foreach (string dir in Directory.GetDirectories(projectFolder))
            {
                string xml = PropertiesFile(dir);
                if (!File.Exists(xml) || ProjectProperties.Read(xml) is not { } p) continue;
                string backups = Path.Combine(dir, "Backups");
                int nBackups = Directory.Exists(backups) ? Directory.GetFiles(backups, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Length : 0;
                string transactions = "";
                if (File.Exists(Path.Combine(dir, "TransActionLogV2.ttl")))
                {
                    var log = new TrionicTransactionLog();
                    if (log.OpenTransActionLog(projectFolder, Path.GetFileName(dir)))
                    {
                        log.ReadTransactionFile();
                        transactions = log.TransCollection.Count.ToString(CultureInfo.InvariantCulture);
                    }
                }
                DateTime modified = File.Exists(p.BinFile) ? File.GetLastAccessTime(p.BinFile) : DateTime.MinValue;
                list.Add(new ProjectSummary(p.Name, nBackups.ToString(CultureInfo.InvariantCulture), transactions, modified.ToString(CultureInfo.CurrentCulture), p.Version));
            }
            return list;
        }

        /// <summary>Create project: a folder named after the project, the binary copied in, the properties written. Returns the folder name.</summary>
        public static string Create(string projectFolder, ProjectProperties properties, string sourceBinary)
        {
            Directory.CreateDirectory(projectFolder);
            string name = MakeDirName(properties.Name);
            string dir = Path.Combine(projectFolder, name);
            if (Directory.Exists(dir)) throw new InvalidOperationException("The chosen projectname already exists, please choose another one");
            Directory.CreateDirectory(dir);
            string bin = Path.Combine(dir, Path.GetFileName(sourceBinary));
            File.Copy(sourceBinary, bin);
            (properties with { Name = name, BinFile = bin }).Write(PropertiesFile(dir));
            return name;
        }

        /// <summary>OpenProject without the UI: properties, logbook, transaction log. Null when the project doesn't exist.</summary>
        public static SuiteProject Open(string projectFolder, string name)
        {
            string dir = Path.Combine(projectFolder, name);
            if (!Directory.Exists(dir) || !File.Exists(PropertiesFile(dir)) || ProjectProperties.Read(PropertiesFile(dir)) is not { } p) return null;
            var project = new SuiteProject(projectFolder, name) { Properties = p };
            project.Logbook.OpenProjectLog(dir);
            if (project.TransactionLog.OpenTransActionLog(projectFolder, name)) project.TransactionLog.ReadTransactionFile();
            return project;
        }

        public string BinaryFile => Properties.BinFile;

        /// <summary>CreateProjectBackupFile: Backups/&lt;bin&gt;-backup-MMddyyyyHHmmss.BIN and a logbook entry.</summary>
        public string CreateBackup()
        {
            string backups = Path.Combine(Dir, "Backups");
            Directory.CreateDirectory(backups);
            string baseName = Path.Combine(backups, Path.GetFileNameWithoutExtension(BinaryFile) + "-backup-" + DateTime.Now.ToString("MMddyyyyHHmmss", CultureInfo.InvariantCulture));
            // ponytail: two backups in the same second made T7Suite throw; number them instead
            string file = baseName + ".BIN";
            for (int i = 2; File.Exists(file); i++) file = $"{baseName}-{i}.BIN";
            File.Copy(BinaryFile, file);
            Logbook.WriteLogbookEntry(LogbookEntryType.BackupfileCreated, file);
            return file;
        }

        /// <summary>SignalTransactionLogChanged's logbook entry: "&lt;symbol at the address, or the address&gt; &lt;note&gt;".</summary>
        public void LogTransaction(SuiteBinary bin, TransactionEntry entry) =>
            Logbook.WriteLogbookEntry(LogbookEntryType.TransactionExecuted, SymbolNameByAddress(bin, entry.SymbolAddress) + " " + entry.Note);

        public static string SymbolNameByAddress(SuiteBinary bin, long address) =>
            bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(sh => sh.Flash_start_address == address)?.Varname ?? address.ToString(CultureInfo.InvariantCulture);

        /// <summary>The ribbon's Roll back: the last entry not rolled back.</summary>
        public TransactionEntry UndoTarget => TransactionLog.TransCollection.Cast<TransactionEntry>().LastOrDefault(e => !e.IsRolledBack);

        /// <summary>The ribbon's Roll forward: the first entry that is rolled back.</summary>
        public TransactionEntry RedoTarget => TransactionLog.TransCollection.Cast<TransactionEntry>().FirstOrDefault(e => e.IsRolledBack);

        /// <summary>RollBack / RollForward: the entry's before / after bytes back into the file (no new entry), the flag, a logbook line.</summary>
        public void Roll(SuiteBinary bin, TransactionEntry entry, bool back)
        {
            int address = entry.SymbolAddress;
            while (address > FileLength(bin)) address -= FileLength(bin);
            bin.WriteData(address, back ? entry.DataBefore : entry.DataAfter);
            bin.UpdateChecksum();
            if (back) TransactionLog.SetEntryRolledBack(entry.TransactionNumber);
            else TransactionLog.SetEntryRolledForward(entry.TransactionNumber);
            Logbook.WriteLogbookEntry(back ? LogbookEntryType.TransactionRolledback : LogbookEntryType.TransactionRolledforward,
                SymbolNameByAddress(bin, entry.SymbolAddress) + " " + entry.Note + " " + entry.TransactionNumber);
        }

        private static int FileLength(SuiteBinary bin) => (int)new FileInfo(bin.FileName).Length;

        /// <summary>Edit project: new properties; a new name moves the folder and the binary path with it.</summary>
        public void Edit(ProjectProperties edited)
        {
            string newName = MakeDirName(edited.Name);
            string bin = edited.BinFile;
            if (newName != Name)
            {
                Directory.Move(Dir, Path.Combine(ProjectFolder, newName));
                bin = Path.Combine(ProjectFolder, newName, Path.GetFileName(BinaryFile));
                Name = newName;
                Logbook.OpenProjectLog(Dir);
            }
            Properties = edited with { Name = newName, BinFile = bin };
            Properties.Write(PropertiesFile(Dir));
            Logbook.WriteLogbookEntry(LogbookEntryType.PropertiesEdited, Properties.Version);
        }

        /// <summary>
        /// Rebuild file: the newest backup up to the date (else the current file) with every transaction between that file's time
        /// and the date applied. Returns the rebuilt temp file; the caller replaces the project file or saves it elsewhere.
        /// The checksum is updated (bin is the project's open binary, which knows how), which T7Suite didn't do.
        /// </summary>
        public string Rebuild(DateTime upTo, SuiteBinary bin)
        {
            string backups = Path.Combine(Dir, "Backups");
            string source = Directory.Exists(backups)
                ? Directory.GetFiles(backups, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                    .Select(f => new FileInfo(f)).Where(f => f.LastAccessTime <= upTo).OrderByDescending(f => f.LastAccessTime).FirstOrDefault()?.FullName
                : null;
            source ??= BinaryFile;
            string tmp = Path.Combine(ProjectFolder, Name + "rebuild.bin");
            File.Delete(tmp);
            CreateBackup();
            File.Copy(source, tmp);
            DateTime from = File.GetLastAccessTime(source);
            SuiteBinary rebuilt = bin.RawFile(tmp);
            foreach (TransactionEntry e in TransactionLog.TransCollection)
            {
                if (e.EntryDateTime < from || e.EntryDateTime > upTo) continue;
                int address = e.SymbolAddress;
                while (address > FileLength(rebuilt)) address -= FileLength(rebuilt);
                rebuilt.WriteData(address, e.DataAfter);
            }
            rebuilt.UpdateChecksum();
            Logbook.WriteLogbookEntry(LogbookEntryType.ProjectFileRecreated, "Reconstruct upto " + upTo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " selected file " + source);
            return tmp;
        }

        /// <summary>
        /// Create backup file: the project's backup when the bin is the project's, else &lt;bin&gt;&lt;yyyyMMddHHmmss&gt;.binarybackup
        /// next to it.
        /// </summary>
        public static string Backup(SuiteBinary bin, SuiteProject project)
        {
            if (project != null && string.Equals(Path.GetFullPath(project.BinaryFile), Path.GetFullPath(bin.FileName), StringComparison.Ordinal))
                return project.CreateBackup();
            string file = Path.Combine(Path.GetDirectoryName(bin.FileName) ?? "", Path.GetFileNameWithoutExtension(bin.FileName) + DateTime.Now.ToString("yyyyMMddHHmmss") + ".binarybackup");
            File.Copy(bin.FileName, file, true);
            return file;
        }

        public record LogbookLine(DateTime Timestamp, string Type, string Description);

        /// <summary>frmProjectLogbook: ddMMyyyyHHmmss|type|info per line, bad lines skipped.</summary>
        public List<LogbookLine> ReadLogbook()
        {
            var lines = new List<LogbookLine>();
            string file = Path.Combine(Dir, "ProjectLogbook.log");
            if (!File.Exists(file)) return lines;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] v = line.Split('|');
                if (v.Length < 3 || !DateTime.TryParseExact(v[0], "ddMMyyyyHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)) continue;
                lines.Add(new LogbookLine(t, v[1], v[2]));
            }
            return lines;
        }
    }
}
