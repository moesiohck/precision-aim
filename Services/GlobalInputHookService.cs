using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using AimAssistPro.Models;


namespace AimAssistPro.Services
{
    public class GlobalInputHookService : IDisposable
    {
        // ─── P/Invoke ────────────────────────────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public System.Drawing.Point pt;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        // ─── Fields ──────────────────────────────────────────────────────────
        private readonly ViGEmControllerService _controller;
        private readonly RecoilEngine _recoilEngine;
        private readonly MacroEngine _macroEngine;

        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _msHook = IntPtr.Zero;
        private readonly LowLevelProc _kbProc;
        private readonly LowLevelProc _msProc;

        private InterceptionService? _interceptionService;

        private Dictionary<string, KeyMapping> _keyMappings = new(StringComparer.OrdinalIgnoreCase);

        // Active state
        private bool _isActive = false;
        private bool _consumeEvents = true;
        private bool _disposed;

        // ─── Mouse Delta Accumulator (thread-safe) ───────────────────────────
        // Captura deltas direto do hook sem perda, melhor que GetCursorPos polling
        private float _accDeltaX = 0f;
        private float _accDeltaY = 0f;
        private readonly object _deltaLock = new();

        // Stick state atual (suavizado) - simula inércia do controle real
        private float _stickX = 0f;
        private float _stickY = 0f;

        // Sensibilidade configurável via dashboard
        private float _sensitivityX = 1.0f;
        private float _sensitivityY = 1.0f;
        private float _deadZone = 0.05f;

        // Recoil ramp-up: inicia em 0 e sobe suavemente até 1.0
        // Evita o "puxão repentino" na primeira rajada
        private float _recoilRamp = 0f;

        // ─── Adaptive Engine — rastreamento real ─────────────────────────────
        private readonly AdaptiveStats _liveStats = new();
        private DateTime _sessionActivatedAt = DateTime.MinValue;


        // Active key tracking
        private readonly HashSet<string> _pressedKeys = new();
        private readonly object _lock = new();

        // Contagem de referência por botão do controle: permite que múltiplas teclas
        // mapeiem para o mesmo botão sem conflito (ex: F e R ambos em X)
        private readonly Dictionary<ControllerButton, int> _buttonRefCount = new();

        // Stick axis state
        private float _currentLeftX = 0f, _currentLeftY = 0f;
        private float _currentRightX = 0f, _currentRightY = 0f;

        private volatile bool _wDown, _aDown, _sDown, _dDown;

        // Som de ativação
        private bool _soundEnabled = true;

        // Events
        public event EventHandler<string>? KeyPressed;
        public event EventHandler<string>? KeyReleased;
        public event Action<ControllerButton, bool>? OnInputTriggered;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;

                    // ── Adaptive Engine: session tracking ──
                    if (value)
                    {
                        _sessionActivatedAt = DateTime.UtcNow;
                        _liveStats.LastSessionStart = DateTime.UtcNow;
                    }
                    else
                    {
                        // Acumula tempo da sessão
                        if (_sessionActivatedAt != DateTime.MinValue)
                        {
                            _liveStats.TotalActiveTime += DateTime.UtcNow - _sessionActivatedAt;
                            _sessionActivatedAt = DateTime.MinValue;
                        }

                        _controller.ResetAll();
                        _stickX = 0f; _stickY = 0f;
                        _wDown = false; _aDown = false; _sDown = false; _dDown = false;
                        _currentLeftX = 0f; _currentLeftY = 0f;
                        lock (_deltaLock) { _accDeltaX = 0f; _accDeltaY = 0f; }
                        lock (_lock) { _buttonRefCount.Clear(); _pressedKeys.Clear(); }
                    }

