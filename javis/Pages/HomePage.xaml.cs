using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Jarvis.Core.Archive;
using javis.Models;
using javis.UI.Dialogs;
using javis.ViewModels;

namespace javis.Pages;

public partial class HomePage : Page
{
    private readonly HomeViewModel _vm = new();

    public HomePage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, __) => await _vm.Refresh();
    }

    private void UpcomingList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not CalendarTodoItem item) return;

        if (Application.Current.MainWindow is MainWindow mw)
            mw.NavigateToTodos(item.Date);
    }

    private void FossilList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is not ListBoxItem)
            return;

        if (FossilList.SelectedItem is not FossilEntry fe)
            return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        var w = new FossilDetailWindow(fe)
        {
            Owner = owner
        };

        w.ShowDialog();
    }

    private void Calendar_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedDate == default) return;

        if (Application.Current.MainWindow is MainWindow mw)
            mw.NavigateToTodos(_vm.SelectedDate);
    }
}
