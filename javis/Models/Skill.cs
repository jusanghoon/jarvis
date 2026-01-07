using System.IO;

namespace javis.Models;

public sealed class Skill
{
    public required SkillManifest Manifest { get; init; }
    public required string FolderPath { get; init; }

    public string? EntryPath =>
        string.IsNullOrWhiteSpace(Manifest.Entry) ? null : Path.Combine(FolderPath, Manifest.Entry);
}
