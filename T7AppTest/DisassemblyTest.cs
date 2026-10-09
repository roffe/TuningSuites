using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaHex;
using AvaloniaHex.Document;
using AvaloniaHex.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    /// <summary>The listing's address parsing and search behind the disassembly's linked hex view.</summary>
    [TestClass]
    public class DisassemblyAddressTest
    {
        [TestMethod]
        public void ParsesBothListings()
        {
            Assert.IsTrue(DisassemblyViewModel.TryParseAddress("0x0000ABCD\tMOVE.L\tD0,D1", out uint a));
            Assert.AreEqual(0xABCDu, a);
            Assert.IsTrue(DisassemblyViewModel.TryParseAddress("0004F00E: 4E75\t\t\t\tRTS", out a));
            Assert.AreEqual(0x4F00Eu, a);
            Assert.IsFalse(DisassemblyViewModel.TryParseAddress("LBL_0000ABCD:", out _));
            Assert.IsFalse(DisassemblyViewModel.TryParseAddress("Reset_Initial_Interrupt_Stack_Pointer:", out _));
            Assert.IsFalse(DisassemblyViewModel.TryParseAddress("", out _));
            Assert.IsFalse(DisassemblyViewModel.TryParseAddress("0xZZ\tNOP", out _));
        }

        [TestMethod]
        public void InstructionRunsToTheNextAddress()
        {
            Assert.AreEqual((0x100u, 0x106u), DisassemblyViewModel.InstructionRange("0x00000100\tMOVE.L\t#1,D0", "0x00000106\tRTS"));
            Assert.AreEqual((0x100u, 0x104u), DisassemblyViewModel.InstructionRange("0x00000100\tRTS", ""));
            Assert.AreEqual((0x100u, 0x104u), DisassemblyViewModel.InstructionRange("0x00000100\tRTS", null));
            Assert.AreEqual((0x100u, 0x104u), DisassemblyViewModel.InstructionRange("0x00000100\tRTS", "LBL_00000200:"));
            Assert.AreEqual((0x100u, 0x104u), DisassemblyViewModel.InstructionRange("0x00000100\tRTS", "0x00000080\tNOP"));
            Assert.AreEqual((0x100u, 0x102u), DisassemblyViewModel.InstructionRange("00000100: 4E71\t\t\t\tNOP", "00000102: 4E75\t\t\t\tRTS"));
            Assert.IsNull(DisassemblyViewModel.InstructionRange("LBL_00000100:", "0x00000100\tNOP"));
        }

        [TestMethod]
        public void FindsTheAddressAsAWord()
        {
            string asm = "\nLBL_00000100:\n0x000001000\tNOP\n0x00000100\tNOP\n";
            Assert.AreEqual((asm.IndexOf("0x00000100\t"), 10), DisassemblyViewModel.FindAddress(asm, 0x100, false));
            Assert.IsNull(DisassemblyViewModel.FindAddress(asm, 0x200, false));
            Assert.IsNull(DisassemblyViewModel.FindAddress(asm.ToLowerInvariant(), 0xABC, false));

            string full = "00000100: 4E71\t\t\t\tBRA\t00000102:\r\n00000102: 4E75\t\t\t\tRTS\r\n";
            Assert.AreEqual((full.IndexOf("00000102: 4E75"), 9), DisassemblyViewModel.FindAddress(full, 0x102, true));
            Assert.IsNull(DisassemblyViewModel.FindAddress(full, 0x104, true));
        }
    }

    public partial class AppTest
    {
        [TestMethod]
        public void DisassemblyLinksTextAndHex()
        {
            string dir = Path.Combine(s_dir, "asmlink");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "link.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.ShowDisassemblyAsync(false, _ => Task.FromResult(true));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the inner window gets its working size
                var asm = (DisassemblyViewModel)vm.SelectedViewer!;
                Assert.AreEqual(new FileInfo(file).Length, (long)asm.Binary.Length);
                Assert.IsTrue(asm.Binary.IsReadOnly);
                window.CaptureRenderedFrame();
                var view = window.GetVisualDescendants().OfType<DisassemblyView>().Single();
                var editor = view.GetVisualDescendants().OfType<TextEditor>().Single();
                var hex = view.GetVisualDescendants().OfType<HexEditor>().Single();

                // text → hex: an instruction a few hundred lines down, followed by another
                var lines = asm.Document.Lines;
                var line = lines.Skip(300).First(l => l.LineNumber < asm.Document.LineCount &&
                    asm.Document.GetText(l).StartsWith("0x") && asm.Document.GetText(lines[l.LineNumber]).StartsWith("0x"));
                DisassemblyViewModel.TryParseAddress(asm.Document.GetText(line), out uint start);
                DisassemblyViewModel.TryParseAddress(asm.Document.GetText(lines[line.LineNumber]), out uint end);
                editor.CaretOffset = line.Offset + 3;
                Assert.AreEqual(new BitRange(start, end), hex.Selection.Range);
                Assert.AreEqual(start, hex.Caret.Location.ByteIndex);
                window.CaptureRenderedFrame();
                Assert.IsTrue(hex.HexView.VisibleRange.Contains(new BitLocation(start)));

                // a label line leaves the hex view alone
                var label = lines.First(l => asm.Document.GetText(l).StartsWith("LBL_"));
                editor.CaretOffset = label.Offset;
                Assert.AreEqual(new BitRange(start, end), hex.Selection.Range);

                // hex → text: double-click the instruction's first byte, the listing selects its "0x%08X"
                editor.CaretOffset = 0;
                editor.ScrollToHome();
                var cell = hex.Columns.Get<HexColumn>().GetCellBounds(hex.HexView.GetVisualLineByLocation(new BitLocation(start))!, new BitLocation(start));
                var at = hex.HexView.TranslatePoint(cell.Center, window)!.Value;
                for (int i = 0; i < 2; i++)
                {
                    window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                }
                Assert.AreEqual($"0x{start:X8}", editor.SelectedText);
                Assert.AreEqual(line.LineNumber, editor.TextArea.Caret.Line);
                Assert.AreEqual(new BitRange(start, end), hex.Selection.Range, "the caret moved to the line, which selects its bytes again");
                Save(window, "disassembly-linked");

                // the full listing: "SSSSAAAA:" lines both ways
                await vm.ShowDisassemblyAsync(true, _ => Task.FromResult(true));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the inner window gets its working size
                var full = (DisassemblyViewModel)vm.SelectedViewer!;
                Assert.IsTrue(full.Full);
                window.CaptureRenderedFrame();
                view = window.GetVisualDescendants().OfType<DisassemblyView>().Single(v => v.DataContext == full);
                editor = view.GetVisualDescendants().OfType<TextEditor>().Single();
                hex = view.GetVisualDescendants().OfType<HexEditor>().Single();
                var fl = full.Document.GetLineByNumber(1000);
                DisassemblyViewModel.TryParseAddress(full.Document.GetText(fl), out uint fs);
                DisassemblyViewModel.TryParseAddress(full.Document.GetText(full.Document.GetLineByNumber(1001)), out uint fe);
                Assert.IsGreaterThan(fs, fe);
                editor.CaretOffset = fl.Offset;
                Assert.AreEqual(new BitRange(fs, fe), hex.Selection.Range);
                editor.CaretOffset = 0;
                view.ShowAddress(fs);
                Assert.AreEqual($"{fs:X8}:", editor.SelectedText);
                Assert.AreEqual(1000, editor.TextArea.Caret.Line);
                Save(window, "disassembly-full-linked");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
