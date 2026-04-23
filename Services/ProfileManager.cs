using System;
using System.Collections.Generic;
using System.IO;
using AimAssistPro.Models;
using Newtonsoft.Json;

namespace AimAssistPro.Services
{
    public class ProfileManager
    {
        private readonly string _profilesFolder;
        private string _settingsFile;
        private string _currentUsername = "";

        private List<Profile> _profiles = new();
        private Profile? _currentProfile;
        private AppSettings _currentSettings = new();

        public IReadOnlyList<Profile> Profiles => _profiles;
        public Profile? CurrentProfile => _currentProfile;
        public AppSettings CurrentSettings => _currentSettings;

        public event EventHandler? ProfileChanged;

        public ProfileManager()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AimAssistPro");
            Directory.CreateDirectory(appData);
            _profilesFolder = Path.Combine(appData, "profiles");
            Directory.CreateDirectory(_profilesFolder);
            _settingsFile = Path.Combine(appData, "settings.json");

            LoadAllProfiles();
            LoadSettings();
        }

        public void ReloadForUser(string username)
        {
            if (string.IsNullOrEmpty(username)) return;
            _currentUsername = username;
            
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AimAssistPro");
                
            _settingsFile = Path.Combine(appData, $"settings_{_currentUsername}.json");
            LoadSettings();
        }

        // ─── Profile CRUD ─────────────────────────────────────────────────────
        public Profile CreateProfile(string name, string game = "")
        {
            var profile = new Profile { Name = name, Game = game };
            _profiles.Add(profile);
            SaveProfile(profile);
            return profile;
        }

        public void SaveProfile(Profile profile)
        {
            profile.UpdatedAt = DateTime.Now;
            var path = GetProfilePath(profile.Id);
            File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
        }

        public void DeleteProfile(string id)
        {
            _profiles.RemoveAll(p => p.Id == id);
            var path = GetProfilePath(id);
            if (File.Exists(path)) File.Delete(path);
        }

