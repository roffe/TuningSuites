using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8App.ViewModels;
using T8App.Views;
using T8SuitePro;

namespace T8AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void PidEditor()
        {
            string file = CopyOfStockBin("pid.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                // no stock bin has a TEM table
                Assert.IsFalse(vm.HasTemTable);

                PidEditorViewModel editor = vm.PidEditor(false)!;
                Assert.AreEqual($"PID editor ({editor.Rows.Count} items)", editor.Title);
                PidRow row = editor.Rows[0];
                Assert.AreEqual("0100", row.Pid);
                Assert.AreEqual(1, row.Read);
                Assert.AreEqual(0, row.Write);
                Assert.IsFalse(row.PidFault || row.SymbolFault || row.WriteFault);
                Assert.ThrowsExactly<DataValidationException>(() => row.Pid = "12345");
                Assert.ThrowsExactly<DataValidationException>(() => row.Pid = "xyz");
                row.Pid = "1ab";
                Assert.AreEqual("01AB", row.Pid);

                // writing a symbol in the flash is marked, as is the address
                row.Write = 1;
                Assert.AreEqual(int.Parse(row.AddressText, System.Globalization.NumberStyles.HexNumber) < 0x100000, row.WriteFault && row.AddressFault);
                row.Write = 0;
                // another symbol from the lookup: its index, description and size follow
                string other = editor.SymbolNames.First(n => n != row.Symbol && vm.Binary!.Find(n) is { Length: 2 });
                row.Symbol = other;
                Assert.AreEqual(vm.Binary!.Find(other).Symbol_number_ECU, row.Entry.SymbolIndex);
                Assert.AreEqual("2", row.SizeText);
                // a name that isn't in the lookup is ignored
                row.Symbol = "No such symbol";
                Assert.AreEqual(other, row.Symbol);

                // "Unknown" is marked
                editor.Rows[2].Read = 3;
                Assert.IsTrue(editor.Rows[2].ReadFault);
                var dialog = new PidEditorWindow { DataContext = editor };
                dialog.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var grid = dialog.FindControl<DataGrid>("Grid")!;
                CollectionAssert.AreEqual(new[] { " ", "PID", "Read", "Write", "Symbol", "Description", "Address", "Size" },
                    grid.Columns.Where(c => c.IsVisible).Select(c => (string)c.Header!).ToArray());
                editor.Filter(other);
                Assert.AreEqual(1, editor.View.Count);
                editor.Filter("");
                Save(dialog, "pideditor");
                dialog.Close();
                editor.Rows[2].Read = 1;

                await vm.ApplyPidEditorAsync(editor);
                T8Binary reopened = T8Binary.Open(file, false);
                Assert.AreEqual("01AB", reopened.Pids[0].PID);
                Assert.AreEqual(row.Entry.SymbolIndex, reopened.Pids[0].SymbolIndex);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, reopened.VerifyChecksum());

                // the TEM editor's columns, on an empty table
                var tem = new PidEditorWindow { DataContext = vm.PidEditor(true) };
                CollectionAssert.AreEqual(new[] { " ", "Label", "Symbol", "Description", "Type", "Address", "Size" },
                    tem.FindControl<DataGrid>("Grid")!.Columns.Where(c => c.IsVisible).Select(c => (string)c.Header!).ToArray());
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void BitmaskViewer()
        {
            string file = CopyOfStockBin("bits.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var bin = (T8Binary)vm.Binary!;
                var masked = bin.Symbols.Cast<CommonSuite.SymbolHelper>().Where(s => s.BitMask > 0).ToList();
                CommonSuite.SymbolHelper sh = masked.First(s => s.Flash_start_address < 0x100000);
                int address = (int)sh.Flash_start_address;
                byte[] before = bin.Read(address, 2);

                // the window's dialog: the symbol's bit is named and set as in the file; flip it
                BitmaskViewModel? shown = null;
                vm.ShowBitmask = bits =>
                {
                    shown = bits;
                    MaskBit bit = bits.Low.Concat(bits.High).Single(b => b.Mask == sh.BitMask);
                    Assert.AreEqual(sh.SmartVarname, bit.Name);
                    Assert.AreEqual(((before[0] << 8 | before[1]) & sh.BitMask) != 0, bit.IsChecked);
                    Assert.IsTrue(bits.Low.Concat(bits.High).Where(b => b.Name == "").All(b => !b.IsEnabled && !b.IsChecked));
                    bit.IsChecked = !bit.IsChecked;
                    var dialog = new BitmaskWindow { DataContext = bits };
                    dialog.Show();
                    Save(dialog, "bitmask");
                    dialog.Close();
                    return System.Threading.Tasks.Task.FromResult(true);
                };
                vm.OpenSymbolCommand.Execute(sh);
                await System.Threading.Tasks.Task.Delay(100);
                Assert.IsNotNull(shown);
                Assert.AreEqual(shown!.Value, bin.Read(address, 2) is var after ? after[0] << 8 | after[1] : -1);
                Assert.AreNotEqual((before[0] << 8 | before[1]) & sh.BitMask, shown.Value & sh.BitMask);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, bin.VerifyChecksum());

                // one in SRAM needs the ECU
                string? info = null;
                vm.Info += text => info = text;
                await vm.OpenBitMaskAsync(bin, masked.First(s => s.Flash_start_address >= 0x100000));
                Assert.AreEqual("Symbol outside of flash boundary and no connection to ECU available", info);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void MapPreviewPopup()
        {
            string file = CopyOfStockBin("preview.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                CommonSuite.SymbolHelper map = vm.Binary!.Find("IgnAbsCal.fi_NormalMAP");
                // off by default, as in T8Suite
                Assert.IsNull(vm.MapPreview(map));
                try
                {
                    vm.Settings.ShowMapPreviewPopup = true;
                    Assert.AreEqual(18, vm.MapPreview(map)!.Map.Cols);
                    // none for a symbol that only lives in SRAM
                    Assert.IsNull(vm.MapPreview(vm.Binary.Symbols.Cast<CommonSuite.SymbolHelper>().First(s => s.Flash_start_address >= 0x100000 && s.Length > 1)));

                    // hovering a name: the tooltip gets the map's table
                    vm.SymbolFilter = vm.SymbolFilters[0];
                    vm.SearchText = "IgnAbsCal.fi_NormalMAP";
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame();
                    var name = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == map && ToolTip.GetTip(b) != null);
                    var opening = new Avalonia.Interactivity.CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
                    name.RaiseEvent(opening);
                    Assert.IsFalse(opening.Cancel);
                    var grid = (MapControls.MapGrid)ToolTip.GetTip(name)!;
                    Assert.IsTrue(grid.IsReadOnly);
                    var popup = new Window { Content = grid, SizeToContent = SizeToContent.WidthAndHeight };
                    popup.Show();
                    Save(popup, "mappreview");
                    popup.Close();
                }
                finally
                {
                    vm.Settings.ShowMapPreviewPopup = false;
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void TuningWizard()
        {
            string file = CopyOfStockBin("wizard.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                // the wizard first in the Tuning menu, the map buttons after it
                var tuning = window.FindControl<MenuItem>("QuickMapsMenu")!;
                Assert.AreEqual("Tuning wizards", ((MenuItem)tuning.Items[0]!).Header);
                Assert.AreEqual("Airmass controller", ((MenuItem)tuning.Items[1]!).Header);

                // an old calibration: the OLD and BOTH packs
                TuningWizardViewModel wizard = vm.TuningWizard()!;
                Assert.AreEqual("FA56", wizard.SoftwareVersion);
                Assert.HasCount(11, wizard.Packs);
                Assert.IsTrue(wizard.Packs.All(p => p.BinType is "OLD" or "BOTH"));
                var dialog = new TuningWizardWindow { DataContext = wizard };
                dialog.Show();
                Assert.IsFalse(wizard.CanBack);
                await wizard.NextAsync();
                Assert.AreEqual("Select Tuning Action", wizard.Title);
                wizard.Selected = wizard.Packs.Single(p => p.Name == "Bosch 550cc Injectors");
                Assert.AreEqual("Mackan", wizard.Author);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(dialog, "wizard-select");
                // no code: straight to the confirmation, which needs the tick
                await wizard.NextAsync();
                Assert.AreEqual("Confirm Tuning Action", wizard.Title);
                Assert.IsFalse(wizard.CanNext);
                wizard.Understood = true;
                wizard.Back();
                Assert.IsFalse(wizard.Understood);
                Assert.AreEqual("Select Tuning Action", wizard.Title);
                await wizard.NextAsync();
                wizard.Understood = true;
                Assert.IsTrue(await wizard.NextAsync());
                Assert.AreEqual("Completed Tuning Wizard", wizard.Title);
                Assert.IsFalse(wizard.CanBack || wizard.CanCancel);
                CollectionAssert.Contains(wizard.Results, "OK: InjCorrCal.InjectorConst");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(dialog, "wizard-completed");
                dialog.Close();
                Assert.AreEqual(1, System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(file)!, "wizard-*-BACKUP-BEFORE-WIZARD-Bosch 550cc Injectors.bin").Length);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, vm.Binary!.VerifyChecksum());
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        [TestMethod]
        public void InformationToolsAndAirmass()
        {
            string file = CopyOfStockBin("info.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1600, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // the MC68377's 120 vectors
                var vectors = new SuiteApp.Views.VectorsWindow(vm.Binary!.InterruptVectors());
                vectors.Show();
                Save(vectors, "vectors");
                vectors.Close();

                // the disassembly with its highlighting (now from SuiteApp's assets) beside the bin's bytes
                await vm.ShowDisassemblyAsync(false, _ => System.Threading.Tasks.Task.FromResult(false));
                var asm = (SuiteApp.ViewModels.DisassemblyViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Disassembly: info.asm", asm.Title);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "disassembly");

                vm.ViewHexCommand.Execute(null);
                Assert.AreEqual("Hexviewer: info.BIN", vm.SelectedViewer!.Title);
                vm.BrowseAxes(null);
                var axes = (SuiteApp.ViewModels.AxisBrowserViewModel)vm.SelectedViewer!;
                Assert.IsTrue(axes.Rows.Any(r => r.Symbol == "IgnAbsCal.fi_NormalMAP" && r.XAxis == "IgnAbsCal.m_AirNormXSP"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "axisbrowser");

                // the airmass result viewer with T8Suite's options
                vm.ShowAirmassResultCommand.Execute(null);
                var am = (SuiteApp.ViewModels.AirmassResultViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Airmass result viewer: info.BIN", am.Title);
                Assert.AreEqual("Car is high output (175/210 hp)", am.VariantCaption);
                Assert.IsTrue(am.Variant);
                Assert.AreEqual("Fifth gear", am.Gears[am.Gear]);
                Assert.HasCount(6, am.Legend);
                int wot = am.Result!.Pedal.Length - 1, c3500 = System.Array.IndexOf(am.Result.Rpm, 3500);
                Assert.AreEqual("808", am.Texts![wot, c3500]);
                am.DisplayMode = 1;
                Assert.AreEqual("261", am.Texts![wot, c3500]);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "airmass-table");
                var tabs = window.GetVisualDescendants().OfType<SuiteApp.Views.AirmassResultView>().Single()
                    .GetVisualDescendants().OfType<TabControl>().First();
                tabs.SelectedIndex = 1;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "airmass-dyno");
                tabs.SelectedIndex = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "airmass-compressor");
                tabs.SelectedIndex = 0;
                // a legend entry opens the table in use
                am.OpenLimiterCommand.Execute(CommonSuite.AirmassLimitType.TorqueLimiterEngine);
                Assert.AreEqual("TrqLimCal.Trq_MaxEngineManTab1", ((SuiteApp.ViewModels.MapViewerViewModel)vm.SelectedViewer!).MapName);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
