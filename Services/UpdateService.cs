using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Verifica se há uma nova versão e baixa + executa o instalador automaticamente.
    /// Se a atualização for obrigatória e o download falhar → fecha o app.
    /// </summary>
    internal static class UpdateService
    {
        public const string CurrentVersion = "1.2.1";

        private static readonly HttpClient _checkHttp = new(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly HttpClient _downloadHttp = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                // ── 1. Verifica versão na API ────────────────────────────────
                var url  = SecureConfig.ApiBase + "/api/version";
                var json = await _checkHttp.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestVersion = root.GetProperty("version").GetString()     ?? "0.0.0";
                var downloadUrl   = root.GetProperty("downloadUrl").GetString()  ?? "";
                var changelog     = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";
                var mandatory     = root.TryGetProperty("mandatory", out var m)  && m.GetBoolean();
                var minVersion    = root.TryGetProperty("minVersion", out var mv) ? mv.GetString() ?? "0.0.0" : "0.0.0";

                // ── 2. Checa se há versão mais nova ──────────────────────────
                if (!IsNewerVersion(latestVersion, CurrentVersion))
                    return;

                bool forcedByMin = IsNewerVersion(minVersion, CurrentVersion);
                bool shouldForce = mandatory || forcedByMin;

                // ── 3. Mostra diálogo no thread UI ───────────────────────────
                bool userWantsUpdate = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dlg = new AimAssistPro.Views.UpdateDialog(
                        CurrentVersion, latestVersion, changelog, shouldForce);
                    dlg.ShowDialog();
                    userWantsUpdate = dlg.UserAccepted;
                });

                // Se o usuário recusou/fechou E a atualização é obrigatória → fecha o app
                if (!userWantsUpdate)
                {
                    if (shouldForce)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            Application.Current.Shutdown());
                    }
                    return;
                }

                // ── 4. Baixa o Setup.exe e executa ───────────────────────────
                string setupUrl = downloadUrl.Replace("AimAssistPro.exe", "PrecisionAimAssist_Setup.exe");
                await DownloadAndRunSetup(setupUrl, shouldForce);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[UpdateService] {ex}");
#endif
                // Silencioso em Release para não bloquear o uso em caso de falha de rede
            }
        }

        private static async Task DownloadAndRunSetup(string setupUrl, bool mandatory)
        {
            if (string.IsNullOrEmpty(setupUrl))
            {
                if (mandatory) Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                return;
            }

            string tempSetup = Path.Combine(Path.GetTempPath(), "PrecisionAimAssist_Setup.exe");

            try
            {
                // Mostra progresso para o usuário não achar que travou
                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (Window w in Application.Current.Windows)
                        w.IsEnabled = false;
                });

                // ── Download via streaming (não carrega tudo na RAM) ─────────
                using var response = await _downloadHttp.GetAsync(
                    setupUrl, HttpCompletionOption.ResponseHeadersRead);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file   = new FileStream(tempSetup, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await file.WriteAsync(buffer, 0, read);
                }

                file.Close();

                // ── Executa o instalador silenciosamente com UAC ─────────────
                Process.Start(new ProcessStartInfo
                {
                    FileName        = tempSetup,
                    Arguments       = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS",
                    UseShellExecute = true,
                    Verb            = "runas"
                });

                // Fecha o app — o instalador substitui e relança
                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                if (File.Exists(tempSetup)) File.Delete(tempSetup);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Reativa as janelas
                    foreach (Window w in Application.Current.Windows)
                        w.IsEnabled = true;

                    if (mandatory)
                    {
                        // Atualização obrigatória falhou → informa e fecha
                        MessageBox.Show(
                            "Não foi possível baixar a atualização necessária.\n\n" +
                            "Verifique sua conexão e tente abrir o aplicativo novamente.\n\n" +
                            $"Erro: {ex.Message}",
                            "Precision Aim Assist — Atualização Necessária",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        Application.Current.Shutdown();
                    }
                    else
                    {
                        // Atualização opcional falhou → apenas avisa
                        MessageBox.Show(
                            $"Falha ao baixar atualização.\n\nVerifique sua internet e tente novamente.\n\nErro: {ex.Message}",
                            "Precision Aim Assist",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                });
            }
        }

        private static bool IsNewerVersion(string candidate, string current)
        {
            if (!Version.TryParse(candidate, out var vNew)) return false;
            if (!Version.TryParse(current,   out var vCur)) return false;
            return vNew > vCur;
        }
    }
}
