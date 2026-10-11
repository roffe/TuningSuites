using System;
using Avalonia.Controls;
using Avalonia.Platform;
using SuiteApp.Views;

namespace T7App.Views;

/// <summary>T7Suite's main window: its MainView in the suites' window.</summary>
public class MainWindow : SuiteWindow
{
    public MainWindow() : base(new MainView()) => Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://T7Suite/T7Suite.ico")));
}
