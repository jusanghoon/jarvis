using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private void SoloToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_soloCts != null) return;

        EnsureSoloOrchestrator();
        _ = _soloOrch?.StartAsync();

        BeginSoloTopicMode();
        ShowSoloStartQuestions();

        var vm = (javis.ViewModels.ChatViewModel)DataContext;
        vm.ContextVars["solo_mode"] = "on";
        vm.ContextVars["user_action"] = "solo_start";

        if (!UseSoloOrchestrator)
            _soloCts = new CancellationTokenSource();

        if (SoloStatusText != null)
            SoloStatusText.Visibility = Visibility.Visible;

        SetSoloStatus("SOLO 활성화");
        if (DuoToggle?.IsChecked != true)
            AddImmediate("assistant", "SOLO ON (최근 대화 기반)");

        try { javis.App.Kernel?.Logger?.Log("solo.start", new { }); } catch { }

        // NOTE: UseSoloOrchestrator==true인 기본 설정에서는 legacy SoloLoopAsync를 사용하지 않음
        // (분할 중 missing symbol 방지를 위해 참조 제거)
    }

    private async void SoloToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (UseSoloOrchestrator)
        {
            if (_soloOrch != null)
                await _soloOrch.StopAsync();
        }
        else
        {
            if (_soloCts == null) return;

            var vm = (javis.ViewModels.ChatViewModel)DataContext;
            vm.ContextVars["solo_mode"] = "off";
            vm.ContextVars["user_action"] = "solo_stop";

            SetSoloStatus("SOLO 종료 중…");
            AppendAssistant("SOLO OFF 요청");

            try { javis.App.Kernel?.Logger?.Log("solo.stop_request", new { }); } catch { }

            _soloCts.Cancel();

            try { if (_soloTask != null) await _soloTask; } catch { }

            _soloCts.Dispose();
            _soloCts = null;
            _soloTask = null;
        }

        _soloStartQuestionsShown = false;
        _soloKickoffPending = false;
        _soloKickoffPick = null;
        _soloKickoffQ1 = "";
        _soloKickoffQ2 = "";

        SetSoloStatus(string.Empty);
        if (SoloStatusText != null)
            SoloStatusText.Visibility = Visibility.Collapsed;

        AppendAssistant("SOLO 종료");

        try { javis.App.Kernel?.Logger?.Log("solo.stopped", new { }); } catch { }
    }
}
