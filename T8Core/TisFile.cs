using System;
using System.IO;
using System.IO.Compression;
using CommonSuite;
using TrionicCANLib.Firmware;

namespace T8SuitePro
{
    /// <summary>
    /// File → Create binary from TIS file (Form1.btnCreateFromTISFile_ItemClick): a base bin's recovery bootloader and adaption
    /// data, the TIS file's program from 0x20000, a programming station field and the adaption region flag after it, FF elsewhere.
    /// The checksum isn't touched.
    /// </summary>
    public static class TisFile
    {
        private const int ProgramStart = 0x20000;
        private const string ProgrammingStation = "T8SuitePro";
        private static readonly byte[] Key = "9hwmG9"u8.ToArray();

        /// <summary>The new bin; without a TIS file only the base's first 0x20000 bytes, as T8Suite still offered to save that.</summary>
        public static byte[] Build(string baseBin, string tisFile)
        {
            var bin = new byte[FileT8.Length];
            Array.Fill(bin, (byte)0xFF);
            Array.Copy(File.ReadAllBytes(baseBin), bin, ProgramStart);
            if (tisFile == null) return bin;
            byte[] gbf = Read(tisFile);
            if (ProgramStart + gbf.Length + ProgrammingStation.Length + 5 > bin.Length) throw new InvalidDataException("The TIS file doesn't fit into a Trionic 8 binary");
            int address = ProgramStart;
            for (int i = 0; i < gbf.Length; i++) bin[address++] = (byte)(gbf[i] ^ Key[i % Key.Length]);
            // the footer fields: length, id, data
            bin[address++] = Footer((byte)ProgrammingStation.Length);
            bin[address++] = Footer(0x10);
            foreach (char c in ProgrammingStation) bin[address++] = Footer((byte)c);
            bin[address++] = Footer(0x01);
            bin[address++] = Footer(0xF9);
            bin[address] = Footer(0x01);
            return bin;
        }

        private static byte Footer(byte b) => (byte)((b ^ 0x21) - 0xD6);

        // a .s19 to binary first, then gunzipped unless it isn't gzip; T8Suite left both steps' files next to the TIS file and in the
        // current folder
        private static byte[] Read(string tisFile)
        {
            byte[] data;
            if (tisFile.EndsWith("s19", StringComparison.OrdinalIgnoreCase))
            {
                string dir = Directory.CreateTempSubdirectory("t8tis").FullName;
                try
                {
                    string copy = Path.Combine(dir, Path.GetFileName(tisFile));
                    File.Copy(tisFile, copy);
                    if (!new Srecord().ConvertSrecToBin(copy, FileT8.Length, out string converted, false)) throw new InvalidDataException("The S19 file could not be converted");
                    data = File.ReadAllBytes(converted);
                }
                finally
                {
                    Directory.Delete(dir, true);
                }
            }
            else data = File.ReadAllBytes(tisFile);
            try
            {
                using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
                var plain = new MemoryStream();
                gz.CopyTo(plain);
                return plain.ToArray();
            }
            catch (InvalidDataException)
            {
                return data;
            }
        }
    }
}
