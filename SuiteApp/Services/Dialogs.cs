using System;
using System.Diagnostics;
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
        public static async Task<string?> OpenFile(Window owner, string filterName, params string[] patterns)
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
        public static async Task<string?> SaveFile(Window owner, string filterName, string extension, string? suggestedName = null)
        {
            owner.IsEnabled = false; // see OpenFile
            try
            {
                var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
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
