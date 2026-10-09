using System.IO;

namespace CommonSuite
{
    /// <summary>The disassembly listing both suites wrote from their disassembler's output (ctrlDisassembler, AsmViewer).</summary>
    public static class Disassembly
    {
        /// <summary>
        /// &lt;bin&gt;.asm: the functions reached from the vectors, labels and vector names, symbol operands; a blank line before
        /// each function label, instructions as "0xADDRESS\tMNEMONIC".
        /// </summary>
        public static void WriteFunctions(MNemonicCollection mnemonics, string output)
        {
            using var sw = new StreamWriter(output, false);
            foreach (MNemonicHelper helper in mnemonics)
            {
                if (helper.Mnemonic.Contains(':'))
                {
                    if (!helper.Mnemonic.Contains("LBL_")) sw.WriteLine();
                    sw.WriteLine(helper.Mnemonic);
                }
                else sw.WriteLine("0x" + helper.Address.ToString("X8") + "\t" + helper.Mnemonic);
            }
        }
    }
}
