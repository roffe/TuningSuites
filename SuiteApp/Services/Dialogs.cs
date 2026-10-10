using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SuiteApp.Services
{
    /// <summary>
    /// Stand-ins for WinForms MessageBox / OpenFileDialog / SaveFileDialog / Process.Start.
    /// </summary>
    public static class Dialogs
    {
        public static Task Info(Window owner, string text, string caption = "") =>
            Show(owner, text, caption, "OK", null, true);

        public static Task<bool> OkCancel(Window owner, string text, string caption = "") =>
            Show(owner, text, caption, "OK", "Cancel", true);

        /// <summary>Default button is No, matching the MessageBoxDefaultButton.Button2 the forms used.</summary>
        public static Task<bool> YesNo(Window owner, string text, string caption = "") =>
            Show(owner, text, caption, "Yes", "No", false);

        /// <summary>Yes / No / Cancel: true, false, or null for Cancel and the title bar's close.</summary>
        public static async Task<bool?> YesNoCancel(Window owner, string text, string caption = "")
        {
            var dlg = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 320,
                MaxWidth = 640,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var (label, result) in new (string, bool?)[] { ("Yes", true), ("No", false), ("Cancel", null) })
            {
                var b = new Button { Content = label, MinWidth = 80, IsDefault = result == true, IsCancel = result == null };
                b.Click += (_, _) => dlg.Close(result);
                buttons.Children.Add(b);
            }
            var body = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
            body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(buttons);
            dlg.Content = body;
            return await dlg.ShowDialog<bool?>(owner);
        }

        /// <summary>A question with the suite's own buttons (T5Suite's Accept / Decline / Reverse): the index clicked, null when closed.</summary>
        public static async Task<int?> Buttons(Window owner, string text, string caption, params string[] labels)
        {
            var dlg = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 320,
                MaxWidth = 640,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var b = new Button { Content = labels[i], MinWidth = 80, IsDefault = i == 0 };
                b.Click += (_, _) => dlg.Close(index);
                buttons.Children.Add(b);
            }
            var body = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
            body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(buttons);
            dlg.Content = body;
            return await dlg.ShowDialog<int?>(owner);
        }

        /// <summary>A long read-only text in a scrolling, resizable window (reports, binary diffs).</summary>
        public static Task Text(Window owner, string caption, string text)
        {
            var dlg = new Window { Title = caption, Width = 900, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var ok = new Button { Content = "Ok", MinWidth = 80, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            ok.Click += (_, _) => dlg.Close();
            var body = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(ok, Avalonia.Controls.Dock.Bottom);
            body.Children.Add(ok);
            body.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, FontFamily = new Avalonia.Media.FontFamily("monospace") });
            dlg.Content = body;
            return dlg.ShowDialog(owner);
        }

        /// <summary>A report (T5Suite's TuningReport): the lines, with Save to a text file (T5Suite saved DevExpress .prnx documents).</summary>
        public static Task Report(Window owner, string caption, IEnumerable<string> lines)
        {
            string text = string.Join(Environment.NewLine, lines);
            var dlg = new Window { Title = caption, Width = 900, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var ok = new Button { Content = "Ok", MinWidth = 80, IsDefault = true, IsCancel = true };
            ok.Click += (_, _) => dlg.Close();
            var save = new Button { Content = "Save...", MinWidth = 80 };
            save.Click += async (_, _) =>
            {
                if (await SaveFile(dlg, "Reports", "txt", caption) is { } file) System.IO.File.WriteAllText(file, text);
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(save);
            buttons.Children.Add(ok);
            var body = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
            body.Children.Add(buttons);
            body.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            dlg.Content = body;
            return dlg.ShowDialog(owner);
        }

        /// <summary>A wizard page of numbers (label, value, minimum, maximum, step) under an introduction; the values on OK, null on Cancel.</summary>
        public static async Task<decimal[]?> Numbers(Window owner, string caption, string intro, params (string label, decimal value, decimal min, decimal max, decimal step)[] fields)
        {
            var dlg = new Window { Title = caption, Width = 560, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var boxes = fields.Select(f => new NumericUpDown { Value = f.value, Minimum = f.min, Maximum = f.max, Increment = f.step, FormatString = f.step < 1 ? "0.00" : "0", Width = 160 }).ToArray();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < fields.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = fields[i].label, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, i);
                Grid.SetRow(boxes[i], i);
                Grid.SetColumn(boxes[i], 1);
                grid.Children.Add(label);
                grid.Children.Add(boxes[i]);
            }
            bool ok = false;
            var okButton = new Button { Content = "Ok", MinWidth = 80, IsDefault = true };
            okButton.Click += (_, _) => { ok = true; dlg.Close(); };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
            cancel.Click += (_, _) => dlg.Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancel);
            var body = new StackPanel { Margin = new Thickness(16) };
            body.Children.Add(new TextBlock { Text = intro, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(grid);
            body.Children.Add(buttons);
            dlg.Content = body;
            await dlg.ShowDialog(owner);
            return ok ? boxes.Select(b => b.Value ?? 0).ToArray() : null;
        }

        /// <summary>A group of check boxes with Ok / Cancel (T5Suite's "Select merge options"); the states, or null when cancelled.</summary>
        public static async Task<bool[]?> Checks(Window owner, string caption, string group, params (string label, bool value)[] fields)
        {
            var dlg = new Window { Title = caption, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 360, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var boxes = fields.Select(f => new CheckBox { Content = f.label, IsChecked = f.value }).ToArray();
            var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
            foreach (CheckBox box in boxes) list.Children.Add(box);
            bool ok = false;
            var okButton = new Button { Content = "Ok", MinWidth = 80, IsDefault = true };
            okButton.Click += (_, _) => { ok = true; dlg.Close(); };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
            cancel.Click += (_, _) => dlg.Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancel);
            var body = new StackPanel { Margin = new Thickness(16) };
            body.Children.Add(new TextBlock { Text = group, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            body.Children.Add(list);
            body.Children.Add(buttons);
            dlg.Content = body;
            await dlg.ShowDialog(owner);
            return ok ? boxes.Select(b => b.IsChecked == true).ToArray() : null;
        }

        /// <summary>One line of text (frmChangeNote "Remark for change"); null when cancelled.</summary>
        public static async Task<string?> Prompt(Window owner, string caption)
        {
            var dlg = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var text = new TextBox { MinWidth = 360 };
            var ok = new Button { Content = "Ok", MinWidth = 80, IsDefault = true };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
            ok.Click += (_, _) => dlg.Close(text.Text ?? "");
            cancel.Click += (_, _) => dlg.Close(null);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
            dlg.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { text, buttons } };
            dlg.Opened += (_, _) => text.Focus();
            return await dlg.ShowDialog<string?>(owner);
        }

        static async Task<bool> Show(Window owner, string text, string caption, string yes, string? no, bool yesIsDefault)
        {
            var dlg = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 320,
                MaxWidth = 640,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = yes, MinWidth = 80, IsDefault = yesIsDefault, IsCancel = no == null };
            ok.Click += (_, _) => dlg.Close(true);
            buttons.Children.Add(ok);
            if (no != null)
            {
                var cancel = new Button { Content = no, MinWidth = 80, IsDefault = !yesIsDefault, IsCancel = true };
                cancel.Click += (_, _) => dlg.Close(false);
                buttons.Children.Add(cancel);
            }
            var body = new StackPanel { Margin = new Thickness(16), Spacing = 16 };
            body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(buttons);
            dlg.Content = body;
            dlg.Opened += (_, _) => (yesIsDefault || no == null ? ok : (Control)buttons.Children[1]).Focus();

            // closing via the title bar returns default(bool) = false, i.e. Cancel/No
            return await dlg.ShowDialog<bool>(owner);
        }

        /// <param name="patterns">e.g. "*.bin"</param>
        /// <returns>local path, or null when cancelled</returns>
        public static Task<string?> OpenFile(Window owner, string filterName, params string[] patterns) => OpenFileIn(owner, null, null, filterName, patterns);

        /// <summary>OpenFile with the picker's title and first folder, where the suite's dialog had them.</summary>
        public static async Task<string?> OpenFileIn(Window owner, string? title, string? folder, string filterName, params string[] patterns)
        {
            // GTK / portal globs are case-sensitive (Windows' aren't): *.bin must also list FOO.BIN
            // ponytail: mixed case like *.Bin still hidden
            // The Linux portal / GTK pickers leave the owner clickable (WinForms' were modal):
            // a double click would open a second picker and start a second ECU operation
            owner.IsEnabled = false;
            try
            {
                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    SuggestedStartLocation = folder != null && Directory.Exists(folder) ? await owner.StorageProvider.TryGetFolderFromPathAsync(folder) : null,
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns.Concat(patterns.Select(p => p.ToUpperInvariant())).Distinct().ToArray() } },
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            }
            finally
            {
                owner.IsEnabled = true;
            }
        }

        /// <param name="extension">default extension without dot, e.g. "bin"</param>
        /// <returns>local path, or null when cancelled</returns>
        public static async Task<string?> SaveFile(Window owner, string filterName, string extension, string? suggestedName = null, string? title = null)
        {
            owner.IsEnabled = false; // see OpenFile
            try
            {
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title,
                    DefaultExtension = extension,
                    SuggestedFileName = suggestedName,
                    ShowOverwritePrompt = true,
                    FileTypeChoices = new[] { new FilePickerFileType(filterName) { Patterns = new[] { "*." + extension } } },
                });
                return file?.TryGetLocalPath();
            }
            finally
            {
                owner.IsEnabled = true;
            }
        }

        /// <summary>
        /// Runs a dialog on the UI thread and blocks the caller until it is answered. For callbacks
        /// the library fires from worker threads (UserPrompt, ChecksumDelegate). On the UI thread
        /// it pumps a nested dispatcher frame instead of deadlocking.
        /// </summary>
        public static T Wait<T>(Func<Task<T>> dialog)
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return Dispatcher.UIThread.InvokeAsync(dialog).GetAwaiter().GetResult();

            var task = dialog();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.UIThread.PushFrame(frame);
            }
            return task.GetAwaiter().GetResult();
        }

        /// <summary>Open a file, folder or URL with the desktop's default handler (xdg-open / open / shell).</summary>
        public static void OpenWithShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { /* ponytail: no handler installed, nothing useful to do */ }
        }
    }
}
