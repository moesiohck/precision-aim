using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AimAssistPro.Models;
using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class AIRecoilView : UserControl
    {
        private RecoilPattern? _selectedPattern;

        public AIRecoilView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Load games
            var games = App.RecoilEngine?.GetGames() ?? new List<string>();
            GameCombo.ItemsSource = games;
            if (games.Count > 0) GameCombo.SelectedIndex = 0;

            // Load settings
            var settings = App.ProfileManager?.CurrentSettings;
            if (settings != null)
            {
                AimAssistToggle.IsChecked = settings.AimAssistEnabled;
                RecoilToggle.IsChecked = settings.RecoilControlEnabled;
                AimStrengthSlider.Value = settings.AimAssistStrength * 100;
                AimRadiusSlider.Value = settings.AimAssistRadius;
                RecoilStrengthSlider.Value = settings.RecoilStrength * 100;
            }

            // Subscribe to recoil events
            if (App.RecoilEngine != null)
                App.RecoilEngine.FiringStateChanged += (s, firing) =>
                    Dispatcher.Invoke(() =>
                    {
                        RecoilStatusDot.Fill = firing
                            ? FindResource("Danger") as Brush
                            : FindResource("TextMuted") as Brush;
                        RecoilStatusText.Text = firing ? "Recoil: ATIVO (atirando)" : "Recoil: Aguardando";
                    });
        }

        private void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameCombo.SelectedItem is not string game) return;

            var patterns = RecoilEngine.BuiltInPatterns.GetValueOrDefault(game, new List<RecoilPattern>());
            WeaponCombo.ItemsSource = patterns;
            WeaponCombo.DisplayMemberPath = "Weapon";
            if (patterns.Count > 0) WeaponCombo.SelectedIndex = 0;
        }

        private void WeaponCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedPattern = WeaponCombo.SelectedItem as RecoilPattern;
            UpdatePatternInfo();
            DrawPattern();
        }

        private void UpdatePatternInfo()
        {
            if (_selectedPattern == null)
            {
                PatternNameText.Text = "—";
                PatternFireRateText.Text = "— ms";
                PatternStepsText.Text = "—";
                return;
            }
            PatternNameText.Text = _selectedPattern.Weapon;
            PatternFireRateText.Text = $"{_selectedPattern.FireRateMs} ms";
            PatternStepsText.Text = _selectedPattern.Steps.Count.ToString();
        }

        private void DrawPattern()
        {
            PatternCanvas.Children.Clear();
            if (_selectedPattern == null) return;

            double cx = PatternCanvas.Width / 2;
            double cy = 20;
            double scale = 4.0;

            // Draw crosshair center
            var center = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)) };
            Canvas.SetLeft(center, cx - 4);
            Canvas.SetTop(center, cy - 4);
            PatternCanvas.Children.Add(center);

            // Draw pattern steps
            double x = cx, y = cy;
            for (int i = 0; i < _selectedPattern.Steps.Count; i++)
            {
                var step = _selectedPattern.Steps[i];
                double nx = x + step.DeltaX * scale;
                double ny = y + (-step.DeltaY) * scale; // inverted: DeltaY negative means recoil up

                // Line
                var line = new Line
                {
                    X1 = x, Y1 = y, X2 = nx, Y2 = ny,
                    Stroke = new SolidColorBrush(Color.FromArgb(150, 0x06, 0xB6, 0xD4)),
                    StrokeThickness = 1.5
                };
                PatternCanvas.Children.Add(line);

                // Dot
                float progress = (float)i / _selectedPattern.Steps.Count;
                byte r = (byte)(124 + progress * 131);
                byte g = (byte)(58 * (1 - progress));
                byte b = (byte)(237 * (1 - progress) + 212 * progress);

                var dot = new Ellipse
                {
                    Width = 5, Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(r, g, b))
                };
                Canvas.SetLeft(dot, nx - 2.5);
                Canvas.SetTop(dot, ny - 2.5);
                PatternCanvas.Children.Add(dot);

                x = nx; y = ny;
            }
        }

        private void RecoilStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RecoilStrengthLabel == null) return;
            RecoilStrengthLabel.Text = $"{(int)e.NewValue}%";
            App.RecoilEngine?.SetStrength((float)e.NewValue / 100f);
            SaveSettings();
        }

        private void AimStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AimStrengthLabel == null) return;
            AimStrengthLabel.Text = $"{(int)e.NewValue}%";
            SaveSettings();
        }

        private void AimRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AimRadiusLabel == null) return;
            AimRadiusLabel.Text = $"{(int)e.NewValue}px";
            SaveSettings();
        }

        private void AimAssistToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = AimAssistToggle.IsChecked == true;
            AimStatusDot.Fill = enabled ? FindResource("Success") as Brush : FindResource("TextMuted") as Brush;
            AimStatusText.Text = enabled ? "Aim Assist: ATIVO" : "Aim Assist: Inativo";
            SaveSettings();
        }

        private void RecoilToggle_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = RecoilToggle.IsChecked == true;
            if (App.RecoilEngine != null) App.RecoilEngine.IsEnabled = enabled;
            SaveSettings();
        }

        private void BtnApplyRecoil_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPattern == null)
            {
                MessageBox.Show("Selecione um jogo e uma arma.", "Padrão de Recoil",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            App.RecoilEngine?.SetPattern(_selectedPattern);
            MessageBox.Show($"Padrão aplicado: {_selectedPattern.Game} — {_selectedPattern.Weapon}",
                "Recoil Control", MessageBoxButton.OK, MessageBoxImage.Information);

            RecoilStatusText.Text = $"Recoil: Padrão '{_selectedPattern.Weapon}' ativo";
        }

        private void BtnClearRecoil_Click(object sender, RoutedEventArgs e)
        {
            App.RecoilEngine?.SetPattern(null);
            WeaponCombo.SelectedIndex = -1;
            _selectedPattern = null;
            UpdatePatternInfo();
            PatternCanvas.Children.Clear();
            RecoilStatusText.Text = "Recoil: Nenhum padrão";
        }

        private void SaveSettings()
        {
            var settings = App.ProfileManager?.CurrentSettings;
            if (settings == null) return;
            settings.AimAssistEnabled = AimAssistToggle.IsChecked == true;
            settings.RecoilControlEnabled = RecoilToggle.IsChecked == true;
            settings.AimAssistStrength = (float)AimStrengthSlider.Value / 100f;
            settings.AimAssistRadius = (float)AimRadiusSlider.Value;
            settings.RecoilStrength = (float)RecoilStrengthSlider.Value / 100f;
            App.ProfileManager?.SaveSettings(settings);
        }
    }
}
