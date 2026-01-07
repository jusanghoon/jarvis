using System;

namespace javis.Services.MainAi;

public static class MainAiEventBus
{
    public static event Action<MainAiEvent>? Raised;

    public static void Publish(MainAiEvent evt)
    {
        try { Raised?.Invoke(evt); }
        catch { }
    }
}

public abstract record MainAiEvent(DateTimeOffset At);

public sealed record ChatRequestStarted(DateTimeOffset At) : MainAiEvent(At);
public sealed record ChatRequestEnded(DateTimeOffset At) : MainAiEvent(At);
public sealed record ChatUserMessageObserved(DateTimeOffset At, string Text) : MainAiEvent(At);
