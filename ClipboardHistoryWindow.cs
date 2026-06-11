using MinimalNotepad.Formatting;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MinimalNotepad;

internal class ClipboardHistoryWindow : Window
{
    private enum HistoryMode { App, Global, Files }

    private static ClipboardHistoryWindow? _instance;

    private NotepadWindow _targetWindow;
    private StackPanel _cardsPanel = null!;
    private WrapPanel _chipsPanel = null!;
    private Border _chipsHost = null!;
    private bool _suppressDeactivationHwndCapture;
    private HistoryMode _mode = HistoryMode.App;
    private bool _singleLineMode = false;
    private TextBlock _lineToggleIcon = null!;
    private Border _appTab = null!;
    private Border _sysTab = null!;
    private Border _filesTab = null!;
    private IntPtr _externalFocusHwnd = IntPtr.Zero;
    private IntPtr _ownHwnd = IntPtr.Zero;
    private bool _isPasting = false;
    private WinEventDelegate? _foregroundEventDelegate;
    private IntPtr _foregroundEventHook = IntPtr.Zero;

    // ── Keyboard navigation + undo ────────────────────────────────────────
    private int _selectedIndex = -1;
    private readonly Stack<(HistoryMode Mode, int Index, object Entry, string? FullPath)> _undoStack = new();
    private readonly List<(Border Card, Brush NormalBg, Action OnActivate, Action OnDelete, object Entry, string? FullPath)> _cardList = new();

    private static readonly Brush _selCardBorder  = new SolidColorBrush(Color.FromRgb(0x55, 0x88, 0xCC));
    private static readonly Brush _normCardBorder = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    // ── Chip keyboard focus (Files mode) ──────────────────────────────────
    private int _chipFocusIndex = -1;
    private static readonly Brush _chipFocusBorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

    // ── Chip drag-and-drop ────────────────────────────────────────────────
    private int   _dragSrcChipIdx  = -1;
    private int   _dragTargetIdx   = -1;
    private bool  _isDraggingChip  = false;
    private Point _dragOriginPt;
    private readonly List<(Border El, int ChipsIdx)> _chipElsForDrag = new();
    private readonly List<(int ChipsIdx, double CenterX)> _chipCenterXs = new();

    // ── Delete toast (undo snackbar) ──────────────────────────────────────
    private Border _toastBar = null!;
    private System.Windows.Threading.DispatcherTimer? _toastTimer;

