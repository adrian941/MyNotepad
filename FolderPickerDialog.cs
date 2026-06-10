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
    /// <summary>
    /// Modal folder picker rooted at the Saved library folder. Lets the user
    /// choose any existing subfolder (returned as a '/'-separated relative path).
    /// </summary>
    internal sealed class FolderPickerDialog : Window
    {
        private readonly string _root;
        private readonly TreeView _tree;
        private readonly Button _okBtn;

        public string? SelectedRelative { get; private set; }

        public FolderPickerDialog(Window owner)
        {
            _root = System.IO.Path.GetFullPath(FolderChipsStore.Root);

            Owner                 = owner;
            Title                 = "Choose folder";
            Width                 = 340;
            Height                = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode            = ResizeMode.CanResize;
            ShowInTaskbar         = false;
            Background            = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF7));

            var root = new DockPanel { LastChildFill = true, Margin = new Thickness(10) };

            // ── Header ─────────────────────────────────────────────────────
            var header = new TextBlock
            {
                Text       = "Select a subfolder to add as a chip",
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x72)),
                Margin     = new Thickness(2, 0, 0, 8)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ── OK / Cancel (bottom) ───────────────────────────────────────
            _okBtn = new Button
            {
                Content   = "Add Folder",
                Height    = 28,
                Width     = 90,
                Margin    = new Thickness(6, 10, 0, 0),
                Cursor    = Cursors.Hand,
                IsEnabled = false,
                IsDefault = true
            };
            var cancelBtn = new Button
            {
                Content  = "Cancel",
                Height   = 28,
                Width    = 80,
                Margin   = new Thickness(0, 10, 0, 0),
                Cursor   = Cursors.Hand,
                IsCancel = true
            };
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(_okBtn);
            DockPanel.SetDock(btnRow, Dock.Bottom);
            root.Children.Add(btnRow);

            // ── Tree (fills) ───────────────────────────────────────────────
            _tree = new TreeView { BorderThickness = new Thickness(1), Padding = new Thickness(4) };
            root.Children.Add(_tree);

            Content = root;

            PopulateTree(null);

            _tree.SelectedItemChanged += (_, _) =>
            {
                _okBtn.IsEnabled = SelectedRel() is { Length: > 0 };
            };

            _okBtn.Click += (_, _) =>
            {
                var rel = SelectedRel();
                if (string.IsNullOrEmpty(rel)) return;
                SelectedRelative = rel;
                DialogResult = true;
            };

        }

        // Relative path of the currently selected node ("" for the root node).
        private string? SelectedRel() =>
            (_tree.SelectedItem as TreeViewItem)?.Tag as string;

        private void CreateFolder(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("Invalid folder name.", "New folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string parentRel = SelectedRel() ?? "";
            string parentFull = parentRel.Length == 0 ? _root : FolderChipsStore.FullPath(parentRel);
            string newRel = (parentRel.Length == 0 ? name : parentRel + "/" + name);
            try
            {
                Directory.CreateDirectory(System.IO.Path.Combine(parentFull, name));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not create folder:\n" + ex.Message, "New folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PopulateTree(newRel);
        }

        // Rebuild the tree; optionally select+reveal the node with the given relative path.
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
            try { subs = Directory.GetDirectories(dir); }
            catch { return; }

            foreach (var sub in subs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string rel = System.IO.Path.GetRelativePath(_root, sub).Replace('\\', '/');
                var item = new TreeViewItem
                {
                    Header = "📁  " + System.IO.Path.GetFileName(sub),
                    Tag    = rel
                };
                AddChildren(item, sub);
                parent.Items.Add(item);
            }
        }

        // Expand+select the item whose Tag == rel (depth-first).
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
