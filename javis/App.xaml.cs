using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using javis.Services;
using javis.Services.Inbox;

namespace javis
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static JarvisKernel Kernel { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Kernel = new JarvisKernel();
            Kernel.Initialize();

            try
            {
                // best-effort: retry any pending inbox compactions from previous runs
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                _ = DailyInboxCompactor.TryCompactAllPendingAsync(cts.Token);
            }
            catch { }

            try
            {
                javis.Services.MainAi.MainAiOrchestrator.Instance.Start();
            }
            catch { }

            try
            {
                // Persist MainAI doc/update suggestions per profile (append-only)
                javis.Services.MainAi.MainAiDocBus.Suggestion += p =>
                {
                    try
                    {
                        var store = new javis.Services.MainAi.MainAiDocSuggestionStore(UserProfileService.Instance.ActiveUserDataDir);
                        store.AppendSuggestion(p.Text, p.Source);
                    }
                    catch { }
                };
            }
            catch { }

            // crash hooks (must never throw)
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                {
                    try
                    {
                        var ex = args.ExceptionObject as System.Exception;
                        Kernel?.Archive.Record(
                            content: $"CRACK_DETECTED: {(ex?.GetType().Name ?? "UnhandledException")}: {(ex?.Message ?? "(null)")}",
                            role: Jarvis.Core.Archive.GEMSRole.Logician,
                            state: Jarvis.Core.Archive.KnowledgeState.Active,
                            sessionId: null,
                            meta: new System.Collections.Generic.Dictionary<string, object?>
                            {
                                ["kind"] = "crash",
                                ["where"] = "AppDomain.UnhandledException",
                                ["isTerminating"] = args.IsTerminating
                            });
                    }
                    catch { }
                };

                TaskScheduler.UnobservedTaskException += (_, args) =>
                {
                    try
                    {
                        Kernel?.Archive.Record(
                            content: $"CRACK_DETECTED: UnobservedTaskException: {args.Exception.Message}",
                            role: Jarvis.Core.Archive.GEMSRole.Logician,
                            state: Jarvis.Core.Archive.KnowledgeState.Active,
                            sessionId: null,
                            meta: new System.Collections.Generic.Dictionary<string, object?>
                            {
                                ["kind"] = "crash",
                                ["where"] = "TaskScheduler.UnobservedTaskException"
                            });
                        args.SetObserved();
                    }
                    catch { }
                };

                DispatcherUnhandledException += (_, args) =>
                {
                    try
                    {
                        Kernel?.Archive.Record(
                            content: $"CRACK_DETECTED: DispatcherUnhandledException: {args.Exception.Message}",
                            role: Jarvis.Core.Archive.GEMSRole.Logician,
                            state: Jarvis.Core.Archive.KnowledgeState.Active,
                            sessionId: null,
                            meta: new System.Collections.Generic.Dictionary<string, object?>
                            {
                                ["kind"] = "crash",
                                ["where"] = "Application.DispatcherUnhandledException"
                            });
                    }
                    catch { }
                };
            }
            catch { }

            try
            {
                // One-time bootstrap release note per profile (prevents repeating on every launch)
                var dataDir = UserProfileService.Instance.ActiveUserDataDir;
                var guardDir = System.IO.Path.Combine(dataDir, "main_ai");
                System.IO.Directory.CreateDirectory(guardDir);
                var guardPath = System.IO.Path.Combine(guardDir, "release_bootstrap.v1");

                if (!System.IO.File.Exists(guardPath))
                {
                    javis.Services.MainAi.MainAiUpdateLogger.Note(
                        title: "프로필별 업데이트 제안/릴리즈 노트 시스템 추가",
                        body: "- 도움말(Main AI) 위젯 질문에서 문서화/업데이트 제안을 자동 적재\n- 업데이트 페이지에서 최근 20개 제안/릴리즈 노트 표시\n- 항목은 삭제 대신 '처리됨(resolved)' 마킹",
                        tag: "main_ai",
                        source: "bootstrap");

                    System.IO.File.WriteAllText(guardPath, DateTimeOffset.Now.ToString("O"), new System.Text.UTF8Encoding(true));
                }
            }
            catch { }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                try { javis.Services.MainAi.MainAiOrchestrator.Instance.Stop(); } catch { }
                try { javis.Services.MainAi.MainAiOrchestrator.Instance.Dispose(); } catch { }
            }
            catch { }

            try
            {
                try
                {
                    Kernel?.Persona?.Dispose();
                }
                catch
                {
                    // ignore
                }

                if (Kernel?.Logger != null)
                    await Kernel.Logger.DisposeAsync();
            }
            catch
            {
                // ignore
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                _ = await DailyInboxCompactor.TryCompactAllPendingAsync(cts.Token);
            }
            catch { }

            base.OnExit(e);
        }
    }
}
