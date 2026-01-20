using System;
using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using javis.Models;
using javis.Services;
using Jarvis.Core.Archive;

namespace javis.ViewModels;

public partial class SkillsViewModel : ObservableObject
{
    public ObservableCollection<Skill> Skills { get; } = new();

    [ObservableProperty]
    private string _status = "READY";

    public event Action? NavigateToChatRequested;

    public SkillsViewModel()
    {
        Reload();
    }

    [RelayCommand]
    public void Reload()
    {
        Skills.Clear();
        foreach (var s in SkillService.Instance.LoadSkills())
            Skills.Add(s);

        Status = $"\uC2A4\uD0AC {Skills.Count}\uAC1C";
    }

    [RelayCommand]
    public void Run(Skill skill)
    {
        var m = skill.Manifest;

        try
        {
            var id = m.Id ?? m.Name ?? "skill";
            javis.Services.MainAi.MainAiEventBus.Publish(
                new javis.Services.MainAi.ProgramEventObserved(DateTimeOffset.Now, $"[skill.run] {id}", "skill.run"));
        }
        catch { }

        if (m.Type == "prompt")
        {
            var text = m.Prompt ?? m.Description ?? m.Name;
            ChatBus.Send(text ?? string.Empty);
            NavigateToChatRequested?.Invoke();
            return;
        }

        if (m.Type == "action")
        {
            if (m.Action is null || m.Action.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                Status = "\uC2A4\uD0AC action \uD544\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4";
                return;
            }

            try
            {
                PluginHost.Instance.EnsureLoaded();

                var action = m.Action.Value;
                if (!action.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    Status = "action.type\uC774 \uC5C6\uAC70\uB098 \uC798\uBABB\uB429\uB2C8\uB2E4";
                    return;
                }

                var actionType = typeEl.GetString() ?? "";
                if (!PluginHost.Instance.Runtime.TryGetAction(actionType, out var handler))
                {
                    Status = $"\uCC98\uB9AC\uD560 \uC218 \uC5C6\uB294 action.type: {actionType}";
                    return;
                }

                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Jarvis");

                var ctx = new SkillContext(
                    dataDir,
                    Path.Combine(dataDir, "memo.txt"),
                    Path.Combine(dataDir, "todos.json"));

                var output = handler(ctx, action);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    ChatBus.Send(output);
                    NavigateToChatRequested?.Invoke();
                }
                return;
            }
            catch (Exception ex)
            {
                try
                {
                    javis.App.Kernel?.Archive.Record(
                        content: $"CRACK_DETECTED: {ex.GetType().Name}: {ex.Message}",
                        role: GEMSRole.Logician,
                        state: KnowledgeState.Active,
                        sessionId: javis.App.Kernel?.Logger?.SessionId,
                        meta: new() { ["kind"] = "crack", ["where"] = "SkillsViewModel.Run" });
                }
                catch { }

                Status = $"Plugin error: {ex.Message}";
                return;
            }
        }

        Status = "\uC544\uC9C1 \uC774 \uD0C0\uC785\uC740 \uC2E4\uD589 \uBD88\uAC00";
    }
}
