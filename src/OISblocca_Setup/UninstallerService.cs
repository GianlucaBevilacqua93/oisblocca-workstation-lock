using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OISblocca_Setup
{
    public static class UninstallerService
    {
        public static async Task UninstallAsync(Action<int, string> progress)
        {
            progress(10, "chiusura delle istanze attive di OISblocca...");
            await Task.Run(() => InstallerService.TerminateRunningInstances());
            await Task.Delay(400);

            progress(30, "ripristino regole del firewall di windows...");
            await Task.Run(() => RemoveFirewallRules());
            await Task.Delay(300);

            progress(50, "rimozione dell'avvio automatico con windows...");
            await Task.Run(() => RemoveStartup());
            await Task.Delay(300);

            progress(70, "rimozione dei collegamenti e delle scorciatoie...");
            await Task.Run(() => RemoveShortcuts());
            await Task.Delay(300);

            progress(85, "rimozione dal pannello di controllo (installazione applicazioni)...");
            await Task.Run(() => RemoveFromControlPanel());
            await Task.Delay(300);

            progress(95, "eliminazione dei file installati...");
            await Task.Run(() => ScheduleFolderCleanup());
            await Task.Delay(300);

            progress(100, "disinstallazione completata con successo!");
        }

        private static void RemoveFirewallRules()
        {
            try
            {
                // Rimuovi tutte le regole con prefisso OIS_
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "advfirewall firewall delete rule name=all dir=out",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                // In alternativa rimuovi specificamente le regole generate da OISblocca
                var psiOis = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-NetFirewallRule -Name 'OIS_*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psiOis);
                proc?.WaitForExit(4000);
            }
            catch
            {
            }
        }

        private static void RemoveStartup()
        {
            try
            {
                // 1. Task Scheduler
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/delete /tn \"OISblocca\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch
            {
            }

            try
            {
                // 2. Bonifica chiavi Run e impostazioni
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    runKey?.DeleteValue("OISblocca", false);
                }

                using (var hklmRun = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    hklmRun?.DeleteValue("OISblocca", false);
                }

                Registry.CurrentUser.DeleteSubKeyTree(@"Software\OISblocca", false);
            }
            catch
            {
            }
        }

        private static void RemoveShortcuts()
        {
            try
            {
                string startMenuLnk = Path.Combine(ShortcutHelper.CommonProgramsFolder, "OISblocca.lnk");
                ShortcutHelper.DeleteShortcut(startMenuLnk);

                string desktopLnk = Path.Combine(ShortcutHelper.CommonDesktopFolder, "OISblocca.lnk");
                ShortcutHelper.DeleteShortcut(desktopLnk);
            }
            catch
            {
            }
        }

        private static void RemoveFromControlPanel()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\OISblocca", false);
            }
            catch
            {
            }
        }

        private static void ScheduleFolderCleanup()
        {
            try
            {
                string? currentExe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExe)) return;

                string installDir = Path.GetDirectoryName(currentExe) ?? "";
                if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) return;

                // Cancella subito i file non bloccati (OISblocca.exe, ecc.)
                foreach (var file in Directory.GetFiles(installDir))
                {
                    try
                    {
                        if (!file.Equals(currentExe, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }

                // Avvia script asincrono per eliminare Uninstall.exe e la cartella subito dopo la chiusura
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 1 /nobreak >nul & rmdir /s /q \"{installDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch
            {
            }
        }
    }
}
