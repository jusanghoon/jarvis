using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Services;
using javis.Services.Device;
using javis.Services.MainAi;

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

        _display.ThinkingStageChanged += () =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    LogScrollViewer?.UpdateLayout();
                    LogScrollViewer?.ScrollToBottom();
                }
                catch { }
            }), DispatcherPriority.Loaded);
        };

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

        private readonly FileService _fileService = new();
        private readonly MainAiHelpResponder _help = new(baseUrl: "http://localhost:11434", model: "qwen3:4b");

        private FileService.AnalysisReport? _lastScanReport;
        private string? _lastRefactorTargetPath;
        private string? _lastProposedContent;

        private readonly string _solutionRoot = AppDomain.CurrentDomain.BaseDirectory;

        private bool _deviceInfoInitialized;
        private string _deviceId = "";
        private double _uiScaleOverrideValue = 1.0;

        [ObservableProperty]
        private string _resolutionSummary = "";

        [ObservableProperty]
        private string _thinkingStage = "";

        [ObservableProperty]
        private bool _isSoloThinkingStarting;

        public event Action? ThinkingStageChanged;

        partial void OnThinkingStageChanged(string value)
        {
            try { ThinkingStageChanged?.Invoke(); } catch { }
        }

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
        private async Task ScanSystemAsync()
        {
            ThinkingStage = "[SYSTEM SCAN] 프로젝트 무결성 검사를 시작합니다...\n";
            IsSoloThinkingStarting = true;

            try
            {
                var report = await _fileService.AnalyzeProjectStructureAsync(_solutionRoot, msg =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(msg)) return;
                        ThinkingStage = (ThinkingStage ?? "") + msg + "\n";
                    }
                    catch { }
                });

                _lastScanReport = report;
                ThinkingStage = (ThinkingStage ?? "") + $"\n[SYSTEM SCAN] 요약: {report}\n";
            }
            catch (OperationCanceledException)
            {
                ThinkingStage = (ThinkingStage ?? "") + "[SYSTEM SCAN] 취소됨\n";
            }
            catch (Exception ex)
            {
                ThinkingStage = (ThinkingStage ?? "") + $"[SYSTEM SCAN] 오류: {ex.Message}\n";
            }
            finally
            {
                IsSoloThinkingStarting = false;
            }
        }

        [RelayCommand]
        private async Task ProposeRefactorAsync()
        {
            var report = _lastScanReport;
            if (report is null)
            {
                ThinkingStage = "[REFACTOR] 먼저 시스템 스캔(ScanSystemCommand)을 실행해 AnalysisReport를 생성해줘.\n";
                return;
            }

            var target = report.LargeFiles1000Lines.FirstOrDefault();
            if (target is null)
            {
                ThinkingStage = "[REFACTOR] 1000라인 이상 파일이 없어. (LargeFiles1000Lines 비어있음)\n";
                return;
            }

            IsSoloThinkingStarting = true;
            ThinkingStage = $"[REFACTOR] 분석 대상: {target.RelativePath} ({target.Lines} lines)\n";

            try
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(report.RootPath, target.RelativePath));
                var content = await _fileService.GetFileContentForAnalysisAsync(full);

                var response = await _help.AnalyzeWithCodeAsync(
                    userQuestion: "AnalysisReport 기반으로 이 파일을 분할/최적화해줘. 반드시 [REASON]과 [PLAN]을 먼저 출력해.",
                    relPath: target.RelativePath,
                    codeSnippet: content,
                    ct: CancellationToken.None);

                ThinkingStage = (ThinkingStage ?? "") + "\n" + (response ?? "").Trim();

                _lastRefactorTargetPath = target.RelativePath;
                _lastProposedContent = response;
            }
            catch (Exception ex)
            {
                ThinkingStage = (ThinkingStage ?? "") + $"\n[REFACTOR] 오류: {ex.Message}\n";
            }
            finally
            {
                IsSoloThinkingStarting = false;
            }
        }

        [RelayCommand]
        private async Task ExecuteRefactorAsync()
        {
            if (IsSoloThinkingStarting) return;

            var report = _lastScanReport;
            if (report is null)
            {
                ThinkingStage = "[SYSTEM EXECUTION] 먼저 시스템 스캔을 실행해 대상 파일을 확정해줘.\n";
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastRefactorTargetPath) || string.IsNullOrWhiteSpace(_lastProposedContent))
            {
                ThinkingStage = "[SYSTEM EXECUTION] 먼저 ProposeRefactorCommand로 제안서를 생성해줘.\n";
                return;
            }

            IsSoloThinkingStarting = true;
            ThinkingStage = (ThinkingStage ?? "") + "\n[SYSTEM EXECUTION] 리팩토링을 집행합니다. 원본은 자동 백업됩니다.\n";

            try
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(report.RootPath, _lastRefactorTargetPath));

                await _fileService.ApplyRefactorChangeAsync(full, _lastProposedContent, msg =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(msg)) return;
                        ThinkingStage = (ThinkingStage ?? "") + msg + "\n";
                    }
                    catch { }
                });

                await ScanSystemAsync();
            }
            catch (Exception ex)
            {
                ThinkingStage = (ThinkingStage ?? "") + $"[SYSTEM EXECUTION] 오류: {ex.Message}\n";
            }
            finally
            {
                IsSoloThinkingStarting = false;
            }
        }

        [RelayCommand]
        private async Task RestoreLastChangeAsync()
        {
            if (IsSoloThinkingStarting) return;

            var report = _lastScanReport;
            if (report is null)
            {
                ThinkingStage = "[SYSTEM RESTORED] 먼저 시스템 스캔을 실행해 루트 경로를 확인해줘.\n";
                return;
            }

            if (string.IsNullOrWhiteSpace(_lastRefactorTargetPath))
            {
                ThinkingStage = "[SYSTEM RESTORED] 복구할 대상 파일이 없습니다.\n";
                return;
            }

            IsSoloThinkingStarting = true;

            try
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(report.RootPath, _lastRefactorTargetPath));
                await _fileService.RestoreFromBackupAsync(full, msg =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(msg)) return;
                        ThinkingStage = (ThinkingStage ?? "") + msg + "\n";
                    }
                    catch { }
                });

                ThinkingStage = (ThinkingStage ?? "") + "[SYSTEM RESTORED] 시스템이 이전 상태로 완벽히 복구되었습니다.\n";
            }
            catch (Exception ex)
            {
                ThinkingStage = (ThinkingStage ?? "") + $"[SYSTEM RESTORED] 오류: {ex.Message}\n";
            }
            finally
            {
                IsSoloThinkingStarting = false;
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
