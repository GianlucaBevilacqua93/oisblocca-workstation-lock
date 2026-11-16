using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using OISblocca.Core;

namespace OISblocca.UI
{
    public partial class PasswordWindow : Window
    {
        public PasswordWindow()
        {
            InitializeComponent();
            DwmHelper.ApplyWindows11Style(this);
            this.Loaded += (s, e) => txtPassword.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndProceed();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void TxtPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ValidateAndProceed();
            }
        }

        private void ValidateAndProceed()
        {
            if (SecurityService.VerifyPassword(txtPassword.Password))
            {
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                lblError.Visibility = Visibility.Visible;
                txtPassword.Clear();
                txtPassword.Focus();

                var shake = (Storyboard)this.Resources["ShakeAnimation"];
                shake.Begin();
            }
        }
    }
}
