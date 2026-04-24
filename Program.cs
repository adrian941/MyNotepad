using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace MinimalNotepad
{
    class Program
    {
        class AppSettings
        {
            public double WindowLeft { get; set; } = 100;
            public double WindowTop { get; set; } = 100;
            public double WindowWidth { get; set; } = 800;
            public double WindowHeight { get; set; } = 600;
            public double FontSize { get; set; } = 12;
        }

        [STAThread]
        static void Main()
        {
            var app = new Application();
            var windows = new List<Window>();
            string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            AppSettings settings;

            try
            {
                if (File.Exists(settingsFile))
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile)) ?? new AppSettings();
                else
                    settings = new AppSettings();
            }
            catch { settings = new AppSettings(); }

            void OpenNewWindow(double offsetX = -1, double offsetY = -1)
            {
                string prefixTitle = "";
                var window = new Window
                {
                    Title = $"{prefixTitle}Minimal Notepad",
                    Width = settings.WindowWidth,
                    Height = settings.WindowHeight,
                    Background = Brushes.White,
                    Left = offsetX >= 0 ? offsetX : settings.WindowLeft,
                    Top = offsetY >= 0 ? offsetY : settings.WindowTop,
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/notepad.ico"))
                };

                var dock = new DockPanel();

                var border = new Border
                {
                    Height = 1,
                    Background = Brushes.Gray
                };
                DockPanel.SetDock(border, Dock.Top);
                dock.Children.Add(border);

                var editor = new TextEditor
                {
                    ShowLineNumbers = false,
                    WordWrap = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = settings.FontSize,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8)
                };
                editor.TextChanged += (s, e) => 
                { 
                    editor.TextArea.Caret.BringCaretToView();

                    if (editor.Text.Length > 80000)
                    {
                        editor.Dispatcher.BeginInvoke(new Action(() => editor.Clear()));
                    }

                };
                dock.Children.Add(editor);

                editor.TextArea.Caret.PositionChanged += (s, e) =>
                {
                    var caret = editor.TextArea.Caret;
                    string viewPrefixTitle = string.IsNullOrEmpty(prefixTitle) ? "" : prefixTitle + " - ";
                    window.Title = $"{viewPrefixTitle}Minimal Notepad - Ln {caret.Line}, Col {caret.Column}";
                };

                editor.PreviewMouseWheel += (s, e) =>
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        editor.FontSize += e.Delta > 0 ? 1 : -1;
                        settings.FontSize = editor.FontSize;
                        e.Handled = true;
                    }
                };

                editor.PreviewKeyDown += (s, e) =>
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        if (e.Key == Key.S || e.Key == Key.T)
                        {
                            e.Handled = true;

                            var inputWindow = new Window
                            {
                                Title = "Introdu titlul:",
                                Width = 300,
                                Height = 120,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                ResizeMode = ResizeMode.NoResize,
                                Owner = window,
                                Topmost = true,
                                Background = Brushes.White
                            };

                            var mainStack = new StackPanel
                            {
                                Margin = new Thickness(10),
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };

                            var textBox = new TextBox
                            {
                                Text = prefixTitle,
                                Margin = new Thickness(0, 0, 0, 10)
                            };

                            var buttonPanel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right
                            };

                            var okButton = new Button
                            {
                                Content = "OK",
                                Width = 60
                            };

                            void ApplyTitle()
                            {
                                prefixTitle = textBox.Text.Trim();
                                string viewPrefixTitle = string.IsNullOrEmpty(prefixTitle) ? "" : prefixTitle + " - ";
                                window.Title = $"{viewPrefixTitle}Minimal Notepad - Ln {editor.TextArea.Caret.Line}, Col {editor.TextArea.Caret.Column}";
                                inputWindow.Close();
                            }

                            okButton.Click += (s2, e2) => ApplyTitle();

                            textBox.PreviewKeyDown += (s3, e3) =>
                            {
                                if (e3.Key == Key.Enter)
                                {
                                    ApplyTitle();
                                    e3.Handled = true;
                                }
                                else if (e3.Key == Key.Escape)
                                {
                                    inputWindow.Close();
                                    e3.Handled = true;
                                }
                            };

                            buttonPanel.Children.Add(okButton);
                            mainStack.Children.Add(textBox);
                            mainStack.Children.Add(buttonPanel);
                            inputWindow.Content = mainStack;

                            // Focus după încărcare
                            inputWindow.Loaded += (s4, e4) =>
                            {
                                textBox.Focus();
                                textBox.SelectAll();
                            };

                            inputWindow.Show(); // NON-MODAL → nu blochează alte ferestre
                        }

                        if (e.Key == Key.N)
                        {
                            OpenNewWindow(window.Left + 30, window.Top + 30);
                            e.Handled = true;

                            var newWindow = windows[^1];
                            newWindow.Activate();
                            if (newWindow.Content is DockPanel dockNew)
                            {
                                foreach (var child in dockNew.Children)
                                {
                                    if (child is TextEditor ed)
                                    {
                                        ed.Focus();
                                        ed.TextArea.Caret.BringCaretToView();
                                        break;
                                    }
                                }
                            }
                        }
                        else if (e.Key == Key.OemPlus || e.Key == Key.Add)
                        {
                            editor.FontSize += 1;
                            settings.FontSize = editor.FontSize;
                            e.Handled = true;
                        }
                        else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                        {
                            editor.FontSize = Math.Max(6, editor.FontSize - 1);
                            settings.FontSize = editor.FontSize;
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.Space)
                    {
                        e.Handled = true;
                        int offset = editor.CaretOffset;
                        string fakeSpace = "\u00A0"; // non-breaking space
                        editor.Document.Insert(offset, fakeSpace);
                        editor.CaretOffset = offset + fakeSpace.Length;
                    }
                };

                window.Content = dock;

                window.Closed += (s, e) =>
                {
                    settings.WindowLeft = window.Left;
                    settings.WindowTop = window.Top;
                    settings.WindowWidth = window.Width;
                    settings.WindowHeight = window.Height;
                    settings.FontSize = editor.FontSize;
                    File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                    windows.Remove(window);
                };

                windows.Add(window);
                window.Show();
            }

            OpenNewWindow();
            app.Run();
        }
    }
}
