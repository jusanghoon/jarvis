using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using javis.Models;
using javis.Services;

namespace javis.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly CalendarTodoStore _store = new();

    public HomeViewModel()
    {
        SelectedDate = DateTime.Today;
        Refresh();
    }

    [ObservableProperty]
    private DateTime _selectedDate;

    public ObservableCollection<CalendarTodoItem> UpcomingTop3 { get; } = new();

    public ObservableCollection<DateTime> TodoDates { get; } = new();

    [ObservableProperty]
    private string _summaryText = "";

    public void Refresh()
    {
        var items = _store
            .GetUpcoming(DateTime.Today, 30)
            .Where(x => !x.IsDone)
            .OrderBy(x => x.Date.Date)
            .ThenBy(x => x.Time ?? TimeSpan.MaxValue)
            .Take(3)
            .ToList();

        UpcomingTop3.Clear();
        foreach (var it in items)
            UpcomingTop3.Add(it);

        SummaryText = items.Count == 0
            ? "다가오는 할 일이 없습니다."
            : $"다가오는 할 일 {items.Count}개 (상위 3개 표시)";

        TodoDates.Clear();
        foreach (var d in _store.LoadAll()
                                .Select(x => x.Date.Date)
                                .Distinct()
                                .OrderBy(x => x))
        {
            TodoDates.Add(d);
        }
    }
}
