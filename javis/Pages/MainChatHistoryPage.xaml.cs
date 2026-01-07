using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using javis.Services.History;

namespace javis.Pages;

public partial class MainChatHistoryPage : Page
{
    private readonly ChatHistoryStore _store;

    private bool _opening;

    private sealed class HistoryItem
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
        public string CreatedAtText { get; init; } = "";
    }

    public sealed class HistoryMessageVm
    {
        public string Role { get; init; } = "";
        public string Text { get; init; } = "";

        public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
        public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(Role, "system", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\u200B", "").Trim();
    }

    public MainChatHistoryPage()
    {
        InitializeComponent();

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis");

        _store = new ChatHistoryStore(dataDir);
        LoadList();
    }

    private void LoadList()
    {
        var items = _store.ListSessions()
            .Select(x => new HistoryItem
            {
                Id = x.Id,
                Title = x.Title,
                CreatedAt = x.CreatedAt,
                CreatedAtText = x.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            })
            .ToList();

        SessionList.ItemsSource = items;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true) NavigationService.GoBack();
        else NavigationService?.Navigate(new ChatEntryPage());
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_opening) return;
        if (SessionList.SelectedItem is not HistoryItem item) return;

        try
        {
            _opening = true;

            var session = await _store.LoadSessionAsync(item.Id, System.Threading.CancellationToken.None);
            if (session == null)
            {
                DetailTitle.Text = "세션을 찾을 수 없습니다.";
                DetailLines.ItemsSource = null;
                return;
            }

            DetailTitle.Text = $"{session.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {session.Title}";

            DetailLines.ItemsSource = session.Messages
                .Select(m => new { Role = m.Role ?? "", Text = NormalizeText(m.Text) })
                .Where(x => x.Text.Length > 0)
                .Select(x => new HistoryMessageVm { Role = x.Role, Text = x.Text })
                .ToList();
        }
        finally
        {
            _opening = false;
        }
    }

    private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 단일 클릭/선택만으로 오른쪽 상세를 갱신
        if (SessionList.SelectedItem is null) return;
        Open_Click(sender, e);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SessionList.SelectedItem is not HistoryItem item) return;
        _store.DeleteSession(item.Id);

        DetailTitle.Text = "";
        DetailLines.ItemsSource = null;

        LoadList();
    }

    private void OpenSelected_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Open_Click(sender, e);
}
