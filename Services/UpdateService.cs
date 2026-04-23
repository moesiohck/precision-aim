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
    /// Verifica se há uma nova versão disponível na API e oferece atualização automática.
    /// Fluxo:
    ///   1. Startup → chama GET /api/version na Vercel
    ///   2. Compara com a versão atual do EXE
    ///   3. Se nova versão → baixa o novo EXE → substitui o atual → reinicia
    /// </summary>
    internal static class UpdateService
    {
        // Versão atual do app — deve bater com <Version> no .csproj
        public const string CurrentVersion = "1.0.0";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>
        /// Executa verificação de atualização. Chamar no startup (após login).
        /// Retorna true se o app foi reiniciado (update aplicado).
        /// </summary>
        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                // ── 1. Busca versão disponível na API ────────────────────────
                var url = SecureConfig.ApiBase + "/api/version";
                var json = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestVersion  = root.GetProperty("version").GetString()  ?? "0.0.0";
                var downloadUrl    = root.GetProperty("downloadUrl").GetString() ?? "";
                var changelog      = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";
                var mandatory      = root.TryGetProperty("mandatory", out var m) && m.GetBoolean();
                var minVersion     = root.TryGetProperty("minVersion", out var mv) ? mv.GetString() ?? "0.0.0" : "0.0.0";

                // ── 2. Compara versões ───────────────────────────────────────
                if (!IsNewerVersion(latestVersion, CurrentVersion))
                    return; // já está na versão mais recente

                bool forcedByMinVersion = IsNewerVersion(minVersion, CurrentVersion);
                bool shouldForce = mandatory || forcedByMinVersion;

                // ── 3. Mostra diálogo estilizado no thread da UI ─────────────
                bool userWantsUpdate = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dlg = new AimAssistPro.Views.UpdateDialog(
                        CurrentVersion, latestVersion, changelog, shouldForce);
                    dlg.ShowDialog();
                    userWantsUpdate = dlg.UserAccepted;
                });

                if (!userWantsUpdate)
                    return;

                // ── 4. Baixa o novo EXE ──────────────────────────────────────
                await DownloadAndApplyUpdate(downloadUrl);
            }
            catch (Exception ex)
            {
                // Silencioso — falha de update não deve impedir uso normal
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[UpdateService] {ex.Message}");
#endif
            }
        }

        private static async Task DownloadAndApplyUpdate(string downloadUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl)) return;

            string currentExe = Process.GetCurrentProcess().MainModule?.FileName
                                ?? AppContext.BaseDirectory + "AimAssistPro.exe";

            // Arquivo temporário para o download
            string tempExe   = currentExe + ".new";
            string backupExe = currentExe + ".bak";

            try
            {
                // Download do novo EXE
                var bytes = await _http.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(tempExe, bytes);

                // Script PowerShell para substituir o EXE após o processo fechar
                // (não é possível substituir um EXE enquanto está sendo executado)
                string script = $@"
Start-Sleep -Seconds 2
Copy-Item -Path '{tempExe}' -Destination '{currentExe}' -Force
Remove-Item -Path '{tempExe}' -ErrorAction SilentlyContinue
Remove-Item -Path '{backupExe}' -ErrorAction SilentlyContinue
Start-Process '{currentExe}'
";
                string scriptPath = Path.Combine(Path.GetTempPath(), "aim_update.ps1");
                await File.WriteAllTextAsync(scriptPath, script);

                // Executa o script e fecha o app atual
                Process.Start(new ProcessStartInfo
                {
                    FileName  = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                // Limpa arquivo temporário em caso de erro
                if (File.Exists(tempExe)) File.Delete(tempExe);
                MessageBox.Show(
                    $"Falha ao baixar atualização: {ex.Message}\n\nTente novamente mais tarde.",
                    "Precision Aim Assist — Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Retorna true se 'candidate' é uma versão mais nova que 'current'.
        /// Usa comparação semântica (1.2.0 > 1.1.9).
        /// </summary>
        private static bool IsNewerVersion(string candidate, string current)
        {
            if (!Version.TryParse(candidate, out var vNew)) return false;
            if (!Version.TryParse(current,   out var vCur)) return false;
            return vNew > vCur;
        }
    }
}
