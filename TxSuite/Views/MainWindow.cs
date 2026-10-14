using System;
using Avalonia.Controls;
using Avalonia.Platform;
using SuiteApp.Views;

namespace TxSuite.Views;

/// <summary>TxSuite's main window: its MainView in the program's window, with TxSuite's icon.</summary>
public class MainWindow : SuiteWindow
{
    public MainWindow() : base(new MainView()) => Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://TxSuite/TxSuite.ico")));
}
