using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using javis.Services;

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
                javis.Services.MainAi.MainAiOrchestrator.Instance.Start();
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

            base.OnExit(e);
        }
    }
}
