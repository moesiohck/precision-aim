using System.Windows;

namespace AimAssistPro.Views
{
    public partial class UpdateDialog : Window
    {
        public bool UserAccepted { get; private set; } = false;

        public UpdateDialog(
            string currentVersion,
            string newVersion,
            string changelog,
            bool mandatory)
        {
            InitializeComponent();

            CurrentVersionText.Text = $"v{currentVersion}";
            NewVersionText.Text     = $"v{newVersion}";
            TitleText.Text          = $"Versão {newVersion} disponível";

            if (!string.IsNullOrWhiteSpace(changelog))
            {
                ChangelogText.Text       = changelog;
                ChangelogBorder.Visibility = Visibility.Visible;
            }

            if (mandatory)
            {
                MandatoryBanner.Visibility = Visibility.Visible;
                BtnSkip.IsEnabled          = false;
                BtnClose.Visibility        = Visibility.Collapsed;
                BtnSkip.Content            = "Obrigatória";
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
            UserAccepted = false;
            Close();
        }

        // Permite arrastar a janela sem barra de título
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }
    }
}
