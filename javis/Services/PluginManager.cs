using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace javis.Services;

public sealed class PluginManager
{
    private readonly string _pluginsDir;
    private readonly SkillRuntime _runtime;

    public PluginManager(string pluginsDir, SkillRuntime runtime)
    {
        _pluginsDir = pluginsDir;
        _runtime = runtime;
        Directory.CreateDirectory(_pluginsDir);
    }

    public void LoadAll()
    {
        try { javis.App.Kernel?.Logger?.Log("plugin.load.start", new { dir = _pluginsDir }); } catch { }

        foreach (var file in Directory.EnumerateFiles(_pluginsDir, "*.plugin.cs"))
        {
            try
            {
                try { javis.App.Kernel?.Logger?.Log("plugin.compile.start", new { file }); } catch { }

                var code = File.ReadAllText(file);
                LoadOne(code, Path.GetFileNameWithoutExtension(file));

                try { javis.App.Kernel?.Logger?.Log("plugin.load.ok", new { file }); } catch { }
            }
            catch (Exception ex)
            {
                try { javis.App.Kernel?.Logger?.Log("plugin.compile.fail", new { file, error = ex.Message, stack = ex.ToString() }); } catch { }
                Console.WriteLine($"[PluginError] {file}: {ex.Message}");
            }
        }

        try { javis.App.Kernel?.Logger?.Log("plugin.load.end", new { actions = _runtime.ActionTypes.ToArray() }); } catch { }
    }

    private void LoadOne(string code, string nameBase)
    {
        var asmName = $"Jarvis.Plugin.{nameBase}.{Guid.NewGuid():N}";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonDocument).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ISkillPlugin).Assembly.Location)
        };

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic) continue;
            if (string.IsNullOrWhiteSpace(a.Location)) continue;

            try
            {
                refs.Add(MetadataReference.CreateFromFile(a.Location));
            }
            catch
            {
                // ignore
            }
        }

        refs = refs
            .GroupBy(r => r.Display)
            .Select(g => g.First())
            .ToList();

        var compilation = CSharpCompilation.Create(
            asmName,
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);

        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new Exception($"컴파일 실패:\n{errors}");
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext(asmName, isCollectible: true);
        var asm = alc.LoadFromStream(ms);

        var pluginTypes = asm.GetTypes()
            .Where(t => typeof(ISkillPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();

        foreach (var pt in pluginTypes)
        {
            var plugin = (ISkillPlugin)Activator.CreateInstance(pt)!;
            plugin.Register(_runtime);
        }
    }
}
