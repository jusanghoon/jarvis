using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using javis.Pages;
using javis.Services;
using javis.ViewModels;

namespace javis
{
    public partial class MainWindow : Window
    {
        private readonly HomeViewModel _homeVm = new();

        private readonly HomePage _homePage = new();
        private readonly ChatEntryPage _chatPage = new();
        private readonly TodosPage _todosPage = new();
        private readonly Pages.SkillsPage _skillsPage = new();
        private readonly SettingsPage _settingsPage = new();
        private readonly UpdatesPage _updatesPage = new();
        private readonly MainAiWidgetViewModel _mainAiWidgetVm = new();
        private readonly MapPage _mapPage = new();

        private bool _rightPanelEnabled = true;

        public void NavigateToTodos(DateTime date)
        {
            _todosPage.SetDate(date);

            foreach (var obj in Nav.Items)
            {
                if (obj is ListBoxItem li && (li.Tag?.ToString() == "todos"))
                {
                    Nav.SelectedItem = li;
                    break;
                }
            }
        }

        public void NavigateToChat()
        {
            foreach (var obj in Nav.Items)
            {
                if (obj is ListBoxItem li && (li.Tag?.ToString() == "chat"))
                {
                    Nav.SelectedItem = li;
                    break;
                }
            }
        }

        private void UpdateWindowTitle()
        {
            var aiName = (RuntimeSettings.Instance.MainAiName ?? "").Trim();
            if (aiName.Length == 0) aiName = "JARVIS";
            Title = $"{aiName} [{RuntimeSettings.Instance.Model}]";
        }


