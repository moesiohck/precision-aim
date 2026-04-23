using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Proteção em runtime do Precision Aim Assist.
    ///
    /// CAMADAS DE PROTEÇÃO:
    ///   1. Anti-Debug   — detecta depuradores gerenciados e nativos
    ///   2. Anti-Tamper  — verifica integridade do executável em disco
    ///   3. Watchdog     — thread em background que re-verifica periodicamente
    ///
    /// COMPORTAMENTO AO DETECTAR AMEAÇA:
    ///   Encerra o processo silenciosamente (sem mensagem de erro reveladora).
    ///   Em modo DEBUG, as checagens são ignoradas para não atrapalhar o desenvolvimento.
    /// </summary>
    internal static class SecurityGuard
    {
        // ─── P/Invoke: Windows Security APIs ──────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        [DllImport("ntdll.dll", SetLastError = false)]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle, int processInformationClass,
            ref int processInformation, int processInformationLength, IntPtr returnLength);

        // Camada extra: NtQueryInformationProcess com ProcessDebugObjectHandle (classe 0x1E)
        // Bypassa patches do ScyllaHide no ProcessDebugPort
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle, int processInformationClass,
            ref IntPtr processInformation, int processInformationLength, IntPtr returnLength);

        // Oculta a thread atual do debugger (NtSetInformationThread HideThreadFromDebugger)
        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationThread(IntPtr threadHandle, int threadInformationClass,
            IntPtr threadInformation, int threadInformationLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        // ─── Estado ──────────────────────────────────────────────────────────
        private static Thread?   _watchdog     = null;
        private static string?   _expectedHash = null; // definido em VerifyEnvironment (Release)
        private static string?   _exePath      = null; // definido em VerifyEnvironment (Release)
        private static bool      _guardRunning = false;

        // ─── Verificação inicial (chamar em OnStartup ANTES de qualquer janela) ──
        /// <summary>
        /// Executa todas as verificações de segurança.
        /// Retorna true se o ambiente for seguro, false se ameaça detectada.
        /// Em modo DEBUG, sempre retorna true.
        /// </summary>
        public static bool VerifyEnvironment()
        {
#if DEBUG
            // Em desenvolvimento, não bloqueia
            return true;
#else
            if (IsBeingDebugged())
                return KillSilently();

            // Captura o hash do próprio exe para o watchdog monitorar
            _exePath    = Process.GetCurrentProcess().MainModule?.FileName;
            _expectedHash = _exePath != null ? ComputeFileHash(_exePath) : null;

            // Oculta a thread do watchdog de depuradores ANTES de qualquer checagem
            // HideThreadFromDebugger = classe 17. Depuradores não conseguem pausar essa thread.
            try { NtSetInformationThread(GetCurrentThread(), 17, IntPtr.Zero, 0); } catch { }

            StartWatchdog();
            return true;
#endif
        }

        // ─── Anti-Debug ──────────────────────────────────────────────────────
        private static bool IsBeingDebugged()
        {
            // Camada 1: Debugger gerenciado (.NET)
            if (Debugger.IsAttached)
                return true;

            // Camada 2: CheckRemoteDebuggerPresent
            bool remoteDebugger = false;
            try
            {
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref remoteDebugger);
                if (remoteDebugger) return true;
            }
            catch { }

            // Camada 3: IsDebuggerPresent nativo
            try { if (IsDebuggerPresent()) return true; }
            catch { }

            // Camada 4: ProcessDebugPort (classe 7)
            try
            {
                int debugPort = 0;
                int result = NtQueryInformationProcess(
                    Process.GetCurrentProcess().Handle, 7,
                    ref debugPort, sizeof(int), IntPtr.Zero);
                if (result == 0 && debugPort != 0) return true;
            }
            catch { }

            // Camada 5: ProcessDebugObjectHandle (0x1E) — bypassa ScyllaHide
            try
            {
                IntPtr debugObj = IntPtr.Zero;
                int r = NtQueryInformationProcess(
                    Process.GetCurrentProcess().Handle, 0x1E,
                    ref debugObj, IntPtr.Size, IntPtr.Zero);
                if (r == 0 && debugObj != IntPtr.Zero) return true;
            }
            catch { }

            // Camada 6: Processo pai — dnSpy/x64dbg lançam o app como filho
            if (IsLaunchedByDebugger()) return true;

            // Camada 7: Timing attack
            if (IsTimingAnomalyDetected()) return true;

            return false;
        }

        private static bool IsLaunchedByDebugger()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId={current.Id}");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    int parentPid = Convert.ToInt32(mo["ParentProcessId"]);
                    try
                    {
                        var parent = Process.GetProcessById(parentPid);
                        string parentName = parent.ProcessName.ToLowerInvariant();
                        var ok = new[]{ "explorer","cmd","powershell","pwsh","svchost",
                            "services","msiexec","setup","winlogon","userinit","taskmgr","conhost" };
                        bool isOk = false;
                        foreach (var o in ok) if (parentName.Contains(o)) { isOk = true; break; }
                        if (!isOk)
                            foreach (var s in _suspiciousProcessNames)
                                if (parentName.Contains(s)) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Checagem de timing: um loop simples deve executar em microsegundos.
        /// Quando há um breakpoint ativo ou single-step debugging, demora muito mais.
        /// </summary>
        private static bool IsTimingAnomalyDetected()
        {
            try
            {
                var sw = Stopwatch.StartNew();
                long dummy = 0;
                // Operação simples que o JIT não elimina (volatile-like via escrita)
                for (int i = 0; i < 10_000; i++)
                    dummy += i ^ (i << 1);
                sw.Stop();

                // Em execução normal: < 5ms. Com debugger ativo: pode ser segundos
                return sw.ElapsedMilliseconds > 500 && dummy > 0;
            }
            catch { return false; }
        }

        // ─── Anti-Tamper ─────────────────────────────────────────────────────
        /// <summary>
        /// Calcula o SHA256 do arquivo informado (o próprio .exe).
        /// </summary>
        private static string? ComputeFileHash(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hash = SHA256.HashData(stream);
                return Convert.ToHexString(hash);
            }
            catch { return null; }
        }

        /// <summary>
        /// Verifica se o executável em disco ainda tem o mesmo hash capturado no startup.
        /// Se foi patchado (cracked), o hash muda e o app fecha.
        /// </summary>
        private static bool IsExecutableTampered()
        {
            if (_exePath == null || _expectedHash == null)
                return false;

            try
            {
                string currentHash = ComputeFileHash(_exePath) ?? "";
                return !string.Equals(currentHash, _expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ─── Watchdog ─────────────────────────────────────────────────────────
        /// <summary>
        /// Thread em background que re-verifica a cada 45 segundos.
        /// Detecta se um patcher modificou o EXE enquanto o app estava rodando.
        /// </summary>
        private static void StartWatchdog()
        {
            if (_guardRunning) return;
            _guardRunning = true;

            _watchdog = new Thread(() =>
            {
                while (_guardRunning)
                {
                    // Intervalo aleatório entre 30-60s (dificulta análise de padrões)
                    var delay = 30_000 + new Random().Next(0, 30_000);
                    Thread.Sleep(delay);

                    if (!_guardRunning) break;

                    // Re-checa debugger
                    if (IsBeingDebugged())
                    {
                        KillSilently();
                        return;
                    }

                    // Re-checa integridade do executável
                    if (IsExecutableTampered())
                    {
                        KillSilently();
                        return;
                    }

                    // Anti-DLL Injection: verifica módulos carregados
                    if (HasSuspiciousModules())
                    {
                        KillSilently();
                        return;
                    }

                    // Anti-Analysis: verifica processos suspeitos
                    if (HasSuspiciousProcesses())
                    {
                        KillSilently();
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Priority     = ThreadPriority.BelowNormal,
                Name         = "SysMonitor" // nome genérico para não chamar atenção
            };
            _watchdog.Start();
        }

        /// <summary>
        /// Para o watchdog de forma limpa (chamar no OnExit).
        /// </summary>
        public static void Shutdown()
        {
            _guardRunning = false;
        }

        // ─── Encerramento silencioso ──────────────────────────────────────────
        // ─── Anti-DLL Injection ──────────────────────────────────────────────
        /// <summary>
        /// Verifica se algum módulo suspeito foi injetado no processo.
        /// Ferramentas de cheat/crack costumam injetar DLLs com nomes conhecidos.
        /// </summary>
        private static bool HasSuspiciousModules()
        {
            try
            {
                var modules = Process.GetCurrentProcess().Modules;
                foreach (ProcessModule mod in modules)
                {
                    string name = (mod.ModuleName ?? "").ToLowerInvariant();
                    // Nomes ESPECÍFICOS de injetores/cheat tools
                    // CUIDADO: NÃO usar termos genéricos que pegam DLLs legítimas do Windows
                    // (dbghelp.dll, webhooks.dll, etc. são legítimas)
                    if (name.Contains("easyhook") || name.Contains("minhook") ||
                        name.Contains("detours")  || name.Contains("titanhide") ||
                        name.Contains("sharphook") || name.Contains("cheatengine") ||
                        name.Contains("ce-inject") || name.Contains("trainerv") ||
                        name.Contains("speedhack"))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // ─── Anti-Analysis: processos de engenharia reversa ──────────────────
        private static readonly string[] _suspiciousProcessNames = {
            // Debuggers e disassemblers
            "dnspy", "ilspy", "dotpeek", "x64dbg", "x32dbg",
            "ollydbg", "ghidra", "ida64", "ida", "radare2",
            // Cheat engines
            "cheatengine", "cheat engine",
            // Process analysis
            "processhacker", "process hacker", "procmon", "procexp",
            // Network sniffers (para interceptar chamadas à API)
            "httpdebugger", "fiddler", "wireshark", "charles", "mitmproxy",
            // .NET deobfuscators/unpackers
            "de4dot", "megadumper", "extractor", "dotdumper", "netshrink",
            // Hex editors usados para patchear
            "hxd", "hexworkshop", "010editor",
            // Tools de bypass de keyauth (relevante ao cracker que contactou)
            "keyauthdumper", "keydumper", "keyextractor",
        };

        private static bool HasSuspiciousProcesses()
        {
            try
            {
                var running = Process.GetProcesses();
                foreach (var proc in running)
                {
                    string name = proc.ProcessName.ToLowerInvariant();
                    foreach (var suspicious in _suspiciousProcessNames)
                    {
                        if (name.Contains(suspicious))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ─── Encerramento silencioso ──────────────────────────────────────────
        /// <summary>
        /// Encerra o processo imediatamente e silenciosamente, sem revelar o motivo.
        /// O atacante vê o app fechar como se fosse um crash qualquer.
        /// </summary>
        private static bool KillSilently()
        {
            try
            {
                // Mata o processo sem passar pelo OnExit do WPF
                // (evita que o código de saída possa ser interceptado)
                Environment.FailFast(null);
            }
            catch
            {
                Process.GetCurrentProcess().Kill();
            }
            return false;
        }
    }
}
