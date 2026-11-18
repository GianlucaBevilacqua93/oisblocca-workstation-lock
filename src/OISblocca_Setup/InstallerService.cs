using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OISblocca_Setup
{
    public static class InstallerService
    {
        public static string DefaultInstallDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OISblocca");

        public static string GetDiskFreeSpaceInfo(string targetPath)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? "C:\\";
                var drive = new DriveInfo(root);
                double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                return $"{freeGb:0.1} GB liberi";
            }
            catch
            {
                return "Spazio disponibile";
            }
        }

        public static void TerminateRunningInstances()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                var targets = new[] { "OISblocca", "OISblocca_v2" };
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == currentPid) continue;
                        if (targets.Any(t => p.ProcessName.Equals(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
            }
        }

        public static async Task InstallAsync(
            string installDir,
            bool createStartMenuShortcut,
            bool createDesktopShortcut,
            bool startWithWindows,
            Action<int, string> progress)
        {
            progress(5, "chiusura di eventuali istanze attive di OISblocca...");
            await Task.Run(() => TerminateRunningInstances());
            await Task.Delay(400);

            progress(15, "creazione della cartella di installazione...");
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }
            await Task.Delay(300);

            progress(35, "estrazione dei file dell'applicazione (OISblocca.exe)...");
            string targetExe = Path.Combine(installDir, "OISblocca.exe");
            await Task.Run(() => ExtractPayload(targetExe));
            await Task.Delay(400);

            progress(55, "creazione del modulo di disinstallazione...");
            string uninstallerExe = Path.Combine(installDir, "Uninstall.exe");
            await Task.Run(() =>
            {
                string? currentExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    File.Copy(currentExe, uninstallerExe, true);
                }
            });
            await Task.Delay(300);

            progress(70, "creazione delle scorciatoie e collegamenti...");
            await Task.Run(() =>
            {
                string desc = "OISblocca - Gestione e protezione postazione gara OIS";

                if (createStartMenuShortcut)
                {
                    string startMenuLnk = Path.Combine(ShortcutHelper.CommonProgramsFolder, "OISblocca.lnk");
                    ShortcutHelper.CreateShortcut(startMenuLnk, targetExe, desc, installDir);
                }

                if (createDesktopShortcut)
                {
                    string desktopLnk = Path.Combine(ShortcutHelper.CommonDesktopFolder, "OISblocca.lnk");
                    ShortcutHelper.CreateShortcut(desktopLnk, targetExe, desc, installDir);
                }
            });
            await Task.Delay(300);

            progress(85, "registrazione nel sistema operativo (installazione applicazioni)...");
            await Task.Run(() => RegisterInControlPanel(installDir, targetExe, uninstallerExe));
            await Task.Delay(300);

            if (startWithWindows)
            {
                progress(95, "configurazione dell'avvio automatico con windows...");
                await Task.Run(() => ConfigureStartup(targetExe));
                await Task.Delay(300);
            }

            progress(100, "installazione completata con successo!");
        }

        private static void ExtractPayload(string targetPath)
        {
            var asm = Assembly.GetExecutingAssembly();
            string resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("OISblocca.exe", StringComparison.OrdinalIgnoreCase)) ?? "";

            if (string.IsNullOrEmpty(resourceName))
            {
                throw new FileNotFoundException("Risorsa payload OISblocca.exe non trovata all'interno del programma di installazione.");
            }

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("Impossibile aprire lo stream della risorsa payload.");
            }

            using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.CopyTo(dest);
        }

        private static void RegisterInControlPanel(string installDir, string targetExe, string uninstallerExe)
        {
            try
            {
                const string uninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OISblocca";
                using var key = Registry.LocalMachine.CreateSubKey(uninstallKey, true);
                if (key == null) return;

                key.SetValue("DisplayName", "OISblocca");
                key.SetValue("DisplayVersion", "2.0.0");
                key.SetValue("Publisher", "Prof. Quarto, Prof. Bevilacqua - Olimpiadi di Informatica a Squadre (oisquadre.it)");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("DisplayIcon", $"{targetExe},0");
                key.SetValue("UninstallString", $"\"{uninstallerExe}\"");
                key.SetValue("QuietUninstallString", $"\"{uninstallerExe}\" --quiet");
                key.SetValue("EstimatedSize", 75000, RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("HelpLink", "https://oisquadre.it");
                key.SetValue("URLInfoAbout", "https://oisquadre.it");
            }
            catch
            {
            }
        }

        private static void ConfigureStartup(string targetExe)
        {
            try
            {
                string cleanExePath = targetExe.Trim('\"');

                // 1. Bonifica della chiave Run: non inserire eseguibili elevati in Run per evitare prompt UAC al riavvio
                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    runKey?.DeleteValue("OISblocca", false);
                }
                catch { }

                try
                {
                    using var hklmRun = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    hklmRun?.DeleteValue("OISblocca", false);
                }
                catch { }

                // 2. Registra preferenza in Software\OISblocca
                try
                {
                    using var settingsKey = Registry.CurrentUser.CreateSubKey(@"Software\OISblocca", true);
                    settingsKey?.SetValue("StartWithWindows", 1, RegistryValueKind.DWord);
                }
                catch { }

                // 3. Windows Task Scheduler: avvio automatico con massimi privilegi al logon (senza prompt UAC né password)
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>OISblocca Avvio Automatico Elevato</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <GroupId>S-1-5-32-544</GroupId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{cleanExePath}</Command>
      <Arguments>--tray</Arguments>
    </Exec>
  </Actions>
</Task>";

                string tempXml = Path.Combine(Path.GetTempPath(), $"ois_task_{Guid.NewGuid():N}.xml");
                File.WriteAllText(tempXml, xml, Encoding.Unicode);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"OISblocca\" /xml \"{tempXml}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                bool success = (proc != null && proc.ExitCode == 0);
                try { File.Delete(tempXml); } catch { }

                // Fallback da riga di comando diretta
                if (!success)
                {
                    var fallbackPsi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"OISblocca\" /tr \"\\\"{cleanExePath}\\\" --tray\" /sc onlogon /rl highest /f",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var fallbackProc = Process.Start(fallbackPsi);
                    fallbackProc?.WaitForExit(3000);
                }
            }
            catch
            {
            }
        }
    }
}
