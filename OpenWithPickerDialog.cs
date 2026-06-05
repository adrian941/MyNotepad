using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinimalNotepad
{
    /// <summary>
    /// Shown after the user picks an .exe via OpenFileDialog.
    /// Lets them confirm the open action and optionally set the program as default.
    /// </summary>
    class OpenWithPickerDialog : Window
    {
        public bool SetAsDefault { get; private set; }

        public OpenWithPickerDialog(string programName, Window owner)
        {
            Owner = owner;
            Title = "Open with";
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var label = new TextBlock
            {
                Text        = $"Open with:  {programName}",
                FontSize    = 13,
                FontWeight  = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth    = 340,
            };

            var checkBox = new CheckBox
            {
                Content = "Set as default for .mnp files in MyNotepad",
                Margin  = new Thickness(0, 12, 0, 0),
                FontSize = 12,
            };

            var okBtn = new Button
            {
                Content   = "Open",
                IsDefault = true,
                Width     = 72,
                Margin    = new Thickness(0, 18, 8, 0),
            };
            var cancelBtn = new Button
            {
                Content  = "Cancel",
                IsCancel = true,
                Width    = 72,
                Margin   = new Thickness(0, 18, 0, 0),
            };

            okBtn.Click += (_, _) =>
            {
                SetAsDefault = checkBox.IsChecked == true;
                DialogResult = true;
            };

            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            btnRow.Children.Add(okBtn);
            btnRow.Children.Add(cancelBtn);

            var stack = new StackPanel
            {
                Margin   = new Thickness(20, 18, 20, 18),
                MinWidth = 300,
            };
            stack.Children.Add(label);
            stack.Children.Add(checkBox);
            stack.Children.Add(btnRow);

            Content = stack;

            Loaded += (_, _) => okBtn.Focus();
        }
    }
}
