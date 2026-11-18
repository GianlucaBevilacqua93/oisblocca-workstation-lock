using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using OISblocca.Core;
using OISblocca.UI;

namespace OISblocca
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = @"Global\OISblocca_SingleInstance_Mutex_2026";
        private const string EventName = @"Global\OISblocca_WakeUpEvent_2026";

        private static Mutex? _instanceMutex;
        private static FileStream? _lockFileStream;
        private static EventWaitHandle? _wakeUpEvent;
        private static RegisteredWaitHandle? _waitHandle;
        private MainWindow? _mainWindow;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        public App()
        {
            var args = Environment.GetCommandLineArgs();

            // Verifica per consentire solo e soltanto una istanza attiva
            if (!AcquireSingleInstanceLock())
            {
                // Se è un avvio manuale (senza parametri silenziosi da tray/avvio automatico),
                // risveglia e porta in primo piano l'unica istanza già attiva
                bool isBackgroundLaunch = args.Any(a =>
                    a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/tray", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

                if (!isBackgroundLaunch)
                {
                    SignalAndActivateExistingInstance();
                }

                // Termina immediatamente il processo a livello di sistema operativo
                Environment.Exit(0);
                return;
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Assicura che l'applicazione rimanga sempre attiva in background anche con finestra invisibile
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Registra listener per risveglio dall'esterno qualora l'utente riapra l'eseguibile
            SetupWakeUpListener();

            _mainWindow = new MainWindow();

            bool startInTray = e.Args.Any(a =>
                a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/tray", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

            if (!startInTray)
            {
                _mainWindow.Show();
            }
        }

        private static bool AcquireSingleInstanceLock()
        {
            // Controllo 1: Verifica processi concorrenti di sistema
            try
            {
                var current = Process.GetCurrentProcess();
                int currentPid = current.Id;
                string currentName = current.ProcessName;

                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == currentPid) continue;

                        if (p.ProcessName.Equals(currentName, StringComparison.OrdinalIgnoreCase) ||
                            p.ProcessName.Equals("OISblocca", StringComparison.OrdinalIgnoreCase) ||
                            p.ProcessName.Equals("OISblocca_v2", StringComparison.OrdinalIgnoreCase))
                        {
                            return false; // Altra istanza già in esecuzione
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

            // Controllo 2: Mutex globale di Windows con permessi per tutti gli utenti
            try
            {
                var mutexSecurity = new MutexSecurity();
                var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                mutexSecurity.AddAccessRule(new MutexAccessRule(worldSid, MutexRights.FullControl, AccessControlType.Allow));

                _instanceMutex = MutexAcl.Create(true, MutexName, out bool createdNew, mutexSecurity);
                if (!createdNew)
                {
                    return false;
                }
            }
            catch
            {
                try
                {
                    _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
                    if (!createdNew)
                    {
                        return false;
                    }
                }
                catch
                {
                    try
                    {
                        _instanceMutex = new Mutex(true, @"Local\OISblocca_SingleInstance_Mutex_2026", out bool createdNewLocal);
                        if (!createdNewLocal)
                        {
                            return false;
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            // Controllo 3: Blocco esclusivo a livello di filesystem su lockfile
            try
            {
                string lockFolder = SecurityService.AppDataFolder;
                if (!Directory.Exists(lockFolder))
                {
                    Directory.CreateDirectory(lockFolder);
                }

                string lockFile = Path.Combine(lockFolder, "instance.lock");
                _lockFileStream = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return false;
            }
            catch
            {
            }

            return true;
        }

        private void SetupWakeUpListener()
        {
            try
            {
                var eventSecurity = new EventWaitHandleSecurity();
                var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(worldSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                _wakeUpEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, EventName, out _, eventSecurity);
            }
            catch
            {
                try
                {
                    _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                }
                catch
                {
                }
            }

            if (_wakeUpEvent != null)
            {
                _waitHandle = ThreadPool.RegisterWaitForSingleObject(_wakeUpEvent, (state, timedOut) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _mainWindow?.RestoreTrayIcon();
                        _mainWindow?.PromptAndShow();
                    });
                }, null, Timeout.Infinite, false);
            }
        }

        private static void SignalAndActivateExistingInstance()
        {
            // 1. Segnala tramite EventWaitHandle
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                }
            }
            catch
            {
            }

            // 2. Se l'istanza precedente ha una finestra visibile, portala in primo piano
            try
            {
                var currentPid = Process.GetCurrentProcess().Id;
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id != currentPid &&
                            p.ProcessName.Equals("OISblocca", StringComparison.OrdinalIgnoreCase))
                        {
                            IntPtr hWnd = p.MainWindowHandle;
                            if (hWnd != IntPtr.Zero)
                            {
                                if (IsIconic(hWnd))
                                {
                                    ShowWindow(hWnd, SW_RESTORE);
                                }
                                SetForegroundWindow(hWnd);
                                break;
                            }
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

        public static bool IsSessionEnding { get; private set; }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);
            IsSessionEnding = true;
            // Non impedire mai la chiusura durante il riavvio o arresto di Windows
            e.Cancel = false;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _waitHandle?.Unregister(null);
                _wakeUpEvent?.Dispose();

                if (_instanceMutex != null)
                {
                    _instanceMutex.ReleaseMutex();
                    _instanceMutex.Dispose();
                }

                if (_lockFileStream != null)
                {
                    _lockFileStream.Dispose();
                }
            }
            catch
            {
            }

            base.OnExit(e);
        }
    }
}
