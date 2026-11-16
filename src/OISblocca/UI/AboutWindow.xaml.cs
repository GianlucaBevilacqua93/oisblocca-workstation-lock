using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using OISblocca.Core;

namespace OISblocca.UI
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            DwmHelper.ApplyWindows11Style(this);
        }

        private void BtnEmailQuarto_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("mailto:profquarto@gmail.com") { UseShellExecute = true });
        }

        private void BtnEmailBevilacqua_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("mailto:gianlucagennaro.bevilacqua@scuola.istruzione.it") { UseShellExecute = true });
        }

        private void LnkWeb_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://oisquadre.it") { UseShellExecute = true });
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
