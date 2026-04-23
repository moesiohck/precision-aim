using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AimAssistPro
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _statusTimer;
        private readonly DateTime _startTime = DateTime.Now;
        private Button? _activeTab;

        private Views.DashboardView? _dashboardView;
        private Views.ControllerView? _controllerView;
        private Views.MacrosView? _macrosView;
        private Views.SettingsView? _settingsView;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => UpdateStatusBar();
            _statusTimer.Start();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Auto-ativar com chave HWID se não estiver ativado - Omitido pois agora usamos o banco online via JWT!
            // if (App.LicenseManager != null && !App.LicenseManager.IsActivated)
            // { ... }

            // Configurar perfil visual
            var username = App.CurrentUsername;
            if (string.IsNullOrEmpty(username)) username = "User";
            ProfileNameText.Text = username;
            ProfileInitialsText.Text = username[0].ToString().ToUpper();
            
            // Default to dashboard — sempre vai direto ao dashboard
            SetActiveTab(TabDashboard);
            MainFrame.Navigate(new Views.DashboardView());

            // ─── Setup RAWINPUT for accurate mouse delta capture in games ───
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);
            RegisterRawMouse(new WindowInteropHelper(this).Handle);

            // Remove DWM shadow behind rounded frameless window
            RemoveDwmShadow();

            // Aplica clip inicial (caso SizeChanged não dispare antes do Loaded)
            ApplyRoundedClip();
        }

        // Clip cirúrgico — garante cantos arredondados mesmo com filhos de fundo sólido
        private void MainBorder_SizeChanged(object sender, SizeChangedEventArgs e)
            => ApplyRoundedClip();

        private void ApplyRoundedClip()
        {
            if (MainBorder == null) return;
            const double r = 14;
            MainBorder.Clip = new System.Windows.Media.RectangleGeometry(
                new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), r, r);
        }

        // ─── Remove CS_DROPSHADOW + clip arredondado ─────────────────────────
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int nIndex, IntPtr dwNewLong);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        private const int GCL_STYLE             = -26;
        private const int CS_DROPSHADOW         = 0x00020000;
        private const int DWMWA_NCRENDERING_POLICY = 2;
        private const int DWMNCRP_DISABLED      = 1;

        private void RemoveDwmShadow()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;

                // Remove sombra da classe Win32 (64-bit correto)
                IntPtr style = GetClassLongPtr(hwnd, GCL_STYLE);
                SetClassLongPtr(hwnd, GCL_STYLE, (IntPtr)(style.ToInt64() & ~CS_DROPSHADOW));

                // Desabilita renderização não-cliente do DWM
                int policy = DWMNCRP_DISABLED;
                DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            }
            catch { }
        }

        // ─── Raw Input (Mouse) ────────────────────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [DllImport("user32.dll")]
        internal static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        internal static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RAWMOUSE
        {
            public ushort usFlags;
            public uint ulButtons;
            public uint ulRawButtons;
            public int lLastX;
            public int lLastY;
            public uint ulExtraInformation;
        }

        private void RegisterRawMouse(IntPtr hwnd)
        {
            var rid = new RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01; // HID_USAGE_PAGE_GENERIC
            rid[0].usUsage = 0x02;     // HID_USAGE_GENERIC_MOUSE
            rid[0].dwFlags = 0x00000100; // RIDEV_INPUTSINK (Receber mesmo em background - vital para o overlay)
            rid[0].hwndTarget = hwnd;

            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(rid[0]));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_INPUT = 0x00FF;
            if (msg == WM_INPUT)
            {
                uint size = 0;
                GetRawInputData(lParam, 0x10000003 /* RID_INPUT */, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                if (size > 0)
                {
                    IntPtr pData = Marshal.AllocHGlobal((int)size);
                    if (GetRawInputData(lParam, 0x10000003, pData, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == size)
                    {
                        var header = Marshal.PtrToStructure<RAWINPUTHEADER>(pData);
                        if (header.dwType == 0) // RIM_TYPEMOUSE
                        {
                            var mouse = Marshal.PtrToStructure<RAWMOUSE>(pData + Marshal.SizeOf(typeof(RAWINPUTHEADER)));
                            // Passa as verdadeiras coordenadas delta lidas diretas do hardware para o nosso serviço AimAssist
                            App.InputHookService?.FeedRawMouse(mouse.lLastX, mouse.lLastY);
                        }
                    }
                    Marshal.FreeHGlobal(pData);
                }
            }
            return IntPtr.Zero;
        }

        // ─── Tab Navigation ──────────────────────────────────────────────────
        private void TabDashboard_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab(TabDashboard);
            _dashboardView ??= new Views.DashboardView();
            MainFrame.Navigate(_dashboardView);
        }

        private void TabRemapear_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab(TabRemapear);
            _controllerView ??= new Views.ControllerView();
            MainFrame.Navigate(_controllerView);
        }

        private void TabMacros_Click(object sender, RoutedEventArgs e)
        {
            SetActiveTab(TabMacros);
            _macrosView ??= new Views.MacrosView();
            MainFrame.Navigate(_macrosView);
        }

        private void ClearActiveTabs()
        {
            TabDashboard.Tag = null;
            TabRemapear.Tag = null;
            if (TabMacros != null) TabMacros.Tag = null;
            _activeTab = null;
        }

        private void SetActiveTab(Button tab)
        {
            ClearActiveTabs();
            tab.Tag = "active";
            _activeTab = tab;
        }

        public void NavigateTo(string view)
        {
            switch (view.ToLower())
            {
                case "dashboard":   TabDashboard_Click(this, new RoutedEventArgs()); break;
                case "remapear":
                case "controller":  TabRemapear_Click(this, new RoutedEventArgs()); break;
                case "macros":
                    TabMacros_Click(this, new RoutedEventArgs()); break;
                case "settings":    
                    ClearActiveTabs();
                    _settingsView ??= new Views.SettingsView();
                    MainFrame.Navigate(_settingsView); 
                    break;
                case "keyboard":    
                    SetActiveTab(TabRemapear);
                    MainFrame.Navigate(new Views.KeyboardView()); // KeyboardView maybe doesn't need cache, but can be cached if wanted
                    break;
                case "recoil":
                    ClearActiveTabs();
                    _settingsView ??= new Views.SettingsView();
                    MainFrame.Navigate(_settingsView); // Treat recoil strictly as config screen
                    MainFrame.Navigate(new Views.AIRecoilView());
                    break;
            }
        }

        public void NavigateToPage(UserControl page)
        {
            ClearActiveTabs();
            MainFrame.Navigate(page);
        }

        // ─── Status Bar Updates ──────────────────────────────────────────────
        private void UpdateStatusBar()
        {
            // Logic moved to settings/controller specific views
        }

        // ─── Window Chrome ───────────────────────────────────────────────────
        private void BtnDiscord_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.gg/aimassistpro", // To be updated if user wants another URI
                UseShellExecute = true
            });
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            ClearActiveTabs();
            _settingsView ??= new Views.SettingsView();
            MainFrame.Navigate(_settingsView);
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ─── Profile Menu ────────────────────────────────────────────────────
        private void ProfileAvatar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent the click from bubbling and immediately re-closing

            if (ProfilePopup == null) return;

            if (ProfilePopup.IsOpen)
            {
                ProfilePopup.IsOpen = false;
                return;
            }

            // Populate data
            PopupUsername.Text = App.CurrentUsername;
            PopupEmail.Text = !string.IsNullOrEmpty(App.CurrentEmail) 
                ? App.CurrentEmail 
                : App.CurrentUsername;

            var license = App.LicenseManager?.CurrentLicense;
            if (license != null)
            {
                PopupPlan.Text = license.PlanName?.ToUpper() ?? "FREE";
                if (license.PlanName?.Contains("Lifetime", StringComparison.OrdinalIgnoreCase) == true)
                    PopupExpires.Text = "Licença vitalícia";
                else
                {
                    var span = license.ExpiresAt - DateTime.Now;
                    int days = Math.Max(0, (int)span.TotalDays);
                    PopupExpires.Text = $"Expira: {days} dias";
                }
            }

            // Key + HWID
            PopupKey.Text  = license?.Key ?? "—";
            PopupHwid.Text = Services.LicenseManager.GetHardwareId();

            ProfilePopup.IsOpen = true;
        }

        private void CloseProfilePopup_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilePopup != null)
                ProfilePopup.IsOpen = false;
        }

        private void MenuLogout_Click(object sender, RoutedEventArgs e)
        {
            var result = Views.CustomDialog.ShowConfirm(
                "Sair da Conta",
                "Tem certeza que deseja sair da conta?\nVocê precisará fazer login novamente.",
                confirmLabel: "Sim, sair",
                cancelLabel:  "Cancelar",
                type:         Views.CustomDialog.DialogType.Warning,
                owner:        this);

            if (result == true)
            {
                Services.LicenseManager.ClearSession();

                // Reinicia a aplicação para forçar tela de login
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    System.Diagnostics.Process.Start(processPath);
                }

                Application.Current.Shutdown();
            }
        }
    }
}
