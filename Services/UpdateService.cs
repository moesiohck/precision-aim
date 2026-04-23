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
        public const string CurrentVersion = "1.2.2";

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

        public enum UpdateResult { NoUpdate, Proceed, ShutdownRequired }

        public static async Task<UpdateResult> CheckForUpdatesAsync()
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
                    return UpdateResult.NoUpdate;

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

                // Usuário recusou / fechou o diálogo
                if (!userWantsUpdate)
                    return shouldForce ? UpdateResult.ShutdownRequired : UpdateResult.NoUpdate;

                // ── 4. Baixa o Setup.exe e executa ───────────────────────────
                string setupUrl = downloadUrl.Replace("AimAssistPro.exe", "PrecisionAimAssist_Setup.exe");
                bool downloadOk = await DownloadAndRunSetup(setupUrl, shouldForce);

                // Se download teve sucesso, o Process.Start do installer já foi chamado
                // e o OnStartup vai receber ShutdownRequired para fechar limpo
                if (downloadOk)  return UpdateResult.ShutdownRequired;
                if (shouldForce) return UpdateResult.ShutdownRequired;
                return UpdateResult.NoUpdate;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[UpdateService] {ex}");
#endif
                // Silencioso em Release
                return UpdateResult.NoUpdate;
            }
        }

        /// <summary>Retorna true se o installer foi iniciado com sucesso.</summary>
        private static async Task<bool> DownloadAndRunSetup(string setupUrl, bool mandatory)
        {
            if (string.IsNullOrEmpty(setupUrl)) return false;

            string tempSetup = Path.Combine(Path.GetTempPath(), "PrecisionAimAssist_Setup.exe");
            bool success = false;

            try
            {
                // Desabilita janelas durante o download para o usuário não navegar
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
                    await file.WriteAsync(buffer, 0, read);

                file.Close();

                // ── Lança o instalador silencioso com UAC ────────────────────
                Process.Start(new ProcessStartInfo
                {
                    FileName        = tempSetup,
                    Arguments       = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS",
                    UseShellExecute = true,
                    Verb            = "runas"
                });

                success = true; // installer iniciado — caller vai fechar o app
            }
            catch (Exception ex)
            {
                if (File.Exists(tempSetup)) try { File.Delete(tempSetup); } catch { }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (Window w in Application.Current.Windows)
                        w.IsEnabled = true;

                    var msg = mandatory
                        ? $"Não foi possível baixar a atualização necessária.\n\nVerifique sua conexão e tente abrir o aplicativo novamente.\n\nErro: {ex.Message}"
                        : $"Falha ao baixar atualização. Verifique sua internet.\n\nErro: {ex.Message}";

                    var icon = mandatory ? MessageBoxImage.Error : MessageBoxImage.Warning;
                    MessageBox.Show(msg, "Precision Aim Assist", MessageBoxButton.OK, icon);
                });
            }

            return success;
        }

        private static bool IsNewerVersion(string candidate, string current)
        {
            if (!Version.TryParse(candidate, out var vNew)) return false;
            if (!Version.TryParse(current,   out var vCur)) return false;
            return vNew > vCur;
        }
    }
}
