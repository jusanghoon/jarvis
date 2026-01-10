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
    private CalendarTodoStore _store;

    public TodosViewModel()
    {
        _store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);

        SelectedDate = DateTime.Today;
        Refresh();

        UserReloadBus.ActiveUserChanged += OnActiveUserChanged;
    }

    private void OnActiveUserChanged(string userId)
    {
        try
        {
            _store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
            Refresh();
        }
        catch { }
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
        try
        {
            javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.TodoChange, new
            {
                op = "upsert",
                id = item.Id,
                date = item.Date,
                time = item.Time,
                title = item.Title,
                isDone = item.IsDone,
                ts = DateTimeOffset.Now
            });
        }
        catch { }
        try { javis.Services.TodoBus.PublishChanged(); } catch { }
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
        try
        {
            javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.TodoChange, new
            {
                op = "toggle_done",
                id = item.Id,
                date = item.Date,
                time = item.Time,
                title = item.Title,
                isDone = item.IsDone,
                ts = DateTimeOffset.Now
            });
        }
        catch { }
        try { javis.Services.TodoBus.PublishChanged(); } catch { }
        Refresh();
    }

    [RelayCommand]
    private void Delete(CalendarTodoItem item)
    {
        _store.Delete(item.Id);
        try
        {
            javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.TodoChange, new
            {
                op = "delete",
                id = item.Id,
                ts = DateTimeOffset.Now
            });
        }
        catch { }
        try { javis.Services.TodoBus.PublishChanged(); } catch { }
        Refresh();
        Status = "삭제가 완료되었습니다.";
    }
}
