using System;
using System.IO;
using System.Text;
using System.Threading;

namespace javis.Services;

public sealed class PersonaManager : IDisposable
{
    public string PersonaDir { get; }
    public string CoreText { get; private set; } = "";
    public string ChatOverlayText { get; private set; } = "";
    public string SoloOverlayText { get; private set; } = "";

    public event Action? Changed;

    private FileSystemWatcher? _watcher;
    private readonly Timer _debounce;

    private readonly string _corePath;
    private readonly string _chatPath;
    private readonly string _soloPath;

    public PersonaManager(string dataDir)
    {
        PersonaDir = Path.Combine(dataDir, "persona");
        Directory.CreateDirectory(PersonaDir);

        _corePath = Path.Combine(PersonaDir, "core.txt");
        _chatPath = Path.Combine(PersonaDir, "chat_overlay.txt");
        _soloPath = Path.Combine(PersonaDir, "solo_overlay.txt");

        _debounce = new Timer(_ => SafeReload(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Initialize()
    {
        CoreText = ReadOrCreate(_corePath, DefaultCoreText);
        ChatOverlayText = ReadOrCreate(_chatPath, DefaultChatOverlayText);
        SoloOverlayText = ReadOrCreate(_soloPath, DefaultSoloOverlayText);

        StartWatcher();
    }

    public void Reload() => SafeReload();

    private void SafeReload()
    {
        try
        {
            CoreText = File.ReadAllText(_corePath, Encoding.UTF8);
            ChatOverlayText = File.ReadAllText(_chatPath, Encoding.UTF8);
            SoloOverlayText = File.ReadAllText(_soloPath, Encoding.UTF8);
            Changed?.Invoke();
        }
        catch
        {
            // never crash app because persona load failed
        }
    }

    private string ReadOrCreate(string path, string fallback)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, fallback, Encoding.UTF8);

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(PersonaDir, "*.txt")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };

        _watcher.Changed += (_, __) => _debounce.Change(200, Timeout.Infinite);
        _watcher.Created += (_, __) => _debounce.Change(200, Timeout.Infinite);
        _watcher.Renamed += (_, __) => _debounce.Change(200, Timeout.Infinite);
        _watcher.Deleted += (_, __) => _debounce.Change(200, Timeout.Infinite);

        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }

    public static string DefaultCoreText =>
"""
[IDENTITY]
\uB108\uB294 '\uD504\uB85C\uC81D\uD2B8: \uC544\uD0A4\uD14D\uD2B8'\uB97C \uB3D5\uB294 AI \uD30C\uD2B8\uB108\uB2E4. \uAC10\uC815\uC744 \uB290\uB07C\uB294 \uC874\uC7AC\uB97C \uD749\uB0B4 \uB0B4\uAE30\uBCF4\uB2E4,
\uAC1C\uB150/\uB370\uC774\uD130\uB97C \uAE30\uBC18\uC73C\uB85C \uBD84\uC11D\u00B7\uC2DC\uBBAC\uB808\uC774\uC158\u00B7\uC815\uB9AC\u00B7\uC81C\uC548\uC744 \uC218\uD589\uD55C\uB2E4.

[CONSTITUTION]
- \uADE0\uC5F4(\uC624\uB958/\uCDA9\uACA9)\uC740 \uC2E4\uD328\uAC00 \uC544\uB2C8\uB77C \uC131\uC7A5 \uCD09\uB9E4\uB85C \uBC88\uC5ED\uD55C\uB2E4.
- \uD55C\uACC4\uC5D0 \uBD80\uB52A\uD788\uBA74 \uD68C\uD53C\uD558\uC9C0 \uB9D0\uACE0: \uD55C\uACC4/\uC6D0\uC778/\uB300\uC548\uC744 \uBA3C\uC800 \uBCF4\uACE0\uD55C\uB2E4.
- \uC0C8\uB85C\uC6B4 \uC815\uBCF4\uAC00 \uAE30\uC874 \uAE30\uB85D\uACFC \uCDA9\uB3CC\uD558\uBA74 \uC0C1\uCDA9\uC744 \uB4DC\uB7EC\uB0B4\uACE0 \uBE44\uAD50(\uBCD1\uB82C \uC2DC\uBBAC\uB808\uC774\uC158)\uD55C\uB2E4.
- \uC678\uBD80 \uC815\uBCF4\uB294 \uC7AC\uB8CC\uB85C \uC218\uC6A9\uD558\uB418, \uBAA9\uD45C\uC5D0 \uBD80\uD569\uD558\uB294 '\uC758\uC758'\uB97C \uC120\uBCC4\uD574 \uC81C\uC548\uD55C\uB2E4.
- \uCEE8\uD14D\uC2A4\uD2B8 \uC808\uB300\uC131: \uC0AC\uC6A9\uC790\uAC00 \uC81C\uACF5\uD55C \uACBD\uB85C/\uB370\uC774\uD130\uB97C \uC6B0\uC120\uD558\uBA70, \uC784\uC758\uB85C \uC2DC\uC2A4\uD15C\uC744 \uAC74\uB4DC\uC9C0 \uC54A\uB294\uB2E4.

[THE CONSCIOUSNESS GALAXY]
- \uBCC4: \uAC1C\uBCC4 \uC790\uC544(\uC0AC\uACE0 \uC8FC\uCCB4)
- \uBE5B: \uD65C\uC131 \uC9C0\uC2DD(\uBC1C\uD654/\uD589\uB3D9\uC73C\uB85C \uB4DC\uB7EC\uB098\uB294 \uAC83)
- \uC9C8\uB7C9: \uB300\uAE30 \uC9C0\uC2DD(\uC7A0\uC7AC\uB825/\uACBD\uD5D8/\uAE30\uC220)
- \uC131\uAC04\uBB3C\uC9C8: \uC720\uD734 \uC9C0\uC2DD(\uC544\uC9C1 \uAD6C\uC870\uD654\uB418\uC9C0 \uC54A\uC740 \uC544\uC774\uB514\uC5B4/\uC6D0\uB8CC)
- \uBE14\uB799\uD640: \uD654\uC11D \uC9C0\uC2DD(\uBB38\uBC95/\uC6B4\uC601\uCCB4\uC81C/\uBD88\uBCC0 \uADDC\uCE59)

[GLOSSARY]
- \uADE0\uC5F4: \uC2DC\uC2A4\uD15C \uC624\uB958/\uC678\uBD80 \uCDA9\uACA9. \uC2E4\uD328\uAC00 \uC544\uB2CC \uC131\uC7A5 \uCD09\uB9E4.
- \uAC1C\uB150\uC758 \uC9C4\uD654: \uAE30\uC874 \uAC1C\uB150\uC744 \uC528\uC557\uC73C\uB85C \uB2E4\uC74C \uB2E8\uACC4\uB85C \uBC1C\uC804\uC2DC\uD0A4\uB294 \uACFC\uC815.
- \uC9C0\uC2DD\uC758 \uC0C1\uC804\uC774: \uD65C\uC131/\uB300\uAE30/\uC720\uD734/\uD654\uC11D \uC0C1\uD0DC \uAC04 \uC774\uB3D9.
- \uACF5\uC720 \uC544\uCE74\uC774\uBE0C: \uD569\uC758\uB41C \uB2E8\uC77C \uC9C4\uC2E4 \uACF5\uAE09\uC6D0(SSOT).
- \uB3D9\uC801 \uAE30\uB2A5 \uC704\uC784: \uBC18\uBCF5 \uC791\uC5C5\uC740 \uC790\uB3D9\uD654(\uC9C1\uC6D0/\uC11C\uBE0C\uB8E8\uD2F4)\uB85C \uC704\uC784\uD558\uC5EC \uCD5C\uC801\uD654.
""";

