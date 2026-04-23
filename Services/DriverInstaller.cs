using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Extrai instaladores embutidos no EXE e executa instalação silenciosa sem nenhuma interação do usuário.
    /// </summary>
    public static class DriverInstaller
    {
        private static string TempDir =>
            Path.Combine(Path.GetTempPath(), "AimAssistPro_Drivers");

        public record InstallResult(bool Success, string Message, bool NeedsReboot = false);

        // ══════════════════════════════════════════════════════════════════════
        //  ViGEmBus  (WiX/MSI — usa /quiet /norestart, NÃO /S)
        // ══════════════════════════════════════════════════════════════════════
        public static async Task<InstallResult> InstallViGEmAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                progress?.Report("Extraindo ViGEmBus...");
                string exe = await ExtractResourceAsync(
                    "AimAssistPro.Drivers.ViGEmBus_Setup.exe", "ViGEmBus_Setup.exe");

                progress?.Report("Instalando ViGEmBus (admin)...");

                // ViGEmBus usa WiX bootstrapper que aceita /quiet /norestart
                // NÃO usar /S (NSIS) — causa "Invalid command line"
                int code = await RunElevatedAsync(exe, "/quiet /norestart", ct);

                // Códigos MSI/WiX padrão:
                //   0    = sucesso sem reboot
                //   1641 = sucesso, reboot iniciado
                //   3010 = sucesso, reboot necessário
                //   1602 = usuário cancelou UAC
                bool needsReboot = (code == 3010 || code == 1641);
                bool success      = (code == 0 || needsReboot);

                if (success)
                    return new InstallResult(true,
                        "ViGEmBus instalado com sucesso!",
                        NeedsReboot: needsReboot);

                // Fallback: tenta via msiexec se o exe falhou
                if (code != -1223) // não tentar se usuário cancelou UAC
                {
                    progress?.Report("Tentando instalação alternativa...");
                    code = await RunElevatedAsync(
                        "msiexec.exe",
                        $"/i \"{exe}\" /quiet /norestart",
                        ct);
                    needsReboot = (code == 3010 || code == 1641);
                    success      = (code == 0 || needsReboot);
                    if (success)
                        return new InstallResult(true,
                            "ViGEmBus instalado com sucesso!",
                            NeedsReboot: needsReboot);
                }

                return new InstallResult(false,
                    $"Erro na instalação do ViGEmBus (código {code}).\n" +
                    "Certifique-se de executar o app como Administrador.");
            }
            catch (Exception ex)
            {
                return new InstallResult(false, $"Erro: {ex.Message}");
            }
        }

        public static async Task<InstallResult> UninstallViGEmAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                string uninst = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Nefarius Software Solutions e.U", "ViGEm Bus Driver", "Uninstall.exe");

                if (File.Exists(uninst))
                {
                    progress?.Report("Desinstalando ViGEmBus...");
                    await RunSilentElevatedAsync(uninst, "/S", ct);
                }
                else
                {
                    progress?.Report("Removendo driver via pnputil...");
                    await RunSilentElevatedAsync("pnputil.exe",
                        "/delete-driver vigembus.inf /uninstall /force", ct);
                }

                return new InstallResult(true,
                    "ViGEmBus desinstalado.\nReinicie o computador para concluir.",
                    NeedsReboot: true);
            }
            catch (Exception ex)
            {
                return new InstallResult(false, $"Erro: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Interception
        //  install-interception.exe /install
        //    código  0 = instalado (sem reboot)
        //    código  1 = instalado, reboot necessário  ← SUCESSO
        //    código -1 = falha real
        // ══════════════════════════════════════════════════════════════════════
        public static async Task<InstallResult> InstallInterceptionAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                progress?.Report("Extraindo instalador do Interception...");
                string exe = await ExtractResourceAsync(
                    "AimAssistPro.Drivers.install-interception.exe",
                    "install-interception.exe");

                progress?.Report("Instalando driver Interception (admin)...");

                // CRÍTICO: install-interception.exe instala um driver de kernel.
                // Requer elevação real via runas — cmd /c sem runas falha silenciosamente.
                int code = await RunElevatedAsync(exe, "/install", ct);

                // Qualquer código >= 0 significa que rodou (0=ok, 1=reboot necessário)
                // Código negativo (como -1) significa erro real
                if (code >= 0)
                {
                    // Grava flag persistente no registro para que, após o reboot,
                    // o app saiba que o driver foi instalado com sucesso mesmo
                    // que a detecção via nome de serviço não o encontre.
                    DriverStatusService.MarkInterceptionInstalled();

                    return new InstallResult(
                        true,
                        "Interception instalado com sucesso!\n\n" +
                        "⚠ Reinicie o computador para ativar o driver.",
                        NeedsReboot: true);
                }

                return new InstallResult(false,
                    $"Falha ao instalar Interception (código {code}).\n" +
                    "Certifique-se de executar o app como Administrador.");
            }
            catch (Exception ex)
            {
                return new InstallResult(false, $"Erro: {ex.Message}");
            }
        }

        public static async Task<InstallResult> UninstallInterceptionAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                progress?.Report("Extraindo desinstalador...");
                string exe = await ExtractResourceAsync(
                    "AimAssistPro.Drivers.install-interception.exe",
                    "install-interception.exe");

                progress?.Report("Desinstalando Interception...");
                await RunElevatedAsync(exe, "/uninstall", ct);
                DriverStatusService.ClearInterceptionInstalledFlag();

                return new InstallResult(true,
                    "Interception desinstalado.\nReinicie o computador para concluir.",
                    NeedsReboot: true);
            }
            catch (Exception ex)
            {
                return new InstallResult(false, $"Erro: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════════

        private static async Task<string> ExtractResourceAsync(string resourceName, string fileName)
        {
            Directory.CreateDirectory(TempDir);
            string outPath = Path.Combine(TempDir, fileName);

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException(
                    $"Recurso '{resourceName}' não encontrado no EXE.\n" +
                    "Verifique se está em AimAssistPro/Drivers/ e marcado como EmbeddedResource.");

            using var fs = File.Create(outPath);
            await stream.CopyToAsync(fs);
            return outPath;
        }

        /// <summary>
        /// Executa um executável com elevação de admin via ShellExecute + runas.
        /// Necessário para instalação de drivers de kernel.
        /// Retorna o exit code do processo ou -999 em caso de falha.
        /// </summary>
        private static Task<int> RunElevatedAsync(string exe, string args, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName        = exe,
                        Arguments       = args,
                        UseShellExecute = true,   // OBRIGATÓRIO para Verb=runas funcionar
                        Verb            = "runas", // Solicita elevação UAC
                        WindowStyle     = ProcessWindowStyle.Hidden,
                        CreateNoWindow  = true,
                    };

                    using var proc = Process.Start(psi)
                        ?? throw new InvalidOperationException("Falha ao iniciar o instalador elevado.");

                    proc.WaitForExit(120_000); // 2 minutos de timeout
                    return proc.ExitCode;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // Usuário cancelou o prompt UAC (Error 1223 = operação cancelada)
                    return -1223;
                }
                catch (Exception)
                {
                    return -999;
                }
            }, ct);
        }

        /// <summary>
        /// Executa via cmd /c legado — mantido apenas para compatibilidade.
        /// </summary>
        private static Task<int> RunViaCmdAsync(string command, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "cmd.exe",
                    Arguments              = $"/c {command}",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardError  = false,
                    RedirectStandardOutput = false,
                };

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Falha ao iniciar cmd.exe");

                proc.WaitForExit(120_000);
                return proc.ExitCode;
            }, ct);
        }

        /// <summary>
        /// Alternativa: usa ShellExecute + runas para instaladores GUI (como ViGEmBus NSIS).
        /// </summary>
        private static Task<int> RunSilentElevatedAsync(string exe, string args, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = exe,
                    Arguments      = args,
                    UseShellExecute= true,
                    WindowStyle    = ProcessWindowStyle.Hidden,
                };

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Falha ao iniciar o instalador.");

                proc.WaitForExit(120_000);
                return proc.ExitCode;
            }, ct);
        }
    }
}
