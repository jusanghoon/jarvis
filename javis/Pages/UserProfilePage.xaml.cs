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

    public UserProfilePage(string userId)
    {
        InitializeComponent();

        _profiles.EnsureDefaultProfile();
        _profile = _profiles.TryGetProfile(userId) ?? new UserProfile(userId, userId, DateTimeOffset.Now);
        DataContext = _profile;

        if (ReportBox != null)
            ReportBox.Text = _profile.ToReportText();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new UserSelectPage());
    }

    private void AutoFill_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // user-edit buffer -> profile
            var edited = (_profile.WithReportText(ReportBox?.Text ?? ""));

            var draft = _profiles.BuildDraftFieldsFromHistory();

            // merge: keep existing values if present
            var merged = edited.Fields.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in draft)
            {
                if (!merged.TryGetValue(kv.Key, out var cur) || string.IsNullOrWhiteSpace(cur))
                    merged[kv.Key] = kv.Value;
            }

            _profile = edited with { Fields = merged };
            DataContext = _profile;

            if (ReportBox != null)
                ReportBox.Text = _profile.ToReportText();
        }
        catch
        {
            // ignore
        }
    }

    private void ExportTsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "프로필 TSV 저장 (엑셀로 열 수 있음)",
                Filter = "TSV (*.tsv)|*.tsv|All files (*.*)|*.*",
                FileName = $"profile-{_profile.DisplayName}-{_profile.Id}.tsv"
            };

            if (dlg.ShowDialog() != true) return;

            var edited = _profile.WithReportText(ReportBox?.Text ?? "");
            _profiles.ExportProfileAsTsv(edited, dlg.FileName);
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
}
