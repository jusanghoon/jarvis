using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using javis.Pages;
using javis.Services;
using javis.ViewModels;

namespace javis
{
    public partial class MainWindow : Window
    {
        private readonly HomePage _homePage = new();
        private readonly ChatEntryPage _chatPage = new();
        private readonly TodosPage _todosPage = new();
        private readonly Pages.SkillsPage _skillsPage = new();
        private readonly SettingsPage _settingsPage = new();
        private readonly MainAiWidgetViewModel _mainAiWidgetVm = new();

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

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                MainAiWidget.DataContext = _mainAiWidgetVm;
                _mainAiWidgetVm.RequestNavigateToChat += () => Dispatcher.InvokeAsync(NavigateToChat);
            }
            catch { }

            Loaded += (_, __) =>
            {
                WindowFx.EnableAcrylic(this);
                WindowFx.EnableRoundedCorners(this);

                Title = $"JARVIS [{RuntimeSettings.Instance.Model}]";

                MainFrame.Navigate(_homePage);
                Nav.SelectedIndex = 0;
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
                "home" => _homePage,
                "chat" => _chatPage,
                "todos" => _todosPage,
                "skills" => _skillsPage,
                "settings" => _settingsPage,
                _ => _homePage
            };

            MainFrame.Navigate(page);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                RuntimeSettings.Instance.Model = "qwen3:4b";
                Title = $"JARVIS [{RuntimeSettings.Instance.Model}]";
                e.Handled = true;
            }
            else if (e.Key == Key.D2 || e.Key == Key.NumPad2)
            {
                RuntimeSettings.Instance.Model = "qwen3:8b";
                Title = $"JARVIS [{RuntimeSettings.Instance.Model}]";
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
