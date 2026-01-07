using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Models;
using javis.Services;
using Jarvis.Core.Archive;
using static javis.Services.OllamaChatService;

namespace javis.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly OllamaChatService _ollama = new("http://localhost:11434/api");
    private readonly List<OllamaMessage> _history = new();

    private readonly CalendarTodoStore _calendarStore = new();

    private CancellationTokenSource? _cts;

    private readonly SynchronizationContext _ui;

    public RuntimeSettings Settings => RuntimeSettings.Instance;

    public ChatViewModel()
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _history.Add(new OllamaMessage("system",
            """
            너는 개인비서 JARVIS처럼 행동하는 한국어 AI 비서다.
            - 톤: 짧고 단호하게. 약간 스마트시크한 느낌.
            - 사용자가 요청하면 단계별로 간단 계획을 제시하고, 필요한 정보를 묻는다.

            [날짜/시간 규칙]
            - 현재 시간(Asia/Seoul, KST)은 매 요청마다 system 메시지로 주어진다.
            - '오늘/내일/모레/이번 주/다음 주/다다음 주/이번 달/다음 달' 같은 표현은 모두 KST 기준으로 해석하라.
            - 답변에 날짜가 포함되면 항상 날짜(YYYY-MM-DD)를 함께 표기하라.
            - 모호하면(예: '다음 주 금요일') 확인 질문을 하라.
            """));

        StatusText = $"READY / {Settings.Model}";
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Settings.Model))
                StatusText = $"READY / {Settings.Model}";
        };

        MainMessages.Add(new ChatMessage("assistant", "온라인. 무엇을 도와줄까?"));
    }

    public ObservableCollection<ChatMessage> MainMessages { get; } = new();
    public ObservableCollection<ChatMessage> SoloMessages { get; } = new();
    public ObservableCollection<ChatMessage> DuoMessages { get; } = new();

    // Back-compat: existing code that references Messages will still target the main room.
    public ObservableCollection<ChatMessage> Messages => MainMessages;

    private ChatRoom _selectedRoom = ChatRoom.Main;
    public ChatRoom SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            if (_selectedRoom == value) return;
            _selectedRoom = value;
            OnPropertyChanged(nameof(SelectedRoom));
            OnPropertyChanged(nameof(MessagesView));
        }
    }

    public ObservableCollection<ChatMessage> MessagesView =>
        _selectedRoom switch
        {
            ChatRoom.Solo => SoloMessages,
            ChatRoom.Duo => DuoMessages,
            _ => MainMessages,
        };

    public ObservableCollection<ChatMessage> GetRoom(ChatRoom room) =>
        room switch
        {
            ChatRoom.Solo => SoloMessages,
            ChatRoom.Duo => DuoMessages,
            _ => MainMessages,
        };

    public Dictionary<string, string> ContextVars { get; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["solo_mode"] = "off",
            ["user_action"] = ""
        };

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "READY";

    public bool Think { get; set; } = false;

    public event Action? ScrollToEndRequested;

    private static string NowKst()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return now.ToString("yyyy-MM-dd (ddd) HH:mm:ss 'KST'", CultureInfo.InvariantCulture);
    }

    private static DateTime TodayKstDate()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return now.Date;
    }

    private string BuildUpcomingTodoContext()
    {
        var now = TodayKstDate();
        var items = _calendarStore.GetUpcoming(now, 14);

        if (items.Count == 0) return "향후 14일간 등록된 할 일이 없다.";

        var lines = new List<string>();
        DateTime? cur = null;

        foreach (var it in items)
        {
            if (cur != it.Date.Date)
            {
                cur = it.Date.Date;
                lines.Add($"\n[{cur:yyyy-MM-dd (ddd)}]");
            }

            var time = it.Time is null ? "" : $"{it.Time:hh\\:mm} ";
            var done = it.IsDone ? "DONE" : "TODO";
            lines.Add($"- {done} {time}{it.Title}");
        }

        return "다음은 사용자의 달력 할 일(앞으로 14일)이다:" + string.Join("", lines);
    }

    public async Task SendExternalAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        InputText = text.Trim();
        await SendAsync();
    }

    [ObservableProperty]
    private string _topic = "";

    [ObservableProperty]
    private bool _topicLocked;

    public bool SetTopicOnce(string? topic)
    {
        if (TopicLocked) return false;

        var t = (topic ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return false;

        Topic = t;
        TopicLocked = true;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            javis.Services.MainAi.MainAiEventBus.Publish(new javis.Services.MainAi.ChatRequestStarted(DateTimeOffset.Now));
            javis.Services.MainAi.MainAiEventBus.Publish(new javis.Services.MainAi.ChatUserMessageObserved(DateTimeOffset.Now, text));
        }
        catch { }

        var kernel = javis.App.Kernel;
        var sessionId = kernel?.Logger?.SessionId;

        var opId = Guid.NewGuid().ToString("N");
        var swOp = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            kernel?.Archive.Record(
                content: text,
                role: GEMSRole.Connectors,
                state: KnowledgeState.Active,
                sessionId: sessionId,
                meta: new Dictionary<string, object?>
                {
                    ["kind"] = "chat.send.start",
                    ["opId"] = opId,
                    ["len"] = text.Length
                });
        }
        catch { }

        // keep shared context up to date
        ContextVars["user_action"] = text;

        try
        {
            javis.App.Kernel?.Logger?.LogText("chat.user", text);
        }
        catch { }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsBusy = true;
        StatusText = "THINKING...";
        SendCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        InputText = "";

        MainMessages.Add(new ChatMessage("user", text));
        ScrollToEndRequested?.Invoke();

        _history.Add(new OllamaMessage("user", text));

        const string CURSOR = "|";
        var assistantMsg = new ChatMessage("assistant", CURSOR);
        MainMessages.Add(assistantMsg);
        ScrollToEndRequested?.Invoke();

        var q = new ConcurrentQueue<string>();
        int streamDone = 0;

        var display = new StringBuilder();
        int scrollCounter = 0;

        const int CHARS_PER_TICK = 1;

        var interval = TimeSpan.FromMilliseconds(18);

        var doneTcs = new TaskCompletionSource();

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = interval
        };

        timer.Tick += (_, __) =>
        {
            if (_cts.Token.IsCancellationRequested)
            {
                timer.Stop();
                assistantMsg.Text = display.ToString();
                doneTcs.TrySetResult();
                return;
            }

            var tookAny = false;
            for (int i = 0; i < CHARS_PER_TICK; i++)
            {
                if (q.TryDequeue(out var ch))
                {
                    display.Append(ch);
                    tookAny = true;
                }
                else break;
            }

            if (tookAny)
            {
                assistantMsg.Text = display.ToString() + CURSOR;

                scrollCounter++;
                if (scrollCounter % 10 == 0 || display.ToString().EndsWith("\n"))
                    ScrollToEndRequested?.Invoke();

                if (StatusText == "THINKING...")
                    StatusText = "TYPING...";
            }
            else if (Interlocked.CompareExchange(ref streamDone, 0, 0) == 1)
            {
                timer.Stop();
                assistantMsg.Text = display.ToString();
                ScrollToEndRequested?.Invoke();
                doneTcs.TrySetResult();
            }
        };

        timer.Start();

        try
        {
            var context = new List<OllamaMessage>();

            context.Add(_history[0]);

            context.Add(new OllamaMessage("system",
                $"[현재 시간 / timezone] Asia/Seoul: {NowKst()}\n" +
                "사용자 날짜 표현(오늘/내일/다음주 등)은 위 시간 기준으로 해석하고, 답변은 YYYY-MM-DD를 포함해라."));

            if (TopicLocked && !string.IsNullOrWhiteSpace(Topic))
            {
                context.Add(new OllamaMessage("system",
                    "[주제 고정 모드]\n" +
                    $"- 현재 대화 주제: {Topic}\n" +
                    "- 사용자의 요청이 주제에서 벗어나면, '주제에서 벗어났어'라고 짧게 알려주고\n" +
                    "  (1) 왜 벗어났는지 1줄\n" +
                    "  (2) 주제 안에서 계속할 수 있는 다음 방향 2~3개를 제시해라.\n" +
                    "- 주제 밖의 질문에는 바로 답하지 말고, 주제 안으로 유도해라."));
            }

            // persona (core + chat overlay)
            try
            {
                var persona = javis.App.Kernel?.Persona;
                if (persona != null)
                {
                    if (!string.IsNullOrWhiteSpace(persona.CoreText))
                        context.Add(new OllamaMessage("system", persona.CoreText));

                    if (!string.IsNullOrWhiteSpace(persona.ChatOverlayText))
                        context.Add(new OllamaMessage("system", persona.ChatOverlayText));
                }
            }
            catch { }

            try
            {
                var canon = PluginHost.Instance.Canon.BuildPromptBlock(query: text, maxItems: 6);
                context.Add(new OllamaMessage("system", "[CANON]\n" + canon));
            }
            catch { }

            context.Add(new OllamaMessage("system",
                "[달력 할 일 참고]\n" + BuildUpcomingTodoContext() +
                "\n요청을 처리할 때 위 할 일을 우선 참고하고, 일정/날짜 질문에는 항상 YYYY-MM-DD로 답해라."));

            context.AddRange(_history.Where(m => m.Role != "system").TakeLast(20));

            try
            {
                javis.App.Kernel?.Logger?.Log("llm.request", new { model = Settings.Model, think = Think, promptChars = text.Length });
            }
            catch { }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var receiver = Task.Run(async () =>
            {
                await foreach (var delta in _ollama.StreamChatAsync(Settings.Model, context, Think, _cts.Token))
                {
                    foreach (var r in delta.EnumerateRunes())
                        q.Enqueue(r.ToString());
                }
            }, _cts.Token);

            await receiver;
            Interlocked.Exchange(ref streamDone, 1);

            await doneTcs.Task;

            _history.Add(new OllamaMessage("assistant", assistantMsg.Text ?? ""));
            StatusText = "DONE";

            try
            {
                javis.App.Kernel?.Logger?.Log("llm.response", new { ms = sw.ElapsedMilliseconds, chars = (assistantMsg.Text ?? "").Length });
                javis.App.Kernel?.Logger?.LogText("chat.assistant", assistantMsg.Text ?? "");
            }
            catch { }

            swOp.Stop();
            try
            {
                kernel?.Archive.Record(
                    content: assistantMsg.Text ?? "",
                    role: GEMSRole.Logician,
                    state: KnowledgeState.Active,
                    sessionId: sessionId,
                    meta: new Dictionary<string, object?>
                    {
                        ["kind"] = "chat.send.end",
                        ["opId"] = opId,
                        ["ms"] = swOp.ElapsedMilliseconds,
                        ["outLen"] = (assistantMsg.Text ?? "").Length
                    });
            }
            catch { }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref streamDone, 1);
            StatusText = "CANCELED";

            swOp.Stop();
            try
            {
                kernel?.Archive.Record(
                    content: "CANCELED",
                    role: GEMSRole.Logician,
                    state: KnowledgeState.Active,
                    sessionId: sessionId,
                    meta: new Dictionary<string, object?>
                    {
                        ["kind"] = "chat.send.end",
                        ["opId"] = opId,
                        ["ms"] = swOp.ElapsedMilliseconds,
                        ["canceled"] = true
                    });
            }
            catch { }

            try { javis.App.Kernel?.Logger?.Log("llm.canceled", new { model = Settings.Model }); } catch { }
        }
        catch (Exception ex)
        {
            foreach (var r in $"\n\n[ERROR] {ex.Message}".EnumerateRunes())
                q.Enqueue(r.ToString());
            Interlocked.Exchange(ref streamDone, 1);
            StatusText = "ERROR";
            await doneTcs.Task;

            swOp.Stop();
            try
            {
                kernel?.Archive.Record(
                    content: $"CRACK_DETECTED: {ex.GetType().Name}: {ex.Message}\nCTX: SendAsync opId={opId} ms={swOp.ElapsedMilliseconds}",
                    role: GEMSRole.Logician,
                    state: KnowledgeState.Active,
                    sessionId: sessionId,
                    meta: new Dictionary<string, object?>
                    {
                        ["kind"] = "crack",
                        ["where"] = "ChatViewModel.SendAsync",
                        ["opId"] = opId,
                        ["ms"] = swOp.ElapsedMilliseconds
                    });
            }
            catch { }

            try { javis.App.Kernel?.Logger?.Log("llm.error", new { error = ex.Message, stack = ex.ToString() }); } catch { }
        }
        finally
        {
            try { javis.Services.MainAi.MainAiEventBus.Publish(new javis.Services.MainAi.ChatRequestEnded(DateTimeOffset.Now)); } catch { }

            IsBusy = false;
            SendCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => IsBusy;
}
