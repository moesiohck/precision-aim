using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AimAssistPro.Views
{
    /// <summary>
    /// Diálogo personalizado que substitui todos os MessageBox do app.
    /// </summary>
    public partial class CustomDialog : Window
    {
        // Ícones SVG path data
        private const string IconShield    = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1Z";
        private const string IconWarning   = "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16";
        private const string IconError     = "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z";
        private const string IconSuccess   = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16.5L18,9.5L16.59,8.09L11,13.67L7.91,10.59L6.5,12L11,16.5Z";
        private const string IconInfo      = "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";
        private const string IconInstall   = "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z";
        private const string IconReboot    = "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z";

        // Resultado do botão clicado
        public bool? Result { get; private set; } = null;

        // ─── Tipos de diálogo ──────────────────────────────────────────────────
        public enum DialogType { Info, Warning, Error, Success, Install, Reboot }

        // ─── Configuração ──────────────────────────────────────────────────────
        private readonly string _heading;
        private readonly string _body;
        private readonly DialogType _type;
        private readonly string[] _buttonLabels;
        private readonly bool[] _isPrimary;

        private CustomDialog(
            string heading,
            string body,
            DialogType type,
            string[] buttonLabels,
            bool[] isPrimary,
            Window? owner = null)
        {
            _heading      = heading;
            _body         = body;
            _type         = type;
            _buttonLabels = buttonLabels;
            _isPrimary    = isPrimary;

            InitializeComponent();

            if (owner != null)
                Owner = owner;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Textos
            TxtHeading.Text = _heading;
            TxtBody.Text    = _body;
            TxtBody.Visibility = string.IsNullOrEmpty(_body) ? Visibility.Collapsed : Visibility.Visible;

            // Ícone e cores conforme o tipo
            ApplyDialogType(_type);

            // Botões
            BuildButtons();
        }

        private void ApplyDialogType(DialogType type)
        {
            string iconData;
            Color  iconColor;
            Color  accentA;
            Color  accentB;
            Color  badgeBg;

            switch (type)
            {
                case DialogType.Success:
                    iconData  = IconSuccess;
                    iconColor = Color.FromRgb(0x22, 0xC5, 0x5E);
                    accentA   = Color.FromRgb(0x16, 0xA3, 0x4A);
                    accentB   = Color.FromRgb(0x14, 0x53, 0x2D);
                    badgeBg   = Color.FromRgb(0x0D, 0x2B, 0x1A);
                    TxtTitle.Text = "Operação concluída";
                    break;

                case DialogType.Warning:
                    iconData  = IconWarning;
                    iconColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
                    accentA   = Color.FromRgb(0xD9, 0x77, 0x06);
                    accentB   = Color.FromRgb(0x78, 0x35, 0x00);
                    badgeBg   = Color.FromRgb(0x2A, 0x1A, 0x00);
                    TxtTitle.Text = "Atenção necessária";
                    break;

                case DialogType.Error:
                    iconData  = IconError;
                    iconColor = Color.FromRgb(0xEF, 0x44, 0x44);
                    accentA   = Color.FromRgb(0xDC, 0x26, 0x26);
                    accentB   = Color.FromRgb(0x7F, 0x1D, 0x1D);
                    badgeBg   = Color.FromRgb(0x2B, 0x0D, 0x0D);
                    TxtTitle.Text = "Erro detectado";
                    break;

                case DialogType.Install:
                    iconData  = IconInstall;
                    iconColor = Color.FromRgb(0xE8, 0xE8, 0xE8);
                    accentA   = Color.FromRgb(0x70, 0x70, 0x70);
                    accentB   = Color.FromRgb(0x30, 0x30, 0x30);
                    badgeBg   = Color.FromRgb(0x25, 0x25, 0x25);
                    TxtTitle.Text = "Instalação de Drivers";
                    break;

                case DialogType.Reboot:
                    iconData  = IconReboot;
                    iconColor = Color.FromRgb(0xC0, 0xC0, 0xC0);
                    accentA   = Color.FromRgb(0x60, 0x60, 0x60);
                    accentB   = Color.FromRgb(0x28, 0x28, 0x28);
                    badgeBg   = Color.FromRgb(0x20, 0x20, 0x20);
                    TxtTitle.Text = "Reinicialização necessária";
                    break;

                default: // Info
                    iconData  = IconShield;
                    iconColor = Color.FromRgb(0xC8, 0xC8, 0xC8);
                    accentA   = Color.FromRgb(0x60, 0x60, 0x60);
                    accentB   = Color.FromRgb(0x28, 0x28, 0x28);
                    badgeBg   = Color.FromRgb(0x20, 0x20, 0x20);
                    TxtTitle.Text = "AimAssist PRO";
                    break;
            }

            StatusIconPath.Data        = Geometry.Parse(iconData);
            StatusIconPath.Fill        = new SolidColorBrush(iconColor);
            IconPath.Data              = Geometry.Parse(IconShield);
            StatusIconBadge.Background = new SolidColorBrush(badgeBg);
        }

        private void BuildButtons()
        {
            for (int i = 0; i < _buttonLabels.Length; i++)
            {
                var index = i; // capture
                var btn = new Button
                {
                    Content = _buttonLabels[i],
                    Style   = _isPrimary[i]
                        ? (Style)FindResource("DlgPrimaryBtn")
                        : (Style)FindResource("DlgSecondaryBtn"),
                    MinWidth = 100,
                    Margin   = new Thickness(i == 0 ? 0 : 10, 0, 0, 0) // espaço entre botões
                };
                btn.Click += (_, _) =>
                {
                    Result = (index == 0); // o primeiro botão sempre é o "sim/confirmar"
                    DialogResult = Result;
                    Close();
                };
                ButtonPanel.Children.Add(btn);
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  FACTORY METHODS — use esses para substituir MessageBox.Show()
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Diálogo simples de informação com um botão "OK".
        /// </summary>
        public static void ShowInfo(string heading, string body, Window? owner = null)
        {
            var dlg = new CustomDialog(heading, body, DialogType.Info,
                new[] { "Entendido" }, new[] { true }, owner);
            dlg.ShowDialog();
        }

        /// <summary>
        /// Diálogo de sucesso com um botão "OK".
        /// </summary>
        public static void ShowSuccess(string heading, string body, Window? owner = null)
        {
            var dlg = new CustomDialog(heading, body, DialogType.Success,
                new[] { "Perfeito!" }, new[] { true }, owner);
            dlg.ShowDialog();
        }

        /// <summary>
        /// Diálogo de erro com um botão "Fechar".
        /// </summary>
        public static void ShowError(string heading, string body, Window? owner = null)
        {
            var dlg = new CustomDialog(heading, body, DialogType.Error,
                new[] { "Fechar" }, new[] { true }, owner);
            dlg.ShowDialog();
        }

        /// <summary>
        /// Diálogo de confirmação com Sim/Não. Retorna true se confirmado.
        /// </summary>
        public static bool? ShowConfirm(string heading, string body,
            string confirmLabel = "Sim, continuar",
            string cancelLabel  = "Cancelar",
            DialogType type     = DialogType.Warning,
            Window? owner       = null)
        {
            var dlg = new CustomDialog(heading, body, type,
                new[] { confirmLabel, cancelLabel },
                new[] { true, false }, owner);
            dlg.ShowDialog();
            return dlg.Result;
        }

        /// <summary>
        /// Diálogo de instalação de driver.
        /// Retorna true se o usuário clicou em "Instalar agora".
        /// </summary>
        public static bool? ShowInstallPrompt(string missingDrivers, Window? owner = null)
        {
            string body =
                $"Os seguintes componentes não foram detectados no sistema:\n\n" +
                $"{missingDrivers}\n\n" +
                "Deseja instalar automaticamente agora?";

            var dlg = new CustomDialog(
                "Componentes Ausentes",
                body,
                DialogType.Install,
                new[] { "  Instalar agora  ", "  Pular por ora  " },
                new[] { true, false },
                owner);
            dlg.ShowDialog();
            return dlg.Result;
        }

        /// <summary>
        /// Diálogo de solicitação de reinicialização.
        /// Retorna true se o usuário clicou em "Reiniciar agora".
        /// </summary>
        public static bool? ShowRebootPrompt(Window? owner = null)
        {
            var dlg = new CustomDialog(
                "Reinicialização Necessária",
                "A instalação dos drivers foi concluída com sucesso.\n\n" +
                "Para que o sistema reconheça os novos módulos, é necessário\n" +
                "reiniciar o computador antes de continuar.",
                DialogType.Reboot,
                new[] { "  Reiniciar agora  ", "  Agora não  " },
                new[] { true, false },
                owner);
            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}
