using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AimAssistPro.Models;
using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadLicenseInfo();
            RefreshDriverStatus();
            
            if (App.ProfileManager?.CurrentSettings != null)
            {
                StreamModeToggle.IsChecked = App.ProfileManager.CurrentSettings.StreamModeEnabled;
                ApplyStreamMode(App.ProfileManager.CurrentSettings.StreamModeEnabled);
            }
        }
        
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        
        private void ApplyStreamMode(bool enable)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                // WDA_EXCLUDEFROMCAPTURE = 0x00000011
                uint affinity = enable ? 0x00000011u : 0u;
                SetWindowDisplayAffinity(hwnd, affinity);
            }
        }

        private void StreamModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (StreamModeToggle.IsChecked.HasValue)
            {
                bool enable = StreamModeToggle.IsChecked.Value;
                if (App.ProfileManager?.CurrentSettings != null)
                {
                    App.ProfileManager.CurrentSettings.StreamModeEnabled = enable;
                    App.ProfileManager.SaveSettings();
                }
                ApplyStreamMode(enable);
            }
        }

        private void LoadLicenseInfo()
        {
            var lm = App.LicenseManager;
            if (lm == null) return;

            var license = lm.CurrentLicense;
            if (license != null && license.Status == LicenseStatus.Active)
            {
                PlanText.Text = license.PlanName;
                ExpiresText.Text = $"Expira em {license.ExpiresAt:dd/MM/yyyy}";
                KeyText.Text = license.Key;
            }
            else
            {
                PlanText.Text = "Nenhum Plano Ativo";
                ExpiresText.Text = "---";
                KeyText.Text = LicenseManager.GetHardwareId(); // Show HWID if not activated
            }
        }

        private void BtnRemoveClipKey_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder: handle clip key removal
        }

        private void BtnRenovar_Click(object sender, RoutedEventArgs e)
        {
            // Pega o token JWT caso precise (o App não guarda, mas podemos pegar?)
            // O ideal seria que a MainWindow ou App guardassem o Token. 
            // Para resolver rapidamente que precisa do token, vamos adicioná-lo no App.xaml.cs
            
            var activateWin = new ActivateKeyWindow(App.CurrentAuthToken);
            bool? result = activateWin.ShowDialog();
            
            if (result == true && activateWin.ActivationSuccess)
            {
                var plan = activateWin.Plan ?? "Standard";
                var expiresAt = activateWin.ExpiresAt ?? DateTime.Now.AddDays(30);

                App.LicenseManager?.SetOnlineLicense(
                    App.CurrentUsername, plan, expiresAt
                );

                LoadLicenseInfo();
                MessageBox.Show("Sua licença foi renovada/atualizada com sucesso!", "Licença Renovada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        // ─── Driver Management ────────────────────────────────────────────────
        private void RefreshDriverStatus()
        {
            var status = DriverStatusService.GetStatus();

            // ViGEm
            bool ve = status.ViGEmInstalled;
            ViGEmDot.Fill = ve ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            ViGEmCard.BorderBrush = ve ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            ViGEmStatusText.Text = ve ? "Status: Instalado e ativo ✓" : "Status: Não encontrado";
            BtnInstallViGEmText.Text = ve ? "Reinstalar" : "Instalar";
            BtnUninstallViGEm.Visibility = ve ? Visibility.Visible : Visibility.Collapsed;

            // Interception
            bool ic = status.InterceptionInstalled;
            InterceptionDot.Fill = ic ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            InterceptionCard.BorderBrush = ic ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            InterceptionStatusText.Text = ic ? "Status: Instalado e ativo ✓" : "Status: Não encontrado";
            BtnInstallInterceptionText.Text = ic ? "Reinstalar" : "Instalar";
            BtnUninstallInterception.Visibility = ic ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnInstallViGEm_Click(object sender, RoutedEventArgs e)
        {
            BtnInstallViGEm.IsEnabled = false;
            BtnInstallViGEmText.Text  = "Instalando...";

            var result = await DriverInstaller.InstallViGEmAsync(
                new Progress<string>(msg => Dispatcher.Invoke(() => BtnInstallViGEmText.Text = msg)),
                CancellationToken.None);

            BtnInstallViGEm.IsEnabled = true;

            if (result.Success)
            {
                RefreshDriverStatus();

                if (result.NeedsReboot)
                {
                    var reboot = MessageBox.Show(
                        result.Message + "\n\nDeseja reiniciar o computador agora?",
                        "ViGEmBus — Reinicialização Necessária",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (reboot == MessageBoxResult.Yes)
                        OfferReboot();
                }
                else
                {
                    MessageBox.Show(result.Message, "ViGEmBus Instalado",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                RefreshDriverStatus();
                MessageBox.Show(result.Message, "Erro ao instalar ViGEmBus",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnUninstallViGEm_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Isso desinstalará o driver ViGEmBus do sistema.\n" +
                "O controle virtual deixará de funcionar após isso.\n\n" +
                "Deseja continuar?",
                "Desinstalar ViGEmBus",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            BtnUninstallViGEm.IsEnabled = false;

            var result = await DriverInstaller.UninstallViGEmAsync(null, CancellationToken.None);

            MessageBox.Show(result.Message,
                result.Success ? "ViGEmBus Desinstalado" : "Erro",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

            BtnUninstallViGEm.IsEnabled = true;
            RefreshDriverStatus();
        }

        private async void BtnInstallInterception_Click(object sender, RoutedEventArgs e)
        {
            BtnInstallInterception.IsEnabled = false;
            BtnInstallInterceptionText.Text  = "Instalando...";

            var result = await DriverInstaller.InstallInterceptionAsync(
                new Progress<string>(msg => Dispatcher.Invoke(() => BtnInstallInterceptionText.Text = msg)),
                CancellationToken.None);

            BtnInstallInterception.IsEnabled = true;

            if (result.Success)
            {
                RefreshDriverStatus();

                if (result.NeedsReboot)
                {
                    var reboot = MessageBox.Show(
                        result.Message + "\n\nDeseja reiniciar o computador agora?",
                        "Interception — Reinicialização Necessária",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (reboot == MessageBoxResult.Yes)
                        OfferReboot();
                }
                else
                {
                    MessageBox.Show(result.Message, "Interception Instalado",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                RefreshDriverStatus();
                MessageBox.Show(result.Message, "Erro ao instalar Interception",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void OfferReboot()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "shutdown.exe",
                Arguments       = "/r /t 5 /c \"AimAssistPro: reiniciando para ativar drivers\"",
                UseShellExecute = false,
                CreateNoWindow  = true
            });
            Application.Current.Shutdown();
        }

        private async void BtnUninstallInterception_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Isso desinstalará o driver Interception do sistema.\n" +
                "O mapeamento de teclado/mouse via kernel deixará de funcionar.\n\n" +
                "Deseja continuar? (Requer reinicialização)",
                "Desinstalar Interception",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            BtnUninstallInterception.IsEnabled = false;

            var result = await DriverInstaller.UninstallInterceptionAsync(null, CancellationToken.None);

            MessageBox.Show(result.Message,
                result.Success ? "Interception Desinstalado" : "Erro",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

            BtnUninstallInterception.IsEnabled = true;
            RefreshDriverStatus();
        }
    }
}
