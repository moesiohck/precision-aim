using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AimAssistPro.Models;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace AimAssistPro.Views
{
    public partial class ControllerView : UserControl
    {
        // ─── State ──────────────────────────────────────────────────────────
        private bool _webReady     = false;
        private bool _unloaded     = false;
        private readonly Queue<string> _pending = new(); // messages queued before WebView ready

        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _keyFeedbackTimer;
        private string? _lastPressedButton;

        // ─── Constructor ────────────────────────────────────────────────────
        // Flag: WebView2 foi inicializado com sucesso pelo menos uma vez.
        // Impede que InitWebViewAsync seja chamado de novo a cada troca de aba.
        private bool _initialized = false;

        public ControllerView()
        {
            InitializeComponent();

            // Timer criado MAS NÃO iniciado aqui — só inicia quando a aba está visível (Loaded)
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _statusTimer.Tick += (_, _) => UpdateViGEmStatus();

            _keyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _keyFeedbackTimer.Tick += OnKeyFeedbackExpired;
        }

        // ─── Loaded / Unloaded ──────────────────────────────────────────────
        private async void ControllerView_Loaded(object sender, RoutedEventArgs e)
        {
            _unloaded = false;

            if (App.ProfileManager?.CurrentSettings != null && HotkeyLabel != null)
                HotkeyLabel.Text = App.ProfileManager.CurrentSettings.ToggleHotkey;

            await InitWebViewAsync();

            if (App.InputHookService != null)
                App.InputHookService.OnInputTriggered += OnHookKeyEvent;

            if (_webReady)
                Dispatcher.Invoke(SendAimAssistState);

            // Inicia o timer APENAS quando a aba está visível
            _statusTimer.Start();

            // Aguarda 1s antes de checar — evita flash durante carregamento inicial da aba
            await System.Threading.Tasks.Task.Delay(1000);
            if (!_unloaded) UpdateViGEmStatus();
        }

        private void ControllerView_Unloaded(object sender, RoutedEventArgs e)
        {
            _unloaded = true;
            _statusTimer.Stop();       // para o timer ao sair da aba
            _keyFeedbackTimer.Stop();

            if (App.InputHookService != null)
                App.InputHookService.OnInputTriggered -= OnHookKeyEvent;

            SaveCurrentMappings();
        }

        // ─── WebView2 init ───────────────────────────────────────────────────
        private async System.Threading.Tasks.Task InitWebViewAsync()
        {
            // ✔ Guard: não re-inicializa se já foi feito (ex: usuário voltou à aba)
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Cria o ambiente WebView2 uma única vez
                var udFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data");
                var env = await CoreWebView2Environment.CreateAsync(null, udFolder);
                await WebView.EnsureCoreWebView2Async(env);

                WebView.CoreWebView2.Settings.IsStatusBarEnabled           = false;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled           = false;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Fundo preto enquanto carrega — evita o flash branco/cinza
                WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x0A, 0x0A, 0x0A);

                // Recebe mensagens do JS
                WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Navega para o HTML do controle
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                               "Resources", "controller_remap.html");
                if (File.Exists(htmlPath))
                    WebView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                else
                    WebView.CoreWebView2.NavigateToString(FallbackHtml());
            }
            catch (Exception ex)
            {
                // Se falhar, reseta o flag para tentar novamente na próxima visita
                _initialized = false;
                System.Diagnostics.Debug.WriteLine($"[WebView2] Init error: {ex.Message}");
            }
        }

        // ─── WebView ready: send initial mappings ────────────────────────────
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var raw = e.TryGetWebMessageAsString();
            try
            {
                var msg = JsonConvert.DeserializeObject<dynamic>(raw);
                string? type = msg?.type?.ToString();

                switch (type)
                {
                    case "ready":
                        _webReady = true;
                        Dispatcher.Invoke(SendInitialMappings);
                        Dispatcher.Invoke(SendAimAssistState);
                        Dispatcher.Invoke(FlushPendingMessages);
                        break;

                    case "mappingChanged":
                        string? btn = msg?.button?.ToString();
                        var keysArray = msg?.keys;
                        List<string> keysList = new();
                        if (keysArray != null)
                        {
                            foreach (var k in keysArray) keysList.Add(k.ToString());
                        }
                        Dispatcher.Invoke(() => HandleMappingChanged(btn, keysList));
                        break;

                    case "keydown":
                        // JS notifying C# about a key press (informational)
                        break;
                }
            }
            catch { /* JSON parse error — ignore */ }
        }

        // ─── Send mappings to WebView ────────────────────────────────────────
        private void SendInitialMappings()
        {
            if (App.ProfileManager?.CurrentProfile == null) return;

            var mappingDict = new Dictionary<string, List<string>>();
            foreach (var m in App.ProfileManager.GetCurrentMappings())
            {
                var btnStr = m.TargetButton.ToString();
                if (!mappingDict.ContainsKey(btnStr))
                    mappingDict[btnStr] = new List<string>();
                mappingDict[btnStr].Add(m.InputKey);
            }

            var payload = JsonConvert.SerializeObject(new
            {
                type     = "setMappings",
                mappings = mappingDict
            });

            SendToWeb(payload);
        }

        // ─── Send aim assist state ───────────────────────────────────────────
        private void SendAimAssistState()
        {
            bool active = App.InputHookService?.IsActive ?? false;
            var payload = JsonConvert.SerializeObject(new
            {
                type   = "aimAssistState",
                active = active
            });
            SendToWeb(payload);
        }

        /// <summary>Called by MainWindow when user presses F8 to toggle aim assist.</summary>
        public void NotifyAimAssistToggle(bool isActive)
        {
            if (_unloaded) return;
            Dispatcher.Invoke(() =>
            {
                var payload = JsonConvert.SerializeObject(new
                {
                    type   = "aimAssistState",
                    active = isActive
                });
                SendToWeb(payload);
            });
        }

        private void SendToWeb(string json)
        {
            if (!_webReady)
            {
                _pending.Enqueue(json);
                return;
            }
            try
            {
                WebView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch { /* WebView disposed */ }
        }

        private void FlushPendingMessages()
        {
            while (_pending.TryDequeue(out var msg))
                SendToWeb(msg);
        }

        // ─── Handle remapping from JS ────────────────────────────────────────
        private void HandleMappingChanged(string? btnName, List<string> keys)
        {
            if (string.IsNullOrEmpty(btnName)) return;
            if (!Enum.TryParse<ControllerButton>(btnName, out var btn)) return;

            var profile = App.ProfileManager?.CurrentProfile;
            if (profile == null) return;

            // Remove old binding for this controller button
            profile.KeyMappings.RemoveAll(m => m.TargetButton == btn);

            if (keys != null)
            {
                foreach (var key in keys)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        profile.KeyMappings.Add(new KeyMapping
                        {
                            InputKey     = key,
                            TargetButton = btn,
                            IsActive     = true
                        });
                    }
                }
            }

            App.ProfileManager?.SaveProfile(profile);
            App.InputHookService?.UpdateMappings(profile.KeyMappings);
        }

        private void SaveCurrentMappings()
        {
            // Mappings are saved immediately on each change — nothing extra needed here
        }

        // ─── Hook key events → WebView visual feedback ───────────────────────
        private void OnHookKeyEvent(ControllerButton btn, bool pressed)
        {
            if (_unloaded) return;

            Dispatcher.Invoke(() =>
            {
                var payload = JsonConvert.SerializeObject(new
                {
                    type   = pressed ? "keydown" : "keyup",
                    button = btn.ToString()
                });

                // Use the 'highlight' path for direct button highlighting
                var hlPayload = JsonConvert.SerializeObject(new
                {
                    type   = "highlight",
                    button = btn.ToString(),
                    on     = pressed
                });
                SendToWeb(hlPayload);

                if (pressed)
                {
                    _lastPressedButton = btn.ToString();
                    _keyFeedbackTimer.Stop();
                    _keyFeedbackTimer.Start();
                }
            });
        }

        private void OnKeyFeedbackExpired(object? sender, EventArgs e)
        {
            _keyFeedbackTimer.Stop();
            if (_lastPressedButton != null)
            {
                var payload = JsonConvert.SerializeObject(new
                {
                    type   = "highlight",
                    button = _lastPressedButton,
                    on     = false
                });
                SendToWeb(payload);
                _lastPressedButton = null;
            }
        }

        // ─── ViGEm status banner ─────────────────────────────────────────────
        private int _vigemFailCount = 0;

        private void UpdateViGEmStatus()
        {
            if (_unloaded) return;

            bool ok = App.ControllerService?.IsConnected ?? false;

            if (ok)
            {
                _vigemFailCount = 0;
                ViGEmWarningBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                // So mostra apos 2 ticks sem conexao (~6s) — evita flash ao trocar aba
                _vigemFailCount++;
                if (_vigemFailCount >= 2)
                    ViGEmWarningBanner.Visibility = Visibility.Visible;
            }
        }

        // ─── Fallback HTML (if file not found) ───────────────────────────────
        private static string FallbackHtml() =>
            """
            <!DOCTYPE html><html><body style="background:#0A0A0A;color:#808080;
            font-family:monospace;display:flex;align-items:center;justify-content:center;
            height:100vh;font-size:14px;">
            <div>⚠ controller_remap.html não encontrado em Resources/</div>
            </body></html>
            """;
    }
}
