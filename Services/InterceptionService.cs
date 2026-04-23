using System;
using System.Runtime.InteropServices;
using System.Threading;
using AimAssistPro.Models;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Wrapper for the Interception kernel-mode driver (oblitum/interception).
    /// Falls back to GlobalInputHookService if Interception is not installed.
    /// 
    /// REQUIRES: interception.dll in same directory as executable
    ///           AND driver installed via: install-interception.exe /install
    /// 
    /// Download: https://github.com/oblitum/Interception/releases
    /// </summary>
    public class InterceptionService : IDisposable
    {
        // ─── Interception DLL P/Invoke ────────────────────────────────────────
        private const string DLL = "interception.dll";

        // Device type constants
        private const int INTERCEPTION_MAX_DEVICE      = 20;
        private const int INTERCEPTION_MAX_KEYBOARD    = 10;
        private const int INTERCEPTION_MAX_MOUSE       = 10;
        private const int INTERCEPTION_MOUSE_BASE_INDEX    = 11;
        private const int INTERCEPTION_KEYBOARD_BASE_INDEX = 1;

        // Filter flags
        private const ushort INTERCEPTION_FILTER_MOUSE_ALL    = 0xFFFF;
        private const ushort INTERCEPTION_FILTER_KEY_ALL      = 0xFFFF;
        private const ushort INTERCEPTION_FILTER_KEY_DOWN     = 0x01;
        private const ushort INTERCEPTION_FILTER_KEY_UP       = 0x02;
        private const ushort INTERCEPTION_FILTER_KEY_NONE     = 0x00;
        private const ushort INTERCEPTION_FILTER_MOUSE_NONE   = 0x00;

        // Key state flags
        private const ushort INTERCEPTION_KEY_DOWN  = 0x00;
        private const ushort INTERCEPTION_KEY_UP    = 0x01;
        private const ushort INTERCEPTION_KEY_E0    = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        public struct InterceptionKeyStroke
        {
            public ushort code;
            public ushort state;
            public uint information;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InterceptionMouseStroke
        {
            public ushort state;
            public ushort flags;
            public short rolling;
            public int x;
            public int y;
            public uint information;
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InterceptionPredicate(int device);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_wait(IntPtr context);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_receive(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_receive")]
        private static extern int interception_receive_mouse(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
        private static extern int interception_send_mouse(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_keyboard(int device);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_mouse(int device);

        // ─── State ────────────────────────────────────────────────────────────
        private static IntPtr _context = IntPtr.Zero;
        private Thread? _captureThread;
        private bool _disposed;
        private bool _isAvailable;

        public bool IsAvailable => _isAvailable;

        // Events (same interface as GlobalInputHookService for drop-in use)
        public event Action<string, bool>? KeyEvent;     // (keyName, isDown)
        public event Action<int, int>? MouseMoveEvent;   // (deltaX, deltaY)
        public event Action<string, bool>? MouseButtonEvent; // (button, isDown)

        // ─── Init ─────────────────────────────────────────────────────────────
        public InterceptionService()
        {
            _isAvailable = TryInitialize();
        }

        private InterceptionPredicate? _isKeyboardPredicate;
        private InterceptionPredicate? _isMousePredicate;

        private int IsKeyboard(int device) => interception_is_keyboard(device);
        private int IsMouse(int device) => interception_is_mouse(device);

        private bool TryInitialize()
        {
            try
            {
                _context = interception_create_context();
                if (_context == IntPtr.Zero) return false;

                // Bind delegates to prevent GC collection during unmanaged call execution
                _isKeyboardPredicate = IsKeyboard;
                _isMousePredicate = IsMouse;

                // Filter all keyboard and mouse events
                interception_set_filter(_context, _isKeyboardPredicate, INTERCEPTION_FILTER_KEY_ALL);
                interception_set_filter(_context, _isMousePredicate, INTERCEPTION_FILTER_MOUSE_ALL);

                System.Diagnostics.Debug.WriteLine("[Interception] Driver initialized successfully.");
                return true;
            }
            catch (DllNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine("[Interception] interception.dll not found. Using WH hooks fallback.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Interception] Init failed: {ex.Message}");
                return false;
            }
        }

        public static void EmergencyUnhook()
        {
            try
            {
                if (_context != IntPtr.Zero)
                {
                    interception_destroy_context(_context);
                    _context = IntPtr.Zero;
                }
            }
            catch { }
        }

        /// <summary>
        /// Verifies the Interception driver is installed at kernel level 
        /// by checking the service registry key.
        /// </summary>
        public static bool IsDriverInstalled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\keyboard_filter",
                    false);
                return key != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Starts capturing inputs and forwarding/blocking based on active state.
        /// </summary>
        // ─── Event Queue para desacoplar receive de processamento ────────────
        private record struct InputEvent(bool IsKeyboard, InterceptionKeyStroke Key, InterceptionMouseStroke Mouse, int Device);
        private readonly System.Collections.Concurrent.ConcurrentQueue<InputEvent> _eventQueue = new();

        public void StartCapture(Func<string, bool> shouldConsumeKey, Func<bool> shouldConsumeMouse)
        {
            if (!_isAvailable) return;

            // Thread de RECEBIMENTO — só coloca na fila, não processa nada
            // Roda em AboveNormal para nunca perder evento do kernel
            var receiveThread = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        int device = interception_wait(_context);
                        if (device <= 0 || _disposed) break;

                        if (interception_is_keyboard(device) > 0)
                        {
                            var stroke = new InterceptionKeyStroke();
                            if (interception_receive(_context, device, ref stroke, 1) > 0)
                                _eventQueue.Enqueue(new InputEvent(true, stroke, default, device));
                        }
                        else if (interception_is_mouse(device) > 0)
                        {
                            var stroke = new InterceptionMouseStroke();
                            if (interception_receive_mouse(_context, device, ref stroke, 1) > 0)
                                _eventQueue.Enqueue(new InputEvent(false, default, stroke, device));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Interception Receive] {ex.Message}");
                    }
                }
            })
            { IsBackground = true, Name = "InterceptionReceive", Priority = ThreadPriority.AboveNormal };
            receiveThread.Start();

            // Thread de PROCESSAMENTO — consome a fila e despacha eventos
            // Prioridade Normal para não competir com o receive
            _captureThread = new Thread(() =>
            {
                while (!_disposed)
                {
                    while (_eventQueue.TryDequeue(out var evt))
                    {
                        try
                        {
                            if (evt.IsKeyboard)
                            {
                                var stroke = evt.Key;
                                bool isDown = (stroke.state & INTERCEPTION_KEY_UP) == 0;
                                string key = ScanCodeToKey(stroke.code, stroke.state);

                                if (!string.IsNullOrEmpty(key))
                                    KeyEvent?.Invoke(key, isDown);

                                if (string.IsNullOrEmpty(key) || !shouldConsumeKey(key))
                                    interception_send(_context, evt.Device, ref stroke, 1);
                            }
                            else
                            {
                                var stroke = evt.Mouse;
                                bool consumeMouse = shouldConsumeMouse();

                                if (stroke.x != 0 || stroke.y != 0)
                                    MouseMoveEvent?.Invoke(stroke.x, stroke.y);

                                HandleMouseButtons(stroke);

                                if (!consumeMouse)
                                    interception_send_mouse(_context, evt.Device, ref stroke, 1);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Interception Process] {ex.Message}");
                        }
                    }
                    Thread.Sleep(0); // yield para não queimar CPU
                }
            })
            { IsBackground = true, Name = "InterceptionProcess", Priority = ThreadPriority.Normal };
            _captureThread.Start();
        }

        private void HandleMouseButtons(InterceptionMouseStroke stroke)
        {
            // ── Scroll wheel (rolling > 0 = up, < 0 = down) ──────────────────
            const ushort INTERCEPTION_MOUSE_WHEEL = 0x0400;
            if ((stroke.state & INTERCEPTION_MOUSE_WHEEL) != 0 && stroke.rolling != 0)
            {
                string wheelKey = stroke.rolling > 0 ? "MouseWheelUp" : "MouseWheelDown";
                MouseButtonEvent?.Invoke(wheelKey, true);
                // Pulse: fire release immediately (scroll is an instant event)
                MouseButtonEvent?.Invoke(wheelKey, false);
            }

            const ushort LEFT_DOWN  = 0x0001;
            const ushort LEFT_UP    = 0x0002;
            const ushort RIGHT_DOWN = 0x0004;
            const ushort RIGHT_UP   = 0x0008;
            const ushort MIDDLE_DOWN = 0x0010;
            const ushort MIDDLE_UP   = 0x0020;
            const ushort X1_DOWN    = 0x0040;
            const ushort X1_UP      = 0x0080;
            const ushort X2_DOWN    = 0x0100;
            const ushort X2_UP      = 0x0200;

            if ((stroke.state & LEFT_DOWN)   != 0) MouseButtonEvent?.Invoke("MouseLeft",   true);
            if ((stroke.state & LEFT_UP)     != 0) MouseButtonEvent?.Invoke("MouseLeft",   false);
            if ((stroke.state & RIGHT_DOWN)  != 0) MouseButtonEvent?.Invoke("MouseRight",  true);
            if ((stroke.state & RIGHT_UP)    != 0) MouseButtonEvent?.Invoke("MouseRight",  false);
            if ((stroke.state & MIDDLE_DOWN) != 0) MouseButtonEvent?.Invoke("MouseMiddle", true);
            if ((stroke.state & MIDDLE_UP)   != 0) MouseButtonEvent?.Invoke("MouseMiddle", false);
            if ((stroke.state & X1_DOWN)     != 0) MouseButtonEvent?.Invoke("XButton1",    true);
            if ((stroke.state & X1_UP)       != 0) MouseButtonEvent?.Invoke("XButton1",    false);
            if ((stroke.state & X2_DOWN)     != 0) MouseButtonEvent?.Invoke("XButton2",    true);
            if ((stroke.state & X2_UP)       != 0) MouseButtonEvent?.Invoke("XButton2",    false);
        }

        // ─── Scan code to key name ────────────────────────────────────────────
        // Common PC/AT Scan Code Set 1 mapping
        private static string ScanCodeToKey(ushort code, ushort state)
        {
            bool extended = (state & INTERCEPTION_KEY_E0) != 0;

            return (code, extended) switch
            {
                (0x1E, _) => "A", (0x30, _) => "B", (0x2E, _) => "C",
                (0x20, _) => "D", (0x12, _) => "E", (0x21, _) => "F",
                (0x22, _) => "G", (0x23, _) => "H", (0x17, _) => "I",
                (0x24, _) => "J", (0x25, _) => "K", (0x26, _) => "L",
                (0x32, _) => "M", (0x31, _) => "N", (0x18, _) => "O",
                (0x19, _) => "P", (0x10, _) => "Q", (0x13, _) => "R",
                (0x1F, _) => "S", (0x14, _) => "T", (0x16, _) => "U",
                (0x2F, _) => "V", (0x11, _) => "W", (0x2D, _) => "X",
                (0x15, _) => "Y", (0x2C, _) => "Z",

                (0x02, _) => "D1", (0x03, _) => "D2", (0x04, _) => "D3",
                (0x05, _) => "D4", (0x06, _) => "D5", (0x07, _) => "D6",
                (0x08, _) => "D7", (0x09, _) => "D8", (0x0A, _) => "D9",
                (0x0B, _) => "D0",

                (0x3B, _) => "F1",  (0x3C, _) => "F2",  (0x3D, _) => "F3",
                (0x3E, _) => "F4",  (0x3F, _) => "F5",  (0x40, _) => "F6",
                (0x41, _) => "F7",  (0x42, _) => "F8",  (0x43, _) => "F9",
                (0x44, _) => "F10", (0x57, _) => "F11", (0x58, _) => "F12",

                (0x39, _) => "Space",
                (0x1C, false) => "Return",
                (0x01, _) => "Escape",
                (0x0F, _) => "Tab",
                (0x0E, _) => "Back",
                (0x3A, _) => "CapsLock",

                (0x2A, _) => "LeftShift",
                (0x36, _) => "RightShift",
                (0x1D, false) => "LeftCtrl",
                (0x1D, true)  => "RightCtrl",
                (0x38, false) => "LeftAlt",
                (0x38, true)  => "RightAlt",
                (0x5B, true)  => "LWin",

                (0x4B, true) => "Left",
                (0x48, true) => "Up",
                (0x4D, true) => "Right",
                (0x50, true) => "Down",

                (0x52, true) => "Insert",
                (0x53, true) => "Delete",
                (0x49, true) => "Prior",   // PageUp
                (0x51, true) => "Next",    // PageDown
                (0x47, true) => "Home",
                (0x4F, true) => "End",

                _ => $"SC{code:X2}"
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_context != IntPtr.Zero)
            {
                interception_destroy_context(_context);
                _context = IntPtr.Zero;
            }
        }
    }
}
