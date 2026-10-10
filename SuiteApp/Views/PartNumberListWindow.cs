using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CommonSuite;

namespace SuiteApp.Views;

/// <summary>Partnumber list window for all suites: browse the stock binaries in Binaries folder.</summary>
public static class PartNumberListWindow
{
    private sealed record PartNumberRow(string Partnumber, string CarModel, string EngineType, string Bhp, string Torque);

    private static List<PartNumberRow> GetPartNumbers(Func<string, PartInfo?> lookup)
    {
        var rows = new List<PartNumberRow>();
        string binDir = Path.Combine(AppContext.BaseDirectory, "Binaries");
        if (!Directory.Exists(binDir)) return rows;

        foreach (var file in Directory.GetFiles(binDir, "*.bin", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name)) continue;
            if (lookup(name) is not { } info) continue;
            rows.Add(new PartNumberRow(name, info.CarModel, info.EngineType, info.Bhp.ToString(), info.Torque.ToString()));
        }
        return rows;
    }

    public static Task<string?> Show(Window owner, Func<string, PartInfo?> lookup)
    {
        var rows = GetPartNumbers(lookup)
            .OrderBy(r => r.CarModel)
            .ThenBy(r => r.EngineType)
            .ToList();
        var view = new Avalonia.Collections.DataGridCollectionView(rows);
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.CarModel)));
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.EngineType)));

        var grid = new DataGrid
        {
            ItemsSource = view,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Columns =
            {
                new DataGridTextColumn { Header = "Partnumber", Binding = new Avalonia.Data.Binding(nameof(PartNumberRow.Partnumber)) },
                new DataGridTextColumn { Header = "Power", Binding = new Avalonia.Data.Binding(nameof(PartNumberRow.Bhp)) },
                new DataGridTextColumn { Header = "Torque", Binding = new Avalonia.Data.Binding(nameof(PartNumberRow.Torque)) },
            }
        };

        Window? window = null;

        void Pick() => window!.Close((grid.SelectedItem as PartNumberRow)?.Partnumber);

        grid.DoubleTapped += (_, _) => Pick();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var okBtn = new Button { Content = "Ok", MinWidth = 80 };
        okBtn.Click += (_, _) => Pick();
        buttons.Children.Add(okBtn);

        var closeBtn = new Button { Content = "Close", MinWidth = 80 };
        closeBtn.Click += (_, _) => window!.Close(null);
        buttons.Children.Add(closeBtn);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(grid);

        window = new Window
        {
            Title = "Partnumber list",
            Width = 600,
            Height = 600,
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };

        return window.ShowDialog<string?>(owner);
    }
}
