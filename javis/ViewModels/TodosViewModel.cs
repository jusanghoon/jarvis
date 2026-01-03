using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Models;
using javis.Services;

namespace javis.ViewModels;

public partial class TodosViewModel : ObservableObject
{
    private readonly CalendarTodoStore _store = new();

    public TodosViewModel()
    {
        SelectedDate = DateTime.Today;
        Refresh();
    }

    private DateTime _selectedDate;

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (SetProperty(ref _selectedDate, value))
                Refresh();
        }
    }

    public ObservableCollection<CalendarTodoItem> Items { get; } = new();

    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private string _newTime = "";
    [ObservableProperty] private string _status = "READY";

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        foreach (var item in _store.GetByDate(SelectedDate))
            Items.Add(item);

        Status = $"{SelectedDate:yyyy-MM-dd} / {Items.Count} items";
    }

    [RelayCommand]
    private void Add()
    {
        var title = (NewTitle ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            Status = "제목이 비어있습니다";
            return;
        }

        TimeSpan? time = null;
        var t = (NewTime ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(t))
        {
            if (TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out var ts))
                time = ts;
            else
            {
                Status = "시간 형식은 HH:mm (예: 09:30) 입니다.";
                return;
            }
        }

        var item = new CalendarTodoItem
        {
            Date = SelectedDate.Date,
            Time = time,
            Title = title,
            IsDone = false
        };

        _store.Upsert(item);
        NewTitle = "";
        NewTime = "";
        Refresh();
        Status = "추가가 완료되었습니다.";
    }

    [RelayCommand]
    private void ToggleDone(CalendarTodoItem item)
    {
        item.IsDone = !item.IsDone;
        _store.Upsert(item);
        Refresh();
    }

    [RelayCommand]
    private void Delete(CalendarTodoItem item)
    {
        _store.Delete(item.Id);
        Refresh();
        Status = "삭제가 완료되었습니다.";
    }
}
