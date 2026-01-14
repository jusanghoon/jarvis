using System.Windows.Controls;
using javis.ViewModels;

namespace javis.Pages;

public partial class MapPage : Page
{
    public MapPage()
    {
        InitializeComponent();
        DataContext = new MapViewModel();
    }
}
