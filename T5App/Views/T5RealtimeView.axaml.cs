using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using T5App.ViewModels;

namespace T5App.Views;

/// <summary>T5Suite's realtime monitor; the ViewLocator picks it for T5RealtimeViewModel.</summary>
public partial class T5RealtimeView : UserControl
{
    private T5RealtimeViewModel? m_vm;

    public T5RealtimeView()
    {
        InitializeComponent();
        // the grid takes Enter (next row) before a bubbling handler sees it
        UserMapsGrid.AddHandler(KeyDownEvent, OnUserMapKeyDown, RoutingStrategies.Tunnel);
    }

    // subscribed only while shown: a view in a closed window lets go of the view model
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Subscribe(false);
        m_vm = DataContext as T5RealtimeViewModel;
        Subscribe(IsLoaded);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Subscribe(true);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Subscribe(false);
    }

    private void Subscribe(bool on)
    {
        if (m_vm == null) return;
        m_vm.GraphChanged -= OnGraphChanged;
        if (on) m_vm.GraphChanged += OnGraphChanged;
    }

    private void OnGraphChanged()
    {
        if (m_vm?.GraphChannels() is var (channels, start)) Graph.SetData(channels, start);
    }

    private void OnToggleAfr(object? sender, PointerPressedEventArgs e) => m_vm?.ToggleAfrModeCommand.Execute(null);

    private void OnResetPeakBoost(object? sender, PointerPressedEventArgs e) => m_vm?.ResetPeakBoostCommand.Execute(null);

    /// <summary>MapEditButton: the tab's maps in a list; one opens in a viewer.</summary>
    private void OnEditMaps(object? sender, RoutedEventArgs e)
    {
        if (m_vm is not { } vm) return;
        var flyout = new MenuFlyout();
        foreach (UserMap map in vm.TabMaps)
            flyout.Items.Add(new MenuItem { Header = map.Description, Command = vm.OpenMapCommand, CommandParameter = map.Mapname });
        flyout.ShowAt(EditMaps);
    }

    private void OnUserMapDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (UserMapsGrid.SelectedItem is UserMap map) m_vm?.OpenMapCommand.Execute(map.Mapname);
    }

    private void OnUserMapKeyDown(object? sender, KeyEventArgs e)
    {
        if (m_vm is null || UserMapsGrid.SelectedItem is not UserMap map) return;
        if (e.Key == Key.Enter) m_vm.OpenMapCommand.Execute(map.Mapname);
        else if (e.Key == Key.Delete) m_vm.RemoveUserMap(map);
        else return;
        e.Handled = true;
    }
}

/// <summary>An engine status LED: lime when on, dark grey when off.</summary>
public class LedBrushConverter : IValueConverter
{
    public static readonly LedBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Brushes.LimeGreen : Brushes.DimGray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The enrichment grid's bars: towards OrangeRed for enrichment, towards blue for enleanment (T5Suite's gradients).</summary>
public class EnrichmentBrushConverter : IValueConverter
{
    public static readonly EnrichmentBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Brushes.SteelBlue : Brushes.OrangeRed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
