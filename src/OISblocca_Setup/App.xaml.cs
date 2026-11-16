using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OISblocca_Setup
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"Errore durante l'installazione:\n{args.Exception.Message}\n\n{args.Exception.InnerException?.Message}",
                    "OISblocca Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                string currentExe = Environment.ProcessPath ?? "";
                string exeName = Path.GetFileNameWithoutExtension(currentExe);

                bool isUninstallMode = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                                       a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase) ||
                                                       a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                                       || exeName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);

                bool isSilent = e.Args.Any(a => a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                                                 a.Equals("-s", StringComparison.OrdinalIgnoreCase));

                if (isSilent)
                {
                    if (isUninstallMode)
                    {
                        await UninstallerService.UninstallAsync((val, msg) => { });
                        Environment.Exit(0);
                        return;
                    }
                    else
                    {
                        await InstallerService.InstallAsync(
                            InstallerService.DefaultInstallDirectory,
                            createStartMenuShortcut: true,
                            createDesktopShortcut: true,
                            startWithWindows: true,
                            (val, msg) => { });
                        Environment.Exit(0);
                        return;
                    }
                }

                var win = new MainWindow(isUninstallMode);
                win.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Impossibile avviare la procedura di installazione:\n{ex.Message}\n\n{ex.StackTrace}",
                    "OISblocca Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }
    }
}
