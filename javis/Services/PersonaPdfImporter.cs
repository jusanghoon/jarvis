using System;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;

namespace javis.Services;

public static class PersonaPdfImporter
{
    public static string? TryExtractText(string pdfPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return null;

            var sb = new StringBuilder();

            using var doc = PdfDocument.Open(pdfPath);
            foreach (var page in doc.GetPages())
            {
                var text = (page.Text ?? string.Empty).Trim();
                if (text.Length == 0) continue;

                sb.AppendLine(text);
                sb.AppendLine();
            }

            var all = sb.ToString().Trim();
            if (all.Length == 0) return null;

            // Basic cleanup (normalize newlines)
            all = all.Replace("\r", "");
            all = string.Join("\n", all.Split('\n').Select(x => x.TrimEnd()));

            return all;
        }
        catch
        {
            return null;
        }
    }
}
