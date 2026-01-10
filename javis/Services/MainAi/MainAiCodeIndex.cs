using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace javis.Services.MainAi;

public static class MainAiCodeIndex
{
    // Minimal, safe, and cheap "code context":
    // - list key files
    // - a few important type names / namespaces
    // Avoid shipping full source to an LLM prompt (cost + leakage).

    public static string BuildIndexText(string solutionRoot, int maxFiles = 220)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(solutionRoot) || !Directory.Exists(solutionRoot))
                return "(code index unavailable)";

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".xaml"
            };

            var files = Directory.EnumerateFiles(solutionRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .Take(maxFiles)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("files:");
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(solutionRoot, f);
                sb.AppendLine("- " + rel.Replace('\\', '/'));
            }

            // Add a handful of known entry points to help the model.
            sb.AppendLine();
            sb.AppendLine("entrypoints:");
            sb.AppendLine("- javis/MainWindow.xaml(.cs)");
            sb.AppendLine("- javis/App.xaml(.cs)");
            sb.AppendLine("- javis/Pages/*");
            sb.AppendLine("- javis/ViewModels/*");
            sb.AppendLine("- javis/Services/*");

            return sb.ToString();
        }
        catch
        {
            return "(code index build failed)";
        }
    }
}
