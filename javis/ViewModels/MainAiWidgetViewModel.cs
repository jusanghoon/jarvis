using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Services.MainAi;

namespace javis.ViewModels;

public partial class MainAiWidgetViewModel : ObservableObject
{
    public event Action? RequestNavigateToChat;

    private readonly MainAiHelpResponder _help;
    private readonly string _codeIndex;

    [ObservableProperty]
    private string _promptText = "";

    [ObservableProperty]
    private string _answerText = "";

    [ObservableProperty]
    private bool _isBusy;

    public MainAiWidgetViewModel()
    {
        _help = new MainAiHelpResponder(baseUrl: "http://localhost:11434", model: "qwen3:4b");

        var root = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var parent = System.IO.Directory.GetParent(root);
            if (parent is null) break;
            root = parent.FullName;
            if (System.IO.Directory.Exists(System.IO.Path.Combine(root, "javis"))) break;
        }

        _codeIndex = MainAiCodeIndex.BuildIndexText(root);

        MainAiDocBus.Suggestion += p =>
        {
            try { AnswerText = p.Text; } catch { }
        };
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        var q = (PromptText ?? "").Trim();
        if (q.Length == 0) return;

        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            var ans = await _help.AnswerAsync(q, _codeIndex, cts.Token);
            AnswerText = string.IsNullOrWhiteSpace(ans) ? "(응답이 비어있음)" : ans;

            try
            {
                // Heuristic: if user asks about app usage/features, log it as a doc suggestion.
                if (q.Contains("업데이트", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("기능", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("어디", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("사용", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("설정", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("방법", StringComparison.OrdinalIgnoreCase))
                {
                    MainAiDocBus.PublishSuggestion($"사용자 질문(도움말): {q}", source: "help_widget");
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            AnswerText = "오류: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenChat()
    {
        try { RequestNavigateToChat?.Invoke(); } catch { }
    }
}
