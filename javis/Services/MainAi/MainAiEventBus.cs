using System;
using javis.Services.Inbox;

namespace javis.Services.MainAi;

public static class MainAiEventBus
{
    public static event Action<MainAiEvent>? Raised;

    public static void Publish(MainAiEvent evt)
    {
        try { Raised?.Invoke(evt); }
        catch { }

        try
        {
            var kind = evt switch
            {
                ChatRequestStarted => InboxKinds.ChatRequest,
                ChatRequestEnded => InboxKinds.ChatRequest,
                ChatUserMessageObserved => InboxEventKind.Chat,
                ProgramEventObserved pe => string.IsNullOrWhiteSpace(pe.Kind) ? InboxEventKind.Audit : pe.Kind,
                _ => InboxEventKind.Audit
            };

            DailyInbox.Append(kind, new
            {
                evt = evt.GetType().Name,
                at = evt.At,
                payload = evt
            });
        }
        catch { }
    }
}

public abstract record MainAiEvent(DateTimeOffset At);

public sealed record ChatRequestStarted(DateTimeOffset At) : MainAiEvent(At);
public sealed record ChatRequestEnded(DateTimeOffset At) : MainAiEvent(At);
public sealed record ChatUserMessageObserved(DateTimeOffset At, string Text) : MainAiEvent(At);
public sealed record ProgramEventObserved(DateTimeOffset At, string Text, string Kind) : MainAiEvent(At);
