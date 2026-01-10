namespace javis.Services.Inbox;

public static class InboxKinds
{
    // generic prefixes
    public const string ChatMessage = "chat.message";
    public const string ChatSession = "chat.session";
    public const string ChatRequest = "chat.request";

    public const string SoloRequest = "solo.request";
    public const string DuoRequest = "duo.request";

    public const string SoloNote = "solo.note";

    public const string TodoChange = "todo.change";

    public const string SkillRun = "skill.run";
    public const string SkillCreate = "skill.create";

    public const string VaultImport = "vault.import";

    public const string Crash = "crash";
    public const string Audit = "audit";
}
