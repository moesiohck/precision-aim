using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AimAssistPro.Models
{
    // ─── Key Mapping ─────────────────────────────────────────────────────────
    public class KeyMapping
    {
        public string InputKey { get; set; } = string.Empty;
        public ControllerButton TargetButton { get; set; }
        public AxisMapping? AxisMap { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class AxisMapping
    {
        public ControllerAxis Axis { get; set; }
        public float Value { get; set; }     // -1.0 to 1.0
        public bool IsNegative { get; set; }
    }

    public enum ControllerButton
    {
        None = 0,
        A, B, X, Y,
        LB, RB,
        LT, RT,
        LS, RS,
        Start, Back,
        DPadUp, DPadDown, DPadLeft, DPadRight
    }

    public enum ControllerAxis
    {
        None = 0,
        LeftX, LeftY,
        RightX, RightY,
        LeftTrigger, RightTrigger
    }

    // ─── Recoil Pattern ──────────────────────────────────────────────────────
    public class RecoilPattern
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        public string Weapon { get; set; } = string.Empty;
        public List<RecoilStep> Steps { get; set; } = new();
        public int FireRateMs { get; set; } = 100;   // ms between shots
        public bool IsBuiltIn { get; set; } = false;
    }

    public class RecoilStep
    {
        public float DeltaX { get; set; }   // horizontal compensation
        public float DeltaY { get; set; }   // vertical compensation
    }

    // ─── Macro ────────────────────────────────────────────────────────────────
    public class Macro
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string TriggerKey { get; set; } = string.Empty;
        public bool IsLoop { get; set; } = false;
        public int RepeatCount { get; set; } = 1;
        public List<MacroAction> Actions { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public class MacroAction
    {
        public MacroActionType Type { get; set; }
        public string Key { get; set; } = string.Empty;
        public int DelayMs { get; set; } = 50;
        public ControllerButton Button { get; set; }
    }

    public enum MacroActionType
    {
        KeyPress,
        KeyDown,
        KeyUp,
        Delay,
        ControllerButton,
        MouseMove,
        MouseClick
    }

    // ─── Profile ─────────────────────────────────────────────────────────────
    public class Profile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Game { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public List<KeyMapping> KeyMappings { get; set; } = new();
        public List<Macro> Macros { get; set; } = new();
        public AppSettings Settings { get; set; } = new();
        public string? ActiveRecoilPatternId { get; set; }
        
        public override string ToString() => string.IsNullOrEmpty(Name) ? "Sem Nome" : Name;
    }

    // ─── App Settings ─────────────────────────────────────────────────────────
    public class AppSettings
    {
        // Sensitivity — basic
        public float MouseSensitivityX { get; set; } = 1.0f;
        public float MouseSensitivityY { get; set; } = 1.0f;
        public float DeadZone { get; set; } = 0.05f;
        public SensitivityCurve Curve { get; set; } = SensitivityCurve.Linear;

        // Sensitivity — advanced (new)
        // Filter: lerp factor when input arrives (0.3=super smooth, 1.0=instant/raw)
        public float SensitivityFilterX { get; set; } = 0.6f;
        public float SensitivityFilterY { get; set; } = 0.6f;
        // Curve exponent: 1.0=linear, 1.4=default, 2.0=aggressive exponential
        public float AimCurveExponentX { get; set; } = 1.4f;
        public float AimCurveExponentY { get; set; } = 1.4f;

        // Aim Assist
        public bool AimAssistEnabled { get; set; } = false;
        public float AimAssistStrength { get; set; } = 0.3f;
        public float AimAssistRadius { get; set; } = 15f;

        // Rotational Aim Assist — new
        public bool RAAEnabled { get; set; } = false;
        public float RAAStrength { get; set; } = 0.15f;

        // Recoil
        public bool RecoilControlEnabled { get; set; } = false;
        public float RecoilStrength { get; set; } = 1.0f;

        // General
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool ShowInSystemTray { get; set; } = true;
        public string ToggleHotkey { get; set; } = "F8";
        public bool ConsumeKeyboardEvents { get; set; } = false;
        public bool ShowOverlay { get; set; } = true;
        
        // Macros
        public bool RapidFireEnabled { get; set; } = false;
        public bool DropCashEnabled { get; set; } = false;
        public string DropCashHotkey { get; set; } = "F9";
        
        // Privacy
        public bool StreamModeEnabled { get; set; } = false;
        
        // Performance
        public int PollingRate { get; set; } = 500;

        // Adaptive Engine — tracking real
        public AdaptiveStats TrackingStats { get; set; } = new();
    }

    public enum SensitivityCurve
    {
        Linear,
        Exponential,
        SCurve
    }

    // ─── License ──────────────────────────────────────────────────────────────
    public class LicenseInfo
    {
        public string Key { get; set; } = string.Empty;
        public string Hwid { get; set; } = string.Empty;
        public LicenseStatus Status { get; set; } = LicenseStatus.Inactive;
        public DateTime ActivatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string PlanName { get; set; } = "Standard";
    }

    public enum LicenseStatus
    {
        Inactive,
        Active,
        Expired,
        Invalid
    }

    // ─── Dashboard Stats ──────────────────────────────────────────────────────
    public class DashboardStats
    {
        public bool ControllerConnected { get; set; }
        public bool IsActive { get; set; }
        public int ActiveMappings { get; set; }
        public int ActiveMacros { get; set; }
        public string ActiveProfile { get; set; } = string.Empty;
        public string ActiveRecoil { get; set; } = string.Empty;
        public TimeSpan SessionTime { get; set; }
    }

    // ─── Adaptive Stats (rastreamento real de uso) ─────────────────────────────
    public class AdaptiveStats
    {
        /// <summary>Total de movimentos do mouse capturados com aim ativo.</summary>
        public long TotalDataPoints { get; set; } = 0;

        /// <summary>Frames em que o stick resultante ficou acima da deadzone (input efetivo).</summary>
        public long EffectiveFrames { get; set; } = 0;

        /// <summary>Total de frames processados pelo AimAssistLoop.</summary>
        public long TotalFrames { get; set; } = 0;

        /// <summary>Accuracy calculada: EffectiveFrames / TotalFrames * 100.</summary>
        [JsonIgnore]
        public double Accuracy => TotalFrames > 0
            ? Math.Round((double)EffectiveFrames / TotalFrames * 100, 1)
            : 0;

        /// <summary>Último momento em que o aim assist foi ativado.</summary>
        public DateTime LastSessionStart { get; set; } = DateTime.MinValue;

        /// <summary>Tempo total acumulado com aim assist ativo.</summary>
        public TimeSpan TotalActiveTime { get; set; } = TimeSpan.Zero;
    }
}
