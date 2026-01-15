using System.Windows.Controls;
using javis.Services;
using javis.ViewModels;

namespace javis.Pages;

public partial class MapPage : Page
{
    private readonly MapViewModel _vm;

    public MapPage()
    {
        InitializeComponent();
        _vm = new MapViewModel();
        DataContext = _vm;

        Loaded += (_, __) =>
        {
            try { TodoBus.Changed += OnTodoChanged; } catch { }
        };

        Unloaded += (_, __) =>
        {
            try { TodoBus.Changed -= OnTodoChanged; } catch { }
        };
    }

    private void OnTodoChanged()
    {
        try
        {
            // Pull all todos from the active user store and sync any "@지역" tags into pins.json
            var store = new CalendarTodoStore(UserProfileService.Instance.ActiveUserDataDir);
            var todos = store.LoadAll();
            TodoMapSync.TrySyncFromTodos(todos);
        }
        catch { }

        try { Dispatcher.InvokeAsync(_vm.Reload); } catch { }
    }
}
