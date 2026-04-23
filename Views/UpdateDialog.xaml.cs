using System.Windows;

namespace AimAssistPro.Views
{
    public partial class UpdateDialog : Window
    {
        public bool UserAccepted { get; private set; } = false;
        private readonly bool _mandatory;

        public UpdateDialog(
            string currentVersion,
            string newVersion,
            string changelog,
            bool mandatory)
        {
            InitializeComponent();

            _mandatory = mandatory;

            CurrentVersionText.Text = $"v{currentVersion}";
            NewVersionText.Text     = $"v{newVersion}";

            if (!string.IsNullOrWhiteSpace(changelog))
            {
                ChangelogText.Text         = changelog.Replace("\\n", "\n");
                ChangelogBorder.Visibility = Visibility.Visible;
            }

            if (mandatory)
            {
                MandatoryBadge.Visibility = Visibility.Visible;
                MandatoryWarn.Visibility  = Visibility.Visible;
                BtnSkip.IsEnabled         = false;
                BtnSkip.Content           = "Obrigatória";
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            UserAccepted = true;
            Close();
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            // Se obrigatória: não chega aqui (botão desabilitado)
            // Se opcional: fecha o dialog sem aceitar
            UserAccepted = false;
            Close();
        }

        // Fechar pela barra de título (X) também fecha o app se for obrigatória
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_mandatory && !UserAccepted)
            {
                // Encerra o app completamente
                Application.Current.Shutdown();
            }
        }

        // Permite arrastar
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }
    }
}
