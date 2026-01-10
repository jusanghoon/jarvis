using System;

namespace javis.Services.MainAi;

public static class MainAiDocBus
{
    public sealed record SuggestionPayload(string Text, string? Source = null);

    public static event Action<SuggestionPayload>? Suggestion;

    public static void PublishSuggestion(string text, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { Suggestion?.Invoke(new SuggestionPayload(text.Trim(), source)); } catch { }
    }
}
