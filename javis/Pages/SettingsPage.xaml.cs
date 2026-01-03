using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = RuntimeSettings.Instance;
    }
}
