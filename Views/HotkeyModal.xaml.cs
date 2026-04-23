using System.Windows;
using System.Windows.Input;

namespace AimAssistPro.Views
{
    public partial class HotkeyModal : Window
    {
        public string SelectedKey { get; private set; }

        public HotkeyModal(string currentKey)
        {
            InitializeComponent();
            SelectedKey = currentKey;
            KeyText.Text = currentKey;
            
            // Focus manual para garantir a captura de teclado
            Loaded += (s, e) => this.Focus();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ignore modifiers only
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
                return;

            e.Handled = true;
            SelectedKey = e.Key.ToString();
            KeyText.Text = SelectedKey;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);
            e.Handled = true;
            SelectedKey = e.Delta > 0 ? "MouseWheelUp" : "MouseWheelDown";
            KeyText.Text = SelectedKey;
            DialogResult = true; // Auto save on scroll
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            // Ignorar clique esquerdo se for na barra de titulo/fechar
            if (e.ChangedButton == MouseButton.Left) return;

            e.Handled = true;
            SelectedKey = "Mouse" + e.ChangedButton.ToString();
            if (e.ChangedButton == MouseButton.XButton1) SelectedKey = "XButton1";
            if (e.ChangedButton == MouseButton.XButton2) SelectedKey = "XButton2";

            KeyText.Text = SelectedKey;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
