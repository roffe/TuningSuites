using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T5App.ViewModels;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>Kvaser with no adapter chosen, or a saved channel that isn't there (Settings shows none): a message, the app carries on.</summary>
        [TestMethod]
        public void ConnectWithoutAnAdapterShowsAMessage()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var infos = new List<string>();
                vm.Info += infos.Add;
                string type = vm.Settings.AdapterType, adapter = vm.Settings.Adapter;
                try
                {
                    vm.Settings.AdapterType = "Kvaser HS";
                    vm.Settings.Adapter = "";
                    Assert.IsFalse(await vm.EnsureConnectedAsync());
                    vm.Settings.Adapter = "Kvaser Leaf Light v2 (0)";
                    Assert.IsFalse(await vm.EnsureConnectedAsync());
                    CollectionAssert.AreEqual(new[] { "Check settings, no CAN adapter has been selected!", "Channel Kvaser Leaf Light v2 (0) cannot be selected" }, infos);
                    Assert.IsFalse(vm.IsConnected);
                }
                finally
                {
                    vm.Settings.AdapterType = type;
                    vm.Settings.Adapter = adapter;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
