using System.IO;

namespace CommonSuite
{
    /// <summary>The disassembly listing both suites wrote from their disassembler's output (ctrlDisassembler, AsmViewer).</summary>
    public static class Disassembly
    {
        // the CPU32's exception vectors (T7Suite's and T5Suite's VectorType)
        private static readonly string[] s_vectors =
        [
            "Reset_initial_stack_pointer", "Reset_initial_program_counter", "Bus_error", "Address_error",
            "Illegal_instruction", "Zero_division", "CHK_CHK2_instructions", "TRAPcc_TRAPV_instructions",
            "Privilege_violation", "Trace", "Line_1010_emulator", "Line_1111_emulator",
            "Hardware_breakpoint", "Coprocessor_protocol_violation", "Format_error_and_uninitialized_interrupt_1", "Format_error_and_uninitialized_interrupt_2",
            "Unassigned_reserved_1", "Unassigned_reserved_2", "Unassigned_reserved_3", "Unassigned_reserved_4",
            "Unassigned_reserved_5", "Unassigned_reserved_6", "Unassigned_reserved_7", "Unassigned_reserved_8",
            "Spurious_interrupt", "Level_1_interrupt_autovector", "Level_2_interrupt_autovector", "Level_3_interrupt_autovector",
            "Level_4_interrupt_autovector", "Level_5_interrupt_autovector", "Level_6_interrupt_autovector", "Level_7_interrupt_autovector",
            "Trap_instruction_vector_0", "Trap_instruction_vector_1", "Trap_instruction_vector_2", "Trap_instruction_vector_3",
            "Trap_instruction_vector_4", "Trap_instruction_vector_5", "Trap_instruction_vector_6", "Trap_instruction_vector_7",
            "Trap_instruction_vector_8", "Trap_instruction_vector_9", "Trap_instruction_vector_10", "Trap_instruction_vector_11",
            "Trap_instruction_vector_12", "Trap_instruction_vector_13", "Trap_instruction_vector_14", "Trap_instruction_vector_15",
            "Reserved_coprocessor_0", "Reserved_coprocessor_1", "Reserved_coprocessor_2", "Reserved_coprocessor_3",
            "Reserved_coprocessor_4", "Reserved_coprocessor_5", "Reserved_coprocessor_6", "Reserved_coprocessor_7",
            "Reserved_coprocessor_8", "Reserved_coprocessor_9", "Reserved_coprocessor_10", "Unassigned_reserved_9",
            "Unassigned_reserved_10", "Unassigned_reserved_11", "Unassigned_reserved_12", "Unassigned_reserved_13",
        ];

        /// <summary>The 256 vector names: the CPU's 64, then "User defined vector N".</summary>
        public static string[] VectorNames()
        {
            var names = new string[256];
            for (int i = 0; i < 256; i++) names[i] = i < 64 ? s_vectors[i] : "User defined vector " + (i - 64);
            return names;
        }

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
