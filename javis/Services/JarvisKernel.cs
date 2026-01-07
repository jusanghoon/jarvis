using System;
using System.IO;
using System.Text;
using Jarvis.Core.Archive;

namespace javis.Services;

public sealed class JarvisKernel
{
    public string DataDir { get; }
    public string SkillsDir { get; }
    public string PluginsDir { get; }
    public string LogsDir { get; }

    public AuditLogger Logger { get; }
    public ArchiveStore Archive { get; }
    public PersonaManager Persona { get; }

    public FossilQueryService Fossils { get; }

    public SkillRuntime Runtime { get; } = new();

    public PluginManager Plugins { get; }

    public SkillService Skills { get; } = SkillService.Instance;

    public VaultManager Vault { get; }
    public VaultIndexManager VaultIndex { get; }

    public DailyObservationNotes DailyNotes { get; }

    public JarvisKernel()
    {
        DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis");

        SkillsDir = Path.Combine(DataDir, "skills");
        PluginsDir = Path.Combine(DataDir, "plugins");
        LogsDir = Path.Combine(DataDir, "logs");

        Directory.CreateDirectory(SkillsDir);
        Directory.CreateDirectory(PluginsDir);
        Directory.CreateDirectory(LogsDir);

        Logger = new AuditLogger(LogsDir);
        Logger.PruneOldLogs(keepDays: 30);
        Logger.Log("app.start", new { tfm = "net10.0-windows" });

        Archive = new ArchiveStore(Logger);
        Archive.Record(
            content: "SYSTEM_BOOT",
            role: GEMSRole.Recorder,
            state: KnowledgeState.Idle,
            sessionId: Logger.SessionId,
            meta: new() { ["kind"] = "system" });

        Fossils = new FossilQueryService(Logger.LogsDir);

        Persona = new PersonaManager(DataDir);
        Persona.Initialize();

        Vault = new VaultManager(DataDir);
        VaultIndex = new VaultIndexManager(DataDir);
        DailyNotes = new DailyObservationNotes(DataDir);

        Plugins = new PluginManager(PluginsDir, Runtime);
    }

    public void Initialize()
    {
        DefaultAssets.EnsureDefaultMathPlugin(PluginsDir);
        DefaultAssets.EnsureDefaultCalculatorSkill(SkillsDir);

        Plugins.LoadAll();
    }

    public void ReloadPluginsAndSkills()
    {
        Plugins.LoadAll();
    }

    public string GetVaultContextForSolo(int maxItems = 10)
    {
        var recent = Vault.ReadRecent(maxItems);
        if (recent.Count == 0) return "(최근 추가된 자료 없음)";

        var sb = new StringBuilder();
        sb.AppendLine("최근 추가된 자료:");

        foreach (var it in recent)
        {
            var indexed = VaultIndex.IsIndexed(it.sha256) ? "indexed" : "not_indexed";
            var key = it.sha256.Length >= 8 ? it.sha256.Substring(0, 8) : it.sha256;
            sb.AppendLine($"- [{key}] {it.fileName} ({it.ext}, {it.sizeBytes} bytes, {indexed})");
        }

        return sb.ToString();
    }
}
