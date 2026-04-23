using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AimAssistPro.Models;

namespace AimAssistPro.Services
{
    public class MacroEngine : IDisposable
    {
        // ─── Win32 SendInput ─────────────────────────────────────────────────
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(4)] public KEYBDINPUT ki;
        }

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        private static extern ushort VkKeyScan(char ch);

        // ─── State ───────────────────────────────────────────────────────────
        private readonly Dictionary<string, CancellationTokenSource> _runningMacros = new();
        private readonly object _lock = new();
        private bool _disposed;

        public event EventHandler<string>? MacroStarted;
        public event EventHandler<string>? MacroStopped;

        // ─── Execute Macro ─────────────────────────────────────────────────
        public void ExecuteMacro(Macro macro)
        {
            lock (_lock)
            {
                if (_runningMacros.ContainsKey(macro.Id))
                {
                    StopMacro(macro.Id);
                    return;
                }

                var cts = new CancellationTokenSource();
                _runningMacros[macro.Id] = cts;
                MacroStarted?.Invoke(this, macro.Id);

                var thread = new Thread(() =>
                {
                    try { RunMacro(macro, cts.Token); }
                    finally
                    {
                        lock (_lock) _runningMacros.Remove(macro.Id);
                        MacroStopped?.Invoke(this, macro.Id);
                    }
                })
                { IsBackground = true };
                thread.Start();
            }
        }

        private void RunMacro(Macro macro, CancellationToken token)
        {
            int repeats = macro.IsLoop ? int.MaxValue : macro.RepeatCount;

            for (int i = 0; i < repeats && !token.IsCancellationRequested; i++)
            {
                foreach (var action in macro.Actions)
                {
                    if (token.IsCancellationRequested) break;
                    ExecuteAction(action);
                    if (action.DelayMs > 0)
                        Thread.Sleep(action.DelayMs);
                }
            }
        }

        private void ExecuteAction(MacroAction action)
        {
            switch (action.Type)
            {
                case MacroActionType.KeyPress:
                    PressKey(action.Key);
                    Thread.Sleep(action.DelayMs > 0 ? action.DelayMs : 30);
                    ReleaseKey(action.Key);
                    break;
                case MacroActionType.KeyDown:
                    PressKey(action.Key);
                    break;
                case MacroActionType.KeyUp:
                    ReleaseKey(action.Key);
                    break;
                case MacroActionType.Delay:
                    Thread.Sleep(action.DelayMs);
                    break;
            }
        }

        public void StopMacro(string macroId)
        {
            lock (_lock)
            {
                if (_runningMacros.TryGetValue(macroId, out var cts))
                {
                    cts.Cancel();
                    _runningMacros.Remove(macroId);
                }
            }
        }

        public bool IsMacroRunning(string macroId)
        {
            lock (_lock) return _runningMacros.ContainsKey(macroId);
        }

        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var cts in _runningMacros.Values)
                    cts.Cancel();
                _runningMacros.Clear();
            }
        }

        // ─── Key Input Helpers ───────────────────────────────────────────────
        private void PressKey(string keyName)
        {
            ushort vk = GetVkCode(keyName);
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT { wVk = vk }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private void ReleaseKey(string keyName)
        {
            ushort vk = GetVkCode(keyName);
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private ushort GetVkCode(string keyName) => keyName switch
        {
            "A" => 0x41, "B" => 0x42, "C" => 0x43, "D" => 0x44, "E" => 0x45,
            "F" => 0x46, "G" => 0x47, "H" => 0x48, "I" => 0x49, "J" => 0x4A,
            "K" => 0x4B, "L" => 0x4C, "M" => 0x4D, "N" => 0x4E, "O" => 0x4F,
            "P" => 0x50, "Q" => 0x51, "R" => 0x52, "S" => 0x53, "T" => 0x54,
            "U" => 0x55, "V" => 0x56, "W" => 0x57, "X" => 0x58, "Y" => 0x59,
            "Z" => 0x5A,
            "D0" => 0x30, "D1" => 0x31, "D2" => 0x32, "D3" => 0x33, "D4" => 0x34,
            "D5" => 0x35, "D6" => 0x36, "D7" => 0x37, "D8" => 0x38, "D9" => 0x39,
            "F1" => 0x70, "F2" => 0x71, "F3" => 0x72, "F4" => 0x73,
            "F5" => 0x74, "F6" => 0x75, "F7" => 0x76, "F8" => 0x77,
            "F9" => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
            "Space" => 0x20, "Return" => 0x0D, "Escape" => 0x1B,
            "LeftShift" => 0xA0, "RightShift" => 0xA1,
            "LeftCtrl" => 0xA2, "RightCtrl" => 0xA3,
            "LeftAlt" => 0xA4, "RightAlt" => 0xA5,
            "Tab" => 0x09, "Back" => 0x08,
            "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
            _ => 0
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAll();
        }
    }
}
