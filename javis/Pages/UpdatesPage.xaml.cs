using System.Windows;
using System.Windows.Controls;
using javis.ViewModels;

namespace javis.Pages;

public partial class UpdatesPage : Page
{
    private readonly UpdatesViewModel _vm = new();

    public UpdatesPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new SettingsPage());
    }
}
