using System.Windows;
using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = RuntimeSettings.Instance;

        // ensure profiles service initializes
        _ = UserProfileService.Instance;
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
}
