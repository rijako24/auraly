using System.Diagnostics;
using System.Drawing.Drawing2D;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Auraly.Desktop;

internal sealed class AuralyDesktopApplicationContext : ApplicationContext
{
    private readonly string root;
    private readonly DesktopConfiguration configuration;
    private readonly CancellationTokenSource shutdown;
    private readonly string data;
    private readonly string sessionToken;
    private readonly string webOrigin;
    private readonly string edgeOrigin;
    private readonly AuralySplashForm splash;
    private readonly Stopwatch splashDuration = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 750 };
    private Process? edge;
    private bool restartingEdge;
    private DateTimeOffset nextEdgeRestartAt;

    public AuralyDesktopApplicationContext(
        string root,
        DesktopConfiguration configuration,
        CancellationTokenSource shutdown)
    {
        this.root = root;
        this.configuration = configuration;
        this.shutdown = shutdown;
        data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Auraly",
            "PosEdge");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        sessionToken = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        webOrigin = $"http://127.0.0.1:{configuration.WebPort}";
        edgeOrigin = $"http://127.0.0.1:{configuration.EdgePort}";
        splash = new AuralySplashForm(root);
        splash.Show();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            splash.SetStage("Iniciando servicios locales", 0);
            Program.StopStaleLocalComponents(root);
            var web = Program.StartWeb(root, configuration, data, webOrigin);
            Program.RegisterChild(web);
            edge = Program.StartEdge(
                root, configuration, data, sessionToken, webOrigin, edgeOrigin);
            Program.RegisterChild(edge);

            await Program.WaitUntilReadyAsync(
                $"{webOrigin}/login", TimeSpan.FromSeconds(45), shutdown.Token);
            splash.SetStage("Preparando Auraly", 1);
            await Program.WaitUntilReadyAsync(
                $"{edgeOrigin}/edge/v1/health", TimeSpan.FromSeconds(45), shutdown.Token);

            splash.SetStage("Abriendo Auraly", 2);
            var target = $"{webOrigin}/login#edgeToken={Uri.EscapeDataString(sessionToken)}";
            var window = new AuralyPosForm(root, data, webOrigin, configuration, shutdown);
            window.FormClosed += (_, _) =>
            {
                shutdown.Cancel();
                ExitThread();
            };
            MainForm = window;
            window.Show();
            await window.InitializeAsync(target, shutdown.Token);
            window.Reveal();
            var remainingSplash = TimeSpan.FromMilliseconds(1400) - splashDuration.Elapsed;
            if (remainingSplash > TimeSpan.Zero)
                await Task.Delay(remainingSplash, shutdown.Token);
            await splash.FadeOutAsync();

            monitor.Tick += async (_, _) => await RestoreEdgeAsync();
            monitor.Start();
        }
        catch (OperationCanceledException)
        {
            ExitThread();
        }
        catch (Exception exception)
        {
            var log = Path.Combine(data, "logs", "desktop-error.log");
            await File.AppendAllTextAsync(
                log,
                $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}");
            splash.ShowFailure(
                "Auraly no pudo iniciar",
                $"Revisa la conexión o informa al supervisor. Detalle técnico: {log}");
        }
    }

    private async Task RestoreEdgeAsync()
    {
        if (restartingEdge || edge is null || !edge.HasExited || shutdown.IsCancellationRequested
            || DateTimeOffset.UtcNow < nextEdgeRestartAt)
            return;
        restartingEdge = true;
        try
        {
            Program.RemoveChild(edge);
            await Task.Delay(1200, shutdown.Token);
            edge = Program.StartEdge(
                root, configuration, data, sessionToken, webOrigin, edgeOrigin);
            Program.RegisterChild(edge);
            await Program.WaitUntilReadyAsync(
                $"{edgeOrigin}/edge/v1/health",
                TimeSpan.FromSeconds(30),
                shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            nextEdgeRestartAt = DateTimeOffset.UtcNow.AddSeconds(5);
            if (edge is not null)
                Program.RemoveChild(edge);
            var log = Path.Combine(data, "logs", "desktop-error.log");
            await File.AppendAllTextAsync(
                log,
                $"{DateTimeOffset.Now:O} No fue posible reiniciar el servicio local. {exception}{Environment.NewLine}");
        }
        finally
        {
            restartingEdge = false;
        }
    }

    protected override void ExitThreadCore()
    {
        monitor.Stop();
        shutdown.Cancel();
        splash.Close();
        Program.StopChildren();
        base.ExitThreadCore();
    }
}

internal sealed class AuralyPosForm : Form
{
    private readonly string data;
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private readonly AuralyDesktopUpdater updater;
    private bool posPresentation;

