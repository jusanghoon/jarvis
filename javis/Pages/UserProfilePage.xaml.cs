using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using javis.Services;

namespace javis.Pages;

public partial class UserProfilePage : Page
{
    private readonly UserProfileService _profiles = UserProfileService.Instance;
    private UserProfile _profile;

    private bool _suspendAutoRefresh;

    public UserProfilePage(string userId)
    {
        InitializeComponent();

        _profiles.EnsureDefaultProfile();
        _profile = _profiles.TryGetProfile(userId) ?? new UserProfile(userId, userId, DateTimeOffset.Now);
        DataContext = _profile;

        if (ReportBox != null)
            ReportBox.Text = _profile.ToReportText(includeSources: false);

        Loaded += (_, __) => _profiles.ProfileChanged += OnProfileChanged;
        Unloaded += (_, __) => _profiles.ProfileChanged -= OnProfileChanged;

        if (ReportBox != null)
        {
            ReportBox.GotKeyboardFocus += (_, __) => _suspendAutoRefresh = true;
            ReportBox.LostKeyboardFocus += (_, __) => _suspendAutoRefresh = false;
        }
    }

    private void OnProfileChanged(string id)
    {
        try
        {
            if (_suspendAutoRefresh) return;
            if (!string.Equals(id, _profile.Id, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.InvokeAsync(() =>
            {
                if (_suspendAutoRefresh) return;

                var latest = _profiles.TryGetProfile(_profile.Id);
                if (latest == null) return;

                _profile = latest;
                DataContext = _profile;

                if (ReportBox != null)
                    ReportBox.Text = _profile.ToReportText(includeSources: false);
            });
        }
        catch
        {
            // ignore
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new UserSelectPage());
    }

    private void ExportTsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "프로필 TSV 저장 (엘셀로 열 수 있음)",
                Filter = "TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
                FileName = $"profile-{_profile.DisplayName}-{_profile.Id}.tsv"
            };

            if (dlg.ShowDialog() != true) return;

            var edited = _profile.WithReportText(ReportBox?.Text ?? "");

            // B mode: hide sources by default in export as well
            var filtered = edited with
            {
                Fields = edited.Fields
                    .Where(kv => !kv.Key.EndsWith("_source", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            };

            _profiles.ExportProfileAsTsv(filtered, dlg.FileName);
        }
        catch
        {
            // ignore
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = (_profile.DisplayName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("이름을 입력해줘.");
            return;
        }

        var edited = _profile.WithReportText(ReportBox?.Text ?? "");
        edited = edited with { DisplayName = name };

        _profile = edited;
        DataContext = _profile;

        _profiles.SaveProfile(_profile);

        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new UserSelectPage());
    }

    private void OpenChangeLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigationService?.Navigate(new MainAiChangeLogPage(_profile.Id));
        }
        catch
        {
            // ignore
        }
    }
}
