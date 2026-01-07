using System.Text.Json;

namespace javis.Models;

public sealed class SkillManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    // "prompt" | "powershell" | "action"
    public string Type { get; set; } = "prompt";

    // prompt 타입일 때, Chat에 보낼 문장
    public string? Prompt { get; set; }

    // powershell 타입일 때 실행할 파일명(예: "handler.ps1")
    public string? Entry { get; set; }

    public bool RequiresConfirmation { get; set; } = false;

    public JsonElement? Action { get; set; }
}
