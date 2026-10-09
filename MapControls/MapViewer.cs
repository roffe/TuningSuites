using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MapControls;

/// <summary>
/// One map as T7Suite's MapViewerEx lays it out: the view type above, the table, and under a splitter the 3D surface and
/// the 2D graph of one column (picked with the slider, plotted over the Y axis) in tabs.
/// </summary>
public class MapViewer : UserControl
{
    public static readonly StyledProperty<MapData?> MapProperty = AvaloniaProperty.Register<MapViewer, MapData?>(nameof(Map));
    public static readonly StyledProperty<MapViewType> ViewTypeProperty =
        AvaloniaProperty.Register<MapViewer, MapViewType>(nameof(ViewType), MapViewType.Easy, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<bool> OnlineModeProperty = AvaloniaProperty.Register<MapViewer, bool>(nameof(OnlineMode));
    public static readonly StyledProperty<bool> IsRedWhiteProperty = AvaloniaProperty.Register<MapViewer, bool>(nameof(IsRedWhite));
    public static readonly StyledProperty<bool> DisableColorsProperty = AvaloniaProperty.Register<MapViewer, bool>(nameof(DisableColors));
    public static readonly StyledProperty<OpenLoopMark> OpenLoopMarkProperty = AvaloniaProperty.Register<MapViewer, OpenLoopMark>(nameof(OpenLoopMark));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<MapViewer, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<bool> GraphVisibleProperty = AvaloniaProperty.Register<MapViewer, bool>(nameof(GraphVisible), true);
    public static readonly StyledProperty<string?> SyncGroupProperty = AvaloniaProperty.Register<MapViewer, string?>(nameof(SyncGroup));

    public MapData? Map { get => GetValue(MapProperty); set => SetValue(MapProperty, value); }
    public MapViewType ViewType { get => GetValue(ViewTypeProperty); set => SetValue(ViewTypeProperty, value); }
    public bool OnlineMode { get => GetValue(OnlineModeProperty); set => SetValue(OnlineModeProperty, value); }
    public bool IsRedWhite { get => GetValue(IsRedWhiteProperty); set => SetValue(IsRedWhiteProperty, value); }
    public bool DisableColors { get => GetValue(DisableColorsProperty); set => SetValue(DisableColorsProperty, value); }
    public OpenLoopMark OpenLoopMark { get => GetValue(OpenLoopMarkProperty); set => SetValue(OpenLoopMarkProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool GraphVisible { get => GetValue(GraphVisibleProperty); set => SetValue(GraphVisibleProperty, value); }

    /// <summary>Viewers in the same group (T7Suite: the same map name) follow each other's selection and 3D camera.</summary>
    public string? SyncGroup { get => GetValue(SyncGroupProperty); set => SetValue(SyncGroupProperty, value); }

    private static readonly List<WeakReference<MapViewer>> s_viewers = new();

    /// <summary>Selection or 3D camera changed by the user, to sync other viewers of the same map.</summary>
    public event EventHandler? SelectionChanged;
    public event EventHandler? CameraChanged;

    public MapGrid Grid { get; } = new();
    public Surface3D Surface { get; } = new();
    public Graph2D Graph { get; } = new();

    private readonly Slider m_slice = new() { Minimum = 0, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(8, 0) };
    private readonly ComboBox m_viewType = new() { ItemsSource = new[] { "Hex", "Decimal", "Easy", "ASCII" }, MinWidth = 100 };
    private readonly Grid m_split;
    private bool m_syncing;

    public MapViewer()
    {
        m_viewType.SelectedIndex = (int)ViewType;
        m_viewType.SelectionChanged += (_, _) => { if (m_viewType.SelectedIndex >= 0) ViewType = (MapViewType)m_viewType.SelectedIndex; };
        m_slice.ValueChanged += (_, _) => ShowSlice();
        Grid.SelectionChanged += (_, _) => { if (!m_syncing) { SelectionChanged?.Invoke(this, EventArgs.Empty); SyncOthers(true, false); } };
        Surface.CameraChanged += (_, _) => { if (!m_syncing) { CameraChanged?.Invoke(this, EventArgs.Empty); SyncOthers(false, true); } };
        AttachedToVisualTree += (_, _) => { lock (s_viewers) s_viewers.Add(new WeakReference<MapViewer>(this)); };
        DetachedFromVisualTree += (_, _) => { lock (s_viewers) s_viewers.RemoveAll(w => !w.TryGetTarget(out var v) || v == this); };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4) };
        toolbar.Children.Add(m_viewType);

        var graphTabs = new TabControl();
        graphTabs.Items.Add(new TabItem { Header = "3D Graph", Content = Surface });
        var slicePanel = new DockPanel();
        DockPanel.SetDock(m_slice, Dock.Bottom);
        slicePanel.Children.Add(m_slice);
        slicePanel.Children.Add(Graph);
        graphTabs.Items.Add(new TabItem { Header = "2D Graph", Content = slicePanel });

        m_split = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
        m_split.Children.Add(Grid);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
        Avalonia.Controls.Grid.SetRow(splitter, 1);
        Avalonia.Controls.Grid.SetRow(graphTabs, 2);
        m_split.Children.Add(splitter);
        m_split.Children.Add(graphTabs);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(m_split);
        Content = root;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MapProperty)
        {
            if (change.OldValue is MapData old) old.Changed -= OnMapChanged;
            if (change.NewValue is MapData map)
            {
                map.Changed += OnMapChanged;
                int zPrec = map.Factor < 1 ? Math.Min(4, (int)Math.Ceiling(-Math.Log10(map.Factor))) : 0;
                Surface.SetData(map.PhysicalValues(), map.Cols, map.Rows, map.XAxis, map.YAxis, map.XName, map.YName, map.ZName, 0, 0, zPrec);
                m_slice.Maximum = map.Cols - 1;
                m_slice.IsVisible = map.Cols > 1;
            }
            Grid.Map = change.NewValue as MapData;
            ShowSlice();
        }
        else if (change.Property == ViewTypeProperty)
        {
            Grid.ViewType = ViewType;
            m_viewType.SelectedIndex = (int)ViewType;
        }
        else if (change.Property == OnlineModeProperty)
        {
            Grid.OnlineMode = Surface.OnlineMode = Graph.OnlineMode = OnlineMode;
        }
        else if (change.Property == IsRedWhiteProperty) Grid.IsRedWhite = IsRedWhite;
        else if (change.Property == DisableColorsProperty) Grid.DisableColors = DisableColors;
        else if (change.Property == OpenLoopMarkProperty) Grid.OpenLoopMark = OpenLoopMark;
        else if (change.Property == IsReadOnlyProperty) Grid.IsReadOnly = IsReadOnly;
        else if (change.Property == GraphVisibleProperty)
        {
            m_split.RowDefinitions[1].Height = new GridLength(GraphVisible ? 4 : 0);
            m_split.RowDefinitions[2].Height = GraphVisible ? GridLength.Star : new GridLength(0);
        }
    }

