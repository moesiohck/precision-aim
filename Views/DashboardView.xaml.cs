using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AimAssistPro.Models;

namespace AimAssistPro.Views
{
    public partial class DashboardView : UserControl
    {
        private bool _isDataLoading = false;
        private bool _isAimActive  = false;
        private bool _isMuted      = false;
        private readonly DispatcherTimer _timer;

        public DashboardView()
        {
            InitializeComponent();
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;   // salva ao trocar de aba

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => SyncStatus();
            _timer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadProfiles();
            LoadSettings();
            SyncStatus();

            // Carrega stats salvos no serviço de input
            var savedStats = App.ProfileManager?.CurrentSettings?.TrackingStats;
            App.InputHookService?.LoadStats(savedStats);
        }

        // Auto-salva ao sair do Dashboard (trocar de aba)
        private void OnUnloaded(object sender, RoutedEventArgs e) => SaveSettings();


        private void LoadProfiles()
        {
            // Perfil fixo: precision.json — nao requer ComboBox
        }


        private void PollingRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDataLoading || PollingRateCombo.SelectedItem is not ComboBoxItem item) return;
            // Lê do Tag (int puro, sem parsing de texto)
            if (item.Tag is not null && int.TryParse(item.Tag.ToString(), out int rate))
            {
                var s = App.ProfileManager?.CurrentSettings;
                if (s != null) { s.PollingRate = rate; SaveSettings(); }
            }
        }


        private void LoadSettings()
        {
            var s = App.ProfileManager?.CurrentSettings;
            if (s == null) return;
            _isDataLoading = true;

            // Mostra os valores como inteiros (ex: 2500, 3500)
            // MouseSensitivityX é armazenado como fator (1.0 = padrão 2500)
            SensXInput.Text  = Math.Round(s.MouseSensitivityX * 2500).ToString();
            SensYInput.Text  = Math.Round(s.MouseSensitivityY * 3500).ToString();
            // Recoil: divisor 3600 = escala de 0 a 3600 (range configurado pelo usuario)
            RecoilInput.Text = Math.Round(s.RecoilStrength * 3600).ToString();

            // Avançado
            if (SensFilterXInput != null) SensFilterXInput.Text = s.SensitivityFilterX.ToString("0.0");
            if (SensFilterYInput != null) SensFilterYInput.Text = s.SensitivityFilterY.ToString("0.0");
            if (AimCurveXInput != null) AimCurveXInput.Text   = s.AimCurveExponentX.ToString("0.0");
            if (AimCurveYInput != null) AimCurveYInput.Text   = s.AimCurveExponentY.ToString("0.0");

            // Seleciona o item correto usando Tag (numero exato)
            int savedRate = s.PollingRate;
            foreach (ComboBoxItem item in PollingRateCombo.Items)
            {
                if (item.Tag is not null && int.TryParse(item.Tag.ToString(), out int hz) && hz == savedRate)
                {
                    PollingRateCombo.SelectedItem = item;
                    break;
                }
            }


            if (HotkeyLabel != null)
                HotkeyLabel.Text = s.ToggleHotkey;
            
            _isDataLoading = false;

            // Sincroniza imediatamente ao abrir o dashboard
            PropagateToHook(s);
        }

        private void SyncStatus()
        {
            bool active = App.InputHookService?.IsActive ?? false;
            if (active != _isAimActive)
            {
                _isAimActive = active;
                UpdateActivateState(active);
            }

            // ── Adaptive Engine: atualiza contadores em tempo real ──
            var stats = App.InputHookService?.GetLiveStats();
            if (stats != null)
            {
                if (AIStatsLabel != null)
                    AIStatsLabel.Text = stats.TotalDataPoints.ToString("N0");
                if (AccuracyLabel != null)
                    AccuracyLabel.Text = $"{stats.Accuracy:F1}%";
            }
        }

        // ─── LED Status ──────────────────────────────────────────────────
        private void UpdateActivateState(bool active)
        {
            if (StatusDot == null || StatusLabel == null) return;

            if (active)
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
                ((DropShadowEffect)StatusDot.Effect).Color = Color.FromRgb(0xFF, 0xFF, 0xFF);
                StatusLabel.Text       = "ATIVADO";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            }
            else
            {
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xAA, 0x44, 0x44));
                ((DropShadowEffect)StatusDot.Effect).Color = Color.FromRgb(0xCC, 0x33, 0x33);
                StatusLabel.Text       = "DESATIVADO";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0x44, 0x44));
            }
        }

        // ─── Profile selection (noop — perfil fixo precision.json) ──────────────
        private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }



        // ─── Botões da barra inferior ─────────────────────────────────────────
        private void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            // Mantido para compatibilidade mas não é mais exibido no XAML
            ToggleAim();
        }

        private void StatusBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ToggleAim();
        }

        private void ToggleAim()
        {
            if (App.InputHookService == null) return;
            // Nao ativa sem login — evita o som fantasma antes de logar
            if (string.IsNullOrEmpty(App.CurrentAuthToken)) return;
            // Lê o estado REAL atual (evita dessincronização com SyncStatus)
            bool nowActive = App.InputHookService.IsActive;
            bool newState  = !nowActive;
            _isAimActive = newState;
            App.InputHookService.IsActive = newState;
            UpdateActivateState(newState);
        }

        // ─── IA Toggle — habilita / desabilita compensação de recoil ───────────
        private void AIToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isDataLoading) return;
            var s = App.ProfileManager?.CurrentSettings;
            if (s != null) { s.RecoilControlEnabled = true; SaveSettings(); }
        }

        private void AIToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isDataLoading) return;
            var s = App.ProfileManager?.CurrentSettings;
            if (s != null) { s.RecoilControlEnabled = false; SaveSettings(); }
        }

        private void BeepToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isMuted = !_isMuted;
            App.InputHookService?.SetSoundEnabled(!_isMuted);

            if (sender is System.Windows.Shapes.Path icon)
            {
                icon.Opacity = _isMuted ? 0.3 : 1.0;
            }
        }

        private void ConfigIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SaveSettings();
            ShowToast();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowToast();
        }

        private void HotkeyIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var s = App.ProfileManager?.CurrentSettings;
            if (s == null) return;

            var modal = new HotkeyModal(s.ToggleHotkey) { Owner = Window.GetWindow(this) };
            if (modal.ShowDialog() == true)
            {
                s.ToggleHotkey = modal.SelectedKey;
                HotkeyLabel.Text = s.ToggleHotkey;
                SaveSettings();
            }
        }

        private void ShowToast(string message = "Configurações salvas com sucesso")
        {
            // Cria um toast flutuante dinamicamente
            var toastBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x18, 0x18, 0x18)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(18, 10, 18, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 60),
                Opacity = 0
            };
            Panel.SetZIndex(toastBorder, 999);

            var text = new System.Windows.Controls.TextBlock
            {
                Text = "✓  " + message,
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            toastBorder.Child = text;

            // Adiciona ao grid raiz do UserControl
            if (this.Content is System.Windows.Controls.Grid rootGrid)
            {
                rootGrid.Children.Add(toastBorder);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
                {
                    BeginTime = TimeSpan.FromSeconds(2)
                };
                fadeOut.Completed += (s, e) => rootGrid.Children.Remove(toastBorder);

                toastBorder.BeginAnimation(OpacityProperty, fadeIn);
                toastBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        private void SensXInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDataLoading) return;
            if (int.TryParse(SensXInput.Text, out int val))
            {
                var s = App.ProfileManager?.CurrentSettings;
                if (s != null) { s.MouseSensitivityX = val / 2500f; SaveSettings(); }
            }
        }

        private void SensYInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDataLoading) return;
            if (int.TryParse(SensYInput.Text, out int val))
            {
                var s = App.ProfileManager?.CurrentSettings;
                if (s != null) { s.MouseSensitivityY = val / 3500f; SaveSettings(); }
            }
        }

        private void RecoilInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDataLoading) return;
            if (int.TryParse(RecoilInput.Text, out int val))
            {
                var s = App.ProfileManager?.CurrentSettings;
                if (s != null) 
                { 
                    s.RecoilStrength = val / 3600f;  // range 0-3600
                    s.RecoilControlEnabled = val > 0;
                    SaveSettings(); 
                }

            }
        }

        private void AdvancedSens_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDataLoading) return;
            if (sender is TextBox tb && float.TryParse(tb.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                var s = App.ProfileManager?.CurrentSettings;
                if (s != null)
                {
                    if (tb == SensFilterXInput) s.SensitivityFilterX = val;
                    else if (tb == SensFilterYInput) s.SensitivityFilterY = val;
                    else if (tb == AimCurveXInput) s.AimCurveExponentX = val;
                    else if (tb == AimCurveYInput) s.AimCurveExponentY = val;
                    SaveSettings();
                }
            }
        }


        private void SaveSettings()
        {
            var profile  = App.ProfileManager?.CurrentProfile;
            var settings = App.ProfileManager?.CurrentSettings;

            // Copia stats vivos do serviço para o modelo antes de salvar
            var liveStats = App.InputHookService?.GetLiveStats();
            if (settings != null && liveStats != null)
            {
                settings.TrackingStats.TotalDataPoints = liveStats.TotalDataPoints;
                settings.TrackingStats.EffectiveFrames = liveStats.EffectiveFrames;
                settings.TrackingStats.TotalFrames     = liveStats.TotalFrames;
                settings.TrackingStats.TotalActiveTime  = liveStats.TotalActiveTime;
                settings.TrackingStats.LastSessionStart = liveStats.LastSessionStart;
            }

            if (profile != null) App.ProfileManager?.SaveProfile(profile);
            if (settings != null) App.ProfileManager?.SaveSettings();

            // Propaga imediatamente para o hook de input
            if (settings != null) PropagateToHook(settings);
        }

        // ─── SYNC DATA ───────────────────────────────────────────────────────
        private void BtnSyncData_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            ShowToast("Dados sincronizados com sucesso");
        }

        /// <summary>
        /// Aplica sensibilidade e polling rate ao GlobalInputHookService em tempo real.
        /// Sem isso, mudanças no Dashboard não teriam efeito até reiniciar.
        /// </summary>
        private void PropagateToHook(AimAssistPro.Models.AppSettings s)
        {
            var hook = App.InputHookService;
            if (hook == null) return;

            // Os valores X e Y são frações: ex 2500/2500=1.0, 3500/3500=1.0
            // Mas o hook escala os deltas do mouse, então passamos o fator direto
            hook.SetSensitivity(s.MouseSensitivityX, s.MouseSensitivityY);
        }
        // ─── Reset IA (WIPE LOGS) ────────────────────────────────────────────
        private void BtnResetAI_Click(object sender, RoutedEventArgs e)
        {
            // Reseta contadores REAIS no serviço de input
            App.InputHookService?.ResetStats();

            // Atualiza UI imediatamente
            if (AIStatsLabel != null) AIStatsLabel.Text = "0";
            if (AccuracyLabel != null) AccuracyLabel.Text = "0%";

            // Persiste o zeramento no perfil
            var s = App.ProfileManager?.CurrentSettings;
            if (s != null)
            {
                s.TrackingStats = new AimAssistPro.Models.AdaptiveStats();
                SaveSettings();
            }

            ShowToast("Logs limpos com sucesso");
        }
    }
}
