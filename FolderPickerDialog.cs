using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    internal sealed class FolderPickerDialog : Window
    {
        private readonly string _root;
        private readonly TreeView _tree;
        private readonly Button _okBtn;
        private readonly Button _renameBtn;
        private readonly Button _createBtn;
        private readonly Button _deleteBtn;
        private readonly TextBox _newNameBox;
        private readonly TextBlock _pathPreview;
        private readonly TextBlock _invalidWarning;
        private System.Windows.Threading.DispatcherTimer? _warnTimer;
        private Border? _renameSection;
        private TextBox? _renameBox;
        private TextBlock? _renameError;

        private static readonly char[] _invalidChars = Path.GetInvalidFileNameChars();

        public string? SelectedRelative { get; private set; }

        public FolderPickerDialog(Window owner, string? headerText = null)
        {
            _root = Path.GetFullPath(FolderChipsStore.Root);

            Owner                 = owner;
            Title                 = "Choose folder";
            Width                 = 360;
            Height                = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode            = ResizeMode.CanResize;
            ShowInTaskbar         = false;
            Background            = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF7));

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(10) };

            // ── Header ─────────────────────────────────────────────────────
            var header = new TextBlock
            {
                Text       = headerText ?? "Select a subfolder to add as a chip",
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x72)),
                Margin     = new Thickness(2, 0, 0, 8)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Order of Dock.Bottom additions (first = very bottom visually):
            // actionRow → pathPreview → invalidWarning → nameRow → sep → btnRow

            // ── [Create Folder] [Delete Selected] — very bottom ───────────
            _createBtn = new Button
            {
                Content   = "Create Folder",
                Height    = 26,
                MinWidth  = 100,
                Margin    = new Thickness(0, 0, 6, 0),
                Cursor    = Cursors.Hand,
                IsEnabled = false
            };
            _deleteBtn = new Button
            {
                Content    = "Delete Selected",
                Height     = 26,
                MinWidth   = 100,
                Cursor     = Cursors.Hand,
                IsEnabled  = false,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30))
            };
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 10, 0, 0)   // top gap = space after preview
            };
            actionRow.Children.Add(_createBtn);
            actionRow.Children.Add(_deleteBtn);
            DockPanel.SetDock(actionRow, Dock.Bottom);
            root.Children.Add(actionRow);

            // ── Path preview — directly below textbox ──────────────────────
            _pathPreview = new TextBlock
            {
                FontSize     = 10,
                FontStyle    = FontStyles.Italic,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x22, 0x7A, 0x22)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(2, 2, 0, 0),
                Text         = ""
            };
            DockPanel.SetDock(_pathPreview, Dock.Bottom);
            root.Children.Add(_pathPreview);

            // ── Invalid chars warning — collapses when not shown (no space taken) ─
            _invalidWarning = new TextBlock
            {
                FontSize    = 10,
                Foreground  = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30)),
                Margin      = new Thickness(2, 2, 0, 0),
                Visibility  = Visibility.Collapsed,
                Text        = ""
            };
            DockPanel.SetDock(_invalidWarning, Dock.Bottom);
            root.Children.Add(_invalidWarning);

            // ── New folder name row ────────────────────────────────────────
            var nameLabel = new TextBlock
            {
                Text              = "New folder name:",
                FontSize          = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x50)),
                Margin            = new Thickness(0, 0, 7, 0)
            };
            _newNameBox = new TextBox
            {
                Height                   = 24,
                FontSize                 = 11,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var nameRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            nameRow.Children.Add(_newNameBox);
            DockPanel.SetDock(nameRow, Dock.Bottom);
            root.Children.Add(nameRow);

            // ── Separator between selection area and create section ────────
            var sep = new Border
            {
                Height     = 1,
                Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                Margin     = new Thickness(-10, 10, -10, 0)
            };
            DockPanel.SetDock(sep, Dock.Bottom);
            root.Children.Add(sep);

            // ── [Cancel] [Rename] [Select Folder] — between tree and create section ─
            _okBtn = new Button
            {
                Content   = "Select Folder",
                Height    = 27,
                Width     = 95,
                Margin    = new Thickness(6, 8, 0, 4),
                Cursor    = Cursors.Hand,
                IsEnabled = false,
                IsDefault = true
            };
            _renameBtn = new Button
            {
                Content   = "Rename…",
                Height    = 27,
                Width     = 80,
                Margin    = new Thickness(6, 8, 0, 4),
                Cursor    = Cursors.Hand,
                IsEnabled = false
            };
            var cancelBtn = new Button
            {
                Content  = "Cancel",
                Height   = 27,
                Width    = 80,
                Margin   = new Thickness(0, 8, 0, 4),
                Cursor   = Cursors.Hand,
                IsCancel = true
            };
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(_renameBtn);
            btnRow.Children.Add(_okBtn);
            DockPanel.SetDock(btnRow, Dock.Bottom);
            root.Children.Add(btnRow);

            // ── Rename inline section (appears below tree when rename is active) ─
            _renameBox   = new TextBox
            {
                Height                   = 24,
                FontSize                 = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin                   = new Thickness(0, 0, 6, 0)
            };
            _renameError = MakeErrorLabel(margin: new Thickness(2, 3, 0, 0));
            var applyBtn = new Button
            {
                Content   = "Apply",
                Height    = 24,
                MinWidth  = 54,
                FontSize  = 11,
                Cursor    = Cursors.Hand,
                IsDefault = false
            };
            var renameBtnRow = new DockPanel();
            DockPanel.SetDock(applyBtn, Dock.Right);
            renameBtnRow.Children.Add(applyBtn);
            renameBtnRow.Children.Add(_renameBox);
            var renameInner = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
            renameInner.Children.Add(new TextBlock
            {
                Text       = "New name:",
                FontSize   = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x50)),
                Margin     = new Thickness(0, 0, 0, 4)
            });
            renameInner.Children.Add(renameBtnRow);
            renameInner.Children.Add(_renameError);
            _renameSection = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF8)),
                BorderBrush  = new SolidColorBrush(Color.FromRgb(0xCC, 0xD8, 0xEC)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Visibility   = Visibility.Collapsed,
                Child        = renameInner
            };
            DockPanel.SetDock(_renameSection, Dock.Bottom);
            root.Children.Add(_renameSection);

            applyBtn.Click += (_, _) => ExecuteRename();
            _renameBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { ke.Handled = true; ExecuteRename(); }
                if (ke.Key == Key.Escape) { ke.Handled = true; CollapseRenameSection(); }
            };
            _renameBox.PreviewTextInput += OnNamePreviewTextInput;

            // ── Tree (fills remaining space) ───────────────────────────────
            _tree = new TreeView { BorderThickness = new Thickness(1), Padding = new Thickness(4) };
            root.Children.Add(_tree);

            Content = root;
            PopulateTree(null);

            // ── Event wiring ───────────────────────────────────────────────
            _tree.SelectedItemChanged += (_, _) =>
            {
                string? sel          = SelectedRel();
                bool    hasSub       = sel is { Length: > 0 };
                _okBtn.IsEnabled     = hasSub;
                _deleteBtn.IsEnabled = hasSub;
                _renameBtn.IsEnabled = hasSub;
                if (!hasSub) CollapseRenameSection();
                UpdatePathPreview();
            };

            _newNameBox.PreviewTextInput += OnNamePreviewTextInput;
            DataObject.AddPastingHandler(_newNameBox, OnNamePasting);
            _newNameBox.TextChanged += (_, _) => { UpdateCreateButton(); UpdatePathPreview(); };
            _newNameBox.KeyDown     += (_, e) =>
            {
                if (e.Key == Key.Enter && _createBtn.IsEnabled)
                { e.Handled = true; ExecuteCreate(); }
            };

            _okBtn.Click     += (_, _) => { SelectedRelative = SelectedRel(); DialogResult = true; };
            _createBtn.Click += (_, _) => ExecuteCreate();
            _deleteBtn.Click += (_, _) => ExecuteDelete();
            _renameBtn.Click += (_, _) => OpenRenameSection();
        }

        // ── Error label factory ───────────────────────────────────────────────

        private static TextBlock MakeErrorLabel(Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                FontSize     = 10,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30)),
                TextWrapping = TextWrapping.Wrap,
                Cursor       = Cursors.Hand,
                Margin       = margin ?? new Thickness(0, 3, 0, 0),
                Visibility   = Visibility.Collapsed,
                ToolTip      = "Click to see full message"
            };
            tb.MouseLeftButtonUp += (_, _) =>
            {
                if (!string.IsNullOrEmpty(tb.Text))
                    MessageBox.Show(tb.Text, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            return tb;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string? SelectedRel() =>
            (_tree.SelectedItem as TreeViewItem)?.Tag as string;

        private static bool HasInvalidChars(string name) =>
            name.IndexOfAny(_invalidChars) >= 0;

        private static char[] CollectInvalid(string text) =>
            text.Where(c => _invalidChars.Contains(c)).Distinct().ToArray();

        private static string FormatInvalidChars(char[] chars) =>
            string.Join("  ", chars.Select(c => c < 32 ? $"[#{(int)c}]" : $"'{c}'"));

        private void ShowInvalidWarning(char[] invalid)
        {
            _warnTimer?.Stop();
            _invalidWarning.Text       = $"Not allowed: {FormatInvalidChars(invalid)}";
            _invalidWarning.Visibility = Visibility.Visible;
            _warnTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1) };
            _warnTimer.Tick += (_, _) =>
            {
                _warnTimer!.Stop();
                _invalidWarning.Visibility = Visibility.Collapsed;
            };
            _warnTimer.Start();
        }

        private void UpdateCreateButton()
        {
            string name = _newNameBox.Text.Trim();
            _createBtn.IsEnabled = name.Length > 0 && !HasInvalidChars(name);
        }

        private void UpdatePathPreview()
        {
            string name = _newNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name) || HasInvalidChars(name))
            { _pathPreview.Text = ""; return; }
            string parent = SelectedRel() ?? "";
            _pathPreview.Text = "📁 " + (parent.Length == 0 ? name : parent + "/" + name);
        }

        // ── Input validation ──────────────────────────────────────────────────

        private void OnNamePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var invalid = CollectInvalid(e.Text);
            if (invalid.Length > 0) { e.Handled = true; ShowInvalidWarning(invalid); }
        }

        private void OnNamePasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text)) return;
            string text    = (string)e.DataObject.GetData(DataFormats.Text) ?? "";
            var    invalid = CollectInvalid(text);
            if (invalid.Length == 0) return;

            e.CancelCommand();
            string clean = new string(text.Where(c => !_invalidChars.Contains(c)).ToArray());
            if (clean.Length > 0)
            {
                int start = _newNameBox.SelectionStart;
                int len   = _newNameBox.SelectionLength;
                _newNameBox.Text       = _newNameBox.Text.Remove(start, len).Insert(start, clean);
                _newNameBox.CaretIndex = start + clean.Length;
            }
            ShowInvalidWarning(invalid);
        }

        // ── Create folder ─────────────────────────────────────────────────────

        private void ExecuteCreate()
        {
            string name = _newNameBox.Text.Trim();
            if (name.Length == 0) return;
            var invalid = CollectInvalid(name);
            if (invalid.Length > 0) { ShowInvalidWarning(invalid); return; }

            string parentRel  = SelectedRel() ?? "";
            string parentFull = parentRel.Length == 0 ? _root : FolderChipsStore.FullPath(parentRel);
            string newFull    = Path.Combine(parentFull, name);
            string newRel     = parentRel.Length == 0 ? name : parentRel + "/" + name;

            if (Directory.Exists(newFull))
            {
                MessageBox.Show($"A folder named \"{name}\" already exists here.",
                    "Create Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Directory.CreateDirectory(newFull); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not create folder:\n" + ex.Message,
                    "Create Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _newNameBox.Clear();
            _pathPreview.Text = "";
            PopulateTree(newRel);
        }

        // ── Delete folder ─────────────────────────────────────────────────────

        private void ExecuteDelete()
        {
            string? rel = SelectedRel();
            if (string.IsNullOrEmpty(rel)) return;

            string fullPath = FolderChipsStore.FullPath(rel);
            bool   hasContents;
            try   { hasContents = Directory.EnumerateFileSystemEntries(fullPath).Any(); }
            catch { hasContents = false; }

            string msg = hasContents
                ? $"Delete folder \"{rel}\" and ALL its contents? This cannot be undone."
                : $"Delete folder \"{rel}\"? This cannot be undone.";

            if (MessageBox.Show(msg, "Delete Folder",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                Directory.Delete(fullPath, recursive: true);
                foreach (var chip in FolderChipsStore.Chips.ToList())
                {
                    if (chip.Equals(rel, StringComparison.OrdinalIgnoreCase) ||
                        chip.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase))
                        FolderChipsStore.RemoveChip(chip);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not delete folder:\n" + ex.Message,
                    "Delete Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PopulateTree(null);
            _okBtn.IsEnabled     = false;
            _deleteBtn.IsEnabled = false;
        }

        // ── Rename folder ─────────────────────────────────────────────────────

        private void OpenRenameSection()
        {
            string? rel = SelectedRel();
            if (string.IsNullOrEmpty(rel) || _renameSection == null || _renameBox == null) return;
            string lastName = rel.Contains('/') ? rel[(rel.LastIndexOf('/') + 1)..] : rel;
            _renameBox.Text           = lastName;
            _renameBox.SelectionStart = 0;
            _renameBox.SelectionLength = lastName.Length;
            if (_renameError != null) _renameError.Visibility = Visibility.Collapsed;
            _renameSection.Visibility = Visibility.Visible;
            _renameBox.Focus();
        }

        private void CollapseRenameSection()
        {
            if (_renameSection != null) _renameSection.Visibility = Visibility.Collapsed;
            if (_renameError  != null) _renameError.Visibility  = Visibility.Collapsed;
        }

        private void ExecuteRename()
        {
            string? oldRel  = SelectedRel();
            string  newName = _renameBox?.Text.Trim() ?? "";
            if (string.IsNullOrEmpty(oldRel) || _renameError == null) return;

            if (newName.Length == 0)
            { _renameError.Text = "Name cannot be empty."; _renameError.Visibility = Visibility.Visible; return; }
            var invalid = CollectInvalid(newName);
            if (invalid.Length > 0)
            { _renameError.Text = $"Not allowed: {FormatInvalidChars(invalid)}"; _renameError.Visibility = Visibility.Visible; return; }

            string parentRel = oldRel.Contains('/') ? oldRel[..oldRel.LastIndexOf('/')] : "";
            string newRel    = parentRel.Length == 0 ? newName : parentRel + "/" + newName;

            var (ok, err) = FolderChipsStore.RenameFolder(oldRel, newRel);
            if (!ok)
            { _renameError.Text = err; _renameError.Visibility = Visibility.Visible; return; }

            CollapseRenameSection();
            PopulateTree(newRel);
        }

        // ── Tree ──────────────────────────────────────────────────────────────

        private void PopulateTree(string? selectRel)
        {
            _tree.Items.Clear();
            var rootItem = new TreeViewItem
            {
                Header     = "📁  Saved (base)",
                Tag        = "",
                IsExpanded = true
            };
            AddChildren(rootItem, _root);
            _tree.Items.Add(rootItem);

            if (!string.IsNullOrEmpty(selectRel))
                SelectByRel(rootItem, selectRel);
        }

        private void AddChildren(TreeViewItem parent, string dir)
        {
            string[] subs;
            try   { subs = Directory.GetDirectories(dir); }
            catch { return; }

            foreach (var sub in subs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string rel  = Path.GetRelativePath(_root, sub).Replace('\\', '/');
                var    item = new TreeViewItem
                {
                    Header = "📁  " + Path.GetFileName(sub),
                    Tag    = rel
                };
                AddChildren(item, sub);
                parent.Items.Add(item);
            }
        }

        private bool SelectByRel(TreeViewItem node, string rel)
        {
            if ((node.Tag as string) == rel)
            {
                node.IsSelected = true;
                node.BringIntoView();
                return true;
            }
            foreach (TreeViewItem child in node.Items)
            {
                if (SelectByRel(child, rel)) { node.IsExpanded = true; return true; }
            }
            return false;
        }
    }
}
