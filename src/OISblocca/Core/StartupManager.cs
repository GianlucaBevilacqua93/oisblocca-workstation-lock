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

        public static bool IsStartingWithWindows()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureDefaultStartup()
        {
            try
            {
                // Assicura sempre la registrazione dell'avvio automatico e i corretti flag di sistema
                SetStartWithWindows(true);
            }
            catch
            {
            }
        }

        public static void SetStartWithWindows(bool enable)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                // 1. Configurazione nel Registro di Windows (HKCU\Run)
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
                {
                    if (key != null)
                    {
                        
                        if (enable)
                        {
                            // Avvia con flag --tray per rimanere minimizzato all'avvio del sistema
                            key.SetValue(AppName, $"\"{exePath}\" --tray");
                        }
                        else
                        {
                            if (key.GetValue(AppName) != null)
                            {
                                key.DeleteValue(AppName, false);
                            }
                        }
                    }
                }

                // 2. Configurazione in Windows Task Scheduler (onlogon con privilegi di amministratore /rl highest)
                if (enable)
                {
                    RegisterScheduledTask(exePath);
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
                // Definizione XML per garantire l'avvio anche quando il PC portatile è alimentato a batteria
                // (DisallowStartIfOnBatteries = false) e prevenire l'arresto a 72 ore (ExecutionTimeLimit = PT0S)
                string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>OISblocca Avvio Automatico Elevato</Description>
  </RegistrationInfo>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
  </Settings>
  <Triggers>
    <LogonTrigger />
  </Triggers>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{exePath}""</Command>
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

                try { File.Delete(tempXml); } catch { }
            }
            catch
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{TaskName}\" /tr \"'{exePath}' --tray\" /sc onlogon /rl highest /f",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                catch
                {
                }
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
