using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Jarvis.Core.Archive;
using javis.Models;
using javis.Services;

namespace javis.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private CalendarTodoStore _store;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public HomeViewModel()
    {
        _store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);

        UserReloadBus.ActiveUserChanged += _ =>
        {
            try { _store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir); } catch { }
            var _ignore = Refresh();
        };

        TodoBus.Changed += () =>
        {
            var _ignore = Refresh();
        };

        SelectedDate = DateTime.Today;
        var _ignore2 = Refresh();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        try
        {
            RefreshSelectedDateTodos();
        }
        catch { }
    }

    private void RefreshSelectedDateTodos()
    {
        var list = _store.GetByDate(SelectedDate)
            .Where(x => !x.IsDone)
            .OrderBy(x => x.Time ?? TimeSpan.MaxValue)
            .ThenBy(x => x.Title)
            .ToList();

        SelectedDateTodos.Clear();
        foreach (var it in list)
            SelectedDateTodos.Add(it);
    }

    [ObservableProperty]
    private DateTime _selectedDate;

    public ObservableCollection<CalendarTodoItem> SelectedDateTodos { get; } = new();

    public ObservableCollection<CalendarTodoItem> UpcomingTop3 { get; } = new();

    public ObservableCollection<DateTime> TodoDates { get; } = new();

    [ObservableProperty]
    private string _summaryText = "";

    public ObservableCollection<FossilEntry> RecentFossils { get; } = new();

    public bool IsFossilsLoading { get; private set; }

    public string? FossilsError { get; private set; }

    public async Task Refresh()
    {
        if (!await _refreshGate.WaitAsync(0)) return;

        try
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
                : $"다가오는 할 일 {items.Count}개 (최대 3개 표시)";

            TodoDates.Clear();
            foreach (var d in _store.LoadAll()
                                    .Select(x => x.Date.Date)
                                    .Distinct()
                                    .OrderBy(x => x))
            {
                TodoDates.Add(d);
            }

            RefreshSelectedDateTodos();

            var kernel = javis.App.Kernel;
            if (kernel?.Fossils is null) return;

            try
            {
                IsFossilsLoading = true;
                FossilsError = null;
                OnPropertyChanged(nameof(IsFossilsLoading));
                OnPropertyChanged(nameof(FossilsError));

                var fossils = await kernel.Fossils.GetRecentFossilsAsync(10, scanDays: 7);

                RecentFossils.Clear();
                foreach (var x in fossils)
                    RecentFossils.Add(x);
            }
            catch (Exception ex)
            {
                FossilsError = ex.Message;
                OnPropertyChanged(nameof(FossilsError));
            }
            finally
            {
                IsFossilsLoading = false;
                OnPropertyChanged(nameof(IsFossilsLoading));
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
