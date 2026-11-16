using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace OISblocca_Setup
{
    public partial class MainWindow : Window
    {
        private readonly bool _isUninstallMode;
        private int _currentStep = 0;

        public MainWindow(bool isUninstallMode = false)
        {
            InitializeComponent();
            SetAppIconSafely();
            _isUninstallMode = isUninstallMode;

            if (_isUninstallMode)
            {
                SetupUninstallMode();
            }
            else
            {
                SetupInstallMode();
            }
        }

        private void SetAppIconSafely()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.RelativeOrAbsolute);
                this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(uri);
            }
            catch
            {
                try
                {
                    var asm = typeof(MainWindow).Assembly;
                    string? res = System.Linq.Enumerable.FirstOrDefault(asm.GetManifestResourceNames(),
                        r => r.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(res))
                    {
                        using var stream = asm.GetManifestResourceStream(res);
                        if (stream != null)
                        {
                            this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream);
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private void SetupInstallMode()
        {
            this.Title = "Installazione di OISblocca v2.0";
            lblBannerTitle.Text = "installazione guidata di OISblocca";
            lblBannerSub.Text = "protezione e gestione postazione gara OIS";

            txtInstallDir.Text = InstallerService.DefaultInstallDirectory;
            UpdateFreeSpaceDisplay();

            UpdateWizardPages();
        }

        private void SetupUninstallMode()
        {
            this.Title = "Disinstallazione di OISblocca v2.0";
            lblBannerTitle.Text = "disinstallazione guidata di OISblocca";
            lblBannerSub.Text = "rimozione componenti dal computer";

            HideAllPages();
            pnlUninstallConfirm.Visibility = Visibility.Visible;

            btnBack.Visibility = Visibility.Collapsed;
            btnNext.Content = "disinstalla";
            btnCancel.Content = "annulla";
        }

        private void UpdateWizardPages()
        {
            HideAllPages();

            btnBack.Visibility = _currentStep > 0 && _currentStep < 3 ? Visibility.Visible : Visibility.Collapsed;
            btnCancel.Visibility = _currentStep < 4 ? Visibility.Visible : Visibility.Collapsed;
            btnNext.Visibility = Visibility.Visible;
            btnNext.IsEnabled = true;

            switch (_currentStep)
            {
                case 0:
                    pnlWelcome.Visibility = Visibility.Visible;
                    btnNext.Content = "avanti >";
                    break;

                case 1:
                    pnlDirectory.Visibility = Visibility.Visible;
                    btnNext.Content = "avanti >";
                    UpdateFreeSpaceDisplay();
                    break;

                case 2:
                    pnlOptions.Visibility = Visibility.Visible;
                    btnNext.Content = "installa";
                    break;

                case 3:
                    pnlProgress.Visibility = Visibility.Visible;
                    btnBack.Visibility = Visibility.Collapsed;
                    btnCancel.Visibility = Visibility.Collapsed;
                    btnNext.Visibility = Visibility.Collapsed;
                    break;

                case 4:
                    pnlFinished.Visibility = Visibility.Visible;
                    btnBack.Visibility = Visibility.Collapsed;
                    btnCancel.Visibility = Visibility.Collapsed;
                    btnNext.Content = "fine";
                    btnNext.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void HideAllPages()
        {
            pnlWelcome.Visibility = Visibility.Collapsed;
            pnlDirectory.Visibility = Visibility.Collapsed;
            pnlOptions.Visibility = Visibility.Collapsed;
            pnlProgress.Visibility = Visibility.Collapsed;
            pnlFinished.Visibility = Visibility.Collapsed;
            pnlUninstallConfirm.Visibility = Visibility.Collapsed;
            pnlUninstallFinished.Visibility = Visibility.Collapsed;
        }

        private void UpdateFreeSpaceDisplay()
        {
            string dir = string.IsNullOrWhiteSpace(txtInstallDir.Text) ? "C:\\" : txtInstallDir.Text;
            lblFreeSpace.Text = $"spazio disponibile sul disco: {InstallerService.GetDiskFreeSpaceInfo(dir)}";
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Seleziona la cartella di installazione per OISblocca",
                SelectedPath = txtInstallDir.Text,
                UseDescriptionForTitle = true
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string chosen = dlg.SelectedPath;
                if (!chosen.EndsWith("OISblocca", StringComparison.OrdinalIgnoreCase))
                {
                    chosen = Path.Combine(chosen, "OISblocca");
                }
                txtInstallDir.Text = chosen;
                UpdateFreeSpaceDisplay();
            }
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_isUninstallMode)
            {
                await HandleUninstallNextAsync();
                return;
            }

            if (_currentStep == 2)
            {
                // Avvia installazione
                _currentStep = 3;
                UpdateWizardPages();
                await RunInstallationAsync();
            }
            else if (_currentStep == 4)
            {
                // Fine installazione
                if (chkLaunchNow.IsChecked == true)
                {
                    try
                    {
                        string targetExe = Path.Combine(txtInstallDir.Text, "OISblocca.exe");
                        if (File.Exists(targetExe))
                        {
                            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                        }
                    }
                    catch
                    {
                    }
                }
                Application.Current.Shutdown();
            }
            else
            {
                _currentStep++;
                UpdateWizardPages();
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0 && _currentStep < 3)
            {
                _currentStep--;
                UpdateWizardPages();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 3) return; // Non annullabile durante l'estrazione

            var res = MessageBox.Show(this,
                "Sei sicuro di voler uscire dalla procedura guidata?",
                "Annulla installazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private async Task RunInstallationAsync()
        {
            string installDir = txtInstallDir.Text.Trim();
            bool startMenu = chkStartMenu.IsChecked == true;
            bool desktop = chkDesktop.IsChecked == true;
            bool autoStart = chkAutoStartup.IsChecked == true;

            try
            {
                await InstallerService.InstallAsync(
                    installDir,
                    startMenu,
                    desktop,
                    autoStart,
                    (pct, status) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            pbProgress.Value = pct;
                            lblProgressPercent.Text = $"{pct}%";
                            lblProgressStatus.Text = status;
                        });
                    });

                await Task.Delay(500);
                _currentStep = 4;
                UpdateWizardPages();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Si è verificato un errore durante l'installazione:\n\n{ex.Message}",
                    "Errore installazione",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                _currentStep = 2;
                UpdateWizardPages();
            }
        }

        private async Task HandleUninstallNextAsync()
        {
            if (_currentStep == 0)
            {
                // Avvia disinstallazione
                _currentStep = 1;
                HideAllPages();
                pnlProgress.Visibility = Visibility.Visible;
                lblProgressTitle.Text = "disinstallazione in corso...";
                btnNext.Visibility = Visibility.Collapsed;
                btnCancel.Visibility = Visibility.Collapsed;

                try
                {
                    await UninstallerService.UninstallAsync((pct, status) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            pbProgress.Value = pct;
                            lblProgressPercent.Text = $"{pct}%";
                            lblProgressStatus.Text = status;
                        });
                    });

                    await Task.Delay(500);
                    _currentStep = 2;
                    HideAllPages();
                    pnlUninstallFinished.Visibility = Visibility.Visible;
                    btnNext.Content = "chiudi";
                    btnNext.Visibility = Visibility.Visible;
                    btnNext.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"Si è verificato un errore durante la disinstallazione:\n\n{ex.Message}",
                        "Errore disinstallazione",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    Application.Current.Shutdown();
                }
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
    }
}
