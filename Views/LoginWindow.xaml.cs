using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class LoginWindow : Window
    {
        public bool      LoginSuccess    { get; private set; } = false;
        public bool      WantsRegister   { get; private set; } = false;
        public string?   AuthToken       { get; private set; }
        public string?   Username        { get; private set; }
        public string?   Email           { get; private set; }
        public string?   Plan            { get; private set; }
        public DateTime? ExpiresAt       { get; private set; }
        public bool      NeedsActivation { get; private set; } = false;

        // URL de produção via SecureConfig (XOR-obfuscated, não aparece em strings.exe)
        private static string API_BASE => SecureConfig.ApiBase + "/api";
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly SolidColorBrush _borderDefault = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        private static readonly SolidColorBrush _borderFocused  = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));


        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UsernameInput.Focus();
        }

        // ─── Drag ────────────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // ─── Focus highlight ─────────────────────────────────────────────────
        private void EmailInput_GotFocus(object sender, RoutedEventArgs e)
            => EmailBorder.BorderBrush = _borderFocused;

        private void EmailInput_LostFocus(object sender, RoutedEventArgs e)
            => EmailBorder.BorderBrush = _borderDefault;

        private void PassInput_GotFocus(object sender, RoutedEventArgs e)
            => PassBorder.BorderBrush = _borderFocused;

        private void PassInput_LostFocus(object sender, RoutedEventArgs e)
            => PassBorder.BorderBrush = _borderDefault;

        // ─── Placeholder ─────────────────────────────────────────────────────
        private void UsernameInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            EmailPlaceholder.Visibility = string.IsNullOrEmpty(UsernameInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PassPlaceholder.Visibility = PasswordInput.Password.Length > 0
                ? Visibility.Collapsed : Visibility.Visible;
        }

        // ─── Navigation ──────────────────────────────────────────────────────
        private void OpenPortal_Click(object sender, MouseButtonEventArgs e)
        {
            WantsRegister = true;
            DialogResult  = false;
            Close();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnLogin_Click(sender, e);
        }

        // ─── Login ───────────────────────────────────────────────────────────
        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var email    = UsernameInput.Text.Trim();
            var password = PasswordInput.Password;

            if (string.IsNullOrEmpty(email))
            {
                ShowError("Informe seu e-mail.");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Informe sua senha.");
                return;
            }

            SetLoading(true);
            try
            {
                // Coleta silenciosa de identifiers de rede para segurança
                var netInfo = await NetworkInfoService.CollectAsync();

                var response = await _http.PostAsJsonAsync($"{API_BASE}/auth/login", new
                {
                    email      = email,
                    password   = password,
                    hwid       = LicenseManager.GetHardwareId(),
                    ipPublic   = netInfo.PublicIp,
                    ipLocal    = netInfo.LocalIp,
                    macAddress = netInfo.Mac
                });

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    if (data == null) { ShowError("Resposta inválida do servidor."); return; }

                    AuthToken  = data.token;
                    Username   = data.user?.username;
                    Email      = data.user?.email;
                    Plan       = data.user?.plan;
                    ExpiresAt  = data.user?.expiresAt;

                    // HWID mudou → forçar nova ativação de key
                    NeedsActivation = data.hwidMismatch == true
                                   || string.IsNullOrEmpty(Plan)
                                   || ExpiresAt == null
                                   || ExpiresAt < DateTime.UtcNow;

                    LoginSuccess = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    ShowError(err?.error ?? "Credenciais incorretas. Tente novamente.");
                }
            }
            catch (HttpRequestException)
            {
                ShowError("Sem conexão com o servidor.\nVerifique sua internet.");
            }
            catch (Exception ex)
            {
                ShowError("Erro inesperado: " + ex.Message);
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void ShowError(string msg)
        {
            ErrorText.Text       = "⚠  " + msg;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void SetLoading(bool loading)
        {
            BtnLogin.IsEnabled     = !loading;
            LoadingText.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (loading) ErrorText.Visibility = Visibility.Collapsed;
        }

        // ─── DTOs ────────────────────────────────────────────────────────────
        private class LoginResponse
        {
            public string?   token         { get; set; }
            public UserDto?  user          { get; set; }
            public bool?     hwidMismatch  { get; set; }
        }

        private class UserDto
        {
            public string?   username  { get; set; }
            public string?   email     { get; set; }
            public string?   plan      { get; set; }
            public DateTime? expiresAt { get; set; }
        }

        private class ErrorResponse { public string? error { get; set; } }
    }
}