        public MainWindow()
        {
            InitializeComponent();

            try
            {
                _rightPanelEnabled = RuntimeSettings.Instance.HomeRightPanelEnabled;

                if (FindName("RightPanelToggle") is ToggleButton tb)
                    tb.IsChecked = _rightPanelEnabled;

                if (FindName("RightPanelFloatingToggle") is ToggleButton ft)
                    ft.IsChecked = _rightPanelEnabled;

                ApplyRightPanelState(show: _rightPanelEnabled);
            }
            catch { }

            try
            {
                MainAiWidget.DataContext = _mainAiWidgetVm;

                _mainAiWidgetVm.RequestNavigate += key => Dispatcher.InvokeAsync(() =>
                {
                    if (key is "home" or "chat" or "todos" or "skills" or "settings")
                    {
                        foreach (var obj in Nav.Items)
                        {
                            if (obj is ListBoxItem li && string.Equals(li.Tag?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                            {
                                Nav.SelectedItem = li;
                                return;
                            }
                        }
                    }
                });

                _mainAiWidgetVm.RequestAction += (name, date) => Dispatcher.InvokeAsync(() =>
                {
                    name = (name ?? "").Trim().ToLowerInvariant();
                    date = (date ?? "").Trim();

                    if (name == "open_todos_today")
                    {
                        NavigateToTodos(DateTime.Today);
                        return;
                    }

                    if (name == "open_todos_tomorrow")
                    {
                        NavigateToTodos(DateTime.Today.AddDays(1));
                        return;
                    }

                    if (name == "open_todos_date")
                    {
                        if (!string.IsNullOrWhiteSpace(date) && javis.Services.ChatActions.KstDateParser.TryParseKoreanRelativeDate(date, out var d))
                        {
                            NavigateToTodos(d);
                            return;
                        }

                        // fallback
                        NavigateToTodos(DateTime.Today);
                        return;
                    }

                    if (name == "open_chat_main")
                    {
                        NavigateToChat();
                        return;
                    }

                    if (name == "open_settings_updates")
                    {
                        // Updates live under Settings
                        foreach (var obj in Nav.Items)
                        {
                            if (obj is ListBoxItem li && string.Equals(li.Tag?.ToString(), "settings", StringComparison.OrdinalIgnoreCase))
                            {
                                Nav.SelectedItem = li;
                                break;
                            }
                        }

                        MainFrame?.Navigate(_settingsPage);
                        return;
                    }

                    if (name == "open_user_profiles")
                    {
                        // Go directly to UserSelectPage (profiles)
                        MainFrame?.Navigate(new UserSelectPage());
                        return;
                    }
                });
            }
            catch { }

            Loaded += (_, __) =>
            {
                WindowFx.EnableAcrylic(this);
                WindowFx.EnableRoundedCorners(this);

                UpdateWindowTitle();

                // Calendar(top) + Summary(bottom)
                MainFrame.Navigate(new HomeCalendarPage(_homeVm));
                AuxFrame?.Navigate(new HomeSummaryPage { DataContext = _homeVm });

                Nav.SelectedIndex = 0;
            };

            RuntimeSettings.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RuntimeSettings.Model) || e.PropertyName == nameof(RuntimeSettings.MainAiName))
                    Dispatcher.InvokeAsync(UpdateWindowTitle);
            };
        }

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainFrame is null)
                return;

            if (Nav.SelectedItem is not ListBoxItem item)
                return;

            var key = item.Tag?.ToString();

            Page page = key switch
            {
                "home" => new HomeCalendarPage(_homeVm),
                "chat" => _chatPage,
                "todos" => _todosPage,
                "map" => _mapPage,
                "skills" => _skillsPage,
                "settings" => _settingsPage,
                _ => new HomeCalendarPage(_homeVm)
            };

            MainFrame.Navigate(page);

            void SetColumnsForHome(bool isHome)
            {
                try
                {
                    // center grid is the direct parent of the pink Border
                    if (MainFrame.Parent is not Border mainBorder) return;
                    if (mainBorder.Parent is not Grid centerGrid) return;
                    if (centerGrid.Parent is not Grid bodyGrid) return;

                    // bodyGrid columns: 0 nav, 1 gap, 2 center, 3 gap, 4 right
                    if (bodyGrid.ColumnDefinitions.Count < 5) return;

                    if (isHome)
                    {
                        bodyGrid.ColumnDefinitions[1].Width = new GridLength(18);
                        bodyGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);

                        // Right rail always stays; actual right panel depends on toggle.
                        bodyGrid.ColumnDefinitions[3].Width = new GridLength(18);
                        bodyGrid.ColumnDefinitions[4].Width = _rightPanelEnabled ? new GridLength(340) : new GridLength(0);
                    }
                    else
                    {
                        // Keep rail visible on other pages too.
                        bodyGrid.ColumnDefinitions[1].Width = new GridLength(10);
                        bodyGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                        bodyGrid.ColumnDefinitions[3].Width = new GridLength(18);
                        bodyGrid.ColumnDefinitions[4].Width = _rightPanelEnabled ? new GridLength(340) : new GridLength(0);
                    }
                }
                catch { }
            }

            void SetRightPanelVisible(bool show)
            {
                try
                {
                    // Show right panel on all pages; still respect the user toggle.
                    var effective = _rightPanelEnabled;
                    if (FindName("RightPanelHost") is FrameworkElement fe)
                        fe.Visibility = effective ? Visibility.Visible : Visibility.Collapsed;
                }
                catch { }
            }

            void SetAuxPanelVisible(bool show)
            {
                try
                {
                    if (AuxFrame is null) return;

                    AuxFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

                    if (AuxFrame.Parent is Border host)
                        host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
                catch { }
            }

            void SetMainAreaRowsForHome(bool isHome)
            {
                try
                {
                    if (MainFrame.Parent is not Border mainBorder) return;
                    if (mainBorder.Parent is not Grid centerGrid) return;

                    if (centerGrid.RowDefinitions.Count < 3) return;

                    if (isHome)
                    {
                        centerGrid.RowDefinitions[0].Height = new GridLength(1.25, GridUnitType.Star);
                        centerGrid.RowDefinitions[1].Height = new GridLength(18);
                        centerGrid.RowDefinitions[2].Height = new GridLength(0.75, GridUnitType.Star);
                    }
                    else
                    {
                        centerGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                        centerGrid.RowDefinitions[1].Height = new GridLength(0);
                        centerGrid.RowDefinitions[2].Height = new GridLength(0);
                    }
                }
                catch { }
            }

            var isHome = key == "home";

            if (isHome)
            {
                AuxFrame?.Navigate(new HomeSummaryPage { DataContext = _homeVm });
            }
            else
            {
                try { AuxFrame?.Navigate(null); } catch { }
                try { AuxFrame!.Content = null; } catch { }
            }

            SetAuxPanelVisible(isHome);
            SetRightPanelVisible(isHome);
            SetMainAreaRowsForHome(isHome);
            SetColumnsForHome(isHome);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                RuntimeSettings.Instance.Model = "qwen3:4b";
                UpdateWindowTitle();
                e.Handled = true;
            }
            else if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                RuntimeSettings.Instance.Model = "qwen3:8b";
                UpdateWindowTitle();
                e.Handled = true;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        }

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Max_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void RightPanelToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _rightPanelEnabled = true;
                RuntimeSettings.Instance.HomeRightPanelEnabled = true;
                try { RuntimeSettingsStore.SaveFrom(RuntimeSettings.Instance); } catch { }

                if (FindName("RightPanelToggle") is ToggleButton tb) tb.IsChecked = true;
                if (FindName("RightPanelFloatingToggle") is ToggleButton ft) ft.IsChecked = true;

                ApplyRightPanelState(show: true);
            }
            catch { }
        }

        private void RightPanelToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _rightPanelEnabled = false;
                RuntimeSettings.Instance.HomeRightPanelEnabled = false;
                try { RuntimeSettingsStore.SaveFrom(RuntimeSettings.Instance); } catch { }

                if (FindName("RightPanelToggle") is ToggleButton tb) tb.IsChecked = false;
                if (FindName("RightPanelFloatingToggle") is ToggleButton ft) ft.IsChecked = false;

                ApplyRightPanelState(show: false);
            }
            catch { }
        }

        private void ApplyRightPanelState(bool show)
        {
            try
            {
                if (FindName("RightPanelHost") is FrameworkElement fe)
                    fe.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

                if (FindName("RightRailCol") is ColumnDefinition railCol)
                    railCol.Width = new GridLength(0);

                if (FindName("RightPanelCol") is ColumnDefinition rightCol)
                    rightCol.Width = show ? new GridLength(300) : new GridLength(0);

                if (FindName("RightPanelToggle") is FrameworkElement headerToggle)
                    headerToggle.Visibility = Visibility.Visible;

                if (FindName("RightPanelFloatingToggle") is FrameworkElement floatingToggle)
                    floatingToggle.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }
    }

    internal static class WindowFx
    {
        public static void EnableAcrylic(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;

            // 4: Acrylic(윈도우 버전에 따라 다를 수 있음)
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)0xCC1A0B00) // AABBGGRR (틴트)
            };

            var size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(accent, ptr, false);

            try
            {
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static void EnableRoundedCorners(Window window)
        {
            // Windows 11에서 둥글게 설정
            var hwnd = new WindowInteropHelper(window).Handle;
            int value = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref value, sizeof(int));
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }
    }
}
