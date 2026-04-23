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
    public partial class RegisterWindow : Window
    {
        public bool    RegisterSuccess { get; private set; } = false;
        public string? AuthToken       { get; private set; }
        public string? Username        { get; private set; }
        public bool    WantsLogin      { get; private set; } = false;

        private static string API_BASE => SecureConfig.ApiBase + "/api";
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly SolidColorBrush _borderDefault = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E));
        private static readonly SolidColorBrush _borderFocused  = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));


        public RegisterWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => NomeInput.Focus();
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
        private void NomeInput_GotFocus(object sender, RoutedEventArgs e)
            => NomeBorder.BorderBrush = _borderFocused;

        private void NomeInput_LostFocus(object sender, RoutedEventArgs e)
            => NomeBorder.BorderBrush = _borderDefault;

        private void EmailInput_GotFocus(object sender, RoutedEventArgs e)
            => EmailBorder.BorderBrush = _borderFocused;

        private void EmailInput_LostFocus(object sender, RoutedEventArgs e)
            => EmailBorder.BorderBrush = _borderDefault;

        private void PassInput_GotFocus(object sender, RoutedEventArgs e)
            => PassBorder.BorderBrush = _borderFocused;

        private void PassInput_LostFocus(object sender, RoutedEventArgs e)
            => PassBorder.BorderBrush = _borderDefault;

        // ─── Placeholders ────────────────────────────────────────────────────
        private void NomeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            NomePlaceholder.Visibility = string.IsNullOrEmpty(NomeInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EmailInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            EmailPlaceholder.Visibility = string.IsNullOrEmpty(EmailInput.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PassInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PassPlaceholder.Visibility = PasswordInput.Password.Length > 0
                ? Visibility.Collapsed : Visibility.Visible;
        }

        // ─── Navigation ──────────────────────────────────────────────────────
        private void BtnGoLogin_Click(object sender, MouseButtonEventArgs e)
        {
            WantsLogin   = true;
            DialogResult = false;
            Close();
        }

        // Alias para o novo XAML
        private void OpenLogin_Click(object sender, MouseButtonEventArgs e)
            => BtnGoLogin_Click(sender, e);


        // ─── Register ────────────────────────────────────────────────────────
        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            var username = NomeInput.Text.Trim();
            var email    = EmailInput.Text.Trim();
            var password = PasswordInput.Password;

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Informe um nome de usuário.");
                return;
            }

            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            {
                ShowError("Informe um e-mail válido.");
                return;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ShowError("A senha deve ter pelo menos 6 caracteres.");
                return;
            }

            SetLoading(true);
            try
            {
                var response = await _http.PostAsJsonAsync($"{API_BASE}/auth/register", new
                {
                    username = username,
                    email    = email,
                    password = password
                });

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<RegisterResponse>();
                    if (data == null) { ShowError("Resposta inválida do servidor."); return; }

                    AuthToken       = data.token;
                    Username        = data.user?.username;
                    RegisterSuccess = true;
                    DialogResult    = true;
                    Close();
                }
                else
                {
                    var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    ShowError(err?.error ?? "Erro ao criar conta. Tente novamente.");
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
            BtnRegister.IsEnabled  = !loading;
            LoadingText.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            if (loading) ErrorText.Visibility = Visibility.Collapsed;
        }

        // ─── DTOs ────────────────────────────────────────────────────────────
        private class RegisterResponse
        {
            public string?  token { get; set; }
            public UserDto? user  { get; set; }
        }

        private class UserDto
        {
            public string? username { get; set; }
            public string? email    { get; set; }
        }

        private class ErrorResponse { public string? error { get; set; } }
    }
}
