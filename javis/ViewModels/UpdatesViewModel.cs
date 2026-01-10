using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Services;
using javis.Services.MainAi;

namespace javis.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private MainAiDocSuggestionStore _store;
    private MainAiReleaseNotesStore _release;

    public UpdatesViewModel()
    {
        _store = new MainAiDocSuggestionStore(UserProfileService.Instance.ActiveUserDataDir);
        _release = new MainAiReleaseNotesStore(UserProfileService.Instance.ActiveUserDataDir);
        Refresh();

        UserReloadBus.ActiveUserChanged += _ =>
        {
            try
            {
                _store = new MainAiDocSuggestionStore(UserProfileService.Instance.ActiveUserDataDir);
                _release = new MainAiReleaseNotesStore(UserProfileService.Instance.ActiveUserDataDir);
            }
            catch { }
            Refresh();
        };

        MainAiDocBus.Suggestion += _ =>
        {
            try { Refresh(); } catch { }
        };
    }

    public ObservableCollection<MainAiDocSuggestionStore.DocSuggestionItem> DocSuggestions { get; } = new();
    public ObservableCollection<MainAiReleaseNotesStore.ReleaseNoteItem> ReleaseNotes { get; } = new();

    [RelayCommand]
    private void Refresh()
    {
        DocSuggestions.Clear();
        foreach (var it in _store.ReadLatestOpen(20))
            DocSuggestions.Add(it);

        ReleaseNotes.Clear();
        foreach (var it in _release.ReadLatest(20))
            ReleaseNotes.Add(it);
    }

    [RelayCommand]
    private void Resolve(MainAiDocSuggestionStore.DocSuggestionItem item)
    {
        if (item is null) return;
        try { _store.MarkResolved(item.Id); } catch { }
        Refresh();
    }
}
