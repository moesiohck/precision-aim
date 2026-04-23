using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using AimAssistPro.Services;

namespace AimAssistPro.Views
{
    public partial class StartupCheckWindow : Window
    {
        // Cores
        private static readonly SolidColorBrush BrushOk      = new(Color.FromRgb(0x22, 0xC5, 0x5E));  // verde sucesso
        private static readonly SolidColorBrush BrushError    = new(Color.FromRgb(0xEF, 0x44, 0x44));  // vermelho erro
        private static readonly SolidColorBrush BrushPending  = new(Color.FromRgb(0x78, 0x78, 0x78));  // cinza neutro
        private static readonly SolidColorBrush BrushAccent   = new(Color.FromRgb(0xC8, 0xC8, 0xC8));  // branco/prata

        // Ícone check (✓)
        private const string IconOk    = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z";
        // Ícone erro (✕)
        private const string IconError = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z";
        // Ícone warn (clock/pendente)
        private const string IconWarn  = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M12.5,7H11V13L16.25,16.15L17,14.92L12.5,12.25V7Z";
        // Ícone spinner (círculo horário)
        private const string IconSpin  = "M12,4V2A10,10 0 0,0 2,12H4A8,8 0 0,1 12,4Z";
        private static readonly SolidColorBrush BrushWarn = new(Color.FromRgb(0xF5, 0x9E, 0x0B)); // amarelo aviso

        public StartupCheckWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RunChecksAsync();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Fluxo principal
        // ═══════════════════════════════════════════════════════════════
        private async Task RunChecksAsync()
        {
            AnimateProgress(0);

            // ── 1. Atualização (simulado — apenas visual) ──────────────
            SetItemSpinning(PathUpdates, IconUpdates);
            SetSubtitle("Verificando atualizações...");
            await Task.Delay(600);
            SetItemOk(PathUpdates, TxtUpdates, IconUpdates, "Atualizado");
            AnimateProgress(25);
            await Task.Delay(200);

            // ── 2. Interception ────────────────────────────────────────
            SetItemSpinning(PathInterception, IconInterception);
            SetSubtitle("Verificando Interception Driver...");
            await Task.Delay(600);

            bool icOk = await Task.Run(() => DriverStatusService.IsInterceptionInstalled());
            // Verifica se está instalado mas aguardando reboot (flag no registro)
            bool icPendingReboot = !icOk && await Task.Run(() => DriverStatusService.IsInterceptionPendingReboot());

            if (icOk)
                SetItemOk(PathInterception, TxtInterception, IconInterception, "Instalado ✓");
            else if (icPendingReboot)
                SetItemWarn(PathInterception, TxtInterception, IconInterception, "Instalado — reinicialização pendente ⏳");
            else
                SetItemError(PathInterception, TxtInterception, IconInterception, "Não instalado");
            AnimateProgress(50);
            await Task.Delay(250);

            // ── 3. ViGEmBus ────────────────────────────────────────────
            SetItemSpinning(PathViGEm, IconViGEm);
            SetSubtitle("Verificando ViGEmBus Driver...");
            await Task.Delay(600);
            bool veOk = await Task.Run(() => DriverStatusService.IsViGEmInstalled());
            if (veOk)
                SetItemOk(PathViGEm, TxtViGEm, IconViGEm, "Instalado ✓");
            else
                SetItemError(PathViGEm, TxtViGEm, IconViGEm, "Não instalado");
            AnimateProgress(75);
            await Task.Delay(250);

            // ── 4. Resultado final ─────────────────────────────────────
            // Considera OK se instalado OU pendente de reboot (já instalou mas precisa reiniciar)
            bool icEffectiveOk = icOk || icPendingReboot;
            bool allOk = icEffectiveOk && veOk;
            AnimateProgress(100);

            if (allOk)
            {
                if (icPendingReboot)
                    SetItemWarn(PathFinal, TxtFinal, IconFinal, "Reinicialização pendente para Interception");
                else
                    SetItemOk(PathFinal, TxtFinal, IconFinal, "Todos os componentes OK");
                SetSubtitle(icPendingReboot ? "Reinicie o PC para ativar o Interception." : "Sistema pronto!");
                await Task.Delay(1200); // tempo suficiente para ler o resultado
                DialogResult = true;
                Close();
            }
            else
            {
                SetItemError(PathFinal, TxtFinal, IconFinal, "Drivers ausentes — ação necessária");
                SetSubtitle("Problemas encontrados.");
                await Task.Delay(500);

                // Pergunta se quer instalar
                await OfferInstallAsync(icOk, veOk);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Instala sequencialmente e pede reboot ao final
        // ═══════════════════════════════════════════════════════════════
        private async Task OfferInstallAsync(bool icOk, bool veOk)
        {
            // Monta lista de componentes ausentes
            var missing = new System.Text.StringBuilder();
            if (!veOk) missing.AppendLine("  ⬡  ViGEmBus Driver  —  emulação de controle virtual");
            if (!icOk) missing.AppendLine("  ⬡  Interception Driver  —  captura de teclado/mouse");

            bool? install = CustomDialog.ShowInstallPrompt(missing.ToString().TrimEnd(), owner: this);

            if (install != true)
            {
                // Usuário optou por pular — continua sem instalar
                DialogResult = true;
                Close();
                return;
            }

            // ── Instala ViGEmBus primeiro ──────────────────────────────
            bool needsReboot = false;

            if (!veOk)
            {
                SetSubtitle("Instalando ViGEmBus...");
                SetItemSpinning(PathViGEm, IconViGEm);
                TxtViGEm.Text = "Instalando, aguarde...";

                var result = await DriverInstaller.InstallViGEmAsync(
                    new Progress<string>(msg2 => Dispatcher.Invoke(() => TxtViGEm.Text = msg2)),
                    CancellationToken.None);

                if (result.Success)
                {
                    SetItemOk(PathViGEm, TxtViGEm, IconViGEm, "Instalado com sucesso ✓");
                    if (result.NeedsReboot) needsReboot = true;
                }
                else
                {
                    SetItemError(PathViGEm, TxtViGEm, IconViGEm, "Falha na instalação");
                    CustomDialog.ShowError("Falha — ViGEmBus Driver", result.Message, owner: this);
                }

                await Task.Delay(400);
            }

            // ── Instala Interception em seguida ────────────────────────
            if (!icOk)
            {
                SetSubtitle("Instalando Interception...");
                SetItemSpinning(PathInterception, IconInterception);
                TxtInterception.Text = "Instalando, aguarde...";

                var result = await DriverInstaller.InstallInterceptionAsync(
                    new Progress<string>(msg2 => Dispatcher.Invoke(() => TxtInterception.Text = msg2)),
                    CancellationToken.None);

                if (result.Success)
                {
                    SetItemOk(PathInterception, TxtInterception, IconInterception, "Instalado com sucesso ✓");
                    if (result.NeedsReboot) needsReboot = true;
                }
                else
                {
                    SetItemError(PathInterception, TxtInterception, IconInterception, "Falha na instalação");
                    CustomDialog.ShowError("Falha — Interception Driver", result.Message, owner: this);
                }

                await Task.Delay(400);
            }

            // ── Pede reboot ────────────────────────────────────────────
            if (needsReboot)
            {
                bool? reboot = CustomDialog.ShowRebootPrompt(owner: this);

                if (reboot == true)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = "shutdown.exe",
                        Arguments       = "/r /t 5 /c \"AimAssistPro: reiniciando para ativar drivers\"",
                        UseShellExecute = false,
                        CreateNoWindow  = true
                    });
                    Application.Current.Shutdown();
                    return;
                }
            }

