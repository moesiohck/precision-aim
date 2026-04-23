using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AimAssistPro.Services;

namespace AimAssistPro
{
    public partial class App : Application
    {
        public static ViGEmControllerService? ControllerService { get; private set; }
        public static GlobalInputHookService? InputHookService  { get; private set; }
        public static RecoilEngine?           RecoilEngine      { get; private set; }
        public static MacroEngine?            MacroEngine       { get; private set; }
        public static ProfileManager?         ProfileManager    { get; private set; }
        public static LicenseManager?         LicenseManager    { get; private set; }

        public static string CurrentAuthToken { get; private set; } = "";
        public static string CurrentUsername  { get; private set; } = "";
        public static string CurrentEmail     { get; private set; } = "";

        // HTTP client para o heartbeat de sessão (validação contínua)
        private static readonly System.Net.Http.HttpClient _heartbeatHttp = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        private System.Threading.Timer? _heartbeatTimer;

#if DEBUG
        // Log de erros — APENAS em Debug (nunca emite em Release para não revelar internos)
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "AimAssistPro_ERROR.txt");
#endif

        protected override async void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // ── CAMADA 1: Verificação de segurança (anti-debug / anti-tamper) ──
            // Deve ser a PRIMEIRA coisa que roda antes de qualquer janela abrir.
            // Em modo DEBUG não faz nada para não atrapalhar o desenvolvimento.
            if (!SecurityGuard.VerifyEnvironment())
            {
                Environment.FailFast(null);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                InterceptionService.EmergencyUnhook();
                string txt = ((Exception)ex.ExceptionObject).ToString();
#if DEBUG
                File.WriteAllText(LogPath, "[FATAL - AppDomain]\n" + txt);
                MessageBox.Show(
                    "Erro fatal:\n" + txt + "\n\nLog: " + LogPath,
                    "FATAL", MessageBoxButton.OK, MessageBoxImage.Error);
#else
                MessageBox.Show(
                    "Erro fatal do sistema. Por favor, me avise deste erro:\n\n" + ((Exception)ex.ExceptionObject).Message,
                    "Aim Assist - CRASH", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.FailFast(null);
#endif
            };

            DispatcherUnhandledException += (s, ex) =>
            {
                InterceptionService.EmergencyUnhook();
#if DEBUG
                File.AppendAllText(LogPath, "\n[DISPATCHER]\n" + ex.Exception.ToString());
                MessageBox.Show(
                    "Erro:\n" + ex.Exception.Message + "\n\nLog: " + LogPath,
                    "AIM ASSIST PRO", MessageBoxButton.OK, MessageBoxImage.Error);
#else
                MessageBox.Show(
                    "Erro na interface. Por favor, me avise deste erro:\n\n" + ex.Exception.Message,
                    "Aim Assist - CRASH DA INTERFACE", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                ex.Handled = true;
            };

            base.OnStartup(e);

            try
            {
                Log("=== INICIO ===");

                Log("1. LicenseManager");
                LicenseManager = new LicenseManager();

                Log("2. ProfileManager");
                ProfileManager = new ProfileManager();

                Log("3. RecoilEngine");
                RecoilEngine = new RecoilEngine();

                Log("4. MacroEngine");
                MacroEngine = new MacroEngine();

                // ── 5. START UP CHECKS E AUTO-LOGIN PARALELO ────────────────────────
                Log("5. Validations: Driver checks & Auto-Login");
                
                // Dispara a validação online em background (evita lentidão após a checagem local)
                string? savedToken = LicenseManager.LoadSession();
                var authTask = Task.Run(() => CheckAutoLoginInternal(savedToken));

                // Abre a janela de checagem dos drivers (espera terminar o processo visual)
                var check = new Views.StartupCheckWindow();
                bool? checkResult = check.ShowDialog();

                if (checkResult != true)
                {
                    Log("Cancelado pelo usuario na checagem de drivers.");
                    Shutdown();
                    return;
                }

                Log("6. ViGEmControllerService");
                ControllerService = new ViGEmControllerService();

                Log("7. GlobalInputHookService");
                InputHookService = new GlobalInputHookService(ControllerService, RecoilEngine, MacroEngine);

                Log("8. Sync mappings");
                ProfileManager.ProfileChanged += (s2, ev) =>
                    InputHookService.UpdateMappings(ProfileManager.GetCurrentMappings());
                InputHookService.UpdateMappings(ProfileManager.GetCurrentMappings());

                // ── RESOLVE AUTENTICAÇÃO (Já deve estar carregada) ───────────────
                Log("8b. Auth flow");
                
                string authToken      = "";
                string username       = "";
                string email          = "";
                string plan           = "";
                DateTime expiresAt    = DateTime.MinValue;
                bool   needsActivation = true;
                bool   authDone        = false;

                // Aguarda o auto-login caso o servidor demore mais que os 2 segundos da visualização dos drivers
                var tRes = authTask.GetAwaiter().GetResult();
                if (tRes != null)
                {
                    username = tRes.Username;
                    email    = tRes.Email;
                    authToken = tRes.AuthToken;
                    plan = tRes.Plan;
                    expiresAt = tRes.ExpiresAt;
                    needsActivation = tRes.NeedsActivation;
                    authDone = true;
                }

                // Loop: permite alternar entre Login e Cadastro caso Auto-Login não funcione
                while (!authDone)
                {
                    var loginWin = new Views.LoginWindow();
                    bool? loginResult = loginWin.ShowDialog();

                    if (loginWin.WantsRegister)
                    {
                        // Usuário quer criar conta → abre RegisterWindow
                        var regWin = new Views.RegisterWindow();
                        bool? regResult = regWin.ShowDialog();

                        if (regWin.WantsLogin)
                        {
                            // Voltou para o login → continua o loop
                            continue;
                        }

                        if (regResult != true || !regWin.RegisterSuccess)
                        {
                            Log("Cadastro cancelado.");
                            Shutdown();
                            return;
                        }

                        authToken       = regWin.AuthToken ?? "";
                        username        = regWin.Username ?? "";
                        needsActivation = true; // novo usuário sempre precisa ativar key
                        authDone        = true;
                    }
                    else if (loginResult == true && loginWin.LoginSuccess)
                    {
                        authToken       = loginWin.AuthToken ?? "";
                        username        = loginWin.Username ?? "";
                        email           = loginWin.Email ?? "";
                        plan            = loginWin.Plan ?? "";
                        expiresAt       = loginWin.ExpiresAt ?? DateTime.MinValue;
                        needsActivation = loginWin.NeedsActivation;
                        authDone        = true;
                    }
                    else
                    {
                        Log("Login cancelado.");
                        Shutdown();
                        return;
                    }
                }

                // ── ATIVAR KEY se usuario nao tem licenca ──────────────────────
                string? activatedKey = null;
                if (needsActivation)
                {
                    Log("8c. ActivateKeyWindow");
                    var activateWin = new Views.ActivateKeyWindow(authToken);
                    bool? activateResult = activateWin.ShowDialog();

                    if (activateResult != true || !activateWin.ActivationSuccess)
                    {
                        Log("Ativacao cancelada.");
                        Shutdown();
                        return;
                    }
                    
                    plan = activateWin.Plan ?? "Standard";
                    expiresAt = activateWin.ExpiresAt ?? DateTime.Now.AddDays(30);
                    activatedKey = activateWin.ActivatedKey;
                }

                CurrentAuthToken = authToken;
                CurrentUsername  = username;
                CurrentEmail     = email;
                
                ProfileManager?.ReloadForUser(username);
                
                // Salva a sessão na máquina para o próximo Auto-Login
                LicenseManager.SaveSession(authToken);

                LicenseManager?.SetOnlineLicense(username, plan, expiresAt, activatedKey);

                // ── Auto-update obrigatorio: roda ANTES do MainWindow ────────
                Log("9. UpdateService");
                var updateResult = await UpdateService.CheckForUpdatesAsync();

                // ShutdownRequired = usuario cancelou update obrigatorio OU download ok (installer ja rodando)
                if (updateResult == UpdateService.UpdateResult.ShutdownRequired)
                {
                    Log("Update: shutdown required, encerrando.");
                    Shutdown();
                    return;
                }

                Log("10. new MainWindow()");
                var mainWin = new MainWindow();
                MainWindow = mainWin;

                Log("11. mainWin.Show()");
                mainWin.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;

                // ── Heartbeat: valida a sessão a cada 20 min com a API ────────
                StartSessionHeartbeat(authToken);

                // ── Auto-update movido para antes do MainWindow ─────────────

                Log("=== SUCESSO ===");
            }
            catch (Exception ex)
            {
#if DEBUG
                File.AppendAllText(LogPath, "\n[CATCH]\n" + ex);
                MessageBox.Show(
                    "Erro:\n" + ex.GetType().Name + ": " + ex.Message + "\n\nLog em:\n" + LogPath,
                    "AIM ASSIST PRO - Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
#else
                File.WriteAllText(@"c:\Users\HCK PC\Downloads\aim assist\error.txt", ex.ToString());
                MessageBox.Show(
                    "Erro fatal de inicialização: " + ex.Message + "\n" + ex.StackTrace,
                    "Precision Aim Assist", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                Shutdown();
            }
        }

        private static void Log(string msg)
        {
#if DEBUG
            try { File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\n"); }
            catch { }
#else
            _ = msg;
#endif
        }

        // ── Heartbeat de sessão — valida o token com a API a cada 10 minutos ──
        private int _heartbeatFailCount = 0;
        private const int MaxHeartbeatFailures = 3; // 3 falhas consecutivas = encerra
        private void StartSessionHeartbeat(string token)
        {
            _heartbeatTimer = new System.Threading.Timer(
                async _ =>
                {
                    try
                    {
                        _heartbeatHttp.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                        var resp = await _heartbeatHttp.GetAsync(SecureConfig.EndpointMe);

                        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                            resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            // Sessão inválida / licença revogada — para o aim assist imediatamente
                            Dispatcher.Invoke(() =>
                            {
                                if (InputHookService?.IsActive == true)
                                    InputHookService.IsActive = false;  // desativa via property setter

                                LicenseManager.ClearSession();  // método estático

                                MessageBox.Show(
                                    "Sua sessão expirou ou foi revogada.\n" +
                                    "O aim assist foi desativado.\n\n" +
                                    "Por favor, faça login novamente.",
                                    "Precision Aim Assist — Sessão Expirada",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);

                                Shutdown();
                            });
                        }
                        // 200 OK — reseta o contador de falhas
                        _heartbeatFailCount = 0;
                    }
                    catch
                    {
                        // Falha de rede — incrementa contador
                        _heartbeatFailCount++;
                        Log($"Heartbeat: falha de conexão ({_heartbeatFailCount}/{MaxHeartbeatFailures}).");

                        // 3 falhas consecutivas = possível bloqueio intencional de API
                        if (_heartbeatFailCount >= MaxHeartbeatFailures)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (InputHookService?.IsActive == true)
                                    InputHookService.IsActive = false;

                                MessageBox.Show(
                                    "Não foi possível verificar sua licença após várias tentativas.\n" +
                                    "Verifique sua conexão com a internet e reinicie o aplicativo.",
                                    "Precision Aim Assist — Sem Conexão",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);

                                Shutdown();
                            });
                        }
                    }
                },
                state: null,
                dueTime: TimeSpan.FromMinutes(10),  // primeira checagem após 10 min
                period:  TimeSpan.FromMinutes(10))  // repete a cada 10 min
            ;
        }


