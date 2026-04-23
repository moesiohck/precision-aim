using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class DriverSetupWindow : Window
    {
        private readonly DriverStatus _status;

        public DriverSetupWindow()
        {
            InitializeComponent();
            _status = DriverStatusService.GetStatus();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateDriverUI();
        }

        private void UpdateDriverUI()
        {
            // ViGEm status
            if (_status.ViGEmInstalled)
            {
                ViGEmDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                ViGEmBadgeText.Text = "INSTALADO";
                ViGEmBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                ViGEmCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                BtnInstallViGEm.Content = "Reinstalar";
                BtnInstallViGEm.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x1F));
            }
            else
            {
                ViGEmCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            }

            // Interception status
            if (_status.InterceptionInstalled)
            {
                InterceptionDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                InterceptionBadgeText.Text = "INSTALADO";
                InterceptionBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                InterceptionCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                BtnInstallInterception.Content = "Ver Instruções";
                BtnInstallInterception.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x2A));
            }
            else
            {
                InterceptionCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            }

            // If all OK, close automatically after showing briefly
            if (_status.AllDriversOk)
            {
                DialogResult = true;
            }
        }

        private void BtnInstallViGEm_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/nefarius/ViGEmBus/releases/latest",
                UseShellExecute = true
            });
        }

        private void BtnInstallInterception_Click(object sender, RoutedEventArgs e)
        {
            // Show instructions dialog
            MessageBox.Show(
                "Para instalar o Interception driver:\n\n" +
                "1. Baixe em: github.com/oblitum/Interception/releases\n" +
                "2. Extraia o arquivo ZIP\n" +
                "3. Execute o CMD como Administrador\n" +
                "4. Rode: install-interception.exe /install\n" +
                "5. Reinicie o computador\n" +
                "6. Copie interception.dll para a pasta do AIM ASSIST PRO\n\n" +
                "Abrindo a página de download...",
                "Instalar Interception",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/oblitum/Interception/releases",
                UseShellExecute = true
            });
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
