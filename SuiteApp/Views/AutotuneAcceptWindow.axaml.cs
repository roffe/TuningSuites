using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MapControls;

namespace SuiteApp.Views;

/// <summary>
/// frmFuelMapAccept: the proposed change per fuel map cell in percent (0.1 % steps), rows as the fuel map; returns the cells to
/// take, all changed ones, or null.
/// </summary>
public partial class AutotuneAcceptWindow : Window
{
    private readonly double[] m_percent = [];

    public AutotuneAcceptWindow() => InitializeComponent();

    public AutotuneAcceptWindow(string fuelMap, double[] percent, double[] xAxis, double[] yAxis, string xName = "mg/c", string yName = "rpm") : this()
    {
        m_percent = percent;
        Title = "Select percent mutations to accept for map " + fuelMap;
        var data = new byte[percent.Length * 2];
        for (int i = 0; i < percent.Length; i++)
        {
            int v = double.IsNaN(percent[i]) ? 0 : (int)Math.Round(Math.Clamp(percent[i], -400, 400) * 10);
            data[i * 2] = (byte)(v >> 8);
            data[i * 2 + 1] = (byte)v;
        }
        Grid.Map = new MapData(fuelMap, data, xAxis.Length > 0 ? xAxis.Length : 18, true)
        {
            Factor = 0.1, UpsideDown = true, XAxis = xAxis, YAxis = yAxis, XName = xName, YName = yName, ZName = "%",
        };
        Grid.ViewType = MapViewType.Easy;
    }

    private IEnumerable<int> Changed => Enumerable.Range(0, m_percent.Length).Where(i => m_percent[i] != 0 && !double.IsNaN(m_percent[i]));

    private void OnAcceptSelected(object? sender, RoutedEventArgs e) => Close((IReadOnlyCollection<int>)Grid.SelectedCells.Intersect(Changed).ToList());

    private void OnAcceptAll(object? sender, RoutedEventArgs e) => Close((IReadOnlyCollection<int>)Changed.ToList());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
