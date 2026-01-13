using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace javis.Services;

public sealed partial class UserProfileService : ObservableObject
{
    public static UserProfileService Instance { get; } = new();

    public event Action<string>? ProfileChanged;

    private readonly string _dataDir;
    private readonly string _registryDir;
    private readonly string _activePath;

    private readonly JsonSerializerOptions _jsonOpt = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [ObservableProperty]
    private string _activeUserId = "default";

    [ObservableProperty]
    private string _activeUserName = "Default";

    private UserProfileService()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");

        // global registry (active user + list)
        _registryDir = Path.Combine(_dataDir, "profiles");
        _activePath = Path.Combine(_registryDir, "active.json");

        Directory.CreateDirectory(_registryDir);
        LoadActive();
    }

    public IReadOnlyList<UserProfile> ListProfiles()
    {
        Directory.CreateDirectory(_registryDir);

        var list = new List<UserProfile>();
        foreach (var file in Directory.EnumerateFiles(_registryDir, "*.profile.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var p = JsonSerializer.Deserialize<UserProfile>(json, _jsonOpt);
                if (p != null) list.Add(p);
            }
            catch { }
        }

        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public UserProfile CreateProfile(string displayName)
    {
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length == 0) name = "User";

        var id = Guid.NewGuid().ToString("N");
        var profile = new UserProfile(id, name, DateTimeOffset.Now);

        File.WriteAllText(ProfilePath(id), JsonSerializer.Serialize(profile, _jsonOpt));

        // registry snapshot for listing
        Directory.CreateDirectory(_registryDir);
        File.WriteAllText(Path.Combine(_registryDir, id + ".profile.json"), JsonSerializer.Serialize(profile, _jsonOpt));

        return profile;
    }

    public void SetActive(string userId)
    {
        var id = (userId ?? string.Empty).Trim();
        if (id.Length == 0) return;

        Directory.CreateDirectory(_registryDir);
        File.WriteAllText(_activePath, JsonSerializer.Serialize(new ActiveUserState(id), _jsonOpt));

        ActiveUserId = id;
        RefreshActiveUserName();

        try { ProfileChanged?.Invoke(id); } catch { }
        try { UserReloadBus.PublishActiveUserChanged(id); } catch { }
    }

    public UserProfile EnsureDefaultProfile()
    {
        var existing = ListProfiles().ToList();
        var def = existing.FirstOrDefault(p => p.Id == "default");
        if (def != null)
        {
            // ensure isolated profile file exists
            try
            {
                if (!File.Exists(ProfilePath(def.Id)))
                    File.WriteAllText(ProfilePath(def.Id), JsonSerializer.Serialize(def, _jsonOpt));
            }
            catch { }
            return def;
        }

        var profile = new UserProfile("default", "Default", DateTimeOffset.Now);

        File.WriteAllText(ProfilePath(profile.Id), JsonSerializer.Serialize(profile, _jsonOpt));
        Directory.CreateDirectory(_registryDir);
        File.WriteAllText(Path.Combine(_registryDir, profile.Id + ".profile.json"), JsonSerializer.Serialize(profile, _jsonOpt));

        return profile;
    }

    public UserProfile? TryGetActiveProfile()
    {
        EnsureDefaultProfile();
        var id = ActiveUserId;
        var path = ProfilePath(id);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserProfile>(json, _jsonOpt);
        }
        catch
        {
            return null;
        }
    }

    public UserProfile? TryGetProfile(string id)
    {
        var pid = (id ?? string.Empty).Trim();
        if (pid.Length == 0) return null;

        var path = ProfilePath(pid);
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserProfile>(json, _jsonOpt);
        }
        catch
        {
            return null;
        }
    }

    public void SaveProfile(UserProfile profile)
    {
        if (profile is null) return;

        Directory.CreateDirectory(_registryDir);

        // isolated profile
        File.WriteAllText(ProfilePath(profile.Id), JsonSerializer.Serialize(profile, _jsonOpt));

        // registry snapshot for listing
        try
        {
            File.WriteAllText(Path.Combine(_registryDir, profile.Id + ".profile.json"), JsonSerializer.Serialize(profile, _jsonOpt));
        }
        catch { }

        if (string.Equals(profile.Id, ActiveUserId, StringComparison.OrdinalIgnoreCase))
        {
            ActiveUserName = profile.DisplayName;
        }

        try { ProfileChanged?.Invoke(profile.Id); } catch { }
    }

    private void LoadActive()
    {
        EnsureDefaultProfile();

        try
        {
            if (File.Exists(_activePath))
            {
                var json = File.ReadAllText(_activePath);
                var state = JsonSerializer.Deserialize<ActiveUserState>(json, _jsonOpt);
                if (state?.ActiveUserId is { Length: > 0 } id)
                {
                    ActiveUserId = id;
                    RefreshActiveUserName();
                    return;
                }
            }
        }
        catch { }

        ActiveUserId = "default";
        RefreshActiveUserName();
    }

    private void RefreshActiveUserName()
    {
        try
        {
            var p = TryGetActiveProfile();
            ActiveUserName = p?.DisplayName?.Trim() is { Length: > 0 } n ? n : ActiveUserId;
        }
        catch
        {
            ActiveUserName = ActiveUserId;
        }
    }

    private string ProfilePath(string id)
    {
        var userRoot = GetUserDataDir(id);
        var dir = Path.Combine(userRoot, "profile");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "profile.json");
    }

    private sealed record ActiveUserState(string ActiveUserId);

    public Dictionary<string, string> BuildDraftFieldsFromHistory(int maxSessions = 80, int maxCharsPerMsg = 400)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var store = new javis.Services.History.ChatHistoryStore(ActiveUserDataDir);
            var sessions = store.ListSessions(max: maxSessions);

            foreach (var s in sessions)
            {
                try
                {
                    var session = store.LoadSessionAsync(s.Id, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                    if (session?.Messages == null) continue;

                    foreach (var m in session.Messages)
                    {
                        if (!string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var text = (m.Text ?? "").Replace("\u200B", "").Trim();
                        if (text.Length == 0) continue;
                        if (text.Length > maxCharsPerMsg) text = text[..maxCharsPerMsg];

                        TryExtractSimpleField(fields, text);
                    }
                }
                catch { }
            }
        }
        catch { }

        return fields;
    }

    private static void TryExtractSimpleField(Dictionary<string, string> fields, string text)
    {
        // NOTE: 규칙 기반(명시된 경우만)으로만 채움. 추정 금지.

        // 취미
        if (!fields.ContainsKey("취미") && text.Contains("취미", StringComparison.OrdinalIgnoreCase))
        {
            var v = ExtractAfter(text, "취미", new[] { ":", "는", "는요", "는요?" });
            v = CleanupValue(v);
            if (!string.IsNullOrWhiteSpace(v)) fields["취미"] = v;
        }
        if (!fields.ContainsKey("취미") && (text.Contains("취미는", StringComparison.OrdinalIgnoreCase) || text.Contains("취미 :", StringComparison.OrdinalIgnoreCase)))
        {
            var v = ExtractAfter(text, "취미", new[] { "는", ":" });
            v = CleanupValue(v);
            if (!string.IsNullOrWhiteSpace(v)) fields["취미"] = v;
        }

        // 결혼
        if (!fields.ContainsKey("결혼") && (text.Contains("미혼") || text.Contains("기혼") || text.Contains("결혼") ))
        {
            if (text.Contains("미혼")) fields["결혼"] = "미혼";
            else if (text.Contains("기혼")) fields["결혼"] = "기혼";
            else
            {
                var v = ExtractAfter(text, "결혼", new[] { ":", "은", "는" });
                v = CleanupValue(v);
                if (!string.IsNullOrWhiteSpace(v)) fields["결혼"] = v;
            }
        }

        // 주소
        if (!fields.ContainsKey("주소") && text.Contains("주소", StringComparison.OrdinalIgnoreCase))
        {
            var v = ExtractAfter(text, "주소", new[] { ":", "는", "은" });
            v = CleanupValue(v);
            if (!string.IsNullOrWhiteSpace(v)) fields["주소"] = v;
        }

        // 특기
        if (!fields.ContainsKey("특기") && text.Contains("특기", StringComparison.OrdinalIgnoreCase))
        {
            var v = ExtractAfter(text, "특기", new[] { ":", "는", "은" });
            v = CleanupValue(v);
            if (!string.IsNullOrWhiteSpace(v)) fields["특기"] = v;
        }

        // 직업
        if (!fields.ContainsKey("직업") && text.Contains("직업", StringComparison.OrdinalIgnoreCase))
        {
            var v = ExtractAfter(text, "직업", new[] { ":", "은", "는" });
            v = CleanupValue(v);
            if (!string.IsNullOrWhiteSpace(v)) fields["직업"] = v;
        }
    }

    private static string ExtractAfter(string text, string key, string[] seps)
    {
        var idx = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var tail = text[(idx + key.Length)..];

        foreach (var sep in seps)
        {
            var sidx = tail.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (sidx >= 0)
                tail = tail[(sidx + sep.Length)..];
        }

        return tail.Trim();
    }

    private static string CleanupValue(string v)
    {
        v = (v ?? "").Trim();
        if (v.Length == 0) return "";

        v = v.TrimStart('-', '–', '—', '·', '*');
        v = v.Trim();

        // cut at sentence end
        var cut = v.IndexOfAny(new[] { '.', '!', '?', '\n', '\r' });
        if (cut > 0) v = v[..cut];

        // too long → likely not a value
        if (v.Length > 80) v = v[..80];

        return v.Trim();
    }

    public void ExportProfileAsTsv(UserProfile profile, string path)
    {
        var lines = new List<string>
        {
            "Field\tValue",
            $"이름\t{profile.DisplayName}",
            $"ID\t{profile.Id}",
            $"생성일\t{profile.CreatedAt:O}"
        };

        foreach (var kv in profile.Fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = kv.Key.Replace("\t", " ");
            var val = (kv.Value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
            lines.Add($"{key}\t{val}");
        }

        File.WriteAllText(path, string.Join("\n", lines));
    }

    public string GetUserDataDir(string userId)
    {
        var id = (userId ?? string.Empty).Trim();
        if (id.Length == 0) id = "default";

        var dir = Path.Combine(_dataDir, "users", id);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string ActiveUserDataDir
    {
        get
        {
            EnsureDefaultProfile();
            return GetUserDataDir(ActiveUserId);
        }
    }
}

public sealed record UserProfile(string Id, string DisplayName, DateTimeOffset CreatedAt)
{
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsSourceKey(string key)
        => key.EndsWith("_source", StringComparison.OrdinalIgnoreCase);

    public string ToReportText(bool includeSources = false)
    {
        var lines = new List<string>
        {
            $"이름: {DisplayName}",
            $"ID: {Id}",
            $"생성일: {CreatedAt:O}",
            ""
        };

        foreach (var kv in Fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!includeSources && IsSourceKey(kv.Key))
                continue;

            lines.Add($"{kv.Key}: {kv.Value}".TrimEnd());
        }

        return string.Join("\n", lines);
    }

    public UserProfile WithReportText(string reportText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in (reportText ?? string.Empty).Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;

            var idx = t.IndexOf(':');
            if (idx <= 0) continue;

            var key = t[..idx].Trim();
            var val = t[(idx + 1)..].Trim();

            if (key.Equals("이름", StringComparison.OrdinalIgnoreCase) || key.Equals("ID", StringComparison.OrdinalIgnoreCase) || key.Equals("생성일", StringComparison.OrdinalIgnoreCase))
                continue;

            if (key.Length == 0) continue;
            dict[key] = val;
        }

        // keep existing sources unless user explicitly typed them
        foreach (var kv in Fields)
        {
            if (IsSourceKey(kv.Key) && !dict.ContainsKey(kv.Key))
                dict[kv.Key] = kv.Value;
        }

        return this with { Fields = dict };
    }
}
