using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class MainAiChangeLogPage : Page
{
    private string _userId;

    public MainAiChangeLogPage(string userId)
    {
        InitializeComponent();
        _userId = userId;
        LoadLog();

        UserReloadBus.ActiveUserChanged += OnActiveUserChanged;
    }

    private void OnActiveUserChanged(string userId)
    {
        try
        {
            _userId = UserProfileService.Instance.ActiveUserId;
            LoadLog();
        }
        catch { }
    }

    private void LoadLog()
    {
        try
        {
            var userDir = javis.Services.UserProfileService.Instance.GetUserDataDir(_userId);
            var path = Path.Combine(userDir, "profiles", "_mainai", $"{_userId}.changes.log");

            if (!File.Exists(path))
            {
                LogBox.Text = "(로그 없음)";
                return;
            }

            var lines = File.ReadLines(path).Reverse().Take(200).Reverse();
            LogBox.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            LogBox.Text = "(로그 로드 실패) " + ex.Message;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new UserSelectPage());

        try { UserReloadBus.ActiveUserChanged -= OnActiveUserChanged; } catch { }
    }
}
