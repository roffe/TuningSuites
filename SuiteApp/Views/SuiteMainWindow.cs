using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CommonSuite;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>
/// The main window the suites share (frmMain / Form1): the workspace with the symbol list and the documents, and the menu actions
/// every suite has. Each app's MainWindow.axaml derives from it with its own menus (its ribbon's order and captions), a StatusBar,
/// a SuiteWorkspace, and the symbol list's row menu as the window resource "SymbolListMenu"; menu items can name the handlers here.
/// </summary>
public partial class SuiteMainWindow : Window
{
    private const string SymbolListKey = "SymbolListProportion";
    private const string SkinKey = "Skin";

    private SuiteWorkspace? m_workspace;

    private SuiteWorkspace Space => m_workspace ??= this.GetLogicalDescendants().OfType<SuiteWorkspace>().First();

    protected MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    /// <summary>Open file's filter name ("Trionic 7 binary or Motorola S19").</summary>
    protected virtual string BinaryFilesName => "Binary or Motorola S19";

    /// <summary>About...: the thanks, support and closing lines of the suite's frmAbout (T7Suite's by default).</summary>
    protected virtual (string thanks, string support, string closing) AboutTexts =>
        ("Dilemma, Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, General Failure, Mattias Claesson, Roffe and...",
         "No e-mail support currently, check out www.trionictuning.com", "Just4pLeisure ;-)");

    private bool m_workspaceReady;

