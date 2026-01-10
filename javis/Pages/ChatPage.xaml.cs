using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Jarvis.Core.Archive;
using javis.Services;
using javis.Services.Solo;
using javis.ViewModels;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private readonly ChatViewModel _vm = new();

    private const bool UseSoloOrchestrator = true;

    private SoloOrchestrator? _soloOrch;
    private long _nextUserMsgId;

    // DUO run cancellation (prevents late debate completion after switching pages)
    private CancellationTokenSource? _duoCts;
    private Task? _duoTask;

    // debate debug UI preview toggle (runtime)
    private bool _debateShow = false;
    private bool _forceDebate = false;

    // MainChat autosave session guard
    private string? _mainChatSessionId;
    private int _mainChatSaveGate; // 0 = not saved, 1 = saving/saved
    private DateTimeOffset _mainChatStartedAt;

    private PluginHost Host => PluginHost.Instance;

    private JarvisKernel Kernel => javis.App.Kernel;

    private readonly ChatMode _mode;

    public ChatPage() : this(ChatMode.MainChat)
    {
    }

    public ChatPage(ChatMode mode)
    {
        _mode = mode;

        InitializeComponent();
        DataContext = _vm;

        _vm.ScrollToEndRequested += ScrollToEnd;

        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
        IsVisibleChanged += ChatPage_IsVisibleChanged;

        UserReloadBus.ActiveUserChanged += OnActiveUserChanged;
    }

    private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();

        ChatBus.MessageQueued += OnBusMessage;

        while (ChatBus.TryDequeue(out var t))
            _ = _vm.SendExternalAsync(t);

        EnsureSoloOrchestrator();

        var vm = (ChatViewModel)DataContext;

        // default: hide history button unless MainChat
        if (MainHistoryButton != null)
            MainHistoryButton.Visibility = _mode == ChatMode.MainChat ? Visibility.Visible : Visibility.Collapsed;

        // Start a new MainChat session if needed
        if (_mode == ChatMode.MainChat)
        {
            _mainChatStartedAt = DateTimeOffset.Now;
            _mainChatSessionId ??= _mainChatStartedAt.ToString("yyyyMMdd_HHmmss_fff");
            System.Threading.Interlocked.Exchange(ref _mainChatSaveGate, 0);
        }

        switch (_mode)
        {
            case ChatMode.MainChat:
                vm.SelectedRoom = javis.ViewModels.ChatRoom.Main;
                RoomMain_Click(this, new RoutedEventArgs());

                // Hard lock: ensure no SOLO/DUO background loop is running
                if (_soloOrch != null)
                {
                    try { await _soloOrch.StopAsync(); } catch { }
                }

                try { _duoCts?.Cancel(); } catch { }
                try { if (_duoTask != null) await _duoTask; } catch { }

                _debateShow = false;
                _forceDebate = false;

                if (SoloToggle != null) SoloToggle.IsChecked = false;
                if (DuoToggle != null) DuoToggle.IsChecked = false;

                if (RoomTabsPanel != null) RoomTabsPanel.Visibility = Visibility.Collapsed;
                if (BigModeButtonsPanel != null) BigModeButtonsPanel.Visibility = Visibility.Collapsed;
                break;

            case ChatMode.SoloThink:
                vm.SelectedRoom = javis.ViewModels.ChatRoom.Solo;
                RoomSolo_Click(this, new RoutedEventArgs());
                if (SoloToggle != null) SoloToggle.IsChecked = true;
                if (DuoToggle != null) DuoToggle.IsChecked = false;

                if (RoomTabsPanel != null) RoomTabsPanel.Visibility = Visibility.Collapsed;
                break;

            case ChatMode.DuoDebate:
                vm.SelectedRoom = javis.ViewModels.ChatRoom.Duo;
                RoomDuo_Click(this, new RoutedEventArgs());
                if (SoloToggle != null) SoloToggle.IsChecked = true;
                if (DuoToggle != null) DuoToggle.IsChecked = true;

                if (RoomTabsPanel != null) RoomTabsPanel.Visibility = Visibility.Collapsed;
                break;
        }

        // In mode pages, room is fixed; hide room tabs to simplify UX.
        if (RoomMainTab != null) RoomMainTab.Visibility = Visibility.Collapsed;
        if (RoomSoloTab != null) RoomSoloTab.Visibility = Visibility.Collapsed;
        if (RoomDuoTab != null) RoomDuoTab.Visibility = Visibility.Collapsed;

        // Hide execution toggles on Main chat page (optional, keeps UX clean)
        if (_mode == ChatMode.MainChat)
        {
            if (SoloToggle != null) SoloToggle.Visibility = Visibility.Collapsed;
            if (DuoToggle != null) DuoToggle.Visibility = Visibility.Collapsed;
        }
    }

    private async void ChatPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (_mode != ChatMode.MainChat) return;
            if (e.NewValue is bool b && b == false)
                await SaveMainChatHistoryIfNeededAsync("hidden", CancellationToken.None);
        }
        catch
        {
            // never crash on visibility change
        }
    }

    private void AppendAssistant(string text)
    {
        ChatBus.Send(ChatTextUtil.SanitizeUiText(text));
    }

    private void AddImmediate(string role, string text)
    {
        var vm = (ChatViewModel)DataContext;
        vm.MainMessages.Add(new javis.Models.ChatMessage(role, ChatTextUtil.SanitizeUiText(text)));
    }

    private void AddImmediate(javis.ViewModels.ChatRoom room, string role, string text)
    {
        var vm = (ChatViewModel)DataContext;
        vm.GetRoom(room).Add(new javis.Models.ChatMessage(role, ChatTextUtil.SanitizeUiText(text)));
    }

    private void SetSoloStatus(string text)
        => _ = UiAsync(() =>
        {
            if (SoloStatusText == null) return;
            SoloStatusText.Text = ChatTextUtil.SanitizeUiText(text ?? "");
        });

    private async void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ChatBus.MessageQueued -= OnBusMessage;

        try { await SaveMainChatHistoryIfNeededAsync("unloaded", CancellationToken.None); } catch { }

        try { _duoCts?.Cancel(); } catch { }
        try { if (_duoTask != null) await _duoTask; } catch { }
        try { _duoCts?.Dispose(); } catch { }
        _duoCts = null;
        _duoTask = null;

        if (_soloOrch != null)
            _ = _soloOrch.DisposeAsync();
    }

    private void EnsureSoloOrchestrator()
    {
        if (_soloOrch != null) return;

        var sink = new ChatPageSoloUiSink(
            Dispatcher,
            addMessage: (room, role, text) =>
            {
                var vm = (ChatViewModel)DataContext;
                vm.GetRoom(room).Add(new javis.Models.ChatMessage(role, ChatTextUtil.SanitizeUiText(text)));
                return true;
            },
            appendDebug: (t) => { /* keep debug quiet by default */ });

        var backend = new ChatPageSoloBackendAdapter(SoloProcessOneTurnAsync);
        _soloOrch = new SoloOrchestrator(sink, backend);
    }

    private void DuoToggle_Checked(object sender, RoutedEventArgs e)
    {
        _debateShow = true;
        _forceDebate = true;

        if (SoloToggle.IsChecked != true)
            SoloToggle.IsChecked = true;
    }

    private void DuoToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _debateShow = false;
        _forceDebate = false;
    }

    private void OpenPersonaFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Kernel.Persona.PersonaDir;

        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });

        ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"📂 Persona folder opened: {dir}"));
    }

    private void ReloadPersona_Click(object sender, RoutedEventArgs e)
    {
        Kernel.Persona.Reload();

        ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage(
            "assistant",
            "✅ Persona reloaded (core/chat/solo txt)"
        ));
    }

    private async void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select files to import into Jarvis",
            Multiselect = true,
            Filter =
                "All files (*.*)|*.*|" +
                "Documents (*.txt;*.md;*.docx;*.pdf)|*.txt;*.md;*.docx;*.pdf|" +
                "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            javis.Services.MainAi.MainAiEventBus.Publish(
                new javis.Services.MainAi.ProgramEventObserved(DateTimeOffset.Now, $"[files.import] count={dlg.FileNames.Length}", "files.import"));
        }
        catch { }

        ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"⏳ Importing {dlg.FileNames.Length} files..."));

        try
        {
            var imported = await Kernel.PersonalVault.ImportAsync(dlg.FileNames);
            var indexedCount = await Kernel.PersonalVaultIndex.IndexNewAsync(imported, maxFiles: 10);

            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"🔎 Indexed: {indexedCount} files"));

            var lines = imported
                .Select(x => $"- {x.fileName} ({x.sizeBytes} bytes, {x.ext})")
                .ToList();

            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage(
                "assistant",
                "✅ Import complete\n" + string.Join("\n", lines) +
                $"\n\nSaved to: {Kernel.PersonalVault.InboxDir}"));
        }
        catch (Exception ex)
        {
            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"❌ Import failed: {ex.Message}"));
        }
    }

    private async void ChatRoot_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        try
        {
            javis.Services.MainAi.MainAiEventBus.Publish(
                new javis.Services.MainAi.ProgramEventObserved(DateTimeOffset.Now, $"[files.drop] count={files.Length}", "files.drop"));
        }
        catch { }

        ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"⏳ Importing dropped files ({files.Length})..."));

        try
        {
            var imported = await Kernel.PersonalVault.ImportAsync(files);
            var indexedCount = await Kernel.PersonalVaultIndex.IndexNewAsync(imported, maxFiles: 10);
            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"🔎 Indexed: {indexedCount} files"));
            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"✅ Dropped files saved to: {Kernel.PersonalVault.InboxDir}"));
        }
        catch (Exception ex)
        {
            ((ChatViewModel)DataContext).MainMessages.Add(new javis.Models.ChatMessage("assistant", $"❌ Drop import failed: {ex.Message}"));
        }
    }

    private async void ExportSft_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "SFT 데이터셋 저장",
            Filter = "JSONL (*.jsonl)|*.jsonl",
            FileName = $"jarvis-sft-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl"
        };

        if (dlg.ShowDialog() != true) return;

        var vm = (ChatViewModel)DataContext;
        vm.MainMessages.Add(new javis.Models.ChatMessage("assistant", "⏳ SFT 데이터셋 저장 중..."));

        try
        {
            var n = await Host.Exporter.ExportChatMlJsonlAsync(
                outputPath: dlg.FileName,
                includeCanon: true,
                includeSoloNotes: true,
                maxCanonItems: 800,
                maxNotesItems: 400,
                ct: CancellationToken.None);

            vm.MainMessages.Add(new javis.Models.ChatMessage("assistant", $"✅ SFT 저장 완료: {n} samples\n{dlg.FileName}"));
        }
        catch (Exception ex)
        {
            vm.MainMessages.Add(new javis.Models.ChatMessage("assistant", $"❌ SFT 저장 실패: {ex.Message}"));
        }
    }

    private void RoomMain_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ChatViewModel)DataContext;
        vm.SelectedRoom = javis.ViewModels.ChatRoom.Main;
        RoomMainTab.IsChecked = true;
        RoomSoloTab.IsChecked = false;
        RoomDuoTab.IsChecked = false;
    }

    private void RoomSolo_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ChatViewModel)DataContext;
        vm.SelectedRoom = javis.ViewModels.ChatRoom.Solo;
        RoomMainTab.IsChecked = false;
        RoomSoloTab.IsChecked = true;
        RoomDuoTab.IsChecked = false;
    }

    private void RoomDuo_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ChatViewModel)DataContext;
        vm.SelectedRoom = javis.ViewModels.ChatRoom.Duo;
        RoomMainTab.IsChecked = false;
        RoomSoloTab.IsChecked = false;
        RoomDuoTab.IsChecked = true;
    }

    private static javis.Services.History.ChatMessageDto ToDto(javis.Models.ChatMessage m)
        => new()
        {
            Role = m.Role ?? "",
            Text = m.Text ?? "",
            Ts = new DateTimeOffset(m.CreatedAt)
        };

    private javis.Services.History.ChatHistoryStore CreateMainChatHistoryStore()
    {
        var dataDir = UserProfileService.Instance.ActiveUserDataDir;
        return new javis.Services.History.ChatHistoryStore(dataDir);
    }

    private string BuildHistoryTitleFast(IReadOnlyList<javis.Models.ChatMessage> msgs)
    {
        var firstUser = msgs.FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Text ?? "";
        firstUser = (firstUser ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (firstUser.Length > 42) firstUser = firstUser.Substring(0, 42);

        if (string.IsNullOrWhiteSpace(firstUser))
            return $"{_mainChatStartedAt:MM/dd HH:mm} 대화";

        return firstUser;
    }

    private async Task SaveMainChatHistoryIfNeededAsync(string reason, CancellationToken ct = default)
    {
        if (_mode != ChatMode.MainChat) return;

        // gate: prevent re-entry / duplication across Back+Unloaded
        if (System.Threading.Interlocked.CompareExchange(ref _mainChatSaveGate, 1, 0) != 0)
            return;

        try
        {
            var vm = (ChatViewModel)DataContext;

            // UI thread snapshot to avoid cross-thread access
            var snapshot = await UiAsync(() => vm.MainMessages.ToList());
            if (snapshot.Count == 0) return;

            static string Clean(string? s)
                => (s ?? "").Replace("\u200B", "").Trim();

            // remove empty/placeholder msgs (prevents blank bubbles & empty sessions)
            snapshot = snapshot
                .Select(m => new javis.Models.ChatMessage(m.Role ?? "", Clean(m.Text)))
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .ToList();

            if (snapshot.Count == 0) return;

            // skip trivial sessions (at least one user message)
            if (!snapshot.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))) return;

            var title = BuildHistoryTitleFast(snapshot);

            var store = CreateMainChatHistoryStore();
            var dto = snapshot.Select(ToDto).ToList();

            var id = _mainChatSessionId ?? DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff");
            await store.SaveOrUpdateSessionAsync(id, title, _mainChatStartedAt, dto, ct);
        }
        catch
        {
            // never block navigation on history save failure
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveMainChatHistoryIfNeededAsync("back", CancellationToken.None);

            if (NavigationService?.CanGoBack == true)
            {
                NavigationService.GoBack();
                return;
            }

            NavigationService?.Navigate(new ChatEntryPage());
        }
        catch
        {
            // never crash on navigation
        }
    }

    private void MainHistory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigationService?.Navigate(new MainChatHistoryPage());
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyTopic_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ChatViewModel)DataContext;

        var topic = (vm.Topic ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            AddImmediate("assistant", "주제를 입력해줘.");
            return;
        }

        if (!vm.SetTopicOnce(topic))
        {
            AddImmediate("assistant", "주제는 이미 설정됐어. 바꾸려면 새 대화를 시작해줘.");
            return;
        }

        if (TopicBox != null) TopicBox.IsEnabled = false;
        if (sender is Button b) b.IsEnabled = false;

        AddImmediate("assistant", $"✅ 주제 고정: {topic}\n이제 이 대화는 해당 주제로만 진행할게.");
    }

    private void OnActiveUserChanged(string userId)
    {
        try
        {
            if (_mode != ChatMode.MainChat) return;

            _ = Dispatcher.InvokeAsync(async () =>
            {
                try { await SaveMainChatHistoryIfNeededAsync("user.changed", CancellationToken.None); } catch { }

                _mainChatStartedAt = DateTimeOffset.Now;
                _mainChatSessionId = _mainChatStartedAt.ToString("yyyyMMdd_HHmmss_fff");
                System.Threading.Interlocked.Exchange(ref _mainChatSaveGate, 0);

                var vm = (ChatViewModel)DataContext;
                vm.MainMessages.Clear();
                vm.MainMessages.Add(new javis.Models.ChatMessage("assistant", $"유저 전환됨: {UserProfileService.Instance.ActiveUserName}"));
            });
        }
        catch { }
    }
}