        // ── Helper class & method for background auto-login ───────────────────────
        private class AuthResult
        {
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string AuthToken { get; set; } = "";
            public string Plan { get; set; } = "";
            public DateTime ExpiresAt { get; set; } = DateTime.MinValue;
            public bool NeedsActivation { get; set; } = true;
        }

        private AuthResult? CheckAutoLoginInternal(string? savedToken)
        {
            if (string.IsNullOrEmpty(savedToken)) return null;

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", savedToken);
                var response = http.GetAsync(SecureConfig.EndpointMe).GetAwaiter().GetResult();
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonString);
                    var userObj = doc.RootElement.GetProperty("user");
                    
                    var username = userObj.GetProperty("username").GetString() ?? "";
                    var userEmail = "";
                    if (userObj.TryGetProperty("email", out var emailProp) && emailProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        userEmail = emailProp.GetString() ?? "";
                    var plan = "";
                    var expiresAt = DateTime.MinValue;
                    var needsActivation = true;

                    var planProp = userObj.GetProperty("plan");
                    if (planProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        plan = planProp.GetString() ?? "";
                        var expProp = userObj.GetProperty("expiresAt");
                        if (expProp.ValueKind == System.Text.Json.JsonValueKind.String && DateTime.TryParse(expProp.GetString(), out var exp))
                        {
                            expiresAt = exp;
                            if (expiresAt > DateTime.Now) needsActivation = false;
                        }
                    }

                    // HWID mismatch: se o servidor tem HWID diferente do PC atual → reativar
                    if (userObj.TryGetProperty("hwid", out var hwidProp) 
                        && hwidProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var serverHwid = hwidProp.GetString() ?? "";
                        var localHwid = Services.LicenseManager.GetHardwareId();
                        if (!string.IsNullOrEmpty(serverHwid) && serverHwid != localHwid)
                            needsActivation = true;
                    }

                    return new AuthResult
                    {
                        Username = username,
                        Email = userEmail,
                        AuthToken = savedToken,
                        Plan = plan,
                        ExpiresAt = expiresAt,
                        NeedsActivation = needsActivation
                    };
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Token inválido ou expirado
                    Current.Dispatcher.Invoke(() => Services.LicenseManager.ClearSession());
                }
            }
            catch (Exception)
            {
                // Se falhou por falta de internet, cai no Login normal
            }

            return null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log("App encerrando.");
            _heartbeatTimer?.Dispose();          // Para o heartbeat
            SecurityGuard.Shutdown();             // Para o watchdog de segurança
            InputHookService?.Dispose();
            ControllerService?.Dispose();
            MacroEngine?.Dispose();
            base.OnExit(e);
        }
    }
}
