using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Jarvis.Core.Archive;
using javis.ViewModels;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        e.Handled = true;

        if (_vm.IsBusy) return;
        if (string.IsNullOrWhiteSpace(_vm.InputText)) return;

        var text = (_vm.InputText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // MainChat hard lock: never route to SOLO/DUO or accept slash commands.
        if (_mode == ChatMode.MainChat)
        {
            if (text.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                AddImmediate("assistant", "MainChat에서는 이 명령을 사용할 수 없습니다.");
                _vm.InputText = "";
                return;
            }

            await _vm.SendAsync();
            return;
        }

        var cmd = text;
        if (cmd.StartsWith("/debate", StringComparison.OrdinalIgnoreCase))
        {
            if (cmd.Equals("/debate", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("/debate status", StringComparison.OrdinalIgnoreCase))
            {
                AddImmediate("assistant", $"(debate) show={_debateShow}, force={_forceDebate}");
            }
            else if (cmd.Equals("/debate show on", StringComparison.OrdinalIgnoreCase))
            {
                _debateShow = true;
                AddImmediate("assistant", "✅ (debate) show ON");
            }
            else if (cmd.Equals("/debate show off", StringComparison.OrdinalIgnoreCase))
            {
                _debateShow = false;
                AddImmediate("assistant", "✅ (debate) show OFF");
            }
            else if (cmd.Equals("/debate on", StringComparison.OrdinalIgnoreCase))
            {
                _forceDebate = true;
                AddImmediate("assistant", "✅ (debate) force ON");
            }
            else if (cmd.Equals("/debate off", StringComparison.OrdinalIgnoreCase))
            {
                _forceDebate = false;
                AddImmediate("assistant", "✅ (debate) force OFF");
            }
            else
            {
                AddImmediate("assistant", "사용법: /debate show on|off, /debate on|off, /debate status");
            }

            _vm.InputText = "";
            return;
        }

        // kickoff selection/answer handling
        if (((ChatViewModel)DataContext).SelectedRoom == javis.ViewModels.ChatRoom.Solo && _soloKickoffPending)
        {
            var s = (_vm.InputText ?? "").Trim();

            if (s == "1" || s == "2")
            {
                _soloKickoffPick = (s == "1") ? 1 : 2;
                var pickedQ = _soloKickoffPick == 1 ? _soloKickoffQ1 : _soloKickoffQ2;

                await UiAsync(() =>
                {
                    var vm = (ChatViewModel)DataContext;
                    vm.SoloMessages.Add(new javis.Models.ChatMessage(
                        "assistant",
                        $"좋아. {_soloKickoffPick}번 질문으로 갈게.\n\n{pickedQ}\n\n이 질문에 답해줘."));
                });

                _vm.InputText = "";
                return;
            }

            var q = (_soloKickoffPick == 2) ? _soloKickoffQ2 : _soloKickoffQ1;
            if (!string.IsNullOrWhiteSpace(s))
            {
                _soloKickoffPending = false;
                _vm.InputText = $"[시작 질문] {q}\n\n[내 답변] {s}";
            }
        }

        var vm = (ChatViewModel)DataContext;
        var room = vm.SelectedRoom;

        try
        {
            javis.App.Kernel?.Archive.Record(
                content: _vm.InputText ?? string.Empty,
                role: GEMSRole.Connectors,
                state: KnowledgeState.Active,
                sessionId: javis.App.Kernel?.Logger?.SessionId,
                meta: new() { ["kind"] = "chat", ["source"] = "user", ["room"] = room.ToString() });
        }
        catch { }

        if (room == javis.ViewModels.ChatRoom.Solo)
        {
            await UiAsync(() => vm.SoloMessages.Add(new javis.Models.ChatMessage("user", javis.Services.ChatTextUtil.SanitizeUiText(text))));

            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                {
                    room = "solo",
                    role = "user",
                    text,
                    ts = DateTimeOffset.Now
                });
            }
            catch { }

            vm.ContextVars["user_action"] = "user_message";

            EnsureSoloOrchestrator();
            var msgId = Interlocked.Increment(ref _nextUserMsgId);
            _soloOrch?.OnUserMessage(msgId, text);

            _vm.InputText = "";
            return;
        }

        if (room == javis.ViewModels.ChatRoom.Duo)
        {
            await UiAsync(() => vm.DuoMessages.Add(new javis.Models.ChatMessage("user", javis.Services.ChatTextUtil.SanitizeUiText(text))));

            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                {
                    room = "duo",
                    role = "user",
                    text,
                    ts = DateTimeOffset.Now
                });
            }
            catch { }

            vm.ContextVars["user_action"] = "user_message";

            _vm.InputText = "";

            _ = StartDuoRunAsync(text);
            return;
        }

        // Main room: keep existing SendAsync pipeline (writes to MainMessages)
        await _vm.SendAsync();
    }
}