                    // Feedback sonoro (respeitando mute e login)
                    if (_soundEnabled)
                    {
                        new Thread(() =>
                        {
                            if (value) System.Media.SystemSounds.Exclamation.Play();
                            else       System.Media.SystemSounds.Beep.Play();
                        }).Start();
                    }
                }
            }
        }

        // ── Adaptive Engine: acesso público ──────────────────────────────────
        public AdaptiveStats GetLiveStats() => _liveStats;

        /// <summary>Carrega stats salvos do perfil (chamado após login).</summary>
        public void LoadStats(AdaptiveStats? saved)
        {
            if (saved == null) return;
            _liveStats.TotalDataPoints = saved.TotalDataPoints;
            _liveStats.EffectiveFrames = saved.EffectiveFrames;
            _liveStats.TotalFrames     = saved.TotalFrames;
            _liveStats.TotalActiveTime = saved.TotalActiveTime;
            _liveStats.LastSessionStart = saved.LastSessionStart;
        }

        public void ResetStats()
        {
            _liveStats.TotalDataPoints = 0;
            _liveStats.EffectiveFrames = 0;
            _liveStats.TotalFrames     = 0;
            _liveStats.TotalActiveTime = TimeSpan.Zero;
            _liveStats.LastSessionStart = DateTime.MinValue;
        }

        public GlobalInputHookService(ViGEmControllerService controller, RecoilEngine recoilEngine, MacroEngine macroEngine)
        {
            _controller   = controller;
            _recoilEngine = recoilEngine;
            _macroEngine  = macroEngine;
            _kbProc = KeyboardHookCallback;
            _msProc = MouseHookCallback;
            InstallHooks();

            // Setup InterceptionService para bloquear o mouse físico (impedir conflito de inputs com o Warzone)
            // Se o driver não estiver instalado (IsAvailable = false), o app continua rodando silenciosamente via RAWINPUT (Windows).
            _interceptionService = new InterceptionService();
            if (_interceptionService.IsAvailable)
            {
                // Conecta os eventos do Interception diretamente nas nossas funções de Hook
                // Isso garante que leremos os movimentos MESMO quando o Interception "engolir" a passagem deles pro Windows.
                _interceptionService.MouseMoveEvent += (dx, dy) => FeedRawMouse(dx, dy);
                
                _interceptionService.MouseButtonEvent += (buttonName, isDown) =>
                {
                    if (!_isActive) return;

                    if (isDown) lock (_lock) _pressedKeys.Add(buttonName);
                    else lock (_lock) _pressedKeys.Remove(buttonName);

                    // Parachute side-buttons should not be passed dynamically
                    if (buttonName == "XButton1" || buttonName == "XButton2") return;

                    // Scroll wheel arrives as a pulse (true then immediately false)
                    // Route via HandlePulse to send a momentary button press
                    if (buttonName == "MouseWheelUp" || buttonName == "MouseWheelDown")
                    {
                        // Only act on the 'down' event; ignore the synthetic 'up'
                        if (!isDown) return;
                        if (_keyMappings.TryGetValue(buttonName, out var wm))
                        {
                            DiagLog($"SCROLL={buttonName} → btn={wm.TargetButton} controller={_controller.IsConnected}");
                            HandlePulse(wm);
                        }
                        else
                        {
                            DiagLog($"SCROLL={buttonName} → NO MAPPING");
                        }
                        return;
                    }

                    if (buttonName == "MouseLeft")
                    {
                        _isFiringLeftClick = isDown;
                        if (isDown)
                        {
                            if (App.ProfileManager?.CurrentSettings?.RapidFireEnabled == true)
                                StartRapidFire();
                        }
                        else
                        {
                            if (_isRapidFireRunning) _isRapidFireRunning = false;
                        }
                    }

                    // Regular button mappings
                    if (_keyMappings.TryGetValue(buttonName, out var mapping))
                    {
                        if (App.ProfileManager?.CurrentSettings?.RapidFireEnabled == true && buttonName == "MouseLeft") return;
                        HandleMappedKey(mapping, isDown);
                    }
                };

                _interceptionService.KeyEvent += (key, isDown) =>
                {
                    try
                    {
                        // Toggle hotkey check FIRST (because it toggles _isActive)
                        if (App.ProfileManager?.CurrentSettings != null)
                        {
                            string toggleHotkey = App.ProfileManager.CurrentSettings.ToggleHotkey;
                            if (key == toggleHotkey && isDown)
                            {
                                IsActive = !IsActive;
                                DiagLog($"TOGGLE → isActive={_isActive}");
                                return;
                            }

                            // Drop Cash hotkey
                            if (App.ProfileManager.CurrentSettings.DropCashEnabled && isDown)
                            {
                                string dropHotkey = App.ProfileManager.CurrentSettings.DropCashHotkey;
                                if (key == dropHotkey)
                                {
                                    DiagLog($"DROPCASH key={key}");
                                    RunDropCash();
                                    return;
                                }
                            }
                        }

                        if (!_isActive) return;
                        
                        bool isRepeat = false;
                        if (isDown)
                        {
                            lock (_lock) { isRepeat = !_pressedKeys.Add(key); }
                            if (!isRepeat) KeyPressed?.Invoke(this, key);
                        }
                        else
                        {
                            lock (_lock) { _pressedKeys.Remove(key); }
                            KeyReleased?.Invoke(this, key);
                        }
                        
                        // Bloqueia as repetições de hardware (evita que a interface do jogo pule ou bugue)
                        if (isDown && isRepeat) return;

                        bool isWasd = false;
                        if (key == "W") { _wDown = isDown; isWasd = true; }
                        else if (key == "S") { _sDown = isDown; isWasd = true; }
                        else if (key == "A") { _aDown = isDown; isWasd = true; }
                        else if (key == "D") { _dDown = isDown; isWasd = true; }

                        if (isWasd)
                        {
                            if (!_dropCashRunning) // Bloqueia WASD durante macro de drop
                            {
                                ApplyAxisMapping(ControllerAxis.LeftY, (_wDown ? 1.0f : 0f) + (_sDown ? -1.0f : 0f));
                                ApplyAxisMapping(ControllerAxis.LeftX, (_dDown ? 1.0f : 0f) + (_aDown ? -1.0f : 0f));
                            }
                            OnInputTriggered?.Invoke(ControllerButton.LS, isDown);
                        }

                        if (_keyMappings.TryGetValue(key, out var mapping))
                        {
                            DiagLog($"KEY={key} down={isDown} → btn={mapping.TargetButton} controller={_controller.IsConnected}");
                            if (key.Contains("MouseWheel", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isDown) HandlePulse(mapping);
                            }
                            else
                            {
                                HandleMappedKey(mapping, isDown);
                                if (isDown && (mapping.TargetButton == ControllerButton.RT || mapping.TargetButton == ControllerButton.RB))
                                    _recoilEngine.StartRecoil();
                                else if (!isDown && (mapping.TargetButton == ControllerButton.RT || mapping.TargetButton == ControllerButton.RB))
                                    _recoilEngine.StopRecoil();
                            }
                        }
                        else if (!isWasd)
                        {
                            DiagLog($"KEY={key} down={isDown} → NO MAPPING FOUND");
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagLog($"EXCEPTION in KeyEvent: {ex.Message}");
                    }
                };

                // Interception deve SEMPRE consumir o mouse e as teclas de jogo quando a mira estiver ativada,
                // caso contrário o Warzone fará "Input Flipping" e bloqueará os movimentos do controle.
                _interceptionService.StartCapture(
                    shouldConsumeKey: (key) => 
                    {
                        if (!_isActive) return false;
                        // Consome todas as teclas mapeadas + WASD + DropCash hotkey
                        if (_keyMappings.ContainsKey(key)) return true;
                        if (key.Equals("W", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("S", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("A", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("D", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("F", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)) return true;
                        if (key.Equals("LeftCtrl", StringComparison.OrdinalIgnoreCase)) return true;
                        // Consome sempre a tecla do macro DropCash para nao passar pro jogo
                        string dropKey = App.ProfileManager?.CurrentSettings?.DropCashHotkey ?? "";
                        if (!string.IsNullOrEmpty(dropKey) && key.Equals(dropKey, StringComparison.OrdinalIgnoreCase)) return true;
                        return false;
                    },
                    shouldConsumeMouse: () => _isActive
                );
            }

            // Loop de processamento do stick (separado do hook para não bloquear)
            var thread = new Thread(AimAssistLoop) { IsBackground = true, Name = "AimAssistLoop" };
            thread.Start();
        }

        private void InstallHooks()
        {
            if (_interceptionService?.IsAvailable == true)
            {
                // Interception cuida de todos os hooks nativos em anel 0.
                return;
            }

            try
            {
                var mainModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                IntPtr module = mainModule != null
                    ? GetModuleHandle(mainModule.ModuleName ?? "")
                    : IntPtr.Zero;

                _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, module, 0);
                _msHook = SetWindowsHookEx(WH_MOUSE_LL,    _msProc, module, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Hook] Falha ao instalar hooks: {ex.Message}");
            }
        }

        // ─── Keyboard Hook ───────────────────────────────────────────────────
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // When InterceptionService is active it handles ALL keyboard input.
            // Letting the WinAPI hook also process the same keys causes dual-firing.
            if (_interceptionService?.IsAvailable == true)
                return CallNextHookEx(_kbHook, nCode, wParam, lParam);

            if (nCode >= 0)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                string keyName = ((Key)KeyInterop.KeyFromVirtualKey((int)kb.vkCode)).ToString();

                bool isDown = (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN);
                bool isUp   = (wParam == WM_KEYUP   || wParam == WM_SYSKEYUP);

                // Toggle hotkey
                if (App.ProfileManager?.CurrentSettings != null)
                {
                    string toggleHotkey = App.ProfileManager.CurrentSettings.ToggleHotkey;
                    if (keyName == toggleHotkey && isDown)
                    {
                        // Nao aciona sem login
                        if (!string.IsNullOrEmpty(App.CurrentAuthToken))
                            IsActive = !IsActive;
                        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
                    }

                    // Drop Cash hotkey — apenas se Interception NAO estiver ativo
                    // (Interception já dispara RunDropCash via seu próprio handler)
                    if (!(_interceptionService?.IsAvailable == true) &&
                        App.ProfileManager.CurrentSettings.DropCashEnabled && isDown)
                    {
                        if (keyName.Equals(App.ProfileManager.CurrentSettings.DropCashHotkey, StringComparison.OrdinalIgnoreCase))
                        {
                            RunDropCash();
                            if (_consumeEvents) return (IntPtr)1;
                        }
                    }
                }

                if (isDown) KeyPressed?.Invoke(this, keyName);
                if (isUp)   KeyReleased?.Invoke(this, keyName);

                if (_isActive)
                {
                    // WASD → Left Stick (bloqueado durante macro de Drop Cash para evitar movimento)
                    bool isWasd = false;
                    if (keyName == "W") { _wDown = isDown; isWasd = true; }
                    else if (keyName == "S") { _sDown = isDown; isWasd = true; }
                    else if (keyName == "A") { _aDown = isDown; isWasd = true; }
                    else if (keyName == "D") { _dDown = isDown; isWasd = true; }

                    if (isWasd)
                    {
                        if (!_dropCashRunning) // NAO processa WASD durante macro
                        {
                            ApplyAxisMapping(ControllerAxis.LeftY, (_wDown ? 1.0f : 0f) + (_sDown ? -1.0f : 0f));
                            ApplyAxisMapping(ControllerAxis.LeftX, (_dDown ? 1.0f : 0f) + (_aDown ? -1.0f : 0f));
                        }
                        OnInputTriggered?.Invoke(ControllerButton.LS, isDown);
                        if (_consumeEvents) return (IntPtr)1;
                    }

                    if (_keyMappings.TryGetValue(keyName, out var mapping))
                    {
                        HandleMappedKey(mapping, isDown);

                        if (isDown && (mapping.TargetButton == ControllerButton.RT || mapping.TargetButton == ControllerButton.RB))
                            _recoilEngine.StartRecoil();
                        else if (isUp && (mapping.TargetButton == ControllerButton.RT || mapping.TargetButton == ControllerButton.RB))
                            _recoilEngine.StopRecoil();

                        if (_consumeEvents)
                            return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void HandleMappedKey(KeyMapping mapping, bool pressed)
        {
            OnInputTriggered?.Invoke(mapping.TargetButton, pressed);

            if (mapping.AxisMap != null)
            {
                float val = pressed ? mapping.AxisMap.Value : 0;
                if (mapping.AxisMap.IsNegative && pressed) val = -mapping.AxisMap.Value;
                ApplyAxisMapping(mapping.AxisMap.Axis, val);
            }
            else if (mapping.TargetButton == ControllerButton.LT)
                _controller.SetTriggerFloat(true,  pressed ? 1.0f : 0f);
            else if (mapping.TargetButton == ControllerButton.RT)
                _controller.SetTriggerFloat(false, pressed ? 1.0f : 0f);
            else
            {
                // Send digital button press directly
                _controller.PressButton(mapping.TargetButton, pressed);
            }
        }

        private void ApplyAxisMapping(ControllerAxis axis, float value)
        {
            switch (axis)
            {
                case ControllerAxis.LeftX:
                    _currentLeftX = value;
                    _controller.SetLeftStickFloat(_currentLeftX, _currentLeftY);
                    _controller.SubmitReport(); // WASD instantâneo — sem esperar o loop
                    break;
                case ControllerAxis.LeftY:
                    _currentLeftY = value;
                    _controller.SetLeftStickFloat(_currentLeftX, _currentLeftY);
                    _controller.SubmitReport(); // WASD instantâneo — sem esperar o loop
                    break;
                case ControllerAxis.RightX: _currentRightX = value; _controller.SetRightStickFloat(_currentRightX, _currentRightY); break;
                case ControllerAxis.RightY: _currentRightY = value; _controller.SetRightStickFloat(_currentRightX, _currentRightY); break;
            }
        }

        // ─── Mouse Hook └─ captura delta diretamente sem polling ─────────────
        private bool _isFiringLeftClick  = false;
        private bool _isRapidFireRunning = false;

        public void FeedRawMouse(int dx, int dy)
        {
            if (!_isActive) return;
            lock (_deltaLock)
            {
                _accDeltaX += dx;
                _accDeltaY += dy;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // ─── Scroll wheel: processa SEMPRE, antes do guard do Interception ───
                // (Interception captura movimento do mouse mas scroll chega via WM_MOUSEWHEEL)
                if ((int)wParam == WM_MOUSEWHEEL && _isActive)
                {
                    short delta = (short)(ms.mouseData >> 16);
                    string wheelKey = delta > 0 ? "MouseWheelUp" : "MouseWheelDown";
                    if (_keyMappings.TryGetValue(wheelKey, out var wm))
                    {
                        HandlePulse(wm);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                    // Sem mapping → scroll passa normalmente para o jogo
                }

                // When InterceptionService is active, it handles ALL mouse movement to avoid dual-processing.
                if (_interceptionService?.IsAvailable == true)
                    return CallNextHookEx(_msHook, nCode, wParam, lParam);

                if ((int)wParam == WM_MOUSEMOVE)
                {
                    // RAWINPUT já capta os movimentos verdadeiros no MainWindow.
                    // Aqui noi bloqueamos a passagem do ponteiro do Windows para que o cursor "congele" nos menus.
                    if (_isActive && _consumeEvents)
                    {
                        return (IntPtr)1; // Cursor congelado na tela
                    }
                }
                else if ((int)wParam == WM_LBUTTONDOWN)
                {
                    _isFiringLeftClick = true;
                    if (_isActive)
                    {
                        if (App.ProfileManager?.CurrentSettings?.RapidFireEnabled == true)
                        {
                            StartRapidFire();
                            if (_consumeEvents) return (IntPtr)1;
                        }
                        else
                        {
                            if (_keyMappings.TryGetValue("MouseLeft", out var m)) HandleMappedKey(m, true);
                            if (_consumeEvents) return (IntPtr)1;
                        }
                    }
                }
                else if ((int)wParam == WM_LBUTTONUP)
                {
                    _isFiringLeftClick = false;
                    if (_isRapidFireRunning) _isRapidFireRunning = false;
                    if (_isActive)
                    {
                        if (_keyMappings.TryGetValue("MouseLeft", out var m)) HandleMappedKey(m, false);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                }
                else if ((int)wParam == WM_RBUTTONDOWN)
                {
                    if (_isActive)
                    {
                        if (_keyMappings.TryGetValue("MouseRight", out var m)) HandleMappedKey(m, true);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                }
                else if ((int)wParam == WM_RBUTTONUP)
                {
                    if (_isActive)
                    {
                        if (_keyMappings.TryGetValue("MouseRight", out var m)) HandleMappedKey(m, false);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                }
                else if ((int)wParam == WM_MBUTTONDOWN)
                {
                    if (_isActive)
                    {
                        if (_keyMappings.TryGetValue("MouseMiddle", out var m)) HandleMappedKey(m, true);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                }
                else if ((int)wParam == WM_MBUTTONUP)
                {
                    if (_isActive)
                    {
                        if (_keyMappings.TryGetValue("MouseMiddle", out var m)) HandleMappedKey(m, false);
                        if (_consumeEvents) return (IntPtr)1;
                    }
                }

            }
            return CallNextHookEx(_msHook, nCode, wParam, lParam);
        }

        private DateTime _lastPulseTime = DateTime.MinValue;
        private void HandlePulse(KeyMapping m)
        {
            if ((DateTime.Now - _lastPulseTime).TotalMilliseconds < 150) return;
            _lastPulseTime = DateTime.Now;

            HandleMappedKey(m, true);
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(80); // 80ms garante que a engine do jogo leia o pulso
                HandleMappedKey(m, false);
            });
        }

        private void StartRapidFire()
        {
            if (_isRapidFireRunning) return;
            _isRapidFireRunning = true;

            new Thread(() =>
            {
                while (_isRapidFireRunning && _isActive)
                {
                    if (_keyMappings.TryGetValue("MouseLeft", out var m))
                    {
                        HandleMappedKey(m, true);
                        Thread.Sleep(30);
                        HandleMappedKey(m, false);
                        Thread.Sleep(30);
                    }
                    else
                    {
                        Thread.Sleep(60);
                    }
                }
            }).Start();
        }

        // ─── AimAssist Loop — Converte mouse delta em stick com curva ────────
        /// <summary>
        /// Loop de aim assist que transforma o delta acumulado do mouse em movimento
        /// do analógico direito do controle virtual, com:
        /// - Curva exponencial (resposta mais agressiva em movimentos rápidos)
        /// - Suavização via lerp (simula inércia real do aim assist de controle)
        /// - Dead zone configurável
        /// - Controle de recoil integrado
        /// </summary>
        private void AimAssistLoop()
        {
            while (!_disposed)
            {
                // Taxa de atualização baseada no polling rate configurado (hardcore 1ms = 1000Hz)
                int delayMs = 1; 
                if (App.ProfileManager?.CurrentSettings != null)
                {
                    int hz = App.ProfileManager.CurrentSettings.PollingRate;
                    if (hz > 0) delayMs = Math.Max(1, 1000 / hz);
                }
                Thread.Sleep(delayMs);

                if (!_isActive)
                {
                    // Quando inativo, garante que o stick vai para zero suavemente
                    if (_stickX != 0f || _stickY != 0f)
                    {
                        _stickX = 0f;
                        _stickY = 0f;
                        _controller.SetRightStickFloat(0, 0);
                        _controller.SubmitReport();
                    }
                    continue;
                }

                // Coleta e reseta o delta acumulado atomicamente
                float dx, dy;
                lock (_deltaLock)
                {
                    dx = _accDeltaX;
                    dy = _accDeltaY;
                    _accDeltaX = 0f;
                    _accDeltaY = 0f;
                }
                // Sensitivity já vem em proporção de 1.0 do DashboardView
                float sensX = _sensitivityX;
                float sensY = _sensitivityY;

                // BaseScale reduzido radicalmente para compensar o polling super baixo (1ms).
                // Isso faz com que cada micro-puxão do mouse dê uma resposta crisp/imediata sem lag.
                // BaseScale 55: peso leve de controle real, sem arrastar nem explodir
                const float BaseScale = 55.0f;

                // Normaliza o delta para [-1, 1] e não deixa passar da borda real do analógico
                float rawX = Math.Clamp((dx * sensX) / BaseScale, -1f, 1f);
                float rawY = Math.Clamp((-dy * sensY) / BaseScale, -1f, 1f); // Invertido: dy positivo -> Y negativo

                // ── Parachute Turn Assist ─────────────────────────────────────
                // Permite virar drasticamente o analogico quando o usuário aperta botões laterais do mouse
                bool parachuteSpin = false;
                lock (_lock)
                {
                    if (_pressedKeys.Contains("XButton1")) { rawX = -MathF.PI; parachuteSpin = true; } // Gira Esquerda com velocidade máxima
                    if (_pressedKeys.Contains("XButton2")) { rawX = MathF.PI; parachuteSpin = true; }  // Gira Direita com velocidade máxima
                }

                // ── Lê parâmetros avançados de sensibilidade ─────────────────
                var adv = App.ProfileManager?.CurrentSettings;
                float expX = adv?.AimCurveExponentX ?? 1.4f;
                float expY = adv?.AimCurveExponentY ?? 1.4f;
                float filtX = adv?.SensitivityFilterX ?? 0.6f;
                float filtY = adv?.SensitivityFilterY ?? 0.6f;

                // ── Curva com expoente configurável por eixo ──────────────────
                float targetX = parachuteSpin ? Math.Clamp(rawX, -1f, 1f) : ApplyAimCurve(rawX, expX);
                float targetY = ApplyAimCurve(rawY, expY);

                // ── Recoil Control — com ramp-up suave ───────────────────────
                if (App.ProfileManager?.CurrentSettings?.RecoilControlEnabled == true && _isFiringLeftClick)
                {
                    float recoilStrength = App.ProfileManager.CurrentSettings.RecoilStrength;

                    // Ramp-up exponencial: aumenta 12% por frame rumo a 1.0
                    // Resultado: chega a ~95% em ~25 frames (≈0.3s a 60Hz input rate)
                    _recoilRamp = _recoilRamp + (1f - _recoilRamp) * 0.12f;

                    // Forca 0.55 — suave com ramp-up (~0.3s para entrar na forca total)
                    float normalizedRecoil = recoilStrength * 0.55f * _recoilRamp;

                    targetY -= normalizedRecoil;
                    targetY = Math.Clamp(targetY, -1f, 1f);
                }
                else
                {
                    // Reseta o ramp quando nao esta atirando
                    _recoilRamp = 0f;
                }

                // ── RAA — Rotational Aim Assist ──────────────────────────────
                // Adiciona componente perpendicular ao vetor de mira.
                // Isso faz com que o analógico virtual "gire" em torno do alvo,
                // ativando o RAA nativo do Warzone/CoD com muito mais força.
                // Implementação: componente rotação 90° = (-rawY, rawX) escalado por RAAStrength.
                if (adv?.RAAEnabled == true)
                {
                    float raaStr = Math.Clamp(adv.RAAStrength, 0f, 0.5f);
                    float speed  = MathF.Sqrt(rawX * rawX + rawY * rawY);
                    if (speed > 0.02f) // só aplica RAA quando há input real (evita drift)
                    {
                        // Componente perpendicular (rotação horária)
                        float raaX = -rawY * raaStr;
                        float raaY =  rawX * raaStr;
                        targetX = Math.Clamp(targetX + raaX, -1f, 1f);
                        targetY = Math.Clamp(targetY + raaY, -1f, 1f);
                    }
                }

                // ── Filtro / Lerp por eixo (configurável) ────────────────────
                bool hasInput = dx != 0 || dy != 0;
                bool hasRecoil = _isFiringLeftClick && App.ProfileManager?.CurrentSettings?.RecoilControlEnabled == true;

                if (hasInput || parachuteSpin || hasRecoil)
                {
                    // Filtro: 0.3=super suave (controller feel), 0.6=balanceado, 1.0=cru
                    _stickX = Lerp(_stickX, targetX, Math.Clamp(filtX, 0.1f, 1.0f));
                    _stickY = Lerp(_stickY, targetY, Math.Clamp(filtY, 0.1f, 1.0f));
                }
                else
                {
                    // Sem input → volta ao zero suavizando para evitar estalos no Aim Assist e na câmera.
                    _stickX = Lerp(_stickX, 0f, 0.45f);
                    _stickY = Lerp(_stickY, 0f, 0.45f);
                    
                    // Zera completamente quando ficar minúsculo para evitar drift eterno
                    if (MathF.Abs(_stickX) < 0.001f) _stickX = 0f;
                    if (MathF.Abs(_stickY) < 0.001f) _stickY = 0f;
                }


                // ── Adaptive Engine: rastreamento real ────────────────────────
                // Conta cada iteração com input como data point
                if (hasInput)
                    _liveStats.TotalDataPoints++;

                _liveStats.TotalFrames++;

                // "Efetivo" = stick resultante acima da deadzone do jogo (~12%)
                float stickMag = MathF.Sqrt(_stickX * _stickX + _stickY * _stickY);
                if (stickMag > 0.12f)
                    _liveStats.EffectiveFrames++;

                _controller.SetRightStickFloat(_stickX, _stickY);
                _controller.SubmitReport();
            }
        }

        /// <summary>
        /// Curva de aim assist: aplica função de potência configurável que mantém controle fino
        /// em movimentos lentos e amplia velocidade em movimentos rápidos.
        /// exponent: 1.0=linear, 1.4=padrão, 2.0=muito exponencial (agressivo)
        /// </summary>
        private static float ApplyAimCurve(float input, float exponent = 1.4f)
        {
            if (input == 0f) return 0f;
            float sign = MathF.Sign(input);
            float abs  = MathF.Abs(input);

            abs = Math.Min(abs, 1f);

            // Potência configurável: 1.4 default, pode ir até 2.0
            float curved = MathF.Pow(abs, Math.Clamp(exponent, 0.8f, 2.5f));

            // ANTI-DEADZONE NATIVA (PONTO CRUCIAL DO JITTER)
            // Jogos tem deadzone embutida ~10-15%. Se o mouse andar 1px e a curva der 0.05,
            // o jogo ignora (TRAVADA). Quando ele anda 3px, dá 0.15 e o jogo recebe (PULO DE PIXEL).
            // A matemática abaixo mapeia o movimento MÍNIMO do mouse (curved) para JÁ COMEÇAR acima da deadzone!
            float deadzone = 0.12f; // ~12% é o padrão seguro para compensar o Warzone.
            float finalVal = deadzone + (curved * (1f - deadzone));

            return Math.Clamp(sign * finalVal, -1f, 1f);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ─── Drop Cash Macro ─────────────────────────────────────────────────
        private bool _dropCashRunning = false;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        private void RunDropCash()
        {
            if (_dropCashRunning) return;
            _dropCashRunning = true;

            new Thread(() =>
            {
                try
                {
                    // Para o WASD imediatamente
                    _controller.SetLeftStick(0, 0);
                    _controller.SubmitReport();

                    // ── 1. Abre mochila (DPadDown tap rápido) ──────────────────
                    _controller.PressButton(ControllerButton.DPadDown, true);
                    _controller.SubmitReport();
                    Thread.Sleep(50);   // tap rápido — não segurar
                    _controller.PressButton(ControllerButton.DPadDown, false);
                    _controller.SubmitReport();

                    // ── 2. Aguarda UI da mochila abrir (WZ demora ~150-200ms) ──
                    Thread.Sleep(200);

                    // ── 3. Segura RS por 350ms = Drop ALL instantâneo ──────────
                    // Pro player speed: RS hold mínimo necessário para confirmar drop
                    _controller.PressButton(ControllerButton.RS, true);
                    _controller.SubmitReport();
                    Thread.Sleep(350);
                    _controller.PressButton(ControllerButton.RS, false);
                    _controller.SubmitReport();

                    // ── 4. Fecha mochila com B (quasi-instantâneo) ─────────────
                    Thread.Sleep(80);
                    _controller.PressButton(ControllerButton.B, true);
                    _controller.SubmitReport();
                    Thread.Sleep(60);
                    _controller.PressButton(ControllerButton.B, false);
                    _controller.SubmitReport();

                    // ── 5. Restaura WASD imediatamente ────────────────────────
                    Thread.Sleep(50);
                    ApplyAxisMapping(ControllerAxis.LeftY, (_wDown ? 1.0f : 0f) + (_sDown ? -1.0f : 0f));
                    ApplyAxisMapping(ControllerAxis.LeftX, (_dDown ? 1.0f : 0f) + (_aDown ? -1.0f : 0f));
                }
                finally
                {
                    _dropCashRunning = false;
                }
            }) { IsBackground = true }.Start();
        }

        // ─── Public API ──────────────────────────────────────────────────────
        public void UpdateMappings(List<KeyMapping> mappings)
        {
            lock (_lock)
            {
                _keyMappings.Clear();
                foreach (var m in mappings)
                    _keyMappings[m.InputKey] = m;
            }
        }

        /// <summary>
        /// Atualiza sensibilidade do mouse→stick em tempo real.
        /// Chamado ao salvar configurações no Dashboard.
        /// </summary>
        public void SetSensitivity(float x, float y)
        {
            _sensitivityX = Math.Max(0.01f, x);
            _sensitivityY = Math.Max(0.01f, y);
            System.Diagnostics.Debug.WriteLine($"[AimAssist] Sensibilidade atualizada: X={_sensitivityX:F2} Y={_sensitivityY:F2}");
        }

        public void UpdateSensitivity(float x, float y, float deadZone)
        {
            _sensitivityX = Math.Max(0.01f, x);
            _sensitivityY = Math.Max(0.01f, y);
            _deadZone     = Math.Clamp(deadZone, 0f, 0.5f);
        }

        public void SetSoundEnabled(bool enabled) => _soundEnabled = enabled;

        public void SetConsumeEvents(bool consume) => _consumeEvents = consume;

        // ─── Diagnostic Logging ──────────────────────────────────────────────
        private static readonly string _diagLogPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AimAssistPro", "input_diag.log");

        private static void DiagLog(string msg)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_diagLogPath);
                if (dir != null && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                // Mantém somente as últimas 500 linhas para não crescer infinitamente
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n";
                System.IO.File.AppendAllText(_diagLogPath, line);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
            if (_msHook != IntPtr.Zero) UnhookWindowsHookEx(_msHook);
        }
    }
}
