using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using javis.Models;

namespace javis.Services;

public sealed class PluginHost
{
    public static PluginHost Instance { get; } = new();

    public SkillRuntime Runtime { get; } = new();

    public string DataDir { get; }
    public string PluginsDir { get; }
    public string SkillsDir { get; }

    public PluginManager PluginManager { get; }

    public PersonaManager Persona { get; }
    public VaultManager Vault { get; }
    public VaultIndexManager VaultIndex { get; }

    public SoloNotesLimiter SoloLimiter { get; } = new();
    public SoloNotesStore SoloNotes { get; }

    public IEnumerable<string> SkillIds => SkillService.Instance.LoadSkills().Select(s => s.Manifest.Id).Distinct().OrderBy(x => x);
    public IEnumerable<string> ActionTypes => Runtime.ActionTypes;

    public KnowledgeCanon Canon { get; }
    public SftDatasetExporter Exporter { get; }

    private bool _loaded;

    private PluginHost()
    {
        DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis");

        SkillsDir = Path.Combine(DataDir, "skills");
        PluginsDir = Path.Combine(DataDir, "plugins");

        Directory.CreateDirectory(SkillsDir);
        Directory.CreateDirectory(PluginsDir);

        Persona = new PersonaManager(DataDir);
        Persona.Initialize();

        Vault = new VaultManager(DataDir);
        VaultIndex = new VaultIndexManager(DataDir);

        SoloNotes = new SoloNotesStore(DataDir);
        SoloLimiter.MinInterval = TimeSpan.FromSeconds(45);
        SoloLimiter.MaxPerHour = 80;

        Canon = new KnowledgeCanon(DataDir);
        Exporter = new SftDatasetExporter(this);

        PluginManager = new PluginManager(PluginsDir, Runtime);
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;

        PluginManager.LoadAll();
        _loaded = true;
    }

    public async Task<(string skillFile, string? pluginFile)> CreateSkillAsync(string requirement)
    {
        EnsureLoaded();

        var actionTypes = Runtime.ActionTypes.ToList();
        var apiNs = typeof(ISkillPlugin).Namespace ?? string.Empty;

        var result = await SkillBuilder.BuildAsync(requirement, actionTypes, apiNs);

        if (result.PluginRequired)
        {
            File.WriteAllText(Path.Combine(PluginsDir, result.PluginFilename!), result.PluginCode ?? "");
        }

        File.WriteAllText(Path.Combine(SkillsDir, result.SkillFilename), result.SkillSpecJson);

        PluginManager.LoadAll();

        return (result.SkillFilename, result.PluginRequired ? result.PluginFilename : null);
    }

    public async Task RunSkillByIdAsync(string skillId, Dictionary<string, string> vars, CancellationToken ct)
    {
        EnsureLoaded();

        var skill = SkillService.Instance.LoadSkills()
            .FirstOrDefault(s => string.Equals(s.Manifest.Id, skillId, StringComparison.OrdinalIgnoreCase));

        if (skill is null)
            throw new Exception($"\uC2A4\uD0AC \uC5C6\uC74C: {skillId}");

        var ctx = BuildSkillContext(vars);

        if (skill.Manifest.Type == "prompt")
        {
            var text = skill.Manifest.Prompt ?? skill.Manifest.Description ?? skill.Manifest.Name;
            ChatBus.Send(text);
            return;
        }

        if (skill.Manifest.Type == "action")
        {
            if (skill.Manifest.Action is null)
                throw new Exception("\uC2A4\uD0AC action \uD544\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4");

            var action = skill.Manifest.Action.Value;
            if (!action.TryGetProperty("type", out var typeEl))
                throw new Exception("action.type\uC774 \uC5C6\uC2B5\uB2C8\uB2E4");

            var actionType = typeEl.GetString() ?? "";
            if (!Runtime.TryGetAction(actionType, out var handler))
                throw new Exception($"\uB4F1\uB85D\uB418\uC9C0 \uC54A\uC740 action.type: {actionType}");

            ct.ThrowIfCancellationRequested();

            var output = handler(ctx, action);
            if (!string.IsNullOrWhiteSpace(output))
                ChatBus.Send(output);

            return;
        }

        throw new Exception($"\uC544\uC9C1 \uC774 \uD0C0\uC785\uC740 \uC2E4\uD589 \uBD88\uAC00: {skill.Manifest.Type}");
    }

    public Task RunSkillByIdAsync(string skillId, Dictionary<string, object> vars, CancellationToken ct)
        => RunSkillByIdAsync(skillId, vars.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "", StringComparer.OrdinalIgnoreCase), ct);

    private SkillContext BuildSkillContext(Dictionary<string, string> vars)
    {
        var memoFile = Path.Combine(DataDir, "memo.txt");
        var todoFile = Path.Combine(DataDir, "todos.json");
        var dictionary = vars.ToDictionary(v => v.Key, v => (object)v.Value);
        return new SkillContext(DataDir, memoFile, todoFile, dictionary);
    }

    public AuditLogger? Logger
    {
        get
        {
            try { return javis.App.Kernel?.Logger; }
            catch { return null; }
        }
    }

    public string GetSkillSummaries()
    {
        try
        {
            var skills = SkillService.Instance.LoadSkills();
            return string.Join("\n", skills.Select(s => $"- {s.Manifest.Id}: {s.Manifest.Name} ? {s.Manifest.Description}"));
        }
        catch
        {
            return string.Empty;
        }
    }

    public string GetActionTypes()
    {
        try
        {
            EnsureLoaded();
            return string.Join(", ", Runtime.ActionTypes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public string GetRecentChatText(int maxChars)
    {
        try
        {
            // no cross-page chat transcript storage yet; return empty for now
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