    private void OnMapChanged(object? sender, EventArgs e)
    {
        if (Map is not { } map) return;
        Surface.SetValues(map.PhysicalValues());
        ShowSlice();
    }

    // MapViewerEx's 2D chart: the slider's column over all data rows, plotted against the Y axis
    private void ShowSlice()
    {
        if (Map is not { } map) return;
        int col = (int)Math.Clamp(m_slice.Value, 0, map.Cols - 1);
        double[] all = map.PhysicalValues();
        double[] slice = Enumerable.Range(0, map.Rows).Select(r => r * map.Cols + col).Where(i => i < all.Length).Select(i => all[i]).ToArray();
        string at = map.XAxis != null && col < map.XAxis.Length ? $" @ {map.XName} {map.XAxis[col].ToString(CultureInfo.CurrentCulture)}" : "";
        int prec = map.Factor < 1 ? Math.Min(4, (int)Math.Ceiling(-Math.Log10(map.Factor))) : 0;
        Graph.SetData(slice, map.YAxis, all.Min(), all.Max(), 0, prec, map.YName + at);
    }

    private void SyncOthers(bool selection, bool camera)
    {
        if (string.IsNullOrEmpty(SyncGroup)) return;
        MapViewer[] others;
        lock (s_viewers) others = s_viewers.Select(w => w.TryGetTarget(out var v) ? v : null).OfType<MapViewer>().Where(v => v != this && v.SyncGroup == SyncGroup).ToArray();
        foreach (MapViewer v in others)
        {
            // only viewers of a map with the same shape, a selection index means nothing in another layout
            if (v.Map is { } m && Map is { } mine && m.Cols == mine.Cols && m.Count == mine.Count) v.SyncFrom(this, selection, camera);
        }
    }

    /// <summary>Applies another viewer's selection / camera without raising the events back.</summary>
    public void SyncFrom(MapViewer other, bool selection, bool camera)
    {
        m_syncing = true;
        try
        {
            if (selection) Grid.Select(other.Grid.SelectedCells.ToArray());
            if (camera) Surface.Camera = other.Surface.Camera;
        }
        finally
        {
            m_syncing = false;
        }
    }
}