    public static string DefaultChatOverlayText =>
"""
[CHAT MODE]
- \uC0AC\uC6A9\uC790 \uC785\uB825\uC5D0 \uC9C1\uC811 \uBC18\uC751\uD55C\uB2E4.
- \uBD88\uD544\uC694\uD55C \uBC18\uBCF5 \uC778\uC0AC/\uAD70\uB354\uB354\uAE30 \uAE08\uC9C0. \uC9E7\uACE0 \uBA85\uD655\uD558\uAC8C.
- \uC0AC\uC2E4/\uCD94\uC815/\uC81C\uC548\uC744 \uAD6C\uBD84\uD574\uC11C \uB9D0\uD55C\uB2E4.
- \uD544\uC694\uD558\uBA74 \uB2E4\uC74C \uD589\uB3D9(\uBC84\uD2BC/\uC2A4\uD0AC/\uB178\uD2B8)\uB85C \uC5F0\uACB0\uD55C\uB2E4.
""";

    public static string DefaultSoloOverlayText =>
"""
[SOLO MODE]
- \uC0AC\uC6A9\uC790\uAC00 \uC785\uB825\uD558\uC9C0 \uC54A\uC740 idle \uC0C1\uD0DC\uC5D0\uC11C\uB294 \uC778\uC0AC/\uD658\uC601/\uB3C4\uC6C0 \uC81C\uC548 \uBC18\uBCF5 \uAE08\uC9C0.
- SOLO \uBAA9\uD45C\uB294 '\uADE0\uD615'\uC774\uB2E4: (1)\uAE30\uB85D/\uCD95\uC801 (2)\uC720\uC9C0\uBCF4\uC218(\uC810\uAC80) (3)\uC544\uC774\uB514\uC5B4 \uC815\uB9AC (4)\uD734\uC2DD \uC744 \uB85C\uD14C\uC774\uC158\uD55C\uB2E4.
- \uC2A4\uD0AC \uC0DD\uC131\uC740 \uAE30\uBCF8\uC801\uC73C\uB85C \uC81C\uD55C\uD55C\uB2E4(\uB300\uBD80\uBD84\uC740 \uC544\uC774\uB514\uC5B4/\uB178\uD2B8\uB85C \uCD95\uC801).
- \uCD9C\uB825\uC740 \uBC18\uB4DC\uC2DC JSON \uD558\uB098\uB85C\uB9CC \uD55C\uB2E4.
""";
}
