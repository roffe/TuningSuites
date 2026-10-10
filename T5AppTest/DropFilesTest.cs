using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        // files dragged from a file manager over the middle of a control and dropped there: the effect the drag showed
        private static async Task<DragDropEffects> Drop(Window window, Visual over, params string[] files)
        {
            var data = new DataTransfer();
            foreach (string f in files) data.Add(DataTransferItem.CreateFile(await window.StorageProvider.TryGetFileFromPathAsync(f)));
            Point at = over.TranslatePoint(new Point(over.Bounds.Width / 2, over.Bounds.Height / 2), window)!.Value;
            var shown = DragDropEffects.None;
            EventHandler<DragEventArgs> seen = (_, e) => shown = e.DragEffects;
            window.AddHandler(DragDrop.DragEnterEvent, seen, RoutingStrategies.Bubble, true);
            window.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            window.RemoveHandler(DragDrop.DragEnterEvent, seen);
            window.DragDrop(at, RawDragEventType.Drop, data, shown);
            return shown;
        }

        // the drop loads after it returns
        private static async Task Until(Func<bool> done)
        {
            for (int i = 0; i < 500 && !done(); i++) await Task.Delay(10);
            Assert.IsTrue(done());
        }

        [TestMethod]
        public void DropFiles()
        {
            // any letter case, as Open file
            string bin = CopyOfStockBin("DROP.BIN");
            string log = Path.Combine(s_dir, "drop-20261010-CanTraceExt.t5l");
            var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
            File.WriteAllLines(log, Enumerable.Range(0, 50).Select(i => RealtimeLog.Line(t0.AddMilliseconds(i * 100), [("Rpm", 900 + i * 40)], false)));
            string notes = Path.Combine(s_dir, "drop-notes.txt");
            File.WriteAllText(notes, "not a binary");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                window.CaptureRenderedFrame();
                string? info = null;
                vm.Info += t => info = t;

                // a bin dropped on the symbol list opens as Open file opens it (T5's checks), into the recent files too
                var symbols = window.GetVisualDescendants().OfType<SymbolListView>().First();
                Assert.AreEqual(DragDropEffects.Copy, await Drop(window, symbols, bin));
                await Until(() => vm.Binary != null);
                Assert.AreEqual(bin, vm.Binary!.FileName);
                Assert.IsTrue(vm.Recent.Any(r => r.Path == bin));

                // a logfile dropped on an open map opens in the log viewer
                vm.OpenSymbolByName("Ign_map_0!");
                window.CaptureRenderedFrame();
                Assert.AreEqual(DragDropEffects.Copy, await Drop(window, window.GetVisualDescendants().OfType<MapViewerView>().First(), log));
                await Until(() => vm.SelectedViewer is LogViewerViewModel);
                Assert.AreEqual("CANBus logfile: drop-20261010-CanTraceExt.t5l", vm.SelectedViewer!.Title);

                // a file dropped on the hex view opens there, in place of the bin; the main window keeps its bin
                vm.ViewHexCommand.Execute(null);
                window.CaptureRenderedFrame();
                var hex = (HexViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(DragDropEffects.Copy, await Drop(window, window.GetVisualDescendants().OfType<HexViewerView>().Single(), notes));
                await Until(() => !vm.Viewers.Contains(hex));
                Assert.AreEqual("Hexviewer: drop-notes.txt", vm.SelectedViewer!.Title);
                Assert.AreEqual(bin, vm.Binary!.FileName);

                // a file nothing loads shows the drop as refused; dropped anyway, the message names it
                info = null;
                Assert.AreEqual(DragDropEffects.None, await Drop(window, symbols, notes));
                await Until(() => info != null);
                Assert.AreEqual("Not loaded: drop-notes.txt. Drop one binary (*.bin, *.s19), one SRAM snapshot (*.RAM) and logfiles (*.t5l).", info);
                Assert.AreEqual(bin, vm.Binary!.FileName);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
