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
            UserAccepted = false;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_mandatory)
            {
                // Obrigatória: X fecha o app (mesmo comportamento que OnClosing)
                UserAccepted = false;
                Application.Current.Shutdown();
            }
            else
            {
                // Opcional: X apenas dispensa o diálogo
                UserAccepted = false;
                Close();
            }
        }

        // Fechar pela barra de título (X do Windows) também trata obrigatoriedade
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            // Não chamamos Shutdown() aqui — quem decide é o UpdateResult em App.OnStartup
            // UserAccepted já é false por padrão, o caller vai verificar
        }

        // Permite arrastar
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }
    }
}
