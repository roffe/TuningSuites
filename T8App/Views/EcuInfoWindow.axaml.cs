using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using T8SuitePro;

namespace T8App.Views;

public partial class EcuInfoWindow : Window
{
    public EcuInfoWindow() => InitializeComponent();

    public EcuInfoWindow(List<EcuInfoRow> rows) : this()
    {
        EcuRows.ItemsSource = rows.Where(r => r.Group == "ECU related data").ToList();
        CalibrationRows.ItemsSource = rows.Where(r => r.Group != "ECU related data").ToList();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
