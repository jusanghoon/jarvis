using System;
using System.Windows;
using System.Windows.Controls;
using javis.Models;
using javis.ViewModels;

namespace javis.Pages;

public partial class HomePage : Page
{
    private readonly HomeViewModel _vm = new();

    public HomePage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += (_, __) => _vm.Refresh();
    }

    private void UpcomingList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not CalendarTodoItem item) return;

        if (Application.Current.MainWindow is MainWindow mw)
            mw.NavigateToTodos(item.Date);
    }

    private void Calendar_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.SelectedDate == default) return;

        if (Application.Current.MainWindow is MainWindow mw)
            mw.NavigateToTodos(_vm.SelectedDate);
    }
}
