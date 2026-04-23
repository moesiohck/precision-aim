using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Apenas verifica se há nova versão disponível na API.
    /// O download e instalação é responsabilidade do UpdateDialog.
    /// </summary>
    internal static class UpdateService
    {
        public const string CurrentVersion = "1.2.4";

        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(15) };

        public class UpdateInfo
        {
            public string LatestVersion { get; set; } = "";
            public string DownloadUrl   { get; set; } = "";
            public string Changelog     { get; set; } = "";
            public bool   ShouldForce   { get; set; } = false;
        }

        /// <summary>
        /// Retorna UpdateInfo se houver versão mais nova, null caso contrário.
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var url  = SecureConfig.ApiBase + "/api/version";
                var json = await _http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestVersion = root.GetProperty("version").GetString()     ?? "0.0.0";
                var downloadUrl   = root.GetProperty("downloadUrl").GetString()  ?? "";
                var changelog     = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";
                var mandatory     = root.TryGetProperty("mandatory", out var m)  && m.GetBoolean();
                var minVersion    = root.TryGetProperty("minVersion", out var mv) ? mv.GetString() ?? "0.0.0" : "0.0.0";

                if (!IsNewerVersion(latestVersion, CurrentVersion))
                    return null; // sem update

                bool forcedByMin = IsNewerVersion(minVersion, CurrentVersion);
                bool shouldForce = mandatory || forcedByMin;

                // Garante que a URL aponte para o Setup e não para o EXE solo
                downloadUrl = downloadUrl.Replace("AimAssistPro.exe", "PrecisionAimAssist_Setup.exe");

                return new UpdateInfo
                {
                    LatestVersion = latestVersion,
                    DownloadUrl   = downloadUrl,
                    Changelog     = changelog,
                    ShouldForce   = shouldForce,
                };
            }
            catch
            {
                return null; // falha de rede → silencioso
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
