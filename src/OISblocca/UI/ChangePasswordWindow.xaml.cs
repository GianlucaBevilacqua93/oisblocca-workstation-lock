using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OISblocca.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace OISblocca.UI
{
    public partial class ChangePasswordWindow : Window
    {
        public ChangePasswordWindow()
        {
            InitializeComponent();
            DwmHelper.ApplyWindows11Style(this);

            Loaded += (s, e) => txtCurrentPassword.Focus();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DoChangePassword();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DoChangePassword();
        }

        private void DoChangePassword()
        {
            string currentPwd = txtCurrentPassword.Password;
            string newPwd = txtNewPassword.Password;
            string confirmPwd = txtConfirmPassword.Password;

            if (string.IsNullOrEmpty(currentPwd))
            {
                ShowError("Inserisci la password attuale.");
                txtCurrentPassword.Focus();
                return;
            }

            if (string.IsNullOrEmpty(newPwd))
            {
                ShowError("Inserisci la nuova password.");
                txtNewPassword.Focus();
                return;
            }

            if (newPwd.Length < 4)
            {
                ShowError("La nuova password deve contenere almeno 4 caratteri.");
                txtNewPassword.Focus();
                return;
            }

            if (newPwd != confirmPwd)
            {
                ShowError("La conferma della nuova password non coincide.");
                txtConfirmPassword.Focus();
                return;
            }

            if (!SecurityService.ChangePassword(currentPwd, newPwd, out string error))
            {
                ShowError(error);
                return;
            }

            MessageBox.Show(this, "Password docente aggiornata con successo!", "Password modificata", MessageBoxButton.OK, MessageBoxImage.Information);
            this.DialogResult = true;
            this.Close();
        }

        private void ShowError(string message)
        {
            lblMessage.Text = message;
            lblMessage.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82));
            lblMessage.Visibility = Visibility.Visible;

            if (FindResource("ShakeAnimation") is Storyboard sb)
            {
                sb.Begin();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
