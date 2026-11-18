using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OISblocca.Core
{
    public static class StartupManager
    {
        private const string AppName = "OISblocca";
        private const string TaskName = "OISblocca";
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsRegistryPath = @"Software\OISblocca";

        public static bool IsStartingWithWindows()
        {
            try
            {
                if (IsScheduledTaskConfigured())
                {
                    return true;
                }

                using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath, false);
                if (key != null)
                {
                    object? val = key.GetValue("StartWithWindows");
                    if (val is int intVal) return intVal == 1;
                }

                return ConfigManager.LoadSettings().StartWithWindows;
            }
            catch
            {
                return true;
            }
        }

        public static void EnsureDefaultStartup()
        {
            try
            {
                // Assicura sempre la registrazione dell'avvio automatico e la bonifica di chiavi legacy
                SetStartWithWindows(true);
            }
            catch
            {
            }
        }

        public static void RemoveFromRegistryRun()
        {
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (hkcu?.GetValue(AppName) != null)
                {
                    hkcu.DeleteValue(AppName, false);
                }
            }
            catch { }

            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(RegistryPath, true);
                if (hklm?.GetValue(AppName) != null)
                {
                    hklm.DeleteValue(AppName, false);
                }
            }
            catch { }
        }

        public static void SetStartWithWindows(bool enable)
        {
            try
            {
                string? exePath = Environment.ProcessPath;

                // 1. Bonifica della chiave Run del Registro di Windows:
                // Le applicazioni con privilegi di amministratore NON devono risiedere in Run
                // perché Windows Explorer richiederebbe la password di amministratore (UAC) a ogni riavvio.
                RemoveFromRegistryRun();

                // 2. Memorizza preferenza nelle impostazioni di configurazione
                try
                {
                    using var settingsKey = Registry.CurrentUser.CreateSubKey(SettingsRegistryPath, true);
                    settingsKey?.SetValue("StartWithWindows", enable ? 1 : 0, RegistryValueKind.DWord);
                }
                catch { }

                // 3. Configurazione in Windows Task Scheduler (onlogon con massimi privilegi /rl highest senza UAC)
                if (enable)
                {
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        RegisterScheduledTask(exePath);
                    }
                    PromoteTrayIcon();
                }
                else
                {
                    UnregisterScheduledTask();
                }
            }
            catch
            {
            }
        }

        public static void PromoteTrayIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                using var baseKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true);
                if (baseKey == null) return;

                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(subName, true);
                        if (subKey == null) continue;

                        string? exec = subKey.GetValue("ExecutablePath") as string;
                        if (string.IsNullOrEmpty(exec)) continue;

                        if ((!string.IsNullOrEmpty(exePath) && exec.Equals(exePath, StringComparison.OrdinalIgnoreCase)) ||
                            exec.Contains("OISblocca", StringComparison.OrdinalIgnoreCase))
                        {
                            // Imposta IsPromoted = 1 per mostrare l'icona sempre visibile sulla barra delle applicazioni
                            // senza nasconderla nel menu a scomparsa (chevron ^) di Windows 11
                            subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsScheduledTaskConfigured()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(2000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void RegisterScheduledTask(string exePath)
        {
            try
            {
                string cleanExePath = exePath.Trim('\"');

                // Definizione XML per Task Scheduler:
                // - Avvio con massimi privilegi al logon (senza prompt UAC o richiesta password)
                // - Avvio abilitato sia su alimentazione a batteria che da rete elettrica
                // - Nessun limite di tempo a 72 ore (ExecutionTimeLimit = PT0S)
                // - NOTA: <Command> non deve contenere virgolette nello schema XML
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
                File.WriteAllText(tempXml, xml, System.Text.Encoding.Unicode);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{TaskName}\" /xml \"{tempXml}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                bool success = (proc != null && proc.ExitCode == 0);
                try { File.Delete(tempXml); } catch { }

                // Fallback da riga di comando diretta qualora l'importazione XML non sia andata a buon fine
                if (!success)
                {
                    var fallbackPsi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /tr \"\\\"{cleanExePath}\\\" --tray\" /sc onlogon /rl highest /f",
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

        private static void UnregisterScheduledTask()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch
            {
            }
        }
    }
}
