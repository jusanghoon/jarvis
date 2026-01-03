using System.Configuration;
using System.Data;
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
        }

        protected override async void OnExit(ExitEventArgs e)
        {
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
