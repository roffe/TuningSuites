using System;
using Avalonia.Controls;
using Avalonia.Platform;
using SuiteApp.Views;

namespace T5App.Views;

/// <summary>T5Suite's main window: its MainView in the suites' window.</summary>
public class MainWindow : SuiteWindow
{
    public MainWindow() : base(new MainView()) => Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://T5Suite/T5Suite.ico")));
}
