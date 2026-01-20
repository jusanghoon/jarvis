using System;
using System.Windows.Threading;
using javis.Services;
using javis.ViewModels;

namespace javis.Services.Solo;

public sealed class ChatPageSoloUiSink : ISoloUiSink
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<ChatRoom, string, string, bool> _addMessage;
    private readonly Action<string> _appendDebug;
    private readonly Action<string>? _setThinkingProgress;

    public ChatPageSoloUiSink(
        Dispatcher dispatcher,
        Func<ChatRoom, string, string, bool> addMessage,
        Action<string> appendDebug,
        Action<string>? setThinkingProgress = null)
    {
        _dispatcher = dispatcher;
        _addMessage = addMessage;
        _appendDebug = appendDebug;
        _setThinkingProgress = setThinkingProgress;
    }

    public void PostSystem(string text)
        => _dispatcher.InvokeAsync(() =>
        {
            _setThinkingProgress?.Invoke(ChatTextUtil.SanitizeUiText(text));
            _addMessage(ChatRoom.Solo, "assistant", ChatTextUtil.SanitizeUiText(text));
            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                {
                    room = "solo",
                    role = "system",
                    text,
                    ts = DateTimeOffset.Now
                });
            }
            catch { }
        });

    public void PostAssistant(string text)
        => _dispatcher.InvokeAsync(() =>
        {
            _setThinkingProgress?.Invoke(ChatTextUtil.SanitizeUiText(text));
            _addMessage(ChatRoom.Solo, "assistant", ChatTextUtil.SanitizeUiText(text));
            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                {
                    room = "solo",
                    role = "assistant",
                    text,
                    ts = DateTimeOffset.Now
                });
            }
            catch { }
        });

    public void PostDebug(string text)
        => _dispatcher.InvokeAsync(() => _appendDebug(ChatTextUtil.SanitizeUiText(text)));
}
