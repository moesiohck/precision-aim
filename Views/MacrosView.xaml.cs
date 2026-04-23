using System;
using System.Windows;
using System.Windows.Controls;
using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class MacrosView : UserControl
    {
        public MacrosView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (App.ProfileManager?.CurrentSettings != null)
            {
                var s = App.ProfileManager.CurrentSettings;
                ToggleRapidFire.IsChecked = s.RapidFireEnabled;
                ToggleDropCash.IsChecked = s.DropCashEnabled;
                
                var hotkey = s.DropCashHotkey;
                TxtDropCashKey.Text = string.IsNullOrEmpty(hotkey) ? "Clique para definir" : hotkey.ToUpper();

                // RAA init
                ToggleRAA.IsChecked = s.RAAEnabled;
                int strPct = (int)Math.Round(s.RAAStrength * 100);
                SliderRAAStrength.Value = strPct;
                TxtRAAStrength.Text = strPct + "%";
            }
        }

        private void ToggleRapidFire_Changed(object sender, RoutedEventArgs e)
        {
            if (ToggleRapidFire.IsChecked.HasValue && App.ProfileManager?.CurrentSettings != null)
            {
                App.ProfileManager.CurrentSettings.RapidFireEnabled = ToggleRapidFire.IsChecked.Value;
                App.ProfileManager.SaveSettings();
            }
        }

        private void ToggleDropCash_Changed(object sender, RoutedEventArgs e)
        {
            if (ToggleDropCash.IsChecked.HasValue && App.ProfileManager?.CurrentSettings != null)
            {
                App.ProfileManager.CurrentSettings.DropCashEnabled = ToggleDropCash.IsChecked.Value;
                App.ProfileManager.SaveSettings();
            }
        }

        private void DropCashKey_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var modal = new HotkeyModal(App.ProfileManager?.CurrentSettings?.DropCashHotkey ?? "");
            modal.Owner = Window.GetWindow(this);
            if (modal.ShowDialog() == true)
            {
                string key = modal.SelectedKey;
                TxtDropCashKey.Text = key.ToUpper();
                if (App.ProfileManager?.CurrentSettings != null)
                {
                    App.ProfileManager.CurrentSettings.DropCashHotkey = key;
                    App.ProfileManager.SaveSettings();
                }
            }
        }

        private void ToggleRAA_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ToggleRAA.IsChecked.HasValue && App.ProfileManager?.CurrentSettings != null)
            {
                App.ProfileManager.CurrentSettings.RAAEnabled = ToggleRAA.IsChecked.Value;
                App.ProfileManager.SaveSettings();
            }
        }

        private void SliderRAAStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            double val = Math.Round(e.NewValue);
            if (TxtRAAStrength != null)
                TxtRAAStrength.Text = val + "%";

            if (App.ProfileManager?.CurrentSettings != null)
            {
                // UI shows 0-50%, mapping to 0.0-0.5 internally
                App.ProfileManager.CurrentSettings.RAAStrength = (float)(val / 100.0);
                App.ProfileManager.SaveSettings();
            }
        }
    }
}

