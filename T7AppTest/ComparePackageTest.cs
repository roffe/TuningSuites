using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>The compare results' "Export as tuning package": the rows' symbols of the chosen file, its bytes in the package.</summary>
        [TestMethod]
        public void CompareExportsTuningPackage()
        {
            string a = Path.Combine(s_dir, "pkg-a.bin"), b = Path.Combine(s_dir, "pkg-b.bin"), pkg = Path.Combine(s_dir, "pkg.t7p");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a, true);
            File.Copy(a, b, true);
            var binB = T7.T7Binary.Open(b, 0, false);
            var sh = binB.Find("IgnNormCal.Map");
            byte[] changed = binB.ReadSymbol(sh);
            changed[1] ^= 0x10;
            binB.WriteSymbol(binB.FileAddress(sh), changed, false);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(a, true));
                await vm.CompareToFileAsync(b);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                Assert.IsTrue(results.CanExportPackage);
                var symbols = results.PackageSymbols(results.Other!);
                CollectionAssert.AreEqual(results.Rows.SourceCollection.Cast<CompareRow>().Select(r => r.SymbolName).ToList(),
                    symbols.Select(s => s.SmartVarname).ToList());
                SymbolFiles.ExportPackage(results.Other!, symbols, pkg);
                var (name, data) = SymbolFiles.ReadPackage(pkg).Single();
                Assert.AreEqual("IgnNormCal.Map", name);
                CollectionAssert.AreEqual(changed, data, "the compared file's bytes");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
