using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// Actions → Show disassembly / Show full disassembly / Show interrupt vectors, over the lifted Disassembler
    /// (ctrlDisassembler, AsmViewer and frmVectorlist's output).
    /// </summary>
    public static class Disassembly
    {
        /// <summary>
        /// &lt;bin&gt;.asm: the functions reached from the 256 vectors, labels and vector names, symbol operands; a blank line
        /// before each function label, instructions as "0xADDRESS\tMNEMONIC".
        /// </summary>
        public static string Functions(T7Binary bin, string output, Action<string, int> progress = null)
        {
            var disasm = new Disassembler();
            if (progress != null) disasm.onProgress += (_, e) => progress(e.Info, e.Percentage);
            disasm.DisassembleFile(null, bin.FileName, output, bin.Symbols);
            using var sw = new StreamWriter(output, false);
            foreach (MNemonicHelper helper in disasm.Mnemonics)
            {
                if (helper.Mnemonic.Contains(':'))
                {
                    if (!helper.Mnemonic.Contains("LBL_")) sw.WriteLine();
                    sw.WriteLine(helper.Mnemonic);
                }
                else sw.WriteLine("0x" + helper.Address.ToString("X8") + "\t" + helper.Mnemonic);
            }
            return output;
        }

        /// <summary>&lt;bin&gt;_full.asm: a linear sweep of the whole file, "SSSSAAAA: words\tmnemonic" per line (plain text, not RTF).</summary>
        public static string Full(T7Binary bin, string output)
        {
            new Disassembler().DisassembleFileRtf(bin.FileName, output, new FileInfo(bin.FileName).Length, bin.Symbols, false);
            return output;
        }

        /// <summary>frmVectorlist: the 256 vectors with their names ("User defined vector 0" onwards above 63).</summary>
        public static List<(string Name, long Address)> Vectors(T7Binary bin)
        {
            long[] addresses = Trionic7File.GetVectorAddresses(bin.FileName);
            string[] names = Trionic7File.GetVectorNames();
            return addresses.Select((a, i) => (names[i].Replace('_', ' '), a)).ToList();
        }
    }
}
