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

    public ChatPageSoloUiSink(
        Dispatcher dispatcher,
        Func<ChatRoom, string, string, bool> addMessage,
        Action<string> appendDebug)
    {
        _dispatcher = dispatcher;
        _addMessage = addMessage;
        _appendDebug = appendDebug;
    }

    public void PostSystem(string text)
        => _dispatcher.InvokeAsync(() =>
        {
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
