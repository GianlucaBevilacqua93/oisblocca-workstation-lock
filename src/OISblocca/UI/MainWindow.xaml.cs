using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OISblocca.Core;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;

namespace OISblocca.UI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SiteItem> Sites { get; } = new();
        private readonly HealthCheckService _probeService = new();
        private bool _isLocked = false;
        private System.Windows.Forms.NotifyIcon _notifyIcon = null!;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        private const uint MSGFLT_ADD = 1;
        private const uint MSGFLT_ALLOW = 1;

        private uint _wmTaskbarCreated;
        private IntPtr _windowHandle = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            SetAppIconSafely();
            DwmHelper.ApplyWindows11Style(this);

            // Assicura l'inizializzazione dell'handle Win32 (HWND) anche quando avviato nascosto (--tray)
            var interopHelper = new WindowInteropHelper(this);
            _windowHandle = interopHelper.EnsureHandle();

            // Registra listener per il messaggio "TaskbarCreated" di explorer.exe
            RegisterTaskbarCreatedListener();

            txtNewDomain.Text = "es. cppreference.com";
            txtNewDomain.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 144, 156));
            txtNewDomain.GotFocus += (s, e) =>
            {
                if (txtNewDomain.Text == "es. cppreference.com")
                {
                    txtNewDomain.Text = "";
                    txtNewDomain.Foreground = System.Windows.Media.Brushes.White;
                }
            };
            txtNewDomain.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtNewDomain.Text))
                {
                    txtNewDomain.Text = "es. cppreference.com";
                    txtNewDomain.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 144, 156));
                }
            };

            txtNewDesc.Text = "Descrizione opzionale...";
            txtNewDesc.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 144, 156));
            txtNewDesc.GotFocus += (s, e) =>
            {
                if (txtNewDesc.Text == "Descrizione opzionale...")
                {
                    txtNewDesc.Text = "";
                    txtNewDesc.Foreground = System.Windows.Media.Brushes.White;
                }
            };
            txtNewDesc.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtNewDesc.Text))
                {
                    txtNewDesc.Text = "Descrizione opzionale...";
                    txtNewDesc.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 144, 156));
                }
            };

            icSites.ItemsSource = Sites;
            SetupApplication();
        }

        private void SetupApplication()
        {
            Log("=== OISblocca v2.0 avviato ===");
            SecurityService.EnsureSecureEnvironment();

            if (!SecurityService.IsAdministrator())
            {
                Log("[Avviso] Privilegi di amministratore assenti. Esegui come amministratore per consentire la protezione della postazione.");
            }

            // Assicura l'avvio con Windows attivo per impostazione predefinita
            StartupManager.EnsureDefaultStartup();

            // 1. Carica configurazione
            var settings = ConfigManager.LoadSettings();
            foreach (var s in settings.Sites)
            {
                Sites.Add(s);
            }
            chkStartup.IsChecked = StartupManager.IsStartingWithWindows();
            UpdateStatsDisplay();

            // 2. Setup System Tray Icon
            SetupSystemTray();

            // 3. Verifica stato di blocco precedente
            _isLocked = FirewallService.IsLockedDown();
            UpdateUiLockState(_isLocked);

            // 4. Avvia probe service in background
            _probeService.OnProbeCycleCompleted += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatsDisplay();
                    if (_isLocked)
                    {
                        // Sincronizza automaticamente eventuali nuovi indirizzi
                        _ = Task.Run(async () => await FirewallService.UpdateActiveRulesAsync(Sites, msg => Log(msg)));
                    }
                });
            };
            _probeService.StartPolling(Sites, 12);

            this.Closing += MainWindow_Closing;
        }

        private void SetupSystemTray()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var miOpen = new System.Windows.Forms.ToolStripMenuItem("Pannello di controllo (richiede password)");
            miOpen.Click += (s, e) => PromptAndShow();
            miOpen.Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);

            var miStatus = new System.Windows.Forms.ToolStripMenuItem("Stato: " + (_isLocked ? "bloccato" : "sbloccato"));
            miStatus.Enabled = false;

            var miUnlock = new System.Windows.Forms.ToolStripMenuItem("Sblocca web rapido (password)");
            miUnlock.Click += async (s, e) => await DoUnlockWithPasswordAsync();

            var miChangePwd = new System.Windows.Forms.ToolStripMenuItem("Modifica password docente");
            miChangePwd.Click += (s, e) => new ChangePasswordWindow().ShowDialog();

            var miSeparator = new System.Windows.Forms.ToolStripSeparator();

            var miExit = new System.Windows.Forms.ToolStripMenuItem("Chiudi applicazione (password)");
            miExit.Click += (s, e) => PromptAndExit();

            menu.Items.Add(miOpen);
            menu.Items.Add(miStatus);
            menu.Items.Add(miUnlock);
            menu.Items.Add(miChangePwd);
            menu.Items.Add(miSeparator);
            menu.Items.Add(miExit);

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "OISblocca v2.0",
                Icon = GetAppIcon(),
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => PromptAndShow();

            // Assicura la promozione dell'icona sulla barra delle applicazioni (Windows 11)
            StartupManager.PromoteTrayIcon();

            // Verifica all'avvio che la shell di Windows (explorer.exe) sia attiva
            EnsureTrayIconReady();
        }

        private void SetAppIconSafely()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/OISblocca;component/Assets/app.ico", UriKind.RelativeOrAbsolute);
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(uri);
            }
            catch
            {
                try
                {
                    var icon = GetAppIcon();
                    if (icon != null)
                    {
                        using var ms = new MemoryStream();
                        icon.Save(ms);
                        ms.Position = 0;
                        this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(ms);
                    }
                }
                catch { }
            }
        }

        private static System.Drawing.Icon GetAppIcon()
        {
            try
            {
                var asm = typeof(MainWindow).Assembly;
                string? res = asm.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(res))
                {
                    using var stream = asm.GetManifestResourceStream(res);
                    if (stream != null)
                    {
                        using var rawIcon = new System.Drawing.Icon(stream);
                        return (System.Drawing.Icon)rawIcon.Clone();
                    }
                }
            }
            catch
            {
            }

            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (icon != null) return icon;
                }
            }
            catch
            {
            }
            return System.Drawing.SystemIcons.Shield;
        }

        private void RegisterTaskbarCreatedListener()
        {
            try
            {
                _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
                if (_wmTaskbarCreated != 0)
                {
                    // Consenti al messaggio TaskbarCreated di superare l'isolamento UIPI
                    // (inviato da explorer.exe a medium integrity verso OISblocca ad alta integrità/admin)
                    try { ChangeWindowMessageFilter(_wmTaskbarCreated, MSGFLT_ADD); } catch { }
                    if (_windowHandle != IntPtr.Zero)
                    {
                        try { ChangeWindowMessageFilterEx(_windowHandle, _wmTaskbarCreated, MSGFLT_ALLOW, IntPtr.Zero); } catch { }
                    }

                    var source = HwndSource.FromHwnd(_windowHandle);
                    source?.AddHook(WndProcHook);
                }
            }
            catch
            {
            }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_wmTaskbarCreated != 0 && (uint)msg == _wmTaskbarCreated)
            {
                // explorer.exe ha ricreato la barra delle applicazioni (crash o riavvio shell): ripristina l'icona
                RestoreTrayIcon();
            }
            return IntPtr.Zero;
        }

        public void RestoreTrayIcon()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => RestoreTrayIcon());
                    return;
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Visible = true;
                    StartupManager.PromoteTrayIcon();
                }
            }
            catch
            {
            }
        }

        private void EnsureTrayIconReady()
        {
            _ = Task.Run(async () =>
            {
                // Verifica una tantum all'avvio: attende che explorer.exe abbia completato l'inizializzazione
                for (int i = 0; i < 30; i++)
                {
                    IntPtr trayHwnd = FindWindow("Shell_TrayWnd", null);
                    if (trayHwnd != IntPtr.Zero)
                    {
                        await Task.Delay(1000);
                        Dispatcher.Invoke(() =>
                        {
                            if (_notifyIcon != null && !_notifyIcon.Visible)
                            {
                                _notifyIcon.Visible = true;
                            }
                            StartupManager.PromoteTrayIcon();
                        });
                        break;
                    }
                    await Task.Delay(1000);
                }
            });
        }

        private bool _isPromptOpen;

        public void PromptAndShow()
        {
            if (_isPromptOpen) return;

            if (this.Visibility == Visibility.Visible && this.WindowState != WindowState.Minimized)
            {
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;
                this.Focus();
                return;
            }

            _isPromptOpen = true;
            try
            {
                var dlg = new PasswordWindow();
                if (dlg.ShowDialog() == true)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                    this.Topmost = true;
                    this.Topmost = false;
                    this.Focus();
                }
            }
            finally
            {
                _isPromptOpen = false;
            }
        }

        public void PromptAndExit()
        {
            var dlg = new PasswordWindow();
            if (dlg.ShowDialog() == true)
            {
                if (_isLocked)
                {
                    FirewallService.RemoveLockdownAsync(msg => { }).Wait();
                }
                _probeService.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void UpdateUiLockState(bool locked)
        {
            _isLocked = locked;
            if (locked)
            {
                pnlStatusPill.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(183, 28, 28));
                dotStatusPill.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82));
                lblStatusPill.Text = "Modalità gara attiva (internet limitato)";
                btnLock.IsEnabled = false;
                btnUnlock.IsEnabled = true;
            }
            else
            {
                pnlStatusPill.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 94, 32));
                dotStatusPill.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118));
                lblStatusPill.Text = "Modalità normale (internet libero)";
                btnLock.IsEnabled = true;
                btnUnlock.IsEnabled = false;
            }
        }

        private async void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            var enabledSites = Sites.Where(s => s.IsEnabled).ToList();
            if (enabledSites.Count == 0)
            {
                MessageBox.Show(this, "nessun sito abilitato nella lista! abilita almeno un sito web consentito per attivare il blocco.", "nessun sito", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnLock.IsEnabled = false;
            Log("[azione] attivazione blocco navigazione in corso...");

            // Risolvi subito tutti gli indirizzi IP (apex, www, CDN e alias)
            foreach (var site in enabledSites)
            {
                await HealthCheckService.ResolveSiteIpsAsync(site);
            }

            bool ok = await FirewallService.ApplyLockdownAsync(Sites, msg => Log(msg));
            if (ok)
            {
                UpdateUiLockState(true);

                // Aggiorna controlli di salute in background senza bloccare la schermata
                _ = Task.Run(async () =>
                {
                    foreach (var site in enabledSites)
                    {
                        await _probeService.CheckSiteHealthAsync(site);
                    }
                    Dispatcher.Invoke(() => UpdateStatsDisplay());
                });
            }
            else
            {
                btnLock.IsEnabled = true;
            }
        }

        private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            await DoUnlockWithPasswordAsync();
        }

        private async Task DoUnlockWithPasswordAsync()
        {
            var dlg = new PasswordWindow();
            if (dlg.ShowDialog() != true)
            {
                Log("[accesso] operazione annullata dal docente.");
                return;
            }

            btnUnlock.IsEnabled = false;
            Log("[accesso] password corretta. ripristino navigazione libera in corso...");
            bool ok = await FirewallService.RemoveLockdownAsync(msg => Log(msg));
            if (ok)
            {
                UpdateUiLockState(false);
            }
            else
            {
                btnUnlock.IsEnabled = true;
            }
        }

        private void ChkSiteToggle_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings();
            UpdateStatsDisplay();

            if (_isLocked)
            {
                Log("[aggiornamento] sincronizzazione regole firewall in tempo reale...");
                _ = Task.Run(async () =>
                {
                    await FirewallService.UpdateActiveRulesAsync(Sites, msg => Log(msg));
                });
            }
        }

        private async void BtnAddSite_Click(object sender, RoutedEventArgs e)
        {
            await AddNewSiteAsync();
        }

        private async void TxtNewDomain_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AddNewSiteAsync();
            }
        }

        private async Task AddNewSiteAsync()
        {
            string domain = HealthCheckService.SanitizeDomain(txtNewDomain.Text);
            if (string.IsNullOrWhiteSpace(domain) || domain == "es. cppreference.com")
            {
                return;
            }

            if (Sites.Any(s => s.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"il dominio '{domain}' è già presente nella lista!", "avviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string desc = txtNewDesc.Text.Trim();
            if (desc == "descrizione opzionale...") desc = "";

            var item = new SiteItem
            {
                Domain = domain,
                Description = string.IsNullOrWhiteSpace(desc) ? "sito personalizzato" : desc,
                IsEnabled = true
            };

            Sites.Add(item);
            txtNewDomain.Text = "";
            txtNewDesc.Text = "";
            Log($"[elenco siti] aggiunto nuovo sito: '{domain}'");

            SaveCurrentSettings();
            UpdateStatsDisplay();

            // Risoluzione e applicazione immediata
            _ = Task.Run(async () =>
            {
                // 1. Risolvi subito tutti gli indirizzi IP (apex, www, CDN e alias)
                await HealthCheckService.ResolveSiteIpsAsync(item);

                // 2. Se il blocco è attivo, applica IMMEDIATAMENTE le regole firewall
                if (_isLocked)
                {
                    await FirewallService.UpdateActiveRulesAsync(Sites, msg => Log(msg));
                }

                // 3. Ora che la regola firewall è attiva, esegui il controllo di raggiungibilità
                await _probeService.CheckSiteHealthAsync(item);
                Dispatcher.Invoke(() => UpdateStatsDisplay());
            });
        }

        private void BtnDeleteSite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SiteItem item)
            {
                Sites.Remove(item);
                Log($"[elenco siti] rimosso sito: '{item.Domain}'");
                SaveCurrentSettings();
                UpdateStatsDisplay();

                if (_isLocked)
                {
                    _ = Task.Run(async () =>
                    {
                        await FirewallService.UpdateActiveRulesAsync(Sites, msg => Log(msg));
                    });
                }
            }
        }

        private async void BtnRefreshProbes_Click(object sender, RoutedEventArgs e)
        {
            Log("[verifica] controllo raggiungibilità dei siti in corso...");
            foreach (var s in Sites)
            {
                await _probeService.CheckSiteHealthAsync(s);
            }
            UpdateStatsDisplay();
            if (_isLocked)
            {
                await FirewallService.UpdateActiveRulesAsync(Sites, msg => Log(msg));
            }
            Log("[verifica] controllo raggiungibilità completato.");
        }

        private void ChkStartup_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool enable = chkStartup.IsChecked == true;
            StartupManager.SetStartWithWindows(enable);
            SaveCurrentSettings();
            Log($"[Impostazioni] Avvio automatico con Windows: {(enable ? "abilitato" : "disabilitato")}");
        }

        private void SaveCurrentSettings()
        {
            ConfigManager.SaveSettings(new AppSettings
            {
                StartWithWindows = chkStartup.IsChecked == true,
                Sites = Sites.ToList()
            });
        }

        private void UpdateStatsDisplay()
        {
            int total = Sites.Count;
            int excluded = Sites.Count(s => !s.IsEnabled);
            int online = Sites.Count(s => s.Status == SiteStatus.Online);
            lblSiteStats.Text = $"{total} Siti ({online} online, {excluded} esclusi)";
        }

        private void BtnMinimizeTray_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            _notifyIcon.ShowBalloonTip(2000, "OISblocca v2.0", "L'applicazione è attiva nell'area di notifica a fianco a Wi-Fi e batteria. Doppio clic per aprirla.", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void BtnTestOis_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://gara.squadre.olinfo.it/check") { UseShellExecute = true });
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow().ShowDialog();
        }

        private void BtnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            new ChangePasswordWindow().ShowDialog();
        }

        private void LnkQuarto_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("mailto:profquarto@gmail.com") { UseShellExecute = true });
        }

        private void LnkBevilacqua_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("mailto:gianlucagennaro.bevilacqua@scuola.istruzione.it") { UseShellExecute = true });
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            txtLogs.Clear();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Minimizza nella System Tray anziché chiudere
            e.Cancel = true;
            this.Hide();
            try
            {
                if (_notifyIcon != null)
                {
                    if (_notifyIcon.Icon == null)
                    {
                        _notifyIcon.Icon = GetAppIcon();
                    }
                    if (!_notifyIcon.Visible)
                    {
                        _notifyIcon.Visible = true;
                    }
                    StartupManager.PromoteTrayIcon();
                    _notifyIcon.ShowBalloonTip(1500, "OISblocca v2.0", "L'applicazione è sempre attiva in background.", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch
            {
            }
        }

        public void Log(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{time}] {message}\r\n";

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            txtLogs.AppendText(line);
            txtLogs.ScrollToEnd();
        }
    }
}
