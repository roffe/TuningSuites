using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

/// <summary>frmVectorlist: Vector and Address (X8) for the 256 vectors.</summary>
public partial class VectorsWindow : Window
{
    public VectorsWindow() => InitializeComponent();

    public VectorsWindow(IEnumerable<(string Name, long Address)> vectors) : this() =>
        List.ItemsSource = vectors.Select(v => $"{v.Address:X8}  {v.Name}").ToList();

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
