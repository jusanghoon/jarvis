using System;
using System.Windows;
using System.Windows.Controls;
using javis.ViewModels;

namespace javis.Pages;

public partial class HomeCalendarPage : Page
{
    public HomeCalendarPage(HomeViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenTodos_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not HomeViewModel vm) return;
            if (Application.Current.MainWindow is not MainWindow mw) return;
            mw.NavigateToTodos(vm.SelectedDate);
        }
        catch
        {
            // ignore
        }
    }
}
