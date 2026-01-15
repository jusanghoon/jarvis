using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Services;
using javis.Services.Device;

namespace javis.Pages;

public partial class SettingsPage : Page
{
    private readonly DisplayInfoVm _display = new();
    private readonly DispatcherTimer _timer;

    public SettingsPage()
    {
        InitializeComponent();

        var settings = RuntimeSettings.Instance;
        _display.Attach(settings);
        DataContext = _display;

        // ensure profiles service initializes
        _ = UserProfileService.Instance;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, __) => _display.RefreshBestEffort(this);
        _timer.Start();

        Loaded += (_, __) => _display.RefreshBestEffort(this);
        Unloaded += (_, __) => _timer.Stop();
    }

    private void UserSelect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigationService?.Navigate(new UserSelectPage());
        }
        catch
        {
            // ignore
        }
    }

    public sealed partial class DisplayInfoVm : ObservableObject
    {
        private RuntimeSettings? _settings;

        private bool _deviceInfoInitialized;
        private string _deviceId = "";
        private double _uiScaleOverrideValue = 1.0;

        [ObservableProperty]
        private string _resolutionSummary = "";

        public Visibility IsResolutionVisible
            => (_settings?.SettingsShowResolution ?? false) ? Visibility.Visible : Visibility.Collapsed;

        public void Attach(RuntimeSettings settings)
        {
            _settings = settings;
            _settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RuntimeSettings.SettingsShowResolution))
                    OnPropertyChanged(nameof(IsResolutionVisible));
            };
        }

        public string Model
        {
            get => _settings?.Model ?? "";
            set { if (_settings != null) _settings.Model = value; }
        }

        public string MainAiName
        {
            get => _settings?.MainAiName ?? "";
            set { if (_settings != null) _settings.MainAiName = value; }
        }

        public bool HomeRightPanelEnabled
        {
            get => _settings?.HomeRightPanelEnabled ?? true;
            set { if (_settings != null) _settings.HomeRightPanelEnabled = value; }
        }

        public double UiScale
        {
            get => _settings?.UiScale ?? 1.0;
            set { if (_settings != null) _settings.UiScale = value; }
        }

        public bool SettingsShowResolution
        {
            get => _settings?.SettingsShowResolution ?? false;
            set { if (_settings != null) _settings.SettingsShowResolution = value; }
        }

        public bool LocalDeviceDiagnosticsEnabled
        {
            get => _settings?.LocalDeviceDiagnosticsEnabled ?? true;
            set { if (_settings != null) _settings.LocalDeviceDiagnosticsEnabled = value; }
        }

        public string DeviceId
        {
            get => _deviceId;
            private set => SetProperty(ref _deviceId, value);
        }

        public double UiScaleOverrideValue
        {
            get => _uiScaleOverrideValue;
            set => SetProperty(ref _uiScaleOverrideValue, value);
        }

        public string UiScaleOverrideValueText
        {
            get => UiScaleOverrideValue.ToString("0.##");
            set
            {
                if (double.TryParse((value ?? "").Trim(), out var v))
                {
                    v = Math.Clamp(v, 0.5, 2.0);
                    UiScaleOverrideValue = v;
                }
            }
        }

        [RelayCommand]
        private void SetUiScaleOverrideToCurrent()
        {
            try
            {
                if (_settings == null) return;
                var v = _settings.UiScale;
                if (!DeviceSettingsOverride.IsValidUiScale(v)) return;
                UiScaleOverrideValue = Math.Clamp(v, 0.5, 2.0);
                OnPropertyChanged(nameof(UiScaleOverrideValueText));
            }
            catch { }
        }

        [RelayCommand]
        private void SaveUiScaleOverride()
        {
            if (string.IsNullOrWhiteSpace(DeviceId)) return;
            if (!DeviceSettingsOverride.IsValidUiScale(UiScaleOverrideValue)) return;

            var store = new DeviceSettingsOverrideStore(UserProfileService.Instance.ActiveUserDataDir);
            store.Save(new DeviceSettingsOverride { DeviceId = DeviceId, UiScaleOverride = UiScaleOverrideValue });
            if (_settings != null) _settings.UiScale = UiScaleOverrideValue;
        }

        [RelayCommand]
        private void ClearUiScaleOverride()
        {
            if (string.IsNullOrWhiteSpace(DeviceId)) return;
            var store = new DeviceSettingsOverrideStore(UserProfileService.Instance.ActiveUserDataDir);
            store.Save(new DeviceSettingsOverride { DeviceId = DeviceId, UiScaleOverride = null });
        }

        public void RefreshBestEffort(FrameworkElement fe)
        {
            if (!_deviceInfoInitialized)
            {
                try
                {
                    var fp = DeviceFingerprintProvider.GetFingerprintBestEffort();
                    DeviceId = fp.DeviceId;

                    // Prime override editor with saved value if present.
                    var store = new DeviceSettingsOverrideStore(UserProfileService.Instance.ActiveUserDataDir);
                    var ov = store.Load(DeviceId);
                    if (ov.UiScaleOverride is double uis && DeviceSettingsOverride.IsValidUiScale(uis))
                        UiScaleOverrideValue = uis;
                    else if (_settings != null && DeviceSettingsOverride.IsValidUiScale(_settings.UiScale))
                        UiScaleOverrideValue = _settings.UiScale;

                    OnPropertyChanged(nameof(UiScaleOverrideValueText));
                }
                catch { }
                finally { _deviceInfoInitialized = true; }
            }

            if (_settings?.SettingsShowResolution != true)
            {
                ResolutionSummary = "";
                return;
            }

            try
            {
                // DIP-based screen size reported by WPF
                var dipW = SystemParameters.PrimaryScreenWidth;
                var dipH = SystemParameters.PrimaryScreenHeight;

                // DPI scale for this visual
                var dpi = VisualTreeHelper.GetDpi(fe);
                var scaleX = dpi.DpiScaleX;
                var scaleY = dpi.DpiScaleY;

                var pxW = (int)Math.Round(dipW * scaleX);
                var pxH = (int)Math.Round(dipH * scaleY);

                ResolutionSummary = $"화면: {pxW}×{pxH}px (WPF {dipW:0}×{dipH:0} DIP)\nDPI: {dpi.PixelsPerInchX:0}×{dpi.PixelsPerInchY:0}  |  Scale: {scaleX:0.##}×{scaleY:0.##}";
            }
            catch
            {
                ResolutionSummary = "해상도 정보를 가져오지 못했어.";
            }
        }
    }

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigationService?.Navigate(new UpdatesPage());
        }
        catch
        {
            // ignore
        }
    }
}
