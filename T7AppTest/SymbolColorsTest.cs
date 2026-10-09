using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void SetSymbolColors()
        {
            string file = Path.Combine(s_dir, "colors.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                Assert.IsTrue(await vm.OpenFileAsync(file, true));
                const string symbol = "ActualIn.p_AirInlet";

                // the SRAM symbols with their colours, the seeded defaults included
                SymbolColorsViewModel colors = vm.SymbolColorChoices();
                Assert.IsTrue(colors.Rows.All(r => vm.Binary!.Symbols.Cast<SymbolHelper>().Any(s => s.SmartVarname == r.Name && s.Start_address > 0)));
                Assert.AreEqual(Color.FromRgb(0xE0, 0xFF, 0xFF), colors.Rows.Single(r => r.Name == "ActualIn.n_Engine").Color, "LightCyan default");
                SymbolColorRow row = colors.Rows.Single(r => r.Name == symbol);
                Assert.AreEqual(Colors.Black, row.Color, "nothing stored");

                // the search box narrows the list
                colors.Search = "p_airinlet";
                Assert.Contains(row, colors.Visible);
                Assert.IsTrue(colors.Visible.All(r => r.Name.Contains("p_AirInlet", StringComparison.OrdinalIgnoreCase)));
                Assert.IsLessThan(colors.Rows.Count, colors.Visible.Count);

                var window = new SymbolColorsWindow { DataContext = colors };
                window.Show();
                Save(window, "symbol-colors");
                colors.Search = "";
                Save(window, "symbol-colors-all");
                window.Close();

                // Ok saves it, SymbolColors and the log viewer use it
                row.Color = Color.FromRgb(0x12, 0x34, 0x56);
                colors.Save();
                System.Drawing.Color stored = new SymbolColors(new T7SuiteRegistry()).GetColorFromRegistry(symbol);
                Assert.AreEqual((0x12, 0x34, 0x56), (stored.R, stored.G, stored.B));
                Assert.AreEqual(row.Color, vm.SymbolColorChoices().Rows.Single(r => r.Name == symbol).Color);

                var start = new DateTime(2024, 1, 1, 12, 0, 0);
                List<T7LogLine> section =
                [
                    new(start, [(symbol, 1.0), ("ActualIn.T_Engine", 80.0)]),
                    new(start.AddSeconds(1), [(symbol, 1.2), ("ActualIn.T_Engine", 81.0)]),
                ];
                var log = new LogViewerViewModel(Path.Combine(s_dir, "x.t7l"), section);
                Assert.AreEqual(Color.FromRgb(0x12, 0x34, 0x56), log.Channels.Single(c => c.Symbol == symbol).Color);
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