        public void SetCurrentProfile(Profile profile)
        {
            _currentProfile = profile;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetCurrentProfile(string id)
        {
            var profile = _profiles.Find(p => p.Id == id);
            if (profile != null) SetCurrentProfile(profile);
        }

        // ─── Settings ─────────────────────────────────────────────────────────
        public void SaveSettings(AppSettings settings)
        {
            _currentSettings = settings;
            SaveSettings();
        }

        public void SaveSettings()
        {
            File.WriteAllText(_settingsFile, JsonConvert.SerializeObject(_currentSettings, Formatting.Indented));
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                    _currentSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(_settingsFile))
                                       ?? new AppSettings();
            }
            catch { _currentSettings = new AppSettings(); }
        }

        // ─── Key Mappings Shortcuts ───────────────────────────────────────────
        public List<KeyMapping> GetCurrentMappings()
            => _currentProfile?.KeyMappings ?? new List<KeyMapping>();

        public List<Macro> GetCurrentMacros()
            => _currentProfile?.Macros ?? new List<Macro>();

        // ─── Persistence ──────────────────────────────────────────────────────
        private void LoadAllProfiles()
        {
            _profiles.Clear();
            foreach (var file in Directory.GetFiles(_profilesFolder, "*.json"))
            {
                try
                {
                    var profile = JsonConvert.DeserializeObject<Profile>(File.ReadAllText(file));
                    if (profile != null)
                    {
                        PatchMissingMappings(profile); // ensure scroll / wheel always present
                        _profiles.Add(profile);
                    }
                }
                catch { }
            }

            if (_profiles.Count == 0)
                CreateDefaultProfiles();

            if (_profiles.Count > 0)
                _currentProfile = _profiles[0];
        }

        /// <summary>
        /// Adds any key mappings that are missing from an existing saved profile
        /// so that old AppData files always get the current default bindings.
        /// </summary>
        private static void PatchMissingMappings(Profile profile)
        {
            var existing = new HashSet<string>(
                profile.KeyMappings.ConvertAll(m => m.InputKey),
                StringComparer.OrdinalIgnoreCase);

            void TryAdd(string key, ControllerButton btn, AxisMapping? axis = null)
            {
                if (!existing.Contains(key))
                    profile.KeyMappings.Add(new KeyMapping
                    {
                        InputKey = key,
                        TargetButton = btn,
                        AxisMap = axis
                    });
            }

            // Scroll wheel → Y (swap weapon) — most critical missing mapping
            TryAdd("MouseWheelUp",   ControllerButton.Y);
            TryAdd("MouseWheelDown", ControllerButton.Y);

            // WASD → Left Stick — in case profile is very old
            TryAdd("W", ControllerButton.None,
                new AxisMapping { Axis = ControllerAxis.LeftY, Value = 1.0f });
            TryAdd("S", ControllerButton.None,
                new AxisMapping { Axis = ControllerAxis.LeftY, Value = 1.0f, IsNegative = true });
            TryAdd("A", ControllerButton.None,
                new AxisMapping { Axis = ControllerAxis.LeftX, Value = 1.0f, IsNegative = true });
            TryAdd("D", ControllerButton.None,
                new AxisMapping { Axis = ControllerAxis.LeftX, Value = 1.0f });
        }


        private void CreateDefaultProfiles()
        {
            var defaults = new[]
            {
                CreateDefaultProfile("CS2", "CS2"),
                CreateDefaultProfile("Valorant", "Valorant"),
                CreateDefaultProfile("Warzone", "Warzone"),
                CreateDefaultProfile("Fortnite", "Fortnite"),
                CreateDefaultProfile("Padrão", ""),
            };
            foreach (var p in defaults)
            {
                _profiles.Add(p);
                SaveProfile(p);
            }
        }

        private Profile CreateDefaultProfile(string name, string game)
        {
            var profile = new Profile { Name = name, Game = game };

            // Default WASD → Left Stick
            profile.KeyMappings.Add(new KeyMapping
            {
                InputKey = "W", TargetButton = ControllerButton.None,
                AxisMap = new AxisMapping { Axis = ControllerAxis.LeftY, Value = 1.0f }
            });
            profile.KeyMappings.Add(new KeyMapping
            {
                InputKey = "S", TargetButton = ControllerButton.None,
                AxisMap = new AxisMapping { Axis = ControllerAxis.LeftY, Value = 1.0f, IsNegative = true }
            });
            profile.KeyMappings.Add(new KeyMapping
            {
                InputKey = "A", TargetButton = ControllerButton.None,
                AxisMap = new AxisMapping { Axis = ControllerAxis.LeftX, Value = 1.0f, IsNegative = true }
            });
            profile.KeyMappings.Add(new KeyMapping
            {
                InputKey = "D", TargetButton = ControllerButton.None,
                AxisMap = new AxisMapping { Axis = ControllerAxis.LeftX, Value = 1.0f }
            });

            // Space -> A (Jump)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "Space", TargetButton = ControllerButton.A });

            // Left Ctrl / C -> B (Crouch/Slide)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "LeftCtrl", TargetButton = ControllerButton.B });
            profile.KeyMappings.Add(new KeyMapping { InputKey = "C", TargetButton = ControllerButton.B });

            // F -> X (Interact/Pickup)
            // NOTA: R (reload) NAO e mapeado pois o jogo trata R e F como acoes distintas.
            // Mapear R->X causaria pickup involuntario ao recarregar.
            profile.KeyMappings.Add(new KeyMapping { InputKey = "F", TargetButton = ControllerButton.X });

            // D2 / Wheel Up / Wheel Down -> Y (Swap Weapon/Armor)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "D2", TargetButton = ControllerButton.Y });
            profile.KeyMappings.Add(new KeyMapping { InputKey = "MouseWheelUp", TargetButton = ControllerButton.Y });
            profile.KeyMappings.Add(new KeyMapping { InputKey = "MouseWheelDown", TargetButton = ControllerButton.Y });

            // Right Mouse -> LT (ADS)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "MouseRight", TargetButton = ControllerButton.LT });

            // Left Mouse -> RT (Fire)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "MouseLeft", TargetButton = ControllerButton.RT });

            // Q -> LB (Tactical)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "Q", TargetButton = ControllerButton.LB });

            // E -> RB (Lethal)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "E", TargetButton = ControllerButton.RB });

            // Left Shift -> LS (Sprint/Focus)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "LeftShift", TargetButton = ControllerButton.LS });

            // V -> RS (Melee)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "V", TargetButton = ControllerButton.RS });

            // Mouse Middle -> DPad Up (Ping)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "MouseMiddle", TargetButton = ControllerButton.DPadUp });

            // Tab -> DPad Down (Inventory)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "Tab", TargetButton = ControllerButton.DPadDown });

            // B -> DPad Left (Fire mode)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "B", TargetButton = ControllerButton.DPadLeft });

            // D3 -> DPad Right (Scorestreaks)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "D3", TargetButton = ControllerButton.DPadRight });

            // M -> Back (Map)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "M", TargetButton = ControllerButton.Back });

            // Escape -> Start (Menu)
            profile.KeyMappings.Add(new KeyMapping { InputKey = "Escape", TargetButton = ControllerButton.Start });

            return profile;
        }

        private string GetProfilePath(string id) => Path.Combine(_profilesFolder, $"{id}.json");
    }
}
