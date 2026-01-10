using System;
using javis.Services;

namespace javis.Services.MainAi;

public static class MainAiUpdateLogger
{
    // "App 기준 로그"로 릴리즈 노트를 쌓는 최소 메커니즘.
    // 실제로는 기능 구현 시점(코드 변경)마다 Copilot이 Append를 호출하는 게 가장 정확함.

    public static void Note(string title, string body, string? tag = null, string? source = null)
    {
        try
        {
            var store = new MainAiReleaseNotesStore(UserProfileService.Instance.ActiveUserDataDir);
            store.Append(title, body, tag, source);
        }
        catch { }
    }
}
