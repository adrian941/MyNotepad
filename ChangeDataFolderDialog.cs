using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinimalNotepad.Config;

namespace MinimalNotepad
{
    sealed class ChangeDataFolderDialog : Window
    {
        private readonly TextBox   _pathBox;
        private readonly TextBlock _statusText;
        private readonly Button    _moveBtn;
        private readonly Button    _browseBtn;

        public ChangeDataFolderDialog(Window owner)
        {
            Owner                 = owner;
            Title                 = "Change Data Folder";
            Width                 = 500;
            Height                = 300;
            ResizeMode            = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar         = false;
            Background            = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF7));

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };

            root.Children.Add(Lbl("Current data folder:", bold: true));
            root.Children.Add(new TextBlock
            {
                Text         = AppDataPath.Root,
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x40)),
                Margin       = new Thickness(0, 2, 0, 14)
            });

            root.Children.Add(Lbl("New data folder:", bold: true));

            _pathBox = new TextBox
            {
                FontSize                 = 11,
                Height                   = 26,
                Padding                  = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _browseBtn = new Button
            {
                Content = "Browse…",
                Height  = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin  = new Thickness(6, 0, 0, 0),
                Cursor  = Cursors.Hand
            };
            _browseBtn.Click += OnBrowse;

            var pathRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 10) };
            DockPanel.SetDock(_browseBtn, Dock.Right);
            pathRow.Children.Add(_browseBtn);
            pathRow.Children.Add(_pathBox);
            root.Children.Add(pathRow);

            _statusText = new TextBlock
            {
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 8),
                Visibility   = Visibility.Collapsed
            };
            root.Children.Add(_statusText);

            root.Children.Add(new TextBlock
            {
                Text         = "All app data will be copied to the new folder, verified, then the old folder will be cleaned up. The app must be restarted after this operation.",
                FontSize     = 10,
                FontStyle    = FontStyles.Italic,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 14)
            });

            _moveBtn = new Button
            {
                Content   = "Move & Apply",
                Height    = 28,
                Padding   = new Thickness(16, 0, 16, 0),
                Cursor    = Cursors.Hand,
                IsDefault = true
            };
            _moveBtn.Click += OnMoveAndApply;

            var cancelBtn = new Button
            {
                Content  = "Cancel",
                Height   = 28,
                Padding  = new Thickness(10, 0, 10, 0),
                Margin   = new Thickness(0, 0, 8, 0),
                Cursor   = Cursors.Hand,
                IsCancel = true
            };

            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(_moveBtn);
            root.Children.Add(btnRow);

            Content = root;
        }

        // ── Browse ────────────────────────────────────────────────────────────

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select folder for app data" };
            if (!string.IsNullOrWhiteSpace(_pathBox.Text) && Directory.Exists(_pathBox.Text))
                dlg.InitialDirectory = _pathBox.Text;
            if (dlg.ShowDialog(this) == true)
                _pathBox.Text = dlg.FolderName;
        }

        // ── Move & Apply ──────────────────────────────────────────────────────

        private async void OnMoveAndApply(object sender, RoutedEventArgs e)
        {
            var raw = _pathBox.Text.Trim();
            if (string.IsNullOrEmpty(raw)) { SetStatus("Enter a folder path.", error: true); return; }

            string fullNew;
            try { fullNew = Path.Combine(Path.GetFullPath(raw), "MinimalNotepad"); }
            catch { SetStatus("Invalid path.", error: true); return; }

            var fullOld = Path.GetFullPath(AppDataPath.Root);

            if (string.Equals(fullNew, fullOld, StringComparison.OrdinalIgnoreCase))
            { SetStatus("That is already the current data folder.", error: true); return; }

            if (fullNew.StartsWith(fullOld + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { SetStatus("Cannot move data into a subfolder of itself.", error: true); return; }

            SetBusy(true);
            SetStatus("Copying files…", error: false);

            try { await Task.Run(() => CopyAll(fullOld, fullNew)); }
            catch (Exception ex) { SetStatus("Copy failed: " + ex.Message, error: true); SetBusy(false); return; }

            SetStatus("Verifying…", error: false);
            bool ok = await Task.Run(() => Verify(fullOld, fullNew));
            if (!ok)
            {
                SetStatus("Verification failed — files may be incomplete. Nothing was changed.", error: true);
                SetBusy(false);
                return;
            }

            // Write pointer before deleting old data so we never lose the path.
            AppDataPath.SetRoot(fullNew);

            SetStatus("Cleaning up old folder…", error: false);
            await Task.Run(() => DeleteOldData(fullOld, AppDataPath.DefaultRoot));

            SetBusy(false);
            SetStatus("Done. Restart the app to use the new data folder.", error: false);

            // Swap button to "Restart Now"
            _moveBtn.Click -= OnMoveAndApply;
            _moveBtn.Content = "Restart Now";
            _moveBtn.Click  += (_, _) => RestartApp();
            _moveBtn.IsEnabled = true;
        }

        // ── File operations ───────────────────────────────────────────────────

        static void CopyAll(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                if (Skip(Path.GetFileName(file))) continue;
                var rel  = Path.GetRelativePath(src, file);
                var dest = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }

        static bool Verify(string src, string dst)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    if (Skip(Path.GetFileName(file))) continue;
                    var rel      = Path.GetRelativePath(src, file);
                    var destFile = Path.Combine(dst, rel);
                    if (!File.Exists(destFile)) return false;
                    if (new FileInfo(file).Length != new FileInfo(destFile).Length) return false;
                }
                return true;
            }
            catch { return false; }
        }

        static void DeleteOldData(string oldRoot, string defaultRoot)
        {
            try
            {
                bool isDefault = string.Equals(
                    Path.GetFullPath(oldRoot), Path.GetFullPath(defaultRoot),
                    StringComparison.OrdinalIgnoreCase);

                if (!isDefault)
                {
                    Directory.Delete(oldRoot, recursive: true);
                }
                else
                {
                    // Default root stays (holds .datapath); only clean its contents.
                    foreach (var d in Directory.GetDirectories(oldRoot))
                        Directory.Delete(d, recursive: true);
                    foreach (var f in Directory.GetFiles(oldRoot))
                        if (!Skip(Path.GetFileName(f))) File.Delete(f);
                }
            }
            catch { }
        }

        static bool Skip(string fileName) =>
            fileName.Equals(".datapath",        StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("open_request.txt", StringComparison.OrdinalIgnoreCase);

        static void RestartApp()
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exe != null)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        void SetBusy(bool busy)
        {
            _moveBtn.IsEnabled   = !busy;
            _browseBtn.IsEnabled = !busy;
            _pathBox.IsEnabled   = !busy;
        }

        void SetStatus(string msg, bool error)
        {
            _statusText.Text       = msg;
            _statusText.Foreground = error
                ? new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00))
                : new SolidColorBrush(Color.FromRgb(0x1A, 0x7A, 0x32));
            _statusText.Visibility = Visibility.Visible;
        }

        static TextBlock Lbl(string text, bool bold = false) => new()
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x72))
        };
    }
}
