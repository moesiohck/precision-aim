using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AimAssistPro.Models;

namespace AimAssistPro.Services
{
    public class RecoilEngine
    {
        // ─── SendInput (Win32) ───────────────────────────────────────────────
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // ─── Built-in Recoil Patterns ────────────────────────────────────────
        private static readonly Dictionary<string, List<RecoilPattern>> _builtInPatterns = new()
        {
            ["CS2"] = new List<RecoilPattern>
            {
                new RecoilPattern
                {
                    Name = "AK-47", Game = "CS2", Weapon = "AK-47", IsBuiltIn = true, FireRateMs = 100,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-8},new(){DeltaX=0,DeltaY=-7},new(){DeltaX=2,DeltaY=-7},
                        new(){DeltaX=1,DeltaY=-6},new(){DeltaX=-1,DeltaY=-6},new(){DeltaX=-2,DeltaY=-5},
                        new(){DeltaX=-2,DeltaY=-4},new(){DeltaX=-1,DeltaY=-4},new(){DeltaX=1,DeltaY=-3},
                        new(){DeltaX=2,DeltaY=-3},new(){DeltaX=1,DeltaY=-4},new(){DeltaX=-1,DeltaY=-4},
                        new(){DeltaX=-2,DeltaY=-3},new(){DeltaX=-1,DeltaY=-3},new(){DeltaX=0,DeltaY=-4},
                        new(){DeltaX=1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "M4A4", Game = "CS2", Weapon = "M4A4", IsBuiltIn = true, FireRateMs = 90,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-7},new(){DeltaX=0,DeltaY=-6},new(){DeltaX=1,DeltaY=-6},
                        new(){DeltaX=1,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=-1,DeltaY=-4},
                        new(){DeltaX=-1,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=1,DeltaY=-3},
                        new(){DeltaX=1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=-1,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "AWP", Game = "CS2", Weapon = "AWP", IsBuiltIn = true, FireRateMs = 1400,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-4},
                    }
                }
            },
            ["Valorant"] = new List<RecoilPattern>
            {
                new RecoilPattern
                {
                    Name = "Vandal", Game = "Valorant", Weapon = "Vandal", IsBuiltIn = true, FireRateMs = 95,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-10},new(){DeltaX=0,DeltaY=-8},new(){DeltaX=1,DeltaY=-7},
                        new(){DeltaX=2,DeltaY=-6},new(){DeltaX=-1,DeltaY=-6},new(){DeltaX=-2,DeltaY=-5},
                        new(){DeltaX=-1,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=1,DeltaY=-4},
                        new(){DeltaX=0,DeltaY=-4},new(){DeltaX=0,DeltaY=-4},new(){DeltaX=0,DeltaY=-4},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},
                    }
                },
                new RecoilPattern
                {
                    Name = "Phantom", Game = "Valorant", Weapon = "Phantom", IsBuiltIn = true, FireRateMs = 78,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-8},new(){DeltaX=0,DeltaY=-7},new(){DeltaX=1,DeltaY=-6},
                        new(){DeltaX=1,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=-1,DeltaY=-4},
                        new(){DeltaX=-1,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "Spectre", Game = "Valorant", Weapon = "Spectre", IsBuiltIn = true, FireRateMs = 70,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=0,DeltaY=-4},
                        new(){DeltaX=0,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-2},
                    }
                }
            },
            ["Fortnite"] = new List<RecoilPattern>
            {
                new RecoilPattern
                {
                    Name = "Assault Rifle", Game = "Fortnite", Weapon = "Assault Rifle", IsBuiltIn = true, FireRateMs = 110,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-6},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=1,DeltaY=-5},
                        new(){DeltaX=0,DeltaY=-4},new(){DeltaX=-1,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "SCAR", Game = "Fortnite", Weapon = "SCAR", IsBuiltIn = true, FireRateMs = 100,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-7},new(){DeltaX=0,DeltaY=-6},new(){DeltaX=1,DeltaY=-6},
                        new(){DeltaX=1,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=-1,DeltaY=-4},
                        new(){DeltaX=0,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                }
            },
            ["Warzone"] = new List<RecoilPattern>
            {
                new RecoilPattern
                {
                    Name = "M4 (Warzone)", Game = "Warzone", Weapon = "M4", IsBuiltIn = true, FireRateMs = 83,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-7},new(){DeltaX=0,DeltaY=-7},new(){DeltaX=1,DeltaY=-6},
                        new(){DeltaX=1,DeltaY=-6},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=-1,DeltaY=-5},
                        new(){DeltaX=-1,DeltaY=-4},new(){DeltaX=0,DeltaY=-4},new(){DeltaX=0,DeltaY=-4},
                        new(){DeltaX=1,DeltaY=-3},new(){DeltaX=1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=-1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "RPK-74 (Warzone)", Game = "Warzone", Weapon = "RPK-74", IsBuiltIn = true, FireRateMs = 90,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-9},new(){DeltaX=0,DeltaY=-8},new(){DeltaX=2,DeltaY=-7},
                        new(){DeltaX=2,DeltaY=-7},new(){DeltaX=1,DeltaY=-6},new(){DeltaX=-1,DeltaY=-6},
                        new(){DeltaX=-2,DeltaY=-5},new(){DeltaX=-1,DeltaY=-5},new(){DeltaX=0,DeltaY=-4},
                        new(){DeltaX=1,DeltaY=-4},new(){DeltaX=1,DeltaY=-4},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=-1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                    }
                },
                new RecoilPattern
                {
                    Name = "MP5 (Warzone)", Game = "Warzone", Weapon = "MP5", IsBuiltIn = true, FireRateMs = 67,
                    Steps = new List<RecoilStep>
                    {
                        new(){DeltaX=0,DeltaY=-5},new(){DeltaX=0,DeltaY=-5},new(){DeltaX=1,DeltaY=-4},
                        new(){DeltaX=0,DeltaY=-4},new(){DeltaX=-1,DeltaY=-3},new(){DeltaX=0,DeltaY=-3},
                        new(){DeltaX=0,DeltaY=-3},new(){DeltaX=0,DeltaY=-2},new(){DeltaX=0,DeltaY=-2},
                        new(){DeltaX=0,DeltaY=-2},
                    }
                }
            }
        };

        // ─── Engine State ────────────────────────────────────────────────────
        private RecoilPattern? _activePattern;
        private bool _isFiring;
        private int _currentStep;
        private float _strengthMultiplier = 1.0f;
        private List<RecoilPattern> _customPatterns = new();
        private CancellationTokenSource? _cts;

        public bool IsEnabled { get; set; } = false;
        public event EventHandler<bool>? FiringStateChanged;

        public static IReadOnlyDictionary<string, List<RecoilPattern>> BuiltInPatterns => _builtInPatterns;

        public void SetPattern(RecoilPattern? pattern)
        {
            _activePattern = pattern;
            _currentStep = 0;
        }

        public void SetStrength(float strength) => _strengthMultiplier = Math.Clamp(strength, 0f, 2f);

        public void StartRecoil()
        {
            if (!IsEnabled || _activePattern == null || _isFiring) return;
            _isFiring = true;
            _currentStep = 0;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            FiringStateChanged?.Invoke(this, true);

            Thread thread = new(() => RecoilLoop(token)) { IsBackground = true };
            thread.Start();
        }

        public void StopRecoil()
        {
            _isFiring = false;
            _cts?.Cancel();
            FiringStateChanged?.Invoke(this, false);
        }

        private void RecoilLoop(CancellationToken token)
        {
            while (_isFiring && !token.IsCancellationRequested && _activePattern != null)
            {
                int stepIdx = _currentStep % _activePattern.Steps.Count;
                var step = _activePattern.Steps[stepIdx];

                int dx = (int)(step.DeltaX * _strengthMultiplier);
                int dy = (int)(step.DeltaY * _strengthMultiplier);

                if (dx != 0 || dy != 0)
                    SendMouseInput(dx, dy);

                _currentStep++;
                Thread.Sleep(_activePattern.FireRateMs);
                if (token.IsCancellationRequested) break;
            }
        }

        private void SendMouseInput(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE },
                padding = new byte[8]
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        public List<RecoilPattern> GetAllPatterns()
        {
            var all = new List<RecoilPattern>();
            foreach (var group in _builtInPatterns.Values)
                all.AddRange(group);
            all.AddRange(_customPatterns);
            return all;
        }

        public void AddCustomPattern(RecoilPattern pattern)
        {
            pattern.IsBuiltIn = false;
            _customPatterns.Add(pattern);
        }

        public List<string> GetGames() => new(new[] { "CS2", "Valorant", "Fortnite", "Warzone" });
    }
}