    // ── Win32 P/Invoke ────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr hmod,
                                                       WinEventDelegate fn, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hHook);
    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
                                   int obj, int child, uint thread, uint time);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        // KEYBDINPUT fields inside the union (union starts at offset 8 on 64-bit)
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint kbdFlags;
        [FieldOffset(16)] public uint kbdTime;
        [FieldOffset(24)] public IntPtr kbdExtra;
    }
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;
    private const ushort VK_CONTROL = 0x11, VK_V = 0x56;

    // ── Singleton ─────────────────────────────────────────────────────────

    public static void ShowOrActivate(NotepadWindow target)
    {
        if (_instance != null)
        {
            _instance._targetWindow = target;
            _instance.CaptureExternalHwnd();
            _instance.RefreshCards();
            _instance.Activate();
            return;
        }

        _instance = new ClipboardHistoryWindow(target);
        _instance.CaptureExternalHwnd();
        _instance.Show();
    }

    public static void ShowOrActivateClipboard(NotepadWindow target)
    {
        if (_instance != null)
        {
            _instance._targetWindow = target;
            _instance.CaptureExternalHwnd();
            if (_instance._mode == HistoryMode.Files)
                _instance.SwitchToAppMode();
            _instance.Activate();
            return;
        }

        _instance = new ClipboardHistoryWindow(target);
        _instance.CaptureExternalHwnd();
        if (_instance._mode == HistoryMode.Files)
            _instance.SwitchToAppMode();
        _instance.Show();
    }

    public static void ShowOrActivateFiles(NotepadWindow target)
    {
        if (_instance != null)
        {
            _instance._targetWindow = target;
            _instance.SwitchToFilesMode();
            _instance.Activate();
            return;
        }

        _instance = new ClipboardHistoryWindow(target);
        _instance.SwitchToFilesMode();
        _instance.Show();
    }

    private void CaptureExternalHwnd()
    {
        // Called when user opens history from a NotepadWindow (keyboard shortcut).
        // Reset so paste goes to _targetWindow, not a stale external window.
        _externalFocusHwnd = IntPtr.Zero;
    }

    private void SwitchToAppMode()
    {
        if (_mode == HistoryMode.App) return;
        if (_mode == HistoryMode.Global) NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
        if (_mode == HistoryMode.Files) SavedFileStore.SavedFilesChanged -= OnHistoryChanged;
        _mode = HistoryMode.App;
        ClipboardHistory.HistoryChanged += OnHistoryChanged;
        Title = "Clipboard History";
        UpdateActiveTab();
        RefreshCards();
    }

    private void SwitchToGlobalMode()
    {
        if (_mode == HistoryMode.Global) return;
        if (_mode == HistoryMode.App)  ClipboardHistory.HistoryChanged      -= OnHistoryChanged;
        if (_mode == HistoryMode.Files) SavedFileStore.SavedFilesChanged     -= OnHistoryChanged;
        _mode = HistoryMode.Global;
        NormalClipboardHistory.HistoryChanged += OnHistoryChanged;
        Title = "Clipboard History — System";
        UpdateActiveTab();
        RefreshCards();
    }

    private void SwitchToFilesMode()
    {
        if (_mode == HistoryMode.Files) return;
        if (_mode == HistoryMode.App) ClipboardHistory.HistoryChanged -= OnHistoryChanged;
        if (_mode == HistoryMode.Global) NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
        _mode = HistoryMode.Files;
        SavedFileStore.SavedFilesChanged += OnHistoryChanged;
        Title = "Saved Files";
        UpdateActiveTab();
        RefreshCards();
    }

    private void CycleTab(int direction)
    {
        _chipFocusIndex = -1;
        var modes = new System.Collections.Generic.List<HistoryMode> { HistoryMode.App };
        if (GlobalClipboardMonitor.IsEnabled) modes.Add(HistoryMode.Global);
        modes.Add(HistoryMode.Files);

        int cur  = modes.IndexOf(_mode);
        if (cur < 0) cur = 0;
        int next = (cur + direction + modes.Count) % modes.Count;

        _selectedIndex = 0; // reset to first card in new tab
        switch (modes[next])
        {
            case HistoryMode.App:    SwitchToAppMode();    break;
            case HistoryMode.Global: SwitchToGlobalMode(); break;
            case HistoryMode.Files:  SwitchToFilesMode();  break;
        }
    }

    // ── Constructor ───────────────────────────────────────────────────────

    private ClipboardHistoryWindow(NotepadWindow target)
    {
        _targetWindow = target;

        MinWidth = 385;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        ShowInTaskbar = false;

        // ── Restore last position / size ──────────────────────────────────
        var state = LoadWindowState();
        Width = state.Width;
        Height = state.Height;
        _singleLineMode = state.SingleLine;
        _mode = state.ViewMode;
        Title = _mode switch
        {
            HistoryMode.Global => "Clipboard History — System",
            HistoryMode.Files => "Saved Files",
            _ => "Clipboard History"
        };
        if (state.HasPosition)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = state.Left;
            Top = state.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // ── Slim scrollbar style (5 px, rounded thumb, no arrows) ─────────
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width'     Value='5'/>
    <Setter Property='MinWidth'  Value='5'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ScrollBar}'>
          <Grid Background='Transparent' Width='5'>
            <Track x:Name='PART_Track' IsDirectionReversed='true'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand'
                              Opacity='0' Focusable='false' Height='0'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'>
                      <Border x:Name='Bd' Background='#C8C8C8'
                              CornerRadius='2.5' Margin='1,3,1,3'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='Bd' Property='Background' Value='#999999'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='Bd' Property='Background' Value='#777777'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand'
                              Opacity='0' Focusable='false' Height='0'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>"));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 10, 10, 10)
        };

        _cardsPanel = new StackPanel();
        scroll.Content = _cardsPanel;
        scroll.MouseRightButtonUp += (_, e) =>
        {
            if (_mode != HistoryMode.Files || e.Handled) return;
            e.Handled = true;
            string? active = FolderChipsStore.Active;
            string folder  = active != null ? FolderChipsStore.FullPath(active) : SavedFileStore.SavedFolder;
            var menu = new ContextMenu { FontSize = 12 };
            var ni   = new MenuItem { Header = "    New .mnp file here" };
            ni.Click += (_, _) => ShowNewFileDialog(folder);
            menu.Items.Add(ni);
            menu.Items.Add(BuildSortSubmenu(active));
            menu.PlacementTarget = scroll;
            menu.Placement       = PlacementMode.MousePoint;
            menu.IsOpen          = true;
        };

        // ── Footer: open folder · clear all · [App][System][Files] · ☰ ────
        var openFolderLink = MakeFooterLink("📂  Open history folder");
        openFolderLink.MouseLeftButtonUp += (_, _) =>
        {
            string dir = _mode == HistoryMode.Files
                ? SavedFileStore.SavedFolder
                : System.IO.Path.GetDirectoryName(
                    _mode == HistoryMode.App
                        ? ClipboardHistory.SavePath
                        : NormalClipboardHistory.SavePath)!;
            if (System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        };

        var clearAllLink = MakeFooterLink("🗑  Clear all");
        clearAllLink.MouseLeftButtonUp += (_, _) =>
        {
            _suppressDeactivationHwndCapture = true;
            if (_mode == HistoryMode.Files)
            {
                var result = MessageBox.Show(
                    "Delete all saved files?",
                    "Clear Saved Files",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                _suppressDeactivationHwndCapture = false;
                if (result == MessageBoxResult.Yes)
                    SavedFileStore.DeleteAll();
            }
            else
            {
                var result = MessageBox.Show(
                    "Are you sure you want to clear the entire clipboard history?",
                    "Clear History",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                _suppressDeactivationHwndCapture = false;
                if (result == MessageBoxResult.Yes)
                {
                    if (_mode == HistoryMode.App) ClipboardHistory.ClearAll();
                    else NormalClipboardHistory.ClearAll();
                }
            }
            Activate();
        };

        // ── Mode tab buttons ───────────────────────────────────────────────
        _appTab = MakeTabButton("App", Color.FromRgb(0x44, 0x72, 0xC4));
        _sysTab = MakeTabButton("System", Color.FromRgb(0x71, 0x53, 0xC4));
        _filesTab = MakeTabButton("Files", Color.FromRgb(0x2E, 0x7D, 0x32));

        _appTab.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == HistoryMode.App) return;
            if (_mode == HistoryMode.Global) NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
            _mode = HistoryMode.App;
            ClipboardHistory.HistoryChanged += OnHistoryChanged;
            Title = "Clipboard History";
            UpdateActiveTab();
            RefreshCards();
        };
        _sysTab.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == HistoryMode.Global) return;
            if (_mode == HistoryMode.App) ClipboardHistory.HistoryChanged -= OnHistoryChanged;
            _mode = HistoryMode.Global;
            NormalClipboardHistory.HistoryChanged += OnHistoryChanged;
            Title = "Clipboard History — System";
            UpdateActiveTab();
            RefreshCards();
        };
        _filesTab.MouseLeftButtonUp += (_, _) =>
        {
            if (_mode == HistoryMode.Files) return;
            if (_mode == HistoryMode.App) ClipboardHistory.HistoryChanged -= OnHistoryChanged;
            if (_mode == HistoryMode.Global) NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
            _mode = HistoryMode.Files;
            Title = "Saved Files";
            UpdateActiveTab();
            RefreshCards();
        };

        UpdateActiveTab();

        var tabRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        tabRow.Children.Add(_appTab);
        tabRow.Children.Add(_sysTab);
        tabRow.Children.Add(_filesTab);

        var dot1 = MakeDot();
        var dot2 = MakeDot();
        var dot3 = MakeDot();

        _lineToggleIcon = new TextBlock
        {
            Text = _singleLineMode ? "⊟" : "☰",
            FontSize = 13,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            ToolTip = _singleLineMode ? "Switch to multi-line view" : "Switch to single-line view"
        };
        _lineToggleIcon.MouseEnter += (_, _) =>
            _lineToggleIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        _lineToggleIcon.MouseLeave += (_, _) =>
            _lineToggleIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        _lineToggleIcon.MouseLeftButtonUp += (_, _) =>
        {
            _singleLineMode = !_singleLineMode;
            _lineToggleIcon.Text = _singleLineMode ? "⊟" : "☰";
            _lineToggleIcon.ToolTip = _singleLineMode ? "Switch to multi-line view" : "Switch to single-line view";
            RefreshCards();
        };

        var footerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 5, 12, 9)
        };
        footerRow.Children.Add(openFolderLink);
        footerRow.Children.Add(dot1);
        footerRow.Children.Add(clearAllLink);
        footerRow.Children.Add(dot2);
        footerRow.Children.Add(tabRow);
        footerRow.Children.Add(dot3);
        footerRow.Children.Add(_lineToggleIcon);

        // Hide the System tab when global monitoring is off
        void UpdateToggleVisibility()
        {
            bool show = GlobalClipboardMonitor.IsEnabled;
            _sysTab.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show && _mode == HistoryMode.Global)
            {
                NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
                _mode = HistoryMode.App;
                ClipboardHistory.HistoryChanged += OnHistoryChanged;
                Title = "Clipboard History";
                UpdateActiveTab();
                RefreshCards();
            }
        }
        UpdateToggleVisibility();
        GlobalClipboardMonitor.EnabledChanged += _ =>
            Dispatcher.InvokeAsync(UpdateToggleVisibility);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
        };

        // ── Folder chips strip (pinned above the cards, Files mode only) ──
        var chipsHost = BuildChipsHost();

        var outerDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(separator, Dock.Bottom);
        DockPanel.SetDock(footerRow, Dock.Bottom);
        DockPanel.SetDock(chipsHost, Dock.Top);
        outerDock.Children.Add(separator);
        outerDock.Children.Add(footerRow);
        outerDock.Children.Add(chipsHost);
        outerDock.Children.Add(scroll);

        // ── Toast overlay (delete undo snackbar) ──────────────────────────
        _toastBar = BuildToastBar();
        var contentGrid = new Grid();
        contentGrid.Children.Add(outerDock);
        contentGrid.Children.Add(_toastBar);
        Content = contentGrid;

        RefreshChips();
        RefreshCards();

        // Live updates — subscribe based on active mode at startup
        if (_mode == HistoryMode.App)
            ClipboardHistory.HistoryChanged += OnHistoryChanged;
        else if (_mode == HistoryMode.Global)
            NormalClipboardHistory.HistoryChanged += OnHistoryChanged;
        SavedFileStore.SavedFilesChanged += OnHistoryChanged;

        // Track which external window was last in the foreground
        _foregroundEventDelegate = OnForegroundWindowChanged;
        const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
        _foregroundEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        Loaded += (_, _) =>
        {
            _ownHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Focus();
        };

        Closed += (_, _) =>
        {
            if (_foregroundEventHook != IntPtr.Zero) UnhookWinEvent(_foregroundEventHook);
            SaveWindowState(Left, Top, Width, Height, _singleLineMode, _mode);
            ClipboardHistory.HistoryChanged -= OnHistoryChanged;
            NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
            SavedFileStore.SavedFilesChanged -= OnHistoryChanged;
            _undoStack.Clear();
            _instance = null;
        };

        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    // ── Foreground window tracking (for external paste) ───────────────────

    // ── Foreground window tracking (for external paste + target window) ─────

    private void OnForegroundWindowChanged(IntPtr hook, uint evt, IntPtr hwnd,
                                   int obj, int child, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || _suppressDeactivationHwndCapture) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (pid != ourPid)
        {
            // External app focused → remember it for paste
            _externalFocusHwnd = hwnd;
        }
        else if (hwnd != _ownHwnd)
        {
            // One of our NotepadWindows got focus → clear external, update _targetWindow
            _externalFocusHwnd = IntPtr.Zero;
            // Find which NotepadWindow matches this HWND and make it the paste target
            Dispatcher.Invoke(() =>
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is NotepadWindow nw)
                    {
                        var helper = new System.Windows.Interop.WindowInteropHelper(nw);
                        if (helper.Handle == hwnd)
                        {
                            _targetWindow = nw;
                            break;
                        }
                    }
                }
            });
        }
        // else: ClipboardHistoryWindow itself got focus → keep state unchanged
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnHistoryChanged() => RefreshCards();

    // ── Folder chips (Files mode) ─────────────────────────────────────────

    private Border BuildChipsHost()
    {
        _chipsPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _chipsPanel.PreviewMouseMove           += OnChipsPanelMouseMove;
        _chipsPanel.PreviewMouseLeftButtonUp   += OnChipsPanelMouseUp;
        _chipsHost = new Border
        {
            Background       = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
            BorderBrush      = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            BorderThickness  = new Thickness(0, 0, 0, 1),
            Padding          = new Thickness(8, 7, 8, 3),
            Child            = _chipsPanel,
            Visibility       = _mode == HistoryMode.Files ? Visibility.Visible : Visibility.Collapsed
        };
        return _chipsHost;
    }

    private void RefreshChips()
    {
        if (_chipsPanel == null) return;
        _chipsPanel.Children.Clear();
        _chipsPanel.Children.Add(BuildMainChip());
        int idx = 0;
        foreach (var rel in FolderChipsStore.Chips)
        {
            int capturedIdx = idx++;
            string capturedRel = rel;
            var chip = BuildChip(rel);
            chip.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragSrcChipIdx = capturedIdx;
                _dragOriginPt   = e.GetPosition(_chipsPanel);
                _isDraggingChip = false;
            };
            chip.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowChipContextMenu(chip, capturedRel);
            };
            _chipsPanel.Children.Add(chip);
        }
        _chipsPanel.Children.Add(BuildAddChip());
        ReapplyChipFocusVisual();
    }

    private void ReapplyChipFocusVisual()
    {
        if (_chipFocusIndex < 0 || _chipFocusIndex >= _chipsPanel.Children.Count) return;
        if (_chipsPanel.Children[_chipFocusIndex] is Border chip)
        {
            chip.BorderBrush     = _chipFocusBorderBrush;
            chip.BorderThickness = new Thickness(2);
        }
    }

    // ── Chip drag-and-drop ────────────────────────────────────────────────

    private void OnChipsPanelMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSrcChipIdx < 0) return;
        var pos = e.GetPosition(_chipsPanel);
        if (!_isDraggingChip)
        {
            if (Math.Abs(pos.X - _dragOriginPt.X) > 5 || Math.Abs(pos.Y - _dragOriginPt.Y) > 5)
            {
                _isDraggingChip = true;
                CaptureDragHitTestPositions();
                Mouse.Capture(_chipsPanel);
            }
            else return;
        }
        int newTarget = FindChipDropTarget(pos);
        if (newTarget != _dragTargetIdx)
        {
            _dragTargetIdx = newTarget;
            RebuildChipsForDrag();
        }
    }

    private void OnChipsPanelMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragSrcChipIdx < 0) return;
        bool wasDragging = _isDraggingChip;
        int src = _dragSrcChipIdx;
        int tgt = _dragTargetIdx;

        _dragSrcChipIdx = -1;
        _dragTargetIdx  = -1;
        _isDraggingChip = false;
        _chipElsForDrag.Clear();
        _chipCenterXs.Clear();

        if (Mouse.Captured == _chipsPanel)
            Mouse.Capture(null);

        if (wasDragging)
        {
            e.Handled = true;
            if (src >= 0 && tgt >= 0 && tgt != src && tgt != src + 1)
                FolderChipsStore.MoveChip(src, tgt);
        }
        RefreshChips();
    }

    private void CaptureDragHitTestPositions()
    {
        _chipCenterXs.Clear();
        var chips = FolderChipsStore.Chips;
        // WrapPanel children: [Main=0][chip0=1][chip1=2]...[chipN=N][+]
        for (int i = 0; i < chips.Count; i++)
        {
            if (i + 1 >= _chipsPanel.Children.Count) break;
            if (_chipsPanel.Children[i + 1] is FrameworkElement el)
            {
                var center = el.TranslatePoint(new Point(el.ActualWidth / 2, 0), _chipsPanel);
                _chipCenterXs.Add((i, center.X));
            }
        }
    }

    private int FindChipDropTarget(Point pos)
    {
        for (int i = 0; i < _chipCenterXs.Count; i++)
        {
            if (pos.X < _chipCenterXs[i].CenterX) return _chipCenterXs[i].ChipsIdx;
        }
        return _chipCenterXs.Count > 0 ? _chipCenterXs[^1].ChipsIdx + 1 : 0;
    }

    private void RebuildChipsForDrag()
    {
        _chipElsForDrag.Clear();
        _chipsPanel.Children.Clear();
        _chipsPanel.Children.Add(BuildMainChip());
        var chips = FolderChipsStore.Chips;
        for (int i = 0; i <= chips.Count; i++)
        {
            if (i == _dragTargetIdx && i != _dragSrcChipIdx && i != _dragSrcChipIdx + 1)
                _chipsPanel.Children.Add(new Border
                {
                    Width             = 2,
                    Background        = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4)),
                    CornerRadius      = new CornerRadius(1),
                    Margin            = new Thickness(0, 2, 4, 5),
                    MinHeight         = 16,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible  = false
                });
            if (i < chips.Count)
            {
                var chip = BuildChip(chips[i]);
                if (i == _dragSrcChipIdx) chip.Opacity = 0.4;
                _chipElsForDrag.Add((chip, i));
                _chipsPanel.Children.Add(chip);
            }
        }
        _chipsPanel.Children.Add(BuildAddChip());
        ReapplyChipFocusVisual();
    }

    // ── Chip right-click context menu ─────────────────────────────────────

    private void ShowChipContextMenu(Border chip, string rel)
    {
        var menu = new ContextMenu { FontSize = 12 };

        var newFileItem = new MenuItem { Header = "    New .mnp file here" };
        newFileItem.Click += (_, _) => ShowNewFileDialog(FolderChipsStore.FullPath(rel));
        menu.Items.Add(newFileItem);
        menu.Items.Add(BuildSortSubmenu(rel));

        menu.Items.Add(new Separator());

        var renameItem = new MenuItem { Header = "    Rename folder…" };
        renameItem.Click += (_, _) => ShowChipRenameDialog(rel);
        menu.Items.Add(renameItem);

        var explorerItem = new MenuItem { Header = "    Open in Explorer" };
        explorerItem.Click += (_, _) =>
        {
            string fullPath = FolderChipsStore.FullPath(rel);
            if (Directory.Exists(fullPath))
                System.Diagnostics.Process.Start("explorer.exe", fullPath);
        };
        menu.Items.Add(explorerItem);

        menu.PlacementTarget = chip;
        menu.Placement       = PlacementMode.Bottom;
        menu.IsOpen          = true;
    }

    private void ShowChipRenameDialog(string rel)
    {
        string lastName  = rel.Contains('/') ? rel[(rel.LastIndexOf('/') + 1)..] : rel;
        string parentRel = rel.Contains('/') ? rel[..rel.LastIndexOf('/')] : "";
        var    badChars  = System.IO.Path.GetInvalidFileNameChars();

        _suppressDeactivationHwndCapture = true;
        try
        {
            var dlg = new Window
            {
                Title                 = "Rename Folder",
                Width                 = 320,
                SizeToContent         = SizeToContent.Height,
                Owner                 = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                ShowInTaskbar         = false,
                Background            = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF7))
            };

            var nameBox = new TextBox
            {
                Text                     = lastName,
                Height                   = 26,
                FontSize                 = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                SelectionStart           = 0,
                SelectionLength          = lastName.Length
            };
            var errorTb = MakeErrorLabel();
            var renameBtn = new Button
            {
                Content   = "Rename",
                Width     = 80,
                Height    = 26,
                IsDefault = true,
                Cursor    = Cursors.Hand,
                Margin    = new Thickness(8, 0, 0, 0)
            };
            var cancelBtn = new Button
            {
                Content    = "Cancel",
                Width      = 70,
                Height     = 26,
                IsCancel   = true,
                Cursor     = Cursors.Hand
            };
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 8, 0, 0)
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(renameBtn);

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text       = $"New name for \"{lastName}\":",
                FontSize   = 11,
                Margin     = new Thickness(0, 0, 0, 5),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44))
            });
            stack.Children.Add(nameBox);
            stack.Children.Add(errorTb);
            stack.Children.Add(btnRow);
            dlg.Content = stack;

            nameBox.PreviewTextInput += (_, e) =>
            {
                if (e.Text.IndexOfAny(badChars) >= 0) e.Handled = true;
            };

            void TryRename()
            {
                string newName = nameBox.Text.Trim();
                if (newName.Length == 0) { errorTb.Text = "Name cannot be empty."; errorTb.Visibility = Visibility.Visible; return; }
                if (newName.IndexOfAny(badChars) >= 0) { errorTb.Text = "Name contains invalid characters."; errorTb.Visibility = Visibility.Visible; return; }
                string newRel = parentRel.Length == 0 ? newName : parentRel + "/" + newName;
                var (ok, err) = FolderChipsStore.RenameFolder(rel, newRel);
                if (!ok) { errorTb.Text = err; errorTb.Visibility = Visibility.Visible; return; }
                dlg.DialogResult = true;
            }

            renameBtn.Click += (_, _) => TryRename();
            dlg.ShowDialog();
        }
        finally { _suppressDeactivationHwndCapture = false; }

        RefreshChips();
        RefreshCards();
        Activate();
    }

    // ── Sort helpers ─────────────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    private sealed class WindowsExplorerComparer : IComparer<string>
    {
        public static readonly WindowsExplorerComparer Instance = new();
        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
    }

    private static List<T> ApplySort<T>(
        List<T> source, ChipSortOrder sort,
        Func<T, DateTime> getDate, Func<T, string> getName)
    {
        switch (sort)
        {
            case ChipSortOrder.DateAsc:  return source.OrderBy(getDate).ToList();
            case ChipSortOrder.NameAsc:  return source.OrderBy(getName, WindowsExplorerComparer.Instance).ToList();
            case ChipSortOrder.NameDesc: return source.OrderByDescending(getName, WindowsExplorerComparer.Instance).ToList();
            default:                     return source.OrderByDescending(getDate).ToList();
        }
    }

    private MenuItem BuildSortSubmenu(string? chipRel)
    {
        var current = FolderChipsStore.GetSort(chipRel);
        var top     = new MenuItem { Header = "    Sort by" };

        void Add(ChipSortOrder order, string label)
        {
            bool sel = order == current;
            var mi   = new MenuItem { Header = (sel ? "✓  " : "    ") + label };
            mi.Click += (_, _) => { FolderChipsStore.SetSort(chipRel, order); RefreshCards(); };
            top.Items.Add(mi);
        }

        Add(ChipSortOrder.DateDesc, "Date: newest first");
        Add(ChipSortOrder.DateAsc,  "Date: oldest first");
        Add(ChipSortOrder.NameAsc,  "Name: A to Z");
        Add(ChipSortOrder.NameDesc, "Name: Z to A");

        return top;
    }

    // ── Shared error label factory ────────────────────────────────────────

    private static TextBlock MakeErrorLabel()
    {
        var tb = new TextBlock
        {
            FontSize     = 10,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30)),
            TextWrapping = TextWrapping.Wrap,
            Cursor       = Cursors.Hand,
            Margin       = new Thickness(0, 3, 0, 0),
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

    // ── New .mnp file ─────────────────────────────────────────────────────

    private void ShowNewFileDialog(string folderFullPath)
    {
        var badChars = System.IO.Path.GetInvalidFileNameChars();

        _suppressDeactivationHwndCapture = true;
        try
        {
            var dlg = new Window
            {
                Title                 = "New File",
                Width                 = 320,
                SizeToContent         = SizeToContent.Height,
                Owner                 = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                ShowInTaskbar         = false,
                Background            = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF7))
            };

            var nameBox = new TextBox
            {
                Height                   = 26,
                FontSize                 = 12,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var errorTb = MakeErrorLabel();
            var createBtn = new Button
            {
                Content   = "Create",
                Width     = 70,
                Height    = 26,
                IsDefault = true,
                Cursor    = Cursors.Hand,
                Margin    = new Thickness(8, 0, 0, 0)
            };
            var cancelBtn = new Button
            {
                Content  = "Cancel",
                Width    = 70,
                Height   = 26,
                IsCancel = true,
                Cursor   = Cursors.Hand
            };
            var btnRow = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 8, 0, 0)
            };
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(createBtn);

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock
            {
                Text       = "File name (without .mnp):",
                FontSize   = 11,
                Margin     = new Thickness(0, 0, 0, 5),
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44))
            });
            stack.Children.Add(nameBox);
            stack.Children.Add(errorTb);
            stack.Children.Add(btnRow);
            dlg.Content = stack;

            nameBox.PreviewTextInput += (_, e) =>
            {
                if (e.Text.IndexOfAny(badChars) >= 0) e.Handled = true;
            };

            void TryCreate()
            {
                string raw  = nameBox.Text.Trim();
                // Strip trailing .mnp if the user typed it
                if (raw.EndsWith(".mnp", StringComparison.OrdinalIgnoreCase))
                    raw = raw[..^4].TrimEnd();
                if (raw.Length == 0)
                {
                    errorTb.Text = "Name cannot be empty.";
                    errorTb.Visibility = Visibility.Visible;
                    return;
                }
                if (raw.IndexOfAny(badChars) >= 0)
                {
                    errorTb.Text = "Name contains invalid characters.";
                    errorTb.Visibility = Visibility.Visible;
                    return;
                }
                string targetPath = System.IO.Path.Combine(folderFullPath, raw + ".mnp");
                if (System.IO.File.Exists(targetPath))
                {
                    errorTb.Text = $"\"{raw}.mnp\" already exists.";
                    errorTb.Visibility = Visibility.Visible;
                    return;
                }
                try
                {
                    SavedFileStore.SaveToPath(targetPath, "", null);
                    dlg.DialogResult = true;
                }
                catch (Exception ex)
                {
                    errorTb.Text = ex.Message;
                    errorTb.Visibility = Visibility.Visible;
                }
            }

            createBtn.Click += (_, _) => TryCreate();
            nameBox.Focus();
            dlg.ShowDialog();
        }
        finally { _suppressDeactivationHwndCapture = false; }
        Activate();
    }

    private void NavigateToChip(int index)
    {
        int count = _chipsPanel.Children.Count;
        if (count == 0) { _chipFocusIndex = -1; return; }
        _chipFocusIndex = ((index % count) + count) % count;

        int plusIndex = count - 1;
        if (_chipFocusIndex < plusIndex)
        {
            bool changed = false;
            if (_chipFocusIndex == 0)
            {
                if (FolderChipsStore.Active != null) { FolderChipsStore.Active = null; changed = true; }
            }
            else
            {
                int relIdx = _chipFocusIndex - 1;
                var chips  = FolderChipsStore.Chips;
                if (relIdx < chips.Count)
                {
                    string rel = chips[relIdx];
                    if (!string.Equals(FolderChipsStore.Active, rel, StringComparison.OrdinalIgnoreCase))
                    { FolderChipsStore.Active = rel; changed = true; }
                }
            }
            if (changed)
            {
                RefreshChips();   // ends with ReapplyChipFocusVisual
                RefreshCards();
                return;
            }
        }
        ReapplyChipFocusVisual();
    }

    private Border BuildMainChip()
    {
        bool active = FolderChipsStore.Active == null;

        var label = new TextBlock
        {
            Text              = "Main",
            FontSize          = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(active ? Colors.White : Color.FromRgb(0x55, 0x55, 0x60))
        };

        var chip = new Border
        {
            Background      = new SolidColorBrush(active ? ChipActiveBg : ChipInactiveBg),
            BorderBrush     = new SolidColorBrush(active
                                  ? Color.FromRgb(0x2E, 0x7D, 0x32)
                                  : Color.FromRgb(0xDC, 0xDC, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(9),
            Padding         = new Thickness(8, 2, 8, 2),
            Margin          = new Thickness(0, 0, 5, 5),
            Cursor          = Cursors.Hand,
            Child           = label,
            ToolTip         = "Base folder (all top-level files)"
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (FolderChipsStore.Active == null) return; // already on Main, no-op
            FolderChipsStore.Active = null;
            RefreshChips();
            RefreshCards();
        };
        chip.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu { FontSize = 12 };
            var ni   = new MenuItem { Header = "    New .mnp file here" };
            ni.Click += (_, _) => ShowNewFileDialog(SavedFileStore.SavedFolder);
            menu.Items.Add(ni);
            menu.Items.Add(BuildSortSubmenu(null));
            menu.PlacementTarget = chip;
            menu.Placement       = PlacementMode.Bottom;
            menu.IsOpen          = true;
        };
        return chip;
    }

    private static readonly Color ChipActiveBg   = Color.FromRgb(0x2E, 0x7D, 0x32);
    private static readonly Color ChipInactiveBg = Color.FromRgb(0xEC, 0xEC, 0xEE);

    private Border BuildChip(string rel)
    {
        bool active = string.Equals(FolderChipsStore.Active, rel, StringComparison.OrdinalIgnoreCase);

        var label = new TextBlock
        {
            Text              = rel,
            FontSize          = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(active ? Colors.White : Color.FromRgb(0x55, 0x55, 0x60))
        };

        var x = new TextBlock
        {
            Text              = "✕",
            FontSize          = 8,
            Margin            = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor            = Cursors.Hand,
            Foreground        = new SolidColorBrush(active
                                    ? Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)
                                    : Color.FromRgb(0xAA, 0xAA, 0xB0)),
            ToolTip           = "Remove chip"
        };
        x.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FolderChipsStore.RemoveChip(rel);
            RefreshChips();
            RefreshCards();
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(label);
        sp.Children.Add(x);

        var chip = new Border
        {
            Background       = new SolidColorBrush(active ? ChipActiveBg : ChipInactiveBg),
            BorderBrush      = new SolidColorBrush(active
                                    ? Color.FromRgb(0x2E, 0x7D, 0x32)
                                    : Color.FromRgb(0xDC, 0xDC, 0xE0)),
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(9),
            Padding          = new Thickness(8, 2, 6, 2),
            Margin           = new Thickness(0, 0, 5, 5),
            Cursor           = Cursors.Hand,
            Child            = sp,
            ToolTip          = rel
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            // Toggle: clicking the active chip clears the filter (back to base folder).
            FolderChipsStore.Active = active ? null : rel;
            RefreshChips();
            RefreshCards();
        };
        return chip;
    }

    private Border BuildAddChip()
    {
        var plus = new TextBlock
        {
            Text                = "+",
            FontSize            = 11,
            FontWeight          = FontWeights.Bold,
            Foreground          = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var chip = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0xF0, 0xF6, 0xF0)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xBE, 0xD8, 0xBE)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(9),
            Padding         = new Thickness(9, 2, 9, 2),
            Margin          = new Thickness(0, 0, 5, 5),
            Cursor          = Cursors.Hand,
            Child           = plus,
            ToolTip         = "Add Folder"
        };
        chip.MouseLeftButtonUp += (_, _) => OpenFolderPicker();
        return chip;
    }

    private void OpenFolderPicker()
    {
        _suppressDeactivationHwndCapture = true;
        try
        {
            var dlg = new FolderPickerDialog(this);
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedRelative))
            {
                FolderChipsStore.AddChip(dlg.SelectedRelative);
                RefreshChips();
            }
        }
        finally { _suppressDeactivationHwndCapture = false; }
        Activate();
    }

    // ── Card list ─────────────────────────────────────────────────────────

    private void RefreshCards()
    {
        int prevIndex = _selectedIndex;
        _cardsPanel.Children.Clear();
        _cardList.Clear();

        if (_mode == HistoryMode.Files)
        {
            string? active = FolderChipsStore.Active;
            if (!string.IsNullOrEmpty(active) && FolderChipsStore.FolderExists(active))
            {
                // Filtered view: .mnp files inside the active subfolder, opened as external files.
                var folderFiles = ApplySort(FolderChipsStore.LoadFolder(active),
                    FolderChipsStore.GetSort(active),
                    t => t.Entry.LastModified, t => t.Entry.FileName);
                if (folderFiles.Count == 0)
                {
                    ShowEmptyMessage($"No .mnp files in \"{active}\"."); _selectedIndex = -1; return;
                }
                foreach (var (f, fullPath) in folderFiles)
                {
                    var (card, normalBg, onActivate, onDelete) = BuildSavedFileCard(f, fullPath);
                    _cardsPanel.Children.Add(card);
                    _cardList.Add((card, normalBg, onActivate, onDelete, f, fullPath));
                }
            }
            else
            {
                // Base view: top-level Saved library files.
                var files = ApplySort(SavedFileStore.LoadAll(),
                    FolderChipsStore.GetSort(null),
                    e => e.LastModified, e => e.FileName);
                if (files.Count == 0) { ShowEmptyMessage("No saved files yet."); _selectedIndex = -1; return; }
                foreach (var f in files)
                {
                    var (card, normalBg, onActivate, onDelete) = BuildSavedFileCard(f);
                    _cardsPanel.Children.Add(card);
                    _cardList.Add((card, normalBg, onActivate, onDelete, f, null));
                }
            }
        }
        else if (_mode == HistoryMode.Global)
        {
            var entries = NormalClipboardHistory.Entries;
            if (entries.Count == 0) { ShowEmptyMessage("No clipboard history yet."); _selectedIndex = -1; return; }
            foreach (var entry in entries)
            {
                var (card, normalBg, onActivate, onDelete) = BuildCard(entry);
                _cardsPanel.Children.Add(card);
                _cardList.Add((card, normalBg, onActivate, onDelete, entry, null));
            }
        }
        else
        {
            var clipEntries = ClipboardHistory.Entries.ToList();
            if (clipEntries.Count == 0) { ShowEmptyMessage("No clipboard history yet."); _selectedIndex = -1; return; }
            foreach (var e in clipEntries)
            {
                var (card, normalBg, onActivate, onDelete) = BuildCard(e);
                _cardsPanel.Children.Add(card);
                _cardList.Add((card, normalBg, onActivate, onDelete, e, null));
            }
        }

        if (_cardList.Count > 0)
        {
            int newIndex = prevIndex < 0 ? 0 : Math.Min(prevIndex, _cardList.Count - 1);
            SetSelectedIndex(newIndex, scrollIntoView: false);
        }
        else
        {
            _selectedIndex = -1;
        }
    }

    private void ShowEmptyMessage(string text)
    {
        _cardsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            Margin = new Thickness(0, 20, 0, 0),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
    }

    private (Border Card, Brush NormalBg, Action OnActivate, Action OnDelete) BuildCard(ClipboardEntry entry)
    {
        var spans = RichClipboard.DeserializeSpans(entry.RichJson);

        // ── Preview ───────────────────────────────────────────────────────
        // single-line mode: NoWrap + Ellipsis per line, but lines still stack
        // and the card still has MaxHeight + fadeout for too-tall content.
        var previewBlock = BuildFormattedBlock(entry.PlainText, spans, 11, _singleLineMode);

        UIElement previewArea;
        Border? fadeOverlay = null;
        Border? clipBox = null;

        // Both modes use MaxHeight + fadeout; single-line mode just adds
        // NoWrap+Ellipsis so long lines truncate instead of wrapping.
        clipBox = new Border
        {
            MaxHeight = 180,
            ClipToBounds = true,
            Child = previewBlock
        };

        fadeOverlay = new Border
        {
            Height = 24,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Background = MakeFade(Colors.White),
            Visibility = Visibility.Collapsed
        };

        var previewGrid = new Grid();
        previewGrid.Children.Add(clipBox);
        previewGrid.Children.Add(fadeOverlay);
        previewArea = previewGrid;

        // ── Footer: timestamp left, delete button right ───────────────────
        var timestamp = new TextBlock
        {
            Text = entry.CopiedAt.ToString("d MMM yyyy  H:mm:ss"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };

        var deleteBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 5, 0, 0),
            ToolTip = "Remove from history"
        };
        deleteBtn.MouseEnter += (_, _) =>
            deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30));
        deleteBtn.MouseLeave += (_, _) =>
            deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        deleteBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            int idx = _cardList.FindIndex(t => t.Entry.Equals(entry));
            if (idx >= 0) DeleteCard(idx);
        };

        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0) };
        DockPanel.SetDock(deleteBtn, Dock.Right);
        DockPanel.SetDock(timestamp, Dock.Left);
        footer.Children.Add(deleteBtn);
        footer.Children.Add(timestamp);

        // ── Outer layout: preview + footer ────────────────────────────────
        var outerStack = new StackPanel();
        outerStack.Children.Add(previewArea);
        outerStack.Children.Add(footer);

        // ── Card border ───────────────────────────────────────────────────
        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand,
            Child = outerStack,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.07,
                BlurRadius = 5,
                ShadowDepth = 1,
                Direction = 270
            }
        };

        // ── Hover tint ────────────────────────────────────────────────────
        var hoverBgColor = Color.FromRgb(0xEE, 0xF4, 0xFF);

        card.MouseEnter += (_, _) =>
        {
            card.Background = new SolidColorBrush(hoverBgColor);
            if (fadeOverlay != null)
                fadeOverlay.Background = MakeFade(hoverBgColor);
        };
        card.MouseLeave += (_, _) =>
        {
            if (card.Tag is true) return; // keep selection styling
            card.Background = Brushes.White;
            if (fadeOverlay != null)
                fadeOverlay.Background = MakeFade(Colors.White);
        };

        // ── After layout: decide fade + bubble based on actual overflow ─────
        {
            bool bubbleAttached = false;
            previewBlock.Loaded += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (previewBlock.ActualWidth <= 0) return;

                    previewBlock.Measure(new Size(previewBlock.ActualWidth, double.PositiveInfinity));
                    bool overflows = previewBlock.DesiredSize.Height > clipBox.MaxHeight;

                    fadeOverlay.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
                    if (overflows && !bubbleAttached)
                    {
                        bubbleAttached = true;
                        AttachFollowMouseBubble(card, entry.PlainText, spans);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        card.MouseLeftButtonUp += (_, _) => PasteEntry(entry.PlainText, spans);

        Action onDelete = _mode == HistoryMode.App
            ? () => ClipboardHistory.Remove(entry)
            : () => NormalClipboardHistory.Remove(entry);

        return (card, Brushes.White, () => PasteEntry(entry.PlainText, spans), onDelete);
    }

    private (Border Card, Brush NormalBg, Action OnActivate, Action OnDelete) BuildSavedFileCard(
        SavedFileEntry entry, string? fullPath = null)
    {
        var spans = RichClipboard.DeserializeSpans(entry.RichJson);

        var previewBlock = BuildFormattedBlock(entry.PlainText, spans, 11, _singleLineMode);

        var clipBox = new Border
        {
            MaxHeight = 180,
            ClipToBounds = true,
            Child = previewBlock
        };

        var fadeOverlay = new Border
        {
            Height = 24,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Background = MakeFade(CardBgSaved),
            Visibility = Visibility.Collapsed
        };

        var previewGrid = new Grid();
        previewGrid.Children.Add(clipBox);
        previewGrid.Children.Add(fadeOverlay);

        // ── Footer: file name + timestamp left, delete button right ───────
        var fileLabel = new TextBlock
        {
            Text = $"📄 {entry.FileName}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 6, 0)
        };

        var timestamp = new TextBlock
        {
            Text = entry.LastModified.ToString("d MMM yyyy  H:mm:ss"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };

        var deleteBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(8, 5, 0, 0),
            ToolTip = "Delete saved file"
        };
        deleteBtn.MouseEnter += (_, _) =>
            deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30));
        deleteBtn.MouseLeave += (_, _) =>
            deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        deleteBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            int idx = _cardList.FindIndex(t => t.Entry.Equals(entry));
            if (idx >= 0) DeleteCard(idx);
        };

        var footerLeft = new StackPanel { Orientation = Orientation.Horizontal };
        footerLeft.Children.Add(fileLabel);
        footerLeft.Children.Add(timestamp);

        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0) };
        DockPanel.SetDock(deleteBtn, Dock.Right);
        DockPanel.SetDock(footerLeft, Dock.Left);
        footer.Children.Add(deleteBtn);
        footer.Children.Add(footerLeft);

        var outerStack = new StackPanel();
        outerStack.Children.Add(previewGrid);
        outerStack.Children.Add(footer);

        var normalBg = new SolidColorBrush(CardBgSaved);
        var hoverBg = new SolidColorBrush(CardBgSavedHover);

        var card = new Border
        {
            Background = normalBg,
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand,
            Child = outerStack,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.07,
                BlurRadius = 5,
                ShadowDepth = 1,
                Direction = 270
            }
        };

        card.MouseEnter += (_, _) =>
        {
            card.Background = hoverBg;
            fadeOverlay.Background = MakeFade(CardBgSavedHover);
        };
        card.MouseLeave += (_, _) =>
        {
            if (card.Tag is true) return;
            card.Background = normalBg;
            fadeOverlay.Background = MakeFade(CardBgSaved);
        };

        {
            bool bubbleAttached = false;
            previewBlock.Loaded += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (previewBlock.ActualWidth <= 0) return;
                    previewBlock.Measure(new Size(previewBlock.ActualWidth, double.PositiveInfinity));
                    bool overflows = previewBlock.DesiredSize.Height > clipBox.MaxHeight;
                    fadeOverlay.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
                    if (overflows && !bubbleAttached)
                    {
                        bubbleAttached = true;
                        AttachFollowMouseBubble(card, entry.PlainText, spans);
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        // fullPath == null → top-level library file (open/delete by name).
        // fullPath != null → subfolder file (open as external file, delete by path).
        Action open = fullPath == null
            ? () => OpenSavedFile(entry)
            : () => NotepadWindow.OpenOrFocusExternalFile(fullPath, entry, _targetWindow);
        Action delete = fullPath == null
            ? () => SavedFileStore.Delete(entry.FileName)
            : () => { try { System.IO.File.Delete(fullPath); } catch { } RefreshCards(); };

        card.MouseLeftButtonUp  += (_, _) => open();
        card.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowOpenWithMenu(card, entry, fullPath); };

        return (card, normalBg, open, delete);
    }

    // ── Keyboard navigation ───────────────────────────────────────────────

    private void SetSelectedIndex(int index, bool scrollIntoView = true)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _cardList.Count)
        {
            var prev = _cardList[_selectedIndex];
            prev.Card.Tag = false;
            prev.Card.BorderBrush     = _normCardBorder;
            prev.Card.BorderThickness = new Thickness(1);
        }

        _selectedIndex = index;

        if (index >= 0 && index < _cardList.Count)
        {
            var curr = _cardList[index];
            curr.Card.Tag = true;
            curr.Card.BorderBrush     = _selCardBorder;
            curr.Card.BorderThickness = new Thickness(2);
            if (scrollIntoView)
                curr.Card.BringIntoView();
        }
    }

    private void DeleteCard(int index)
    {
        if (index < 0 || index >= _cardList.Count) return;
        var info = _cardList[index];
        _undoStack.Push((_mode, index, info.Entry, info.FullPath));
        info.OnDelete();
        ShowToast("Deleted — Undo?");
        // RefreshCards fires via HistoryChanged; _selectedIndex clamped there
    }

    private void UndoDelete()
    {
        HideToast();
        if (_undoStack.Count == 0) return;
        var (mode, index, entry, fullPath) = _undoStack.Pop();

        if (entry is ClipboardEntry clipEntry)
        {
            _selectedIndex = index;
            if (mode == HistoryMode.App) ClipboardHistory.InsertAt(index, clipEntry);
            else                         NormalClipboardHistory.InsertAt(index, clipEntry);
        }
        else if (entry is SavedFileEntry fileEntry)
        {
            _selectedIndex = 0;
            if (!string.IsNullOrEmpty(fullPath))
                SavedFileStore.RestoreToPath(fileEntry, fullPath);
            else
                SavedFileStore.Restore(fileEntry);
        }
    }

    // ── Delete toast (undo snackbar) ──────────────────────────────────────

    private Border BuildToastBar()
    {
        var undoBtn = new TextBlock
        {
            Text              = "Undo",
            FontSize          = 11,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0xFF)),
            Cursor            = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(14, 0, 0, 0)
        };
        undoBtn.MouseEnter += (_, _) =>
            undoBtn.Foreground = new SolidColorBrush(Colors.White);
        undoBtn.MouseLeave += (_, _) =>
            undoBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0xFF));
        undoBtn.MouseLeftButtonUp += (_, _) => UndoDelete();

        var msgText = new TextBlock
        {
            FontSize          = 11,
            Foreground        = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            VerticalAlignment = VerticalAlignment.Center,
            Text              = "Deleted — Undo?"
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(msgText);
        row.Children.Add(undoBtn);

        var bar = new Border
        {
            Background          = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
            CornerRadius        = new CornerRadius(5),
            Padding             = new Thickness(14, 8, 14, 8),
            Margin              = new Thickness(14, 0, 14, 46),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Visibility          = Visibility.Collapsed,
            Child               = row,
            IsHitTestVisible    = true
        };

        // store ref to msgText so ShowToast can update the message
        bar.Tag = msgText;
        return bar;
    }

    private void ShowToast(string message)
    {
        if (_toastBar.Tag is TextBlock tb) tb.Text = message;
        _toastBar.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) => HideToast();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        _toastBar.Visibility = Visibility.Collapsed;
    }

    private void OnWindowPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        if (ctrl && e.Key == Key.Z)
        {
            UndoDelete();
            e.Handled = true;
            return;
        }

        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        switch (e.Key)
        {
            case Key.Tab:
                if (_mode == HistoryMode.Files && _chipsPanel.Children.Count > 0)
                {
                    int count    = _chipsPanel.Children.Count;
                    int newIndex = _chipFocusIndex < 0
                        ? (shift ? count - 1 : 0)
                        : ((_chipFocusIndex + (shift ? -1 : 1)) % count + count) % count;
                    NavigateToChip(newIndex);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (_chipFocusIndex >= 0 && _mode == HistoryMode.Files)
                {
                    _chipFocusIndex = -1;
                    RefreshChips();
                    if (_selectedIndex < 0 && _cardList.Count > 0) SetSelectedIndex(0);
                }
                else
                {
                    if (_selectedIndex < _cardList.Count - 1)
                        SetSelectedIndex(_selectedIndex + 1);
                    else if (_selectedIndex < 0 && _cardList.Count > 0)
                        SetSelectedIndex(0);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_chipFocusIndex >= 0 && _mode == HistoryMode.Files)
                {
                    _chipFocusIndex = -1;
                    RefreshChips();
                    if (_selectedIndex < 0 && _cardList.Count > 0) SetSelectedIndex(0);
                }
                else
                {
                    if (_selectedIndex > 0)
                        SetSelectedIndex(_selectedIndex - 1);
                    else if (_selectedIndex < 0 && _cardList.Count > 0)
                        SetSelectedIndex(0);
                }
                e.Handled = true;
                break;

            case Key.Left:
                CycleTab(-1);
                e.Handled = true;
                break;

            case Key.Right:
                CycleTab(1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_chipFocusIndex >= 0 && _mode == HistoryMode.Files)
                {
                    if (_chipFocusIndex == _chipsPanel.Children.Count - 1)
                        OpenFolderPicker();
                    e.Handled = true;
                }
                else if (_selectedIndex >= 0 && _selectedIndex < _cardList.Count)
                {
                    _cardList[_selectedIndex].OnActivate();
                    e.Handled = true;
                }
                break;

            case Key.F2:
                if (_mode == HistoryMode.Files &&
                    _selectedIndex >= 0 && _selectedIndex < _cardList.Count)
                {
                    var cardEntry = _cardList[_selectedIndex].Entry;
                    if (cardEntry is SavedFileEntry fe)
                    {
                        string? fp = GetFullPathForCardEntry(fe);
                        ShowCardRenameDialog(fe, fp);
                    }
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                if (_selectedIndex >= 0 && _selectedIndex < _cardList.Count)
                {
                    DeleteCard(_selectedIndex);
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private async void PasteEntry(string plainText, List<FormattingManager.SpanRecord>? spans)
    {
        if (_externalFocusHwnd != IntPtr.Zero)
        {
            if (_isPasting) return;
            _isPasting = true;
            _cardsPanel.IsHitTestVisible = false;   // block all card clicks until done
            try
            {
                var target = _externalFocusHwnd;
                Clipboard.SetText(plainText);
                SetForegroundWindow(target);
                await Task.Delay(120);  // non-blocking; give target window time to activate
                SetForegroundWindow(target);  // ensure still foreground before SendInput
                var inputs = new INPUT[]
                {
                    new INPUT { type=INPUT_KEYBOARD, wVk=VK_CONTROL },
                    new INPUT { type=INPUT_KEYBOARD, wVk=VK_V },
                    new INPUT { type=INPUT_KEYBOARD, wVk=VK_V,       kbdFlags=KEYEVENTF_KEYUP },
                    new INPUT { type=INPUT_KEYBOARD, wVk=VK_CONTROL, kbdFlags=KEYEVENTF_KEYUP },
                };
                SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
                await Task.Delay(200);  // keep cards disabled long enough to prevent double-fire
            }
            finally
            {
                _isPasting = false;
                _cardsPanel.IsHitTestVisible = true;
            }
        }
        else
        {
            _targetWindow.Activate();
            _targetWindow.PasteContent(plainText, spans);
        }
    }

    private void OpenSavedFile(SavedFileEntry entry) =>
        NotepadWindow.OpenOrFocusSavedFile(entry, _targetWindow);

    // ── Card rename ───────────────────────────────────────────────────────

    private string? GetFullPathForCardEntry(SavedFileEntry entry)
    {
        // Reconstruct fullPath from the active chip filter context
        string? active = FolderChipsStore.Active;
        if (string.IsNullOrEmpty(active)) return null;  // top-level library file
        // Find the file in the active folder by matching entry
        var folderFiles = FolderChipsStore.LoadFolder(active);
        foreach (var (e, path) in folderFiles)
            if (e.FileName == entry.FileName && e.LastModified == entry.LastModified)
                return path;
        return null;
    }

    private void DuplicateFile(SavedFileEntry entry, string sourcePath, string folder)
    {
        string baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        // Strip any existing " (N)" suffix so we always count from the original name
        var m = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*) \((\d+)\)$");
        string root = m.Success ? m.Groups[1].Value : baseName;

        string destPath = "";
        for (int n = 2; n < 1000; n++)
        {
            string candidate = System.IO.Path.Combine(folder, $"{root} ({n}).mnp");
            if (!System.IO.File.Exists(candidate)) { destPath = candidate; break; }
        }
        if (destPath.Length == 0) return;

        try { System.IO.File.Copy(sourcePath, destPath); }
        catch { }
    }

    private void ShowCardRenameDialog(SavedFileEntry entry, string? fullPath)
    {
        string sourceFile = fullPath ?? Formatting.SavedFileStore.GetFilePath(entry.FileName);
        string fileDir    = System.IO.Path.GetDirectoryName(sourceFile)!;
        string oldName    = entry.FileName;
        bool   isLib      = fullPath == null;

        _suppressDeactivationHwndCapture = true;

        var dialog = new Window
        {
            Title                 = "Rename File",
            Width                 = 320,
            SizeToContent         = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode            = ResizeMode.NoResize,
            Owner                 = this,
            ShowInTaskbar         = false,
            Background            = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF8))
        };

        var stack = new StackPanel { Margin = new Thickness(14) };

        var nameBox = new TextBox
        {
            Text   = oldName,
            Height = 26,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var errorLabel = MakeErrorLabel();
        errorLabel.Margin = new Thickness(0, 0, 0, 6);

        var renameBtn = new Button { Content = "Rename", Width = 80, IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(6, 0, 0, 0), IsCancel = true };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(renameBtn);
        btnRow.Children.Add(cancelBtn);

        stack.Children.Add(nameBox);
        stack.Children.Add(errorLabel);
        stack.Children.Add(btnRow);
        dialog.Content = stack;

        void TryRename()
        {
            string newName = nameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            if (newName == oldName) { dialog.Close(); return; }

            // Validate filename chars
            char[] bad = newName.Where(c => System.IO.Path.GetInvalidFileNameChars().Contains(c)).ToArray();
            if (bad.Length > 0)
            {
                errorLabel.Text       = "Invalid characters: " + string.Join(" ", bad.Select(c => $"'{c}'"));
                errorLabel.Visibility = Visibility.Visible;
                return;
            }

            string newFile = System.IO.Path.Combine(fileDir, newName + ".mnp");
            if (System.IO.File.Exists(newFile))
            {
                errorLabel.Text       = $"\"{newName}\" already exists in this folder.";
                errorLabel.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                System.IO.File.Move(sourceFile, newFile);
                // FileSystemWatcher fires → RefreshCards automatically
                dialog.Close();
            }
            catch (Exception ex)
            {
                errorLabel.Text       = "Error: " + ex.Message;
                errorLabel.Visibility = Visibility.Visible;
            }
        }

        renameBtn.Click        += (_, _) => TryRename();
        cancelBtn.Click        += (_, _) => dialog.Close();
        nameBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)  { TryRename();    e.Handled = true; }
            if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
        };

        dialog.Closed += (_, _) => { _suppressDeactivationHwndCapture = false; Activate(); };
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        dialog.Show();
    }

    // ── Open With (right-click on file cards) ─────────────────────────────

    private void ShowOpenWithMenu(UIElement anchor, SavedFileEntry entry, string? fullPath = null)
    {
        string filePath = fullPath ?? Formatting.SavedFileStore.GetFilePath(entry.FileName);

        var known  = OpenWithStore.GetKnownEditors();
        var recent = OpenWithStore.GetRecent();
        string? defPath = OpenWithStore.DefaultPath;

        // Build deduped ordered list: default first (if any), then known, then extra-recent
        var shownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var menu = new ContextMenu { FontSize = 12 };

        MenuItem MakeItem(string label, string exePath, string exeName)
        {
            bool isDef = string.Equals(exePath, defPath, StringComparison.OrdinalIgnoreCase);
            var mi = new MenuItem
            {
                Header  = (isDef ? "✓  " : "    ") + label,
                Tag     = exePath,
            };
            mi.Click += (_, _) => OpenFileWith(exePath, exeName, filePath);
            return mi;
        }

        // 1 — Known editors (in detection order)
        foreach (var (name, path) in known)
        {
            if (shownPaths.Add(path))
                menu.Items.Add(MakeItem($"Open with  {name}", path, name));
        }

        // 2 — Recent programs not already shown
        var extraRecent = recent.Where(r => shownPaths.Add(r.Path)).ToList();
        if (extraRecent.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var r in extraRecent)
                menu.Items.Add(MakeItem($"Open with  {r.Name}", r.Path, r.Name));
        }

        // 3 — Choose other
        menu.Items.Add(new Separator());
        var chooseItem = new MenuItem { Header = "    Choose other program…" };
        chooseItem.Click += (_, _) => ChooseOtherProgram(filePath);
        menu.Items.Add(chooseItem);

        // 4 — File actions (Files mode only)
        if (_mode == HistoryMode.Files)
        {
            menu.Items.Add(new Separator());

            string cardFolder = fullPath != null
                ? System.IO.Path.GetDirectoryName(fullPath)!
                : SavedFileStore.SavedFolder;
            var newMnpItem = new MenuItem { Header = "    New .mnp file here" };
            newMnpItem.Click += (_, _) => ShowNewFileDialog(cardFolder);
            menu.Items.Add(newMnpItem);
            menu.Items.Add(BuildSortSubmenu(FolderChipsStore.Active));

            var dupItem = new MenuItem { Header = "    Duplicate This File" };
            dupItem.Click += (_, _) => DuplicateFile(entry, filePath, cardFolder);
            menu.Items.Add(dupItem);

            menu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "    Rename…" };
            renameItem.Click += (_, _) => ShowCardRenameDialog(entry, fullPath);
            menu.Items.Add(renameItem);

            var deleteItem = new MenuItem { Header = "    Delete" };
            deleteItem.Click += (_, _) =>
            {
                int idx = _cardList.FindIndex(t => t.Entry.Equals(entry));
                if (idx >= 0) DeleteCard(idx);
            };
            menu.Items.Add(deleteItem);

            menu.Items.Add(new Separator());
            menu.Items.Add(BuildFolderSubmenu("Move to Folder", entry, fullPath, move: true));
            menu.Items.Add(BuildFolderSubmenu("Copy to Folder", entry, fullPath, move: false));
        }

        menu.PlacementTarget = anchor;
        menu.Placement       = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen          = true;
    }

    // ── Move / Copy to folder ─────────────────────────────────────────────

    private MenuItem BuildFolderSubmenu(
        string label, SavedFileEntry entry, string? fullPath, bool move)
    {
        var top = new MenuItem { Header = (move ? "📦  " : "📋  ") + label };

        // Current location as relative path (null = base folder)
        string? currentRel = null;
        if (fullPath != null)
        {
            try
            {
                string root = System.IO.Path.GetFullPath(FolderChipsStore.Root);
                string dir  = System.IO.Path.GetDirectoryName(fullPath)!;
                string rel  = System.IO.Path.GetRelativePath(root, dir).Replace('\\', '/');
                currentRel  = rel == "." ? null : rel;
            }
            catch { }
        }

        var folders  = FolderChipsStore.EnumerateSubfolders(); // sorted alphabetically
        bool showAll = folders.Count > 20;
        int  limit   = showAll ? 19 : folders.Count;

        // ── Main (base folder) ────────────────────────────────────────────
        top.Items.Add(MakeFolderDestItem("Main  (base folder)", 0, null, currentRel, entry, fullPath, move));

        if (folders.Count > 0)
            top.Items.Add(new Separator());

        // ── Subfolders ────────────────────────────────────────────────────
        foreach (var rel in folders.Take(limit))
        {
            int    depth = rel.Count(c => c == '/') + 1;
            string name  = rel.Contains('/') ? rel[(rel.LastIndexOf('/') + 1)..] : rel;
            top.Items.Add(MakeFolderDestItem(name, depth, rel, currentRel, entry, fullPath, move));
        }

        // ── Show all folders… ─────────────────────────────────────────────
        if (showAll)
        {
            top.Items.Add(new Separator());
            var more = new MenuItem
            {
                Header   = "    Show all folders…",
                FontStyle = FontStyles.Italic
            };
            more.Click += (_, _) =>
            {
                _suppressDeactivationHwndCapture = true;
                var dlg = new FolderPickerDialog(this,
                    move ? "Select destination folder to move into"
                         : "Select destination folder to copy into");
                bool? result = dlg.ShowDialog();
                _suppressDeactivationHwndCapture = false;
                Activate();
                if (result == true)
                    ExecuteMoveOrCopy(entry, fullPath, dlg.SelectedRelative, move);
            };
            top.Items.Add(more);
        }

        return top;
    }

    private MenuItem MakeFolderDestItem(
        string name, int depth, string? destRel,
        string? currentRel, SavedFileEntry entry, string? fullPath, bool move)
    {
        bool isCurrent = string.Equals(
            destRel  ?? "", currentRel ?? "",
            StringComparison.OrdinalIgnoreCase);

        var label = new TextBlock
        {
            Text              = "📁  " + name,
            Margin            = new Thickness(depth * 14, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = isCurrent
                ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA))
                : SystemColors.ControlTextBrush
        };

        var item = new MenuItem
        {
            Header    = label,
            IsEnabled = !isCurrent,
            ToolTip   = destRel is { Length: > 0 } ? destRel : "(base folder)"
        };

        if (!isCurrent)
        {
            var rel = destRel;
            item.Click += (_, _) => ExecuteMoveOrCopy(entry, fullPath, rel, move);
        }

        return item;
    }

    private void ExecuteMoveOrCopy(
        SavedFileEntry entry, string? fullPath, string? destRel, bool move)
    {
        string sourceFile = fullPath ?? Formatting.SavedFileStore.GetFilePath(entry.FileName);
        string destDir    = string.IsNullOrEmpty(destRel)
            ? System.IO.Path.GetFullPath(Formatting.SavedFileStore.SavedFolder)
            : FolderChipsStore.FullPath(destRel);
        string destFile   = System.IO.Path.Combine(destDir,
            System.IO.Path.GetFileName(sourceFile));

        if (string.Equals(sourceFile, destFile, StringComparison.OrdinalIgnoreCase))
            return;

        try { System.IO.Directory.CreateDirectory(destDir); } catch { }

        if (System.IO.File.Exists(destFile))
        {
            string verb = move ? "Move" : "Copy";
            if (MessageBox.Show(
                    $"\"{System.IO.Path.GetFileName(sourceFile)}\" already exists in the destination. Overwrite?",
                    $"{verb} File", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;
        }

        try
        {
            if (move)
                System.IO.File.Move(sourceFile, destFile, overwrite: true);
            else
                System.IO.File.Copy(sourceFile, destFile, overwrite: true);
            // FileSystemWatcher fires SavedFilesChanged → RefreshCards automatically
        }
        catch (Exception ex)
        {
            string verb = move ? "move" : "copy";
            MessageBox.Show($"Could not {verb} file:\n{ex.Message}",
                move ? "Move File" : "Copy File",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFileWith(string exePath, string name, string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName  = exePath,
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false,
            });
            OpenWithStore.Use(exePath, name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open with {name}:\n{ex.Message}",
                "Open with", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChooseOtherProgram(string filePath)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title            = "Choose a program to open this file",
            Filter           = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };

        _suppressDeactivationHwndCapture = true;
        bool? picked = dlg.ShowDialog(this);
        _suppressDeactivationHwndCapture = false;
        if (picked != true) return;

        string exePath = dlg.FileName;
        string name    = System.IO.Path.GetFileNameWithoutExtension(exePath);

        _suppressDeactivationHwndCapture = true;
        var confirm = new OpenWithPickerDialog(name, this);
        bool? confirmed = confirm.ShowDialog();
        _suppressDeactivationHwndCapture = false;
        if (confirmed != true) return;

        OpenWithStore.Use(exePath, name, confirm.SetAsDefault);
        OpenFileWith(exePath, name, filePath);
    }

    // ── Footer helpers ────────────────────────────────────────────────────

    private static TextBlock MakeFooterLink(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.MouseEnter += (_, _) =>
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        tb.MouseLeave += (_, _) =>
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        return tb;
    }

    private static TextBlock MakeDot() => new TextBlock
    {
        Text = "·",
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 6, 0)
    };

    // ── Saved-file card colours ───────────────────────────────────────────

    private static readonly Color CardBgSaved = Colors.White;
    private static readonly Color CardBgSavedHover = Color.FromRgb(0xEE, 0xF4, 0xFF);

    // ── Mode tab helpers ──────────────────────────────────────────────────

    private void UpdateActiveTab()
    {
        SetTabActive(_appTab, _mode == HistoryMode.App, Color.FromRgb(0x44, 0x72, 0xC4));
        SetTabActive(_sysTab, _mode == HistoryMode.Global, Color.FromRgb(0x71, 0x53, 0xC4));
        SetTabActive(_filesTab, _mode == HistoryMode.Files, Color.FromRgb(0x2E, 0x7D, 0x32));
        if (_chipsHost != null)
            _chipsHost.Visibility = _mode == HistoryMode.Files ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetTabActive(Border tab, bool active, Color color)
    {
        tab.Background = new SolidColorBrush(active
            ? Color.FromArgb(0x28, color.R, color.G, color.B)
            : Color.FromArgb(0x10, 0x88, 0x88, 0x88));
        tab.BorderBrush = new SolidColorBrush(active
            ? Color.FromArgb(0x90, color.R, color.G, color.B)
            : Color.FromArgb(0x30, 0x88, 0x88, 0x88));
        if (tab.Child is TextBlock tb)
            tb.Foreground = new SolidColorBrush(active
                ? color
                : Color.FromRgb(0x99, 0x99, 0x99));
    }

    private static Border MakeTabButton(string label, Color accentColor)
    {
        var tb = new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0)
        };
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0x88, 0x88, 0x88)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            Margin = new Thickness(2, 0, 2, 0),
            Child = tb,
            Tag = accentColor
        };
        return border;
    }

    // ── Rich text helpers ─────────────────────────────────────────────────

    private static TextBlock BuildFormattedBlock(
        string text,
        List<FormattingManager.SpanRecord>? spans,
        double fontSize,
        bool singleLine = false)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            TextTrimming = singleLine ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        };

        if (string.IsNullOrEmpty(text)) return tb;

        if (spans == null || spans.Count == 0)
        {
            tb.Inlines.Add(new Run(text));
            return tb;
        }

        // Collect span break-points and sort them
        var pts = new SortedSet<int> { 0, text.Length };
        foreach (var s in spans)
        {
            pts.Add(Math.Clamp(s.Start, 0, text.Length));
            pts.Add(Math.Clamp(s.End, 0, text.Length));
        }

        var sorted = pts.ToList();
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            int segStart = sorted[i];
            int segEnd = sorted[i + 1];
            if (segStart >= segEnd) continue;

            string segment = text.Substring(segStart, segEnd - segStart);

            // Merge all spans whose clamped range fully covers this segment
            bool bold = false, italic = false, under = false, strike = false;
            string? fore = null, back = null;

            foreach (var s in spans)
            {
                int ss = Math.Clamp(s.Start, 0, text.Length);
                int se = Math.Clamp(s.End, 0, text.Length);
                if (ss > segStart || se < segEnd) continue;

                bold |= s.Format.Bold;
                italic |= s.Format.Italic;
                under |= s.Format.Underline;
                strike |= s.Format.Strikethrough;
                if (s.Format.ForeColorHex != null) fore = s.Format.ForeColorHex;
                if (s.Format.BackColorHex != null) back = s.Format.BackColorHex;
            }

            var run = new Run(segment);
            if (bold) run.FontWeight = FontWeights.Bold;
            if (italic) run.FontStyle = FontStyles.Italic;
            if (fore != null) run.Foreground = HexBrush(fore);
            if (back != null) run.Background = HexBrush(back);

            if (under || strike)
            {
                var td = new TextDecorationCollection();
                if (under) td.Add(TextDecorations.Underline[0]);
                if (strike) td.Add(TextDecorations.Strikethrough[0]);
                run.TextDecorations = td;
            }

            tb.Inlines.Add(run);
        }

        return tb;
    }

    private static SolidColorBrush HexBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static LinearGradientBrush MakeFade(Color solidColor)
    {
        var transparent = Color.FromArgb(0, solidColor.R, solidColor.G, solidColor.B);
        return new LinearGradientBrush(transparent, solidColor, 90);
    }

    // ── Follow-mouse popup bubble ─────────────────────────────────────────

    private static void AttachFollowMouseBubble(
        Border card,
        string plainText,
        List<FormattingManager.SpanRecord>? spans)
    {
        double maxH = SystemParameters.WorkArea.Height * 0.75;

        // Content TextBlock — clipped, no scrollbar
        var content = BuildFormattedBlock(plainText, spans, 12);
        content.Margin = new Thickness(12, 10, 12, 30); // extra bottom padding for fade

        var clipHost = new Border
        {
            MaxHeight = maxH,
            ClipToBounds = true,
            Child = content
        };

        // Bottom fade gradient — only visible when content overflows
        var fadeBar = new System.Windows.Shapes.Rectangle
        {
            Height = 32,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush(
                Color.FromArgb(0, 0xFF, 0xFF, 0xFF),
                Colors.White,
                90)
        };

        var bubbleGrid = new Grid { MaxWidth = 480, MinWidth = 180 };
        bubbleGrid.Children.Add(clipHost);
        bubbleGrid.Children.Add(fadeBar);

        var bubbleBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = bubbleGrid,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.15,
                BlurRadius = 10,
                ShadowDepth = 2,
                Direction = 270
            }
        };

        var popup = new Popup
        {
            Child = bubbleBorder,
            AllowsTransparency = true,
            Placement = PlacementMode.Absolute,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None
        };

        // ── 1.5 s hover-still delay before showing ────────────────────────
        var hoverTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };

        Point lastMouseDiu = default;

        hoverTimer.Tick += (_, _) =>
        {
            hoverTimer.Stop();
            popup.HorizontalOffset = lastMouseDiu.X + 18;
            popup.VerticalOffset = lastMouseDiu.Y + 18;
            popup.IsOpen = true;
        };

        card.MouseEnter += (_, me) =>
        {
            lastMouseDiu = ToScreenDiu(card, me.GetPosition(card));
            hoverTimer.Stop();
            hoverTimer.Start();
        };

        card.MouseMove += (_, me) =>
        {
            lastMouseDiu = ToScreenDiu(card, me.GetPosition(card));
            if (popup.IsOpen)
            {
                // already open — just reposition
                PositionPopup(popup, bubbleBorder, lastMouseDiu);
            }
            else
            {
                // not yet open — reset the delay on movement
                hoverTimer.Stop();
                hoverTimer.Start();
            }
        };

        popup.Opened += (_, _) =>
            PositionPopup(popup, bubbleBorder, lastMouseDiu);

        card.MouseLeave += (_, _) =>
        {
            hoverTimer.Stop();
            popup.IsOpen = false;
        };
    }

    /// <summary>Converts a local element point to screen coords in device-independent units.</summary>
    private static Point ToScreenDiu(Visual source, Point localPoint)
    {
        var physPx = source.PointToScreen(localPoint);
        var ps = PresentationSource.FromVisual(source);
        if (ps?.CompositionTarget == null) return physPx;
        return ps.CompositionTarget.TransformFromDevice.Transform(physPx);
    }

    private static void PositionPopup(Popup popup, FrameworkElement child, Point mouseDiu)
    {
        double popupW = child.ActualWidth > 0 ? child.ActualWidth : 480;
        double popupH = child.ActualHeight > 0 ? child.ActualHeight : 200;

        var work = SystemParameters.WorkArea;

        // ── X: appear to the right of cursor; flip left if it overflows ────
        double x = mouseDiu.X + 18;
        if (x + popupW > work.Right)
            x = mouseDiu.X - popupW - 8;
        x = Math.Max(work.Left, x);

        // ── Y: try below → try above → center vertically ──────────────────
        double y;
        double yBelow = mouseDiu.Y + 18;
        double yAbove = mouseDiu.Y - popupH - 8;

        if (yBelow + popupH <= work.Bottom)
            y = yBelow;                                       // fits below
        else if (yAbove >= work.Top)
            y = yAbove;                                       // fits above
        else
            y = work.Top + ((work.Height - popupH) / 2.0);     // center as last resort

        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - popupH));

        popup.HorizontalOffset = x;
        popup.VerticalOffset = y;
    }

    // ── Window state persistence ──────────────────────────────────────────

    private static readonly string WindowStatePath = System.IO.Path.Combine(
        MinimalNotepad.Config.AppDataPath.Root, "clipboard_window_state.json");

    private record ClipboardWindowState(double Left, double Top, double Width, double Height, bool HasPosition, bool SingleLine = false, HistoryMode ViewMode = HistoryMode.App);

    private static ClipboardWindowState LoadWindowState()
    {
        try
        {
            if (File.Exists(WindowStatePath))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, double>>(
                            File.ReadAllText(WindowStatePath));
                if (d != null)
                {
                    bool singleLine = d.TryGetValue("SingleLine", out double sl) && sl > 0;
                    var viewMode = d.TryGetValue("ViewMode", out double vm)
                                      ? (HistoryMode)(int)vm
                                      : HistoryMode.App;
                    return new ClipboardWindowState(d["Left"], d["Top"], d["Width"], d["Height"], true, singleLine, viewMode);
                }
            }
        }
        catch { }
        return new ClipboardWindowState(0, 0, 380, 560, false);
    }

    private static void SaveWindowState(double left, double top, double width, double height,
                                bool singleLine = false,
                                HistoryMode viewMode = HistoryMode.App)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowStatePath)!);
            var d = new Dictionary<string, double>
            {
                ["Left"] = left,
                ["Top"] = top,
                ["Width"] = width,
                ["Height"] = height,
                ["SingleLine"] = singleLine ? 1.0 : 0.0,
                ["ViewMode"] = (double)(int)viewMode
            };
            File.WriteAllText(WindowStatePath, JsonSerializer.Serialize(d));
        }
        catch { }
    }
}
