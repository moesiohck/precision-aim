using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class ActivateKeyWindow : Window
    {
        public bool ActivationSuccess { get; private set; } = false;
        public string? Plan      { get; private set; }
        public DateTime? ExpiresAt { get; private set; }
        public string? ActivatedKey { get; private set; }

        private readonly string _authToken;
        private static string API_BASE => SecureConfig.ApiBase + "/api";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public ActivateKeyWindow(string authToken)
        {
            InitializeComponent();
            _authToken = authToken;
            KeyInput.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // Fechar sem ativar → sair do app
            Application.Current.Shutdown();
        }

        // ── Auto-formata PRCSN-XXXX-XXXX-XXXX enquanto digita ──────────────
        private bool _formatting = false;
        private void KeyInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_formatting) return;
            _formatting = true;

            var raw    = Regex.Replace(KeyInput.Text, @"[^A-Za-z0-9]", "").ToUpper();
            var capped = raw.Length > 17 ? raw[..17] : raw;

            // Formato: PRCSN-XXXX-XXXX-XXXX (5-4-4-4)
            var formatted = "";
            int[] dashPositions = { 5, 9, 13 };
            for (int i = 0; i < capped.Length; i++)
            {
                if (Array.IndexOf(dashPositions, i) >= 0) formatted += "-";
                formatted += capped[i];
            }

            KeyInput.Text            = formatted;
            KeyInput.SelectionStart  = formatted.Length;
            _formatting = false;

            // Habilita botão com PRCSN-XXXX-XXXX-XXXX (20 chars) ou legacy AAPR (19 chars)
            BtnActivate.IsEnabled = formatted.Length == 20 || formatted.Length == 19;
        }

        private void KeyInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnActivate.IsEnabled)
                BtnActivate_Click(sender, e);
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyInput.Text.Trim();
            if (key.Length < 19) { ShowError("Key inválida. Use o formato PRCSN-XXXX-XXXX-XXXX."); return; }

            SetLoading(true);
            try
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _authToken);

                var hwid = GetHwid();

                var response = await _http.PostAsJsonAsync($"{API_BASE}/keys/activate", new
                {
                    key  = key.ToUpper(), // Envia AAPR-F81C-2B91-E705 exatamente como no DB (com traços)
                    hwid = hwid
                });

                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadFromJsonAsync<ActivateResponse>();
                    Plan         = data?.plan;
                    ExpiresAt    = data?.expiresAt;
                    ActivatedKey = key;  // guarda a key real para exibir no popup de perfil

                    // Mostra sucesso e fecha após 2s
                    // Formata expiração: mostra hora se for menos de 1 dia (ex: key de teste de 3h)
                    string expStr;
                    if (data?.durationDays < 1)
                        expStr = $"em {ExpiresAt:HH:mm} de hoje (teste)";
                    else
                        expStr = $"em {ExpiresAt:dd/MM/yyyy}";
                    SuccessText.Text = $"Licença {Plan} ativada! Expira {expStr}";
                    SuccessPanel.Visibility = Visibility.Visible;
                    ErrorPanel.Visibility   = Visibility.Collapsed;
                    BtnActivate.IsEnabled   = false;

                    await System.Threading.Tasks.Task.Delay(2000);
                    ActivationSuccess = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                    ShowError(err?.error ?? "Chave inválida ou já utilizada.");
                }
            }
            catch (HttpRequestException)
            {
                ShowError("Sem conexão com o servidor.\nVerifique sua internet.");
            }
            catch (Exception ex)
            {
                ShowError($"Erro: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void ShowError(string msg)
        {
            ErrorText.Text        = msg;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void SetLoading(bool loading)
        {
            BtnActivate.IsEnabled    = !loading && KeyInput.Text.Length == 19;
            LoadingText.Visibility   = loading ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string GetHwid() => LicenseManager.GetHardwareId();

        private class ActivateResponse
        {
            public string? plan      { get; set; }
            public DateTime? expiresAt { get; set; }
            public double durationDays { get; set; }
        }

        private class ErrorResponse { public string? error { get; set; } }
    }
}
