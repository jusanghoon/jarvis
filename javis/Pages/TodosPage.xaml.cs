using System;
using System.Windows.Controls;
using javis.ViewModels;

namespace javis.Pages;

public partial class TodosPage : Page
{
    private readonly TodosViewModel _vm = new();

    public TodosPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    public void SetDate(DateTime date)
    {
        _vm.SelectedDate = date.Date;
    }
}
