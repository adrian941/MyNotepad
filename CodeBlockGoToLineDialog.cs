using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MinimalNotepad
{
    class CodeBlockGoToLineDialog : Window
    {
        private readonly TextBox _lineBox;
        private readonly int     _maxLine;

        public int? ResultLine { get; private set; }

        public CodeBlockGoToLineDialog(int maxLine, Window owner)
        {
            _maxLine = maxLine;
            Owner    = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode    = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title         = "Go to Line";
            ShowInTaskbar = false;

            _lineBox = new TextBox
            {
                Width    = 72,
                MinHeight = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding  = new Thickness(3, 1, 3, 1),
            };

            // Only allow digit characters
            _lineBox.PreviewTextInput += (_, e) =>
                e.Handled = e.Text.Length == 0 || !char.IsDigit(e.Text, 0);

            // Block Space
            _lineBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Space) e.Handled = true;
            };

            // Block non-numeric pastes
            DataObject.AddPastingHandler(_lineBox, (_, e) =>
            {
                if (e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    string t = (e.DataObject.GetData(DataFormats.Text) as string) ?? "";
                    if (!int.TryParse(t.Trim(), out int _dummy)) e.CancelCommand();
                }
                else e.CancelCommand();
            });

            var okBtn = new Button
            {
                Content   = "OK",
                Width     = 52,
                IsDefault = true,
                Margin    = new Thickness(8, 0, 0, 0),
            };
            okBtn.Click += (_, _) => TryConfirm();

            var label = new TextBlock
            {
                Text              = $"Linie (1 – {maxLine}):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(12),
            };
            row.Children.Add(label);
            row.Children.Add(_lineBox);
            row.Children.Add(okBtn);
            Content = row;

            Loaded  += (_, _) => _lineBox.Focus();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            };
        }

        void TryConfirm()
        {
            if (int.TryParse(_lineBox.Text, out int n) && n >= 1 && n <= _maxLine)
            {
                ResultLine   = n;
                DialogResult = true;
            }
            else
            {
                _lineBox.BorderBrush = Brushes.Red;
                _lineBox.SelectAll();
                _lineBox.Focus();
            }
        }
    }
}
