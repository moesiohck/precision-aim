using System;
using System.Collections.Generic;
using AimAssistPro.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Emulates an Xbox One controller via ViGEmBus.
    /// Uses Xbox360 protocol internally (ViGEm maps it as Xbox One in modern Windows).
    /// Warzone and most modern games detect it as Xbox One / XInput compatible.
    /// </summary>
    public class ViGEmControllerService : IDisposable
    {
        private ViGEmClient? _client;
        private IXbox360Controller? _controller;
        private bool _disposed;
        private bool _isConnected;

        // Current state tracking
        private short _leftX, _leftY, _rightX, _rightY;
        private byte  _leftTrigger, _rightTrigger;

        public bool IsConnected => _isConnected;
        public string DeviceName => _isConnected ? "Xbox One Controller (Virtual)" : "Não conectado";

        public event EventHandler<bool>? ConnectionChanged;

        public ViGEmControllerService()
        {
            TryConnect();
        }

        public bool TryConnect()
        {
            try
            {
                _client = new ViGEmClient();

                // Xbox 360 protocol — Windows Game Input layer presents this as
                // a generic XInput device which Warzone/CoD reads as Xbox One compatible.
                _controller = _client.CreateXbox360Controller();
                _controller.AutoSubmitReport = false;
                _controller.Connect();

                _isConnected = true;
                ConnectionChanged?.Invoke(this, true);
                System.Diagnostics.Debug.WriteLine("[ViGEm] Xbox One virtual controller connected.");
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                System.Diagnostics.Debug.WriteLine($"[ViGEm] Connection failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("[ViGEm] Make sure ViGEmBus is installed: https://github.com/nefarius/ViGEmBus/releases");
                
                try
                {
                    var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AimAssistPro", "input_diag.log");
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [VIGEM FATAL] {ex.ToString()}\n");
                } 
                catch {}
                
                return false;
            }
        }

        private readonly object _vigemLock = new object();
        private readonly Dictionary<ControllerButton, int> _buttonRefCount = new();

        // ─── Button Control ──────────────────────────────────────────────────
        public void PressButton(ControllerButton button, bool pressed)
        {
            if (!_isConnected || _controller == null) return;
            var index = MapButton(button);
            if (index == null) return;

            try
            {
                lock (_vigemLock)
                {
                    _buttonRefCount.TryGetValue(button, out int count);
                    if (pressed)
                    {
                        _buttonRefCount[button] = count + 1;
                        _controller.SetButtonState(index.Value, true);
                    }
                    else
                    {
                        count = Math.Max(0, count - 1);
                        _buttonRefCount[button] = count;
                        if (count == 0)
                            _controller.SetButtonState(index.Value, false);
                    }
                    _controller.SubmitReport();
                }
            }
            catch (Exception ex) 
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AimAssistPro", "input_diag.log"),
                    $"[ERROR] PressButton failed: {ex.Message}\n"
                );
            }
        }

        // ─── Stick Control ───────────────────────────────────────────────────
        public void SetLeftStick(short x, short y)
        {
            if (!_isConnected || _controller == null) return;
            lock (_vigemLock)
            {
                _leftX = x; _leftY = y;
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, x);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, y);
            }
        }

        public void SetRightStick(short x, short y)
        {
            if (!_isConnected || _controller == null) return;
            lock (_vigemLock)
            {
                _rightX = x; _rightY = y;
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, x);
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, y);
            }
        }

        public void SetLeftStickFloat(float x, float y)
        {
            short sx = (short)Math.Clamp(x * 32767, -32768, 32767);
            short sy = (short)Math.Clamp(y * 32767, -32768, 32767);
            SetLeftStick(sx, sy);
        }

        public void SetRightStickFloat(float x, float y)
        {
            short sx = (short)Math.Clamp(x * 32767, -32768, 32767);
            short sy = (short)Math.Clamp(y * 32767, -32768, 32767);
            SetRightStick(sx, sy);
        }

        // ─── Trigger Control ─────────────────────────────────────────────────
        public void SetLeftTrigger(byte value)
        {
            if (!_isConnected || _controller == null) return;
            lock (_vigemLock)
            {
                _leftTrigger = value;
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, value);
            }
        }

        public void SetRightTrigger(byte value)
        {
            if (!_isConnected || _controller == null) return;
            lock (_vigemLock)
            {
                _rightTrigger = value;
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, value);
            }
        }

        public void SetTriggerFloat(bool isLeft, float value)
        {
            byte b = (byte)Math.Clamp(value * 255, 0, 255);
            if (isLeft) SetLeftTrigger(b);
            else SetRightTrigger(b);
        }

        // ─── Reset ───────────────────────────────────────────────────────────
        public void ResetAll()
        {
            if (!_isConnected || _controller == null) return;
            lock (_vigemLock)
            {
                _controller.ResetReport();
                _leftX = _leftY = _rightX = _rightY = 0;
                _leftTrigger = _rightTrigger = 0;
            }
        }

        public (float lx, float ly, float rx, float ry) GetStickValues() =>
            (_leftX / 32767f, _leftY / 32767f, _rightX / 32767f, _rightY / 32767f);

        public void SubmitReport()
        {
            if (!_isConnected || _controller == null) return;
            try 
            { 
                _controller.SubmitReport(); 
            }
            catch { }
        }

        // ─── Button Mapping ──────────────────────────────────────────────────
        private static int? MapButton(ControllerButton button) => button switch
        {
            ControllerButton.DPadUp    => 0,
            ControllerButton.DPadDown  => 1,
            ControllerButton.DPadLeft  => 2,
            ControllerButton.DPadRight => 3,
            ControllerButton.Start     => 4,
            ControllerButton.Back      => 5,
            ControllerButton.LS        => 6,
            ControllerButton.RS        => 7,
            ControllerButton.LB        => 8,
            ControllerButton.RB        => 9,
            ControllerButton.A         => 11,
            ControllerButton.B         => 12,
            ControllerButton.X         => 13,
            ControllerButton.Y         => 14,
            _                          => null
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _controller?.Disconnect();
                _client?.Dispose();
            }
            catch { }
            _isConnected = false;
        }
    }
}
