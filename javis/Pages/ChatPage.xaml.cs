using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
    private ChatPageSoloBackendAdapter? _soloOrchBackend;
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

    private void OnModeChanged(javis.ViewModels.ChatRoom room)
    {
        var vm = (ChatViewModel)DataContext;
        if (vm.SelectedRoom != room)
            vm.SelectedRoom = room;

        // Keep segmented buttons in sync
        if (RoomMainTab != null) RoomMainTab.IsChecked = room == javis.ViewModels.ChatRoom.Main;
        if (RoomSoloTab != null) RoomSoloTab.IsChecked = room == javis.ViewModels.ChatRoom.Solo;
        if (RoomDuoTab != null) RoomDuoTab.IsChecked = room == javis.ViewModels.ChatRoom.Duo;

        // Solo orchestrator lifecycle
        if (room == javis.ViewModels.ChatRoom.Solo || room == javis.ViewModels.ChatRoom.Duo)
        {
            EnsureSoloOrchestrator();
            _ = _soloOrch?.StartAsync();

            // [추가] 자동 사유 트리거: 버튼 클릭 없이 즉시 사유 프로세스 가동
            // Solo backend는 userText가 비어있을 때(idle=true) 내부 루프를 '유휴 사유'로 처리한다.
            var msgId = Interlocked.Increment(ref _nextUserMsgId);
            _soloOrch?.OnUserMessage(msgId, "");

            if (room == javis.ViewModels.ChatRoom.Solo)
            {
                try { SoloHeart?.IsThinking = true; } catch { }
                try
                {
                    ((ChatViewModel)DataContext).ThinkingStage = "사유 엔진 예열 중...";
                }
                catch { }
                ScrollToBottomHard();
            }

            BeginSoloTopicMode();
            ShowSoloStartQuestions();

            // 심장 가동 상태 강제 확인
            if (SoloHeart != null) SoloHeart.IsThinking = true;

            vm.ContextVars["solo_mode"] = "on";
            vm.ContextVars["user_action"] = "solo_start";

            if (SoloStatusText != null)
                SoloStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            vm.ContextVars["solo_mode"] = "off";
            vm.ContextVars["user_action"] = "solo_stop";

            try { _ = _soloOrch?.StopAsync(); } catch { }

            SetSoloStatus(string.Empty);
            if (SoloStatusText != null)
                SoloStatusText.Visibility = Visibility.Collapsed;
        }

        _debateShow = room == javis.ViewModels.ChatRoom.Duo;
        _forceDebate = room == javis.ViewModels.ChatRoom.Duo;
    }

    public ChatPage(ChatMode mode)
    {
        _mode = mode;

        InitializeComponent();
        DataContext = _vm;

        _vm.ScrollToEndRequested += ScrollToEnd;

        // Hard force scroll-to-bottom for any room when new messages arrive.
        _vm.MainMessages.CollectionChanged += (_, __) => ScrollToBottomHard();
        _vm.SoloMessages.CollectionChanged += (_, __) => ScrollToBottomHard();
        _vm.DuoMessages.CollectionChanged += (_, __) => ScrollToBottomHard();

        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
        IsVisibleChanged += ChatPage_IsVisibleChanged;

        UserReloadBus.ActiveUserChanged += OnActiveUserChanged;

        _vm.ForceThinkingRequested += OnForceThinkingRequested;
    }

    private void ScrollToBottomHard()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (ChatList == null) return;
                if (ChatList.Items.Count == 0) return;
                ChatList.UpdateLayout();
                ChatList.ScrollIntoView(ChatList.Items[^1]);
                ChatList.UpdateLayout();
                ChatList.ScrollIntoView(ChatList.Items[^1]);
            }
            catch { }
        }, DispatcherPriority.Loaded);
    }

    private void OnForceThinkingRequested()
    {
        try
        {
            ScrollToBottomHard();

            if (_vm.SelectedRoom != javis.ViewModels.ChatRoom.Solo)
                OnModeChanged(javis.ViewModels.ChatRoom.Solo);

            EnsureSoloOrchestrator();

            try { SoloHeart?.IsThinking = true; } catch { }
            try { _ = _soloOrch?.StartAsync(); } catch { }

            var msgId = Interlocked.Increment(ref _nextUserMsgId);
            _soloOrch?.OnUserMessage(msgId, "(user) 즉시 사유 시작");
        }
        catch
        {
            // best-effort
        }
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
                OnModeChanged(javis.ViewModels.ChatRoom.Main);

                // Hard lock: ensure no SOLO/DUO background loop is running
                if (_soloOrch != null)
                {
                    try { await _soloOrch.StopAsync(); } catch { }
                }

                try { _duoCts?.Cancel(); } catch { }
                try { if (_duoTask != null) await _duoTask; } catch { }

                _debateShow = false;
                _forceDebate = false;

                break;

            case ChatMode.SoloThink:
                OnModeChanged(javis.ViewModels.ChatRoom.Solo);
                break;

            case ChatMode.DuoDebate:
                OnModeChanged(javis.ViewModels.ChatRoom.Duo);
                break;
        }

        // In mode pages, room is fixed; hide room tabs to simplify UX.
        if (RoomMainTab != null) RoomMainTab.Visibility = Visibility.Collapsed;
        if (RoomSoloTab != null) RoomSoloTab.Visibility = Visibility.Collapsed;
        if (RoomDuoTab != null) RoomDuoTab.Visibility = Visibility.Collapsed;

        // Legacy UI is removed; mode is controlled by segmented buttons.
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
            appendDebug: (t) => { /* keep debug quiet by default */ },
            setThinkingProgress: (t) =>
            {
                var vm = (ChatViewModel)DataContext;
                vm.ThinkingProgress = t;
                vm.ThinkingStage = GuessThinkingStage(t);
            });

        _soloOrchBackend = new ChatPageSoloBackendAdapter(SoloProcessOneTurnAsync);
        _soloOrch = new SoloOrchestrator(sink, _soloOrchBackend)
        {
            ModelName = "gemma3:4b",
            AutoContinue = true
        };

        // Solo live stream: show the sentence being generated and flash the heart per token.
        var streamSb = new System.Text.StringBuilder();
        _soloOrch.OnTokenReceived += token =>
        {
            _ = UiAsync(() =>
            {
                if (string.IsNullOrEmpty(token)) return;
                // newline token can be used as "reset" from the backend
                if (token == "\n")
                    streamSb.Clear();
                else
                    streamSb.Append(token);

                var s = streamSb.ToString();
                if (s.Length > 240)
                    s = s.Substring(s.Length - 240);

                ((ChatViewModel)DataContext).ThinkingStage = s.Length == 0 ? "[데이터 분석 중...]" : s;
                try { SoloHeart?.Flash(); } catch { }
            });
        };

        _ = UiAsync(() =>
        {
            try
            {
                SoloHeart?.Flash();
                SoloHeart?.InvalidateVisual();
            }
            catch { }
        });
    }

    private static string GuessThinkingStage(string? t)
    {
        var s = (t ?? string.Empty).Trim();
        if (s.Length == 0) return "[데이터 분석 중...]";
        if (s.Contains("proposal", StringComparison.OrdinalIgnoreCase) || s.Contains("제안", StringComparison.OrdinalIgnoreCase))
            return "[기능 제안서 작성 중...]";
        if (s.Contains("opt", StringComparison.OrdinalIgnoreCase) || s.Contains("최적", StringComparison.OrdinalIgnoreCase))
            return "[시스템 최적화 방안 구상 중...]";
        return "[데이터 분석 중...]";
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
        OnModeChanged(javis.ViewModels.ChatRoom.Main);
    }

    private void RoomSolo_Click(object sender, RoutedEventArgs e)
    {
        OnModeChanged(javis.ViewModels.ChatRoom.Solo);
    }

    private void RoomDuo_Click(object sender, RoutedEventArgs e)
    {
        OnModeChanged(javis.ViewModels.ChatRoom.Duo);
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

        // Topic UI removed from ChatPage.xaml; keep logic best-effort.
        if (sender is Button b) b.IsEnabled = false;

        AddImmediate("assistant", $"✅ 주제 고정: {topic}\n이제 이 대화는 해당 주제로만 진행할게.");
    }

    private void TopicBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers == ModifierKeys.Shift) return;

        e.Handled = true;
        ApplyTopic_Click(sender, new RoutedEventArgs());
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
