using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AimAssistPro.Models;

namespace AimAssistPro.Views
{
    public partial class KeyboardView : UserControl
    {
        private string? _selectedInputKey;
        private string? _selectedTargetTag;
        private Button? _selectedKeyBtn;
        private Button? _selectedTargetBtn;

        public KeyboardView()
        {
            // Register inline styles needed
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachKeyboardHandlers(KeyboardPanel);
            AttachControllerHandlers();
            UpdatePendingText();
        }

        // Attach click handlers to all keyboard buttons
        private void AttachKeyboardHandlers(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn && btn.Tag is string tag)
                    btn.Click += KeyboardBtn_Click;
                else
                    AttachKeyboardHandlers(child);
            }
        }

        private void AttachControllerHandlers()
        {
            AttachControllerBtnHandlers(this);
        }

        private void AttachControllerBtnHandlers(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button btn && btn.Style == TryFindResource("ControllerBtn") as Style)
                    btn.Click += ControllerBtn_Click;
                AttachControllerBtnHandlers(child);
            }
        }

        private void KeyboardBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            if (_selectedKeyBtn != null)
                _selectedKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

            _selectedKeyBtn = btn;
            _selectedInputKey = btn.Tag as string;
            btn.BorderBrush = FindResource("AccentPrimary") as Brush;

            SelectedKeyText.Text = $"Selecionada: {btn.Content}";
            UpdatePendingText();
        }

        private void ControllerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;

            if (_selectedTargetBtn != null)
                _selectedTargetBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

            _selectedTargetBtn = btn;
            _selectedTargetTag = btn.Tag as string;
            btn.BorderBrush = FindResource("AccentSecondary") as Brush;

            UpdatePendingText();
        }

        private void UpdatePendingText()
        {
            if (_selectedInputKey != null && _selectedTargetTag != null)
                PendingMappingText.Text = $"Pendente: {_selectedInputKey} → {_selectedTargetTag}";
            else if (_selectedInputKey != null)
                PendingMappingText.Text = $"Tecla: {_selectedInputKey} — selecione o botão destino";
            else
                PendingMappingText.Text = "Clique em uma tecla acima para começar";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedInputKey == null || _selectedTargetTag == null)
            {
                ShowToast("Selecione uma tecla e um bot\u00e3o de destino.", isError: true);
                return;
            }

            var profile = App.ProfileManager?.CurrentProfile;
            if (profile == null) return;

            profile.KeyMappings.RemoveAll(m => m.InputKey == _selectedInputKey);

            var mapping = CreateMapping(_selectedInputKey, _selectedTargetTag);
            profile.KeyMappings.Add(mapping);

            App.ProfileManager?.SaveProfile(profile);
            App.InputHookService?.UpdateMappings(profile.KeyMappings);

            ShowToast($"{_selectedInputKey} \u2192 {_selectedTargetTag} salvo!", isError: false);

            BtnClear_Click(sender, e);
        }

        private void ShowToast(string message, bool isError)
        {
            // Localiza o painel pai para sobrepor o toast
            var parent = Window.GetWindow(this);
            if (parent == null) return;

            var toast = new System.Windows.Controls.Border
            {
                Padding = new Thickness(16, 10, 16, 10),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 60),
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new System.Windows.Shapes.Path
            {
                Data = isError
                    ? System.Windows.Media.Geometry.Parse("M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z")
                    : System.Windows.Media.Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"),
                Fill = new SolidColorBrush(isError ? Color.FromRgb(0xF8, 0x71, 0x71) : Color.FromRgb(0x22, 0xC5, 0x5E)),
                Width = 14, Height = 14,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var txt = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(icon);
            sp.Children.Add(txt);
            toast.Child = sp;

            // Encontra o grid raiz do MainWindow para sobrepor
            if (parent.Content is Grid rootGrid)
            {
                System.Windows.Controls.Panel.SetZIndex(toast, 999);
                rootGrid.Children.Add(toast);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                toast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                    fadeOut.Completed += (_, _) => rootGrid.Children.Remove(toast);
                    toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };
                timer.Start();
            }
        }

        private KeyMapping CreateMapping(string inputKey, string targetTag)
        {
            // Check if it's an axis mapping
            if (targetTag.StartsWith("Left") || targetTag.StartsWith("Right"))
            {
                ControllerAxis axis = targetTag switch
                {
                    "LeftX+" or "LeftX-"   => ControllerAxis.LeftX,
                    "LeftY+" or "LeftY-"   => ControllerAxis.LeftY,
                    "RightX+" or "RightX-" => ControllerAxis.RightX,
                    "RightY+" or "RightY-" => ControllerAxis.RightY,
                    _ => ControllerAxis.None
                };
                bool isNeg = targetTag.EndsWith("-");

                if (axis != ControllerAxis.None)
                    return new KeyMapping
                    {
                        InputKey = inputKey,
                        TargetButton = ControllerButton.None,
                        AxisMap = new AxisMapping { Axis = axis, Value = 1.0f, IsNegative = isNeg }
                    };
            }

            ControllerButton button = targetTag switch
            {
                "A"          => ControllerButton.A,
                "B"          => ControllerButton.B,
                "X"          => ControllerButton.X,
                "Y"          => ControllerButton.Y,
                "LB"         => ControllerButton.LB,
                "RB"         => ControllerButton.RB,
                "LT"         => ControllerButton.LT,
                "RT"         => ControllerButton.RT,
                "LS"         => ControllerButton.LS,
                "RS"         => ControllerButton.RS,
                "Start"      => ControllerButton.Start,
                "Back"       => ControllerButton.Back,
                "DPadUp"     => ControllerButton.DPadUp,
                "DPadDown"   => ControllerButton.DPadDown,
                "DPadLeft"   => ControllerButton.DPadLeft,
                "DPadRight"  => ControllerButton.DPadRight,
                _            => ControllerButton.A
            };

            return new KeyMapping { InputKey = inputKey, TargetButton = button };
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _selectedInputKey = null;
            _selectedTargetTag = null;
            if (_selectedKeyBtn != null)
            { _selectedKeyBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)); _selectedKeyBtn = null; }
            if (_selectedTargetBtn != null)
            { _selectedTargetBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)); _selectedTargetBtn = null; }
            SelectedKeyText.Text = "Nenhuma tecla selecionada";
            UpdatePendingText();
        }
    }
}