    // once the derived window has loaded its XAML; a Window's OnInitialized runs in its base constructor, before that
    private void InitOnce()
    {
        if (m_workspaceReady) return;
        m_workspaceReady = true;
        InitWorkspace();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm) return;
        InitOnce();
        using (var settings = SettingsKey.Open(vm.Suite))
        {
            ApplySkin(settings.GetValue(SkinKey) as string);
            // the symbol list's width from the last session, as the suites' saved dock layout kept it
            if (double.TryParse(settings.GetValue(SymbolListKey) as string, NumberStyles.Float, CultureInfo.CurrentCulture, out double width)
                && width is > 0.05 and < 0.95)
                Space.SymbolPane.Proportion = width;
        }
        Space.Documents.ItemsSource = vm.DockedViewers;
        vm.Viewers.CollectionChanged += (_, e) =>
        {
            foreach (DocumentViewModel d in e.NewItems?.OfType<DocumentViewModel>() ?? []) d.PropertyChanged += OnFloatingChanged;
            foreach (DocumentViewModel d in e.OldItems?.OfType<DocumentViewModel>() ?? [])
            {
                d.PropertyChanged -= OnFloatingChanged;
                if (m_floating.Remove(d, out var w)) w.CloseQuietly();
            }
        };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainWindowViewModel.SelectedViewer)) ActivateDocument(vm.SelectedViewer); };
        ApplyHideSymbolTable();
        WatchMapMenus(vm);
        vm.Info += text => _ = Dialogs.Info(this, text, vm.Caption);
        vm.AskYesNoCancel = text => Dialogs.YesNoCancel(this, text, "Question");
        vm.AskText = caption => Dialogs.Prompt(this, caption);
        vm.AskOkCancel = text => Dialogs.OkCancel(this, text, "Transaction log size warning...");
    }

    protected async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        string? path = await Dialogs.OpenFile(this, BinaryFilesName, "*.bin", "*.s19");
        if (path != null) await Vm.OpenPlainFileAsync(path, true);
    }

    protected async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.SaveFile(this, "Binary files", "bin", Path.GetFileName(bin.FileName)) is not { } target) return;
        File.Copy(bin.FileName, target, true);
        if (await Dialogs.YesNo(this, "Do you want to open the newly saved file?", "Question")) await Vm.OpenPlainFileAsync(target, true);
    }

    /// <summary>A write the user asked for; a failure (read-only file) says so.</summary>
    protected async Task WriteSafely(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Info(this, "Failed to write to binary. Is it read-only? Details: " + ex.Message);
        }
    }

    // ---- skin and help ----

    /// <summary>Skin: light, dark or the system's theme, remembered (the suites' DevExpress skins).</summary>
    protected void OnSkin(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string skin }) return;
        ApplySkin(skin);
        using var settings = SettingsKey.Open(Vm.Suite);
        settings.SetValue(SkinKey, skin);
    }

    private static void ApplySkin(string? skin)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = skin switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>Help: the manuals next to the program, opened with the system's viewer.</summary>
    protected async void OnHelpFile(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name }) return;
        string file = Path.Combine(AppContext.BaseDirectory, name);
        try
        {
            if (!File.Exists(file)) throw new FileNotFoundException();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Exception)
        {
            await Dialogs.Info(this, $"{name} could not be found or opened!");
        }
    }

    // ---- updates ----

    /// <summary>
    /// frmMain_Shown and Help → Check for updates: the dialog when a newer release of the suite is out; OK downloads its setup in
    /// the browser on Windows, elsewhere the release page lists the packages. A build without a release tag (0.0.0) skips the
    /// startup check, every release would be newer.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool startup)
    {
        if (startup && Vm.BuildVersion == new Version(0, 0, 0, 0)) return;
        if (await Vm.CheckForUpdatesAsync() is not { } release) return;
        if (await new UpdateAvailableWindow(release, Vm.Suite).ShowDialog<bool>(this))
            Dialogs.OpenWithShell(OperatingSystem.IsWindows() && release.Msi != null ? release.Msi : release.Page);
    }

    protected async void OnCheckForUpdates(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync(false);

    // the old release notes viewer showed the updater's notes; they're the GitHub releases' now
    protected void OnReleaseNotes(object? sender, RoutedEventArgs e) => Dialogs.OpenWithShell(UpdateCheck.ReleasesPage);

    protected async void OnAbout(object? sender, RoutedEventArgs e)
    {
        string version = Vm.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        // the build metadata (+commit) doesn't belong in the title
        int plus = version.IndexOf('+');
        await NewAboutWindow(plus > 0 ? version[..plus] : version).ShowDialog(this);
    }

    /// <summary>The suite's About... for a version.</summary>
    public AboutWindow NewAboutWindow(string version)
    {
        var (thanks, support, closing) = AboutTexts;
        return new AboutWindow(Vm.Caption, version, thanks, support, closing);
    }

    // ---- projects ----

    protected async void OnCreateProject(object? sender, RoutedEventArgs e)
    {
        ProjectPropertiesViewModel p = Vm.NewProjectProperties();
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.CreateProjectAsync(p);
    }

    protected async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var projects = SuiteProject.List(Vm.Settings.ProjectFolder);
        if (projects.Count == 0)
        {
            await Dialogs.Info(this, "No projects were found, please create one first!", Vm.Caption);
            return;
        }
        if (await new ProjectSelectionWindow(projects).ShowDialog<string?>(this) is { Length: > 0 } name) await Vm.OpenProjectAsync(name);
    }

    protected async void OnEditProject(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is not { } project) return;
        var p = ProjectPropertiesViewModel.From(project.Properties);
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.EditProjectAsync(p);
    }

    protected void OnShowTransactionLog(object? sender, RoutedEventArgs e) =>
        new TransactionLogWindow { DataContext = new TransactionLogViewModel(Vm) }.Show(this);

    protected void OnShowLogbook(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is { } project) new LogbookWindow { DataContext = new LogbookViewModel(project) }.Show(this);
    }

    protected async void OnRebuild(object? sender, RoutedEventArgs e)
    {
        var p = new RebuildViewModel();
        if (!await new RebuildWindow { DataContext = p }.ShowDialog<bool>(this)) return;
        // the date picker gives midnight; the whole chosen day counts
        DateTime upTo = (p.UpTo ?? DateTime.Now).Date.AddDays(1).AddTicks(-1);
        if (Vm.Rebuild(upTo, p.StoreAsCurrent) is not { } rebuilt) return;
        if (await Dialogs.SaveFile(this, "Binary files", "bin", "rebuild.bin") is { } dest) File.Copy(rebuilt, dest, true);
        File.Delete(rebuilt);
    }

    protected async void OnProduceLatest(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "Binary files", "bin", Path.GetFileName(bin.FileName)) is { } dest)
            File.Copy(bin.FileName, dest, true);
    }

    // ---- closing ----

    // unsaved maps ask before the app closes; Cancel keeps it open
    private bool m_closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainWindowViewModel { CloseBlocker: { } blocker } busy)
        {
            // stopping a flash halfway leaves the ECU without a working program
            e.Cancel = true;
            busy.ShowInfo(blocker);
            return;
        }
        if (m_closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.Viewers.OfType<MapViewerViewModel>().Any(v => v.Map.Mutated)) return;
        e.Cancel = true;
        if (!await vm.CloseMutatedViewersAsync()) return;
        m_closeConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is not MainWindowViewModel vm) return;
        if (!double.IsNaN(Space.SymbolPane.Proportion))
            using (var settings = SettingsKey.Open(vm.Suite)) settings.SetValue(SymbolListKey, Space.SymbolPane.Proportion);
        vm.Shutdown();
    }

    // ---- workspace ----

    private bool m_syncingDock;
    private readonly HashSet<Dock.Model.Core.IDockable> m_sized = [];

    private Dock.Model.Core.IFactory? DockFactory => Space.Workspace.Factory;

    private void InitWorkspace()
    {
        // the close button goes through the view model, which asks about unsaved maps and then drops the document
        DockFactory!.DockableClosing += (_, e) =>
        {
            if (e.Dockable?.Context is not DocumentViewModel doc) return;
            e.Cancel = true;
            _ = Vm.CloseViewerAsync(doc);
        };
        DockFactory!.ActiveDockableChanged += (_, e) =>
        {
            if (!m_syncingDock && e.Dockable?.Context is DocumentViewModel doc) Vm.SelectedViewer = doc;
            // Dock brings an inner window forward by raising its ZIndex, and Avalonia's compositor re-sorts the windows without
            // repainting them: only the restyled title bars were redrawn, the other window's table and graph stayed painted over
            // the one brought forward until it was dragged. Redrawing each window's frame repaints the whole window.
            foreach (var w in Space.Workspace.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>())
                w.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_OuterBorder")?.InvalidateVisual();
        };
        // a window dragged by its title bar and let go outside the main window floats (the suites' floating panels)
        AddHandler(PointerPressedEvent, OnWorkspacePressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnWorkspaceReleased, RoutingStrategies.Tunnel, true);
    }

    /// <summary>
    /// Hide symbol window: the symbol list pinned to the side (the suites' auto hide), sliding out when its tab is pointed at;
    /// opening a map slides it back in (ActivateDocument).
    /// </summary>
    protected void ApplyHideSymbolTable()
    {
        if (DockFactory!.IsDockablePinned(Space.SymbolTool) == Vm.Settings.HideSymbolTable) return;
        if (Vm.Settings.HideSymbolTable) DockFactory.PinDockable(Space.SymbolTool);
        else DockFactory.UnpinDockable(Space.SymbolTool);
    }

    private DocumentViewModel? m_titleDrag;
    private readonly Dictionary<DocumentViewModel, FloatingDocumentWindow> m_floating = [];
    private PixelPoint m_floatAt;

    private void OnWorkspacePressed(object? sender, PointerPressedEventArgs e)
    {
        m_titleDrag = null;
        if (e.Source is not Visual source) return;
        bool header = source.GetSelfAndVisualAncestors().OfType<Control>().Any(c => c.Name == "PART_Header");
        if (header && source.FindAncestorOfType<Dock.Avalonia.Controls.MdiDocumentWindow>()?.DataContext is Dock.Model.Core.IDockable { Context: DocumentViewModel doc })
            m_titleDrag = doc;
    }

    private void OnWorkspaceReleased(object? sender, PointerReleasedEventArgs e)
    {
        DocumentViewModel? doc = m_titleDrag;
        m_titleDrag = null;
        Point p = e.GetPosition(this);
        if (doc == null || new Rect(Bounds.Size).Contains(p)) return;
        Float(doc, this.PointToScreen(p));
    }

    /// <summary>A document into a window of its own at a screen position.</summary>
    public void Float(DocumentViewModel doc, PixelPoint at)
    {
        m_floatAt = at;
        doc.IsFloating = true;
    }

    private void OnFloatingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentViewModel.IsFloating) || sender is not DocumentViewModel doc) return;
        if (doc.IsFloating && !m_floating.ContainsKey(doc))
        {
            var window = new FloatingDocumentWindow(Vm, doc) { WindowStartupLocation = WindowStartupLocation.Manual, Position = m_floatAt, Icon = Icon };
            m_floating[doc] = window;
            window.Show(this);
        }
        else if (!doc.IsFloating && m_floating.Remove(doc, out var window))
        {
            window.CloseQuietly();
            ActivateDocument(doc);
        }
    }

    // a document shown from the view model comes to the front (the dock's own document appears after the collection change)
    private void ActivateDocument(DocumentViewModel? viewer)
    {
        if (viewer == null) return;
        if (Vm.Settings.HideSymbolTable && Space.Workspace.Layout is Dock.Model.Controls.IRootDock root) DockFactory!.HidePreviewingDockables(root);
        if (m_floating.TryGetValue(viewer, out var floating))
        {
            floating.Activate();
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Space.Documents.VisibleDockables?.FirstOrDefault(d => d.Context == viewer) is not { } dockable) return;
            if (m_sized.Add(dockable))
            {
                // documents stay inner windows: dropping them on dock targets turned them into tabs, splits (an empty strip
                // by the symbol list) or floating windows that couldn't be brought back
                dockable.CanFloat = false;
                dockable.CanDrop = false;
                // a new inner window opens at a working size, cascaded below the last one (Dock's default is small)
                if (dockable is Dock.Model.Controls.IMdiDocument mdi)
                {
                    int n = Space.Documents.VisibleDockables.Count - 1;
                    double width = Math.Max(640, Space.Workspace.Bounds.Width * 0.6), height = Math.Max(460, Space.Workspace.Bounds.Height * 0.8);
                    mdi.MdiBounds = new Dock.Model.Core.DockRect(24 * (n % 8), 24 * (n % 8), width, height);
                }
            }
            m_syncingDock = true;
            try
            {
                DockFactory!.SetActiveDockable(dockable);
                DockFactory!.SetFocusedDockable(Space.Documents, dockable);
            }
            finally
            {
                m_syncingDock = false;
            }
        });
    }

    protected void OnCascade(object? sender, RoutedEventArgs e) => Space.Documents.CascadeDocuments?.Execute(null);

    protected void OnTileHorizontal(object? sender, RoutedEventArgs e) => Space.Documents.TileDocumentsHorizontal?.Execute(null);

    protected void OnTileVertical(object? sender, RoutedEventArgs e) => Space.Documents.TileDocumentsVertical?.Execute(null);
}