    public AuralyPosForm(
        string root,
        string data,
        string webOrigin,
        DesktopConfiguration configuration,
        CancellationTokenSource shutdown)
    {
        this.data = data;
        updater = new AuralyDesktopUpdater(
            browser,
            webOrigin,
            configuration,
            data,
            shutdown);
        Text = "Auraly";
        Icon = AuralyDesktopVisuals.LoadIcon();
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1024, 680);
        BackColor = Color.FromArgb(7, 26, 29);
        Opacity = 0;
        Controls.Add(browser);
    }

    public void Reveal()
    {
        Opacity = 1;
        Activate();
    }

    public async Task InitializeAsync(string target, CancellationToken cancellationToken)
    {
        var profile = Path.Combine(data, "webview2");
        Directory.CreateDirectory(profile);
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: profile);
        cancellationToken.ThrowIfCancellationRequested();
        await browser.EnsureCoreWebView2Async(environment);
        browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        browser.CoreWebView2.SourceChanged += (_, _) =>
            ApplyPresentation(browser.Source);
        updater.Start();
        browser.CoreWebView2.Navigate(target);
    }

    private void ApplyPresentation(Uri? source)
    {
        var isPos = source is not null &&
                    string.Equals(source.AbsolutePath.TrimEnd('/'), "/pos",
                        StringComparison.OrdinalIgnoreCase);
        if (isPos == posPresentation) return;
        posPresentation = isPos;
        var screen = Screen.FromHandle(Handle);
        SuspendLayout();
        if (isPos)
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            SetBounds(
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height,
                BoundsSpecified.All);
            TopMost = true;
        }
        else
        {
            TopMost = false;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = screen.WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        ResumeLayout(performLayout: true);
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            updater.Stop();
            browser.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class AuralySplashForm : Form
{
    private readonly Image? logo;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    private string status = "Iniciando Auraly";
    private string? detail;
    private int stage;
    private float phase;
    private bool failed;

    public AuralySplashForm(string root)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 390);
        BackColor = Color.FromArgb(7, 26, 29);
        ShowInTaskbar = true;
        TopMost = true;
        DoubleBuffered = true;
        Icon = AuralyDesktopVisuals.LoadIcon();
        var logoPath = Path.Combine(root, "auraly-splash-logo.png");
        if (File.Exists(logoPath)) logo = Image.FromFile(logoPath);
        animation.Tick += (_, _) =>
        {
            phase = (phase + .016f) % 1f;
            Invalidate();
        };
        animation.Start();
    }

    public void SetStage(string value, int valueStage)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStage(value, valueStage));
            return;
        }
        status = value;
        stage = valueStage;
        Invalidate();
    }

    public void ShowFailure(string title, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowFailure(title, message));
            return;
        }
        failed = true;
        status = title;
        detail = message;
        TopMost = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Invalidate();
    }

    public async Task FadeOutAsync()
    {
        while (!IsDisposed && Opacity > .08)
        {
            Opacity = Math.Max(0, Opacity - .1);
            await Task.Delay(18);
        }
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(5, 21, 24),
            Color.FromArgb(11, 50, 52),
            35f);
        graphics.FillRectangle(background, ClientRectangle);

        var glowSize = 250 + (int)(Math.Sin(phase * Math.PI * 2) * 12);
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse((Width - glowSize) / 2, 25, glowSize, glowSize);
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(58, 94, 234, 212),
            SurroundColors = [Color.FromArgb(0, 94, 234, 212)]
        };
        graphics.FillPath(glow, glowPath);

        if (logo is not null)
            graphics.DrawImage(logo, new Rectangle((Width - 92) / 2, 82, 92, 92));

        using var brandFont = new Font("Segoe UI Variable Display", 24, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI Variable Text", 10, FontStyle.Regular);
        using var statusFont = new Font("Segoe UI Variable Text", 10, FontStyle.Bold);
        DrawCentered(graphics, "AURALY", brandFont, Color.White, 190);
        DrawCentered(graphics, "Comercio conectado. Caja resistente.", subtitleFont,
            Color.FromArgb(185, 214, 218), 235);

        if (failed)
        {
            DrawCentered(graphics, status, statusFont, Color.FromArgb(255, 185, 175), 294);
            if (!string.IsNullOrWhiteSpace(detail))
                DrawCentered(graphics, detail, subtitleFont,
                    Color.FromArgb(220, 232, 234), 325);
            return;
        }

        var startX = Width / 2 - 62;
        for (var index = 0; index < 3; index++)
        {
            var color = index < stage
                ? Color.FromArgb(94, 234, 212)
                : index == stage
                    ? Color.FromArgb(255, 222, 128)
                    : Color.FromArgb(55, 105, 110);
            using var brush = new SolidBrush(color);
            var pulse = index == stage
                ? 5 + (int)(Math.Sin(phase * Math.PI * 2) * 2)
                : 5;
            graphics.FillEllipse(brush, startX + index * 62 - pulse, 288 - pulse,
                pulse * 2, pulse * 2);
            if (index < 2)
            {
                using var line = new Pen(
                    index < stage ? Color.FromArgb(94, 234, 212) : Color.FromArgb(55, 105, 110),
                    2);
                graphics.DrawLine(line, startX + index * 62 + 7, 288,
                    startX + (index + 1) * 62 - 7, 288);
            }
        }
        DrawCentered(graphics, status, statusFont, Color.FromArgb(220, 239, 240), 315);
    }

    private static void DrawCentered(
        Graphics graphics,
        string value,
        Font font,
        Color color,
        float y)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var lineHeight = Math.Max(52f, font.GetHeight(graphics) + 18f);
        graphics.DrawString(value, font, brush, new RectangleF(35, y, 550, lineHeight), format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose();
            logo?.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class AuralyDesktopVisuals
{
    public static Icon LoadIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath)
               ?? (Icon)SystemIcons.Application.Clone();
    }
}
