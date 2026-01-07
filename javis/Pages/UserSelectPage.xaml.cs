using System.Linq;
using System.Windows;
using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class UserSelectPage : Page
{
    private readonly UserProfileService _profiles = UserProfileService.Instance;

    public UserSelectPage()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        var list = _profiles.ListProfiles().ToList();
        if (list.Count == 0)
        {
            _profiles.EnsureDefaultProfile();
            list = _profiles.ListProfiles().ToList();
        }

        UserList.ItemsSource = list;

        // preselect active
        var activeId = _profiles.ActiveUserId;
        UserList.SelectedItem = list.FirstOrDefault(p => p.Id == activeId);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new SettingsPage());
    }

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewUserNameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            MessageBox.Show("유저 이름을 입력해줘.");
            return;
        }

        var p = _profiles.CreateProfile(name);
        _profiles.SetActive(p.Id);

        NewUserNameBox.Text = "";
        Load();
    }

    private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserList.SelectedItem is not UserProfile p) return;

        _profiles.SetActive(p.Id);
    }

    private void EditUser_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Button btn) return;
        if (btn.DataContext is not UserProfile p) return;

        try
        {
            NavigationService?.Navigate(new UserProfilePage(p.Id));
        }
        catch
        {
            // ignore
        }
    }
}