            DialogResult = true;
            Close();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Helpers de UI
        // ═══════════════════════════════════════════════════════════════
        private void SetSubtitle(string text) =>
            TxtSubtitle.Text = text;

        private void SetItemOk(System.Windows.Shapes.Path path, System.Windows.Controls.TextBlock label,
                               Border icon, string text)
        {
            path.RenderTransform = null; // Para de girar
            path.Data       = Geometry.Parse(IconOk);
            path.Fill       = BrushOk;
            icon.Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x2B, 0x1A));  // fundo verde escuríssimo
            label.Foreground = BrushOk;
            label.Text      = text;
        }

        private void SetItemWarn(System.Windows.Shapes.Path path, System.Windows.Controls.TextBlock label,
                                 Border icon, string text)
        {
            path.RenderTransform = null; // Para de girar
            path.Data        = Geometry.Parse(IconWarn);
            path.Fill        = BrushWarn;
            icon.Background  = new SolidColorBrush(Color.FromRgb(0x2B, 0x1E, 0x05));
            label.Foreground = BrushWarn;
            label.Text       = text;
        }

        private void SetItemError(System.Windows.Shapes.Path path, System.Windows.Controls.TextBlock label,
                                  Border icon, string text)
        {
            path.RenderTransform = null; // Para de girar
            path.Data       = Geometry.Parse(IconError);
            path.Fill       = BrushError;
            icon.Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x0D, 0x0D));  // fundo vermelho escuríssimo
            label.Foreground = BrushError;
            label.Text      = text;
        }

        private void SetItemSpinning(System.Windows.Shapes.Path path, Border icon)
        {
            path.Data       = Geometry.Parse(IconSpin);
            path.Fill       = BrushAccent;
            icon.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));  // fundo cinza escuro neutro

            // Animação de rotação
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            var rot = new RotateTransform(0);
            path.RenderTransform = rot;
            var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            rot.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void AnimateProgress(double percent)
        {
            // Calcula largura total disponível (janela 520 - margens 64)
            double totalWidth = 456;
            var anim = new DoubleAnimation(ProgressBar.Width, totalWidth * percent / 100,
                new Duration(TimeSpan.FromMilliseconds(400)));
            ProgressBar.BeginAnimation(WidthProperty, anim);
        }
    }
}
