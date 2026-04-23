using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace AimAssistPro.Views
{
    public partial class UpdateDialog : Window
    {
        // True se o usuario aceitou E o installer foi lançado com sucesso
        public bool ShouldExitApp { get; private set; } = false;

        private readonly bool   _mandatory;
        private readonly string _downloadUrl;
        private bool            _downloading = false;

        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public UpdateDialog(string currentVersion, string newVersion, string changelog, bool mandatory, string downloadUrl)
        {
            InitializeComponent();

            _mandatory   = mandatory;
            _downloadUrl = downloadUrl;

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
                BtnClose.IsEnabled        = false;
                BtnClose.Opacity          = 0.2;
            }
        }

        // ── Botão X ──────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading) return;

            if (_mandatory)
            {
                ShouldExitApp = true;
                Close();
            }
            else
            {
                ShouldExitApp = false;
                Close();
            }
        }

        // ── Agora não ────────────────────────────────────────────────────────
        private void BtnSkip_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading) return;
            ShouldExitApp = _mandatory; // se obrigatória, encerra o app
            Close();
        }

        // ── Atualizar agora ──────────────────────────────────────────────────
        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading) return;
            _downloading = true;

            // Oculta botões e mostra painel de progresso
            ButtonsPanel.Visibility = Visibility.Collapsed;
            InfoPanel.Visibility    = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnClose.IsEnabled      = false;
            BtnClose.Opacity        = 0.2;

            await StartDownloadAsync();
        }

        private async Task StartDownloadAsync()
        {
            var tempSetup = Path.Combine(Path.GetTempPath(), "PrecisionAimAssist_Setup.exe");

            try
            {
                SetStatus("Baixando atualização...", "Conectando ao servidor...");
                DownloadBar.IsIndeterminate = true;

                using var response = await _http.GetAsync(
                    _downloadUrl, HttpCompletionOption.ResponseHeadersRead);

                response.EnsureSuccessStatusCode();

                var total    = response.Content.Headers.ContentLength ?? -1;
                long received = 0;

                // Progresso determinado agora que sabemos o tamanho
                if (total > 0)
                    Dispatcher.Invoke(() => DownloadBar.IsIndeterminate = false);

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file   = new FileStream(
                    tempSetup, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read);
                    received += read;

                    if (total > 0)
                    {
                        double pct     = (double)received / total * 100;
                        double recMb   = received / 1_048_576.0;
                        double totalMb = total    / 1_048_576.0;

                        Dispatcher.Invoke(() =>
                        {
                            DownloadBar.Value = pct;
                            PctText.Text      = $"{pct:F0}%";
                            DetailText.Text   = $"{recMb:F1} MB / {totalMb:F1} MB";
                        });
                    }
                }

                file.Close();

                // ── Fase: instalando ──────────────────────────────────────────
                Dispatcher.Invoke(() =>
                {
                    ProgressIcon.Text          = "✓";
                    StatusText.Text            = "Instalando...";
                    DetailText.Text            = "Aguarde, isso levará alguns segundos.";
                    DownloadBar.IsIndeterminate = true;
                    PctText.Text               = "";

                    // Para a animação de bounce e fica estático
                    ProgressIcon.BeginAnimation(RenderTransformProperty, null);
                });

                // Lança o instalador com UAC
                Process.Start(new ProcessStartInfo
                {
                    FileName        = tempSetup,
                    Arguments       = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS",
                    UseShellExecute = true,
                    Verb            = "runas"
                });

                // Pequena pausa para o UAC subir
                await Task.Delay(1500);

                // Sinaliza para o App.OnStartup fechar
                ShouldExitApp = true;
                Dispatcher.Invoke(() => Close());
            }
            catch (Exception ex)
            {
                if (File.Exists(tempSetup)) try { File.Delete(tempSetup); } catch { }

                Dispatcher.Invoke(() =>
                {
                    ProgressIcon.Text           = "✕";
                    StatusText.Text             = "Falha no download";
                    DetailText.Text             = ex.Message;
                    DownloadBar.IsIndeterminate  = false;
                    DownloadBar.Value            = 0;
                    PctText.Text                = "";

                    // Mostra botões novamente para tentar de novo
                    ButtonsPanel.Visibility     = Visibility.Visible;
                    BtnUpdate.Content           = "Tentar novamente";
                    BtnClose.IsEnabled          = !_mandatory;
                    BtnClose.Opacity            = _mandatory ? 0.2 : 1.0;
                    _downloading = false;
                });
                return;
            }
        }

        private void SetStatus(string main, string detail)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text  = main;
                DetailText.Text  = detail;
            });
        }

        // Arrastar pela janela
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }

        // Fechar pela ALT+F4 durante download → impede se obrigatória
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_downloading && _mandatory)
            {
                e.Cancel = true; // não pode fechar enquanto baixa
                return;
            }
            base.OnClosing(e);
        }
    }
}
