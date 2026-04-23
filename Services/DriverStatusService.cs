using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Verifica o status de instalação dos drivers necessários:
    ///   1. ViGEmBus  — emulação de controle virtual Xbox/DS4
    ///   2. Interception — filtro de teclado/mouse em modo kernel
    ///
    /// A detecção do Interception usa 6 camadas em ordem de confiabilidade:
    ///   Camada 0 → Conexão real ao driver via P/Invoke (mais confiável — detecta qualquer instalação)
    ///   Camada 1 → Flag persistente no registro do app (sobrevive ao reboot)
    ///   Camada 2 → Chaves de serviço no HKLM (nomes conhecidos do Interception)
    ///   Camada 3 → Varredura de TODOS os serviços kernel via sc.exe
    ///   Camada 4 → Arquivo .sys em System32\drivers
    ///   Camada 5 → interception.dll na pasta do app
    /// </summary>
    public static class DriverStatusService
    {
        // ─── P/Invoke para teste de conexão real ao driver ────────────────────
        // Tenta chamar interception_create_context() diretamente.
        // Se retornar != IntPtr.Zero, o driver está FUNCIONANDO no kernel,
        // independente de como ou por quem foi instalado.
        private const string InterceptionDll = "interception.dll";

        [DllImport(InterceptionDll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "interception_create_context")]
        private static extern IntPtr interception_create_context();

        [DllImport(InterceptionDll, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "interception_destroy_context")]
        private static extern void interception_destroy_context(IntPtr ctx);

        // ─── Flag persistente no registro ─────────────────────────────────────
        private const string AppRegKey    = @"SOFTWARE\AimAssistPro";
        private const string IcInstalledV = "InterceptionInstalledConfirmed";

        public static void MarkInterceptionInstalled()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(AppRegKey, true);
                key?.SetValue(IcInstalledV, 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        public static void ClearInterceptionInstalledFlag()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppRegKey, true);
                key?.DeleteValue(IcInstalledV, false);
            }
            catch { }
        }

        private static bool HasInstalledFlag()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AppRegKey, false);
                if (key == null) return false;
                var val = key.GetValue(IcInstalledV);
                return val != null && (int)val == 1;
            }
            catch { return false; }
        }

        // ─── ViGEmBus Check ───────────────────────────────────────────────────
        public static bool IsViGEmInstalled()
        {
            // Testa conexão direta: a forma mais confiável
            try
            {
                using var client = new Nefarius.ViGEm.Client.ViGEmClient();
                client.Dispose();
                return true;
            }
            catch { }

            // Chave de serviço no registro
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\ViGEmBus", false);
                if (key != null) return true;
            }
            catch { }

            // Alternativa: nssProxy (versão mais antiga do ViGEmBus)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\nssProxy", false);
                if (key != null) return true;
            }
            catch { }

            return false;
        }

        // ─── Interception Check ───────────────────────────────────────────────
        public static bool IsInterceptionInstalled()
        {
            // ══ CAMADA 0: conexão real ao driver via P/Invoke ══════════════════
            // Esta é a verificação definitiva. Se a DLL existir e o driver
            // estiver no kernel, interception_create_context() retorna != Zero.
            bool layer0 = TryConnectToDriver();
            if (layer0)
            {
                MarkInterceptionInstalled();
                return true;
            }

            // ══ CAMADA 1: flag persistente (sobrevive ao reboot) ═══════════════
            // REGRA: se a flag está setada, CONFIAR SEMPRE.
            // A flag só é limpa quando o usuário clica em Desinstalar.
            // Antes havia um ClearInterceptionInstalledFlag() aqui que causava
            // o loop infinito pós-reboot — REMOVIDO.
            if (HasInstalledFlag())
                return true;

            // ══ CAMADAS 2-5: detecção de evidências no sistema ═════════════════
            if (IsInterceptionKernelEvidencePresent())
            {
                MarkInterceptionInstalled();
                return true;
            }

            return false;
        }

        // ─── Camada 0: teste de conexão real ao driver ────────────────────────
        /// <summary>
        /// Tenta criar e destruir um contexto Interception real.
        /// Retorna true se o driver está ativo no kernel — independente
        /// do nome do serviço ou de onde/como foi instalado.
        /// </summary>
        private static bool TryConnectToDriver()
        {
            // Só funciona se a interception.dll estiver na pasta do app
            string dllPath = Path.Combine(AppContext.BaseDirectory, InterceptionDll);
            if (!File.Exists(dllPath))
                return false;

            try
            {
                IntPtr ctx = interception_create_context();
                if (ctx == IntPtr.Zero)
                    return false;

                interception_destroy_context(ctx);
                return true;
            }
            catch (DllNotFoundException)
            {
                // DLL não carregada ainda — fallback para outras camadas
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ─── Camadas 2-5: evidências no sistema ───────────────────────────────
        /// <summary>
        /// Verifica evidências do driver no sistema sem testar conexão direta.
        /// Cobre: registro, sc query, arquivos .sys.
        /// </summary>
        private static bool IsInterceptionKernelEvidencePresent()
        {
            // ── Camada 2: chaves de registro (nomes conhecidos) ────────────────
            string[] serviceKeys =
            {
                @"SYSTEM\CurrentControlSet\Services\keyboard_filter",
                @"SYSTEM\CurrentControlSet\Services\mouse_filter",
                @"SYSTEM\CurrentControlSet\Services\interception",
                @"SYSTEM\CurrentControlSet\Services\Interception",
                @"SYSTEM\ControlSet001\Services\interception",
                @"SYSTEM\ControlSet001\Services\Interception",
                @"SYSTEM\ControlSet001\Services\keyboard_filter",
                @"SYSTEM\ControlSet001\Services\mouse_filter",
                @"SYSTEM\ControlSet002\Services\interception",
                @"SYSTEM\ControlSet002\Services\Interception",
                @"SYSTEM\ControlSet002\Services\keyboard_filter",
                @"SYSTEM\ControlSet002\Services\mouse_filter",
            };

            foreach (var path in serviceKeys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path, false);
                    if (key != null) return true;
                }
                catch { }
            }

            // ── Camada 3: varredura de todos os serviços kernel via sc.exe ─────
            // Captura nomes de serviço não listados acima (versões customizadas)
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "sc.exe",
                    Arguments              = "query type= kernel state= all",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);

                    if (output.Contains("interception",   StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("keyboard_filter",StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("mouse_filter",   StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            // ── Camada 4: arquivos .sys em System32\drivers ────────────────────
            string sysDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");

            string[] sysFiles =
            {
                Path.Combine(sysDir, "interception.sys"),
                Path.Combine(sysDir, "keyboard_filter.sys"),
                Path.Combine(sysDir, "mouse_filter.sys"),
            };

            foreach (var sys in sysFiles)
                if (File.Exists(sys)) return true;

            // ── Camada 5: DLL na pasta do app (indica que o driver foi usado) ──
            return File.Exists(Path.Combine(AppContext.BaseDirectory, InterceptionDll));
        }

        // ─── Pending Reboot Check ─────────────────────────────────────────────
        /// <summary>
        /// Retorna true quando o Interception foi instalado na sessão atual mas
        /// ainda não foi ativado no kernel — ou seja, reboot é necessário.
        /// Usado para distinguir "não instalado" de "instalado, aguardando reboot".
        /// </summary>
        public static bool IsInterceptionPendingReboot()
        {
            // Se a flag está setada mas a instalação real ainda não foi detectada
            // pelo kernel, significa que o driver foi instalado e aguarda reboot.
            return HasInstalledFlag() && !IsInterceptionKernelEvidencePresent();
        }

        // ─── Full Status ──────────────────────────────────────────────────────
        public static DriverStatus GetStatus() => new()
        {
            ViGEmInstalled        = IsViGEmInstalled(),
            InterceptionInstalled = IsInterceptionInstalled()
        };
    }

    public class DriverStatus
    {
        public bool ViGEmInstalled        { get; set; }
        public bool InterceptionInstalled { get; set; }

        public bool AllDriversOk => ViGEmInstalled && InterceptionInstalled;

        public string ViGEmStatusText =>
            ViGEmInstalled ? "ViGEmBus: Instalado ✓" : "ViGEmBus: NÃO INSTALADO";

        public string InterceptionStatusText =>
            InterceptionInstalled ? "Interception: Instalado ✓" : "Interception: NÃO INSTALADO";
    }
}
