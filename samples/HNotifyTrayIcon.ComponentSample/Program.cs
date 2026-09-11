using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Two fully independent units — neither owns the other's lifetime:
//
//   tray unit   = a never-shown host window (WindowSpec.ActivateOnOpen = false)
//                 + TrayHostComponent (HNotifyComponent.UseTrayIcon inside)
//   main window = its own window + MainPanel component
//
// Each can be created and destroyed any number of times without touching the
// other. When BOTH are gone the process exits; destroying either unit releases
// everything that unit created, so repeated cycles stay flat.

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

ReactorApp.Run(_ =>
{
    // Let ShutdownPolicy.Explicit actually take effect: WinUI otherwise quits
    // the message loop when the last window closes. (Windows App SDK 1.8+)
    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

    var host = new AppHost();

    host.EnableTray();     // independent unit #1
    host.OpenMainWindow(); // independent unit #2
});

/// <summary>
/// Application-level orchestrator: creates/destroys the two independent units
/// and applies the only global rule — exit once nothing is left.
/// </summary>
sealed class AppHost
{
    private static readonly WindowKey TrayHostKey = WindowKey.Of("tray-host");
    private static readonly WindowKey MainWindowKey = WindowKey.Of("main-window");

    private ReactorWindow? _trayHostWindow;
    private ReactorWindow? _mainWindow;
    private bool _exiting;

    public int TrayUnitCreations { get; private set; }
    public int MainWindowCreations { get; private set; }

    public bool TrayActive => _trayHostWindow is not null;

    // ── unit #1: the tray ─────────────────────────────────────────────────

    /// <summary>
    /// Creates the tray unit: a hidden host window whose component owns the
    /// tray icon. The window is never shown — ActivateOnOpen = false mounts the
    /// tree (hooks run) without ever activating/displaying the window.
    /// </summary>
    public void EnableTray()
    {
        if (_trayHostWindow is not null) return;

        _trayHostWindow = ReactorApp.OpenWindow(
            new WindowSpec
            {
                Title = "Tray host (hidden, never shown)",
                Key = TrayHostKey,
                ActivateOnOpen = false,
                ShowInSwitcher = false,
            },
            () => new TrayHostComponent(this));

        TrayUnitCreations++;
    }

    /// <summary>
    /// Destroys the tray unit. Closing the host window unmounts the component,
    /// so UseTrayIcon's cleanup closes the tray icon and the component disposes
    /// its own bitmap — the unit releases everything it created.
    /// </summary>
    public void DisableTray()
    {
        var window = _trayHostWindow;
        if (window is null) return;

        _trayHostWindow = null;
        window.Close();
        MaybeExit();
    }

    // ── unit #2: the main window ──────────────────────────────────────────

    public void OpenMainWindow()
    {
        if (_mainWindow is { } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }

        var window = ReactorApp.OpenWindow(
            new WindowSpec
            {
                Title = "HNotify Independent Units Sample",
                Width = 720,
                Height = 520,
                StartPosition = WindowStartPosition.CenterOnPrimary,
                Key = MainWindowKey,
            },
            () => new MainPanel(this));

        _mainWindow = window;
        MainWindowCreations++;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mainWindow, window)) _mainWindow = null;
            MaybeExit();
        };
    }

    /// <summary>Destroys the main window (real close — nothing is hidden).</summary>
    public void CloseMainWindow() => _mainWindow?.Close();

    public void ExitApp()
    {
        _exiting = true;
        DisableTray();
        ReactorApp.Exit();
    }

    /// <summary>The only global lifetime rule: exit when both units are gone.</summary>
    private void MaybeExit()
    {
        if (_exiting) return;
        if (_mainWindow is null && _trayHostWindow is null) ReactorApp.Exit();
    }
}

/// <summary>
/// The tray unit's root. Mounted in the never-shown host window; owns the tray
/// icon via UseTrayIcon. Mount → icon exists; unmount (host window closed) →
/// icon closed and the caller-owned bitmap disposed.
/// </summary>
sealed class TrayHostComponent : HNotifyComponent
{
    private readonly System.Drawing.Icon _icon = HNotifyWindowIcon.FromBytes(LoadEmbeddedIcon());
    private readonly HNotifyTrayIconSpec _spec;

    public TrayHostComponent(AppHost host)
    {
        _spec = new HNotifyTrayIconSpec(_icon, "HNotify independent tray", WindowKey.Of("tray"))
        {
            ContextMenu = HNotifyMenu.Create(
                MenuItem("打开主窗口", host.OpenMainWindow),
                MenuItem("关闭主窗口", host.CloseMainWindow),
                MenuSeparator(),
                MenuItem("退出", host.ExitApp)),
            MenuActivation = HNotifyMenuActivation.RightClick,

            // PopupMenu, not SecondWindow: measured — cycling a TaskbarIcon with
            // SecondWindow mode retains ~60 KB per create/destroy (H.NotifyIcon
            // preview mode), while PopupMenu cycles flat. This unit exists to be
            // created/destroyed repeatedly, so popup mode it is.
            // Trade-off: popup mode renders text-only items (no icons).
            ContextMenuMode = HNotifyContextMenuMode.PopupMenu,
        };
    }

    public override Element Render()
    {
        // Opened on first render; closed by the hook's cleanup on unmount.
        UseTrayIcon(_spec);

        // The bitmap is caller-owned (the tray gets a clone) — release it with
        // the unit so repeated create/destroy cycles stay flat.
        UseEffect(() =>
        {
            return () => _icon.Dispose();
        });

        // The host window is never shown; the component has nothing to draw.
        return TextBlock(string.Empty);
    }

    private static byte[] LoadEmbeddedIcon()
    {
        using var stream =
            typeof(TrayHostComponent).Assembly.GetManifestResourceStream("HNotifyTrayIcon.ComponentSample.app.ico")
            ?? throw new InvalidOperationException("Embedded icon resource 'HNotifyTrayIcon.ComponentSample.app.ico' not found.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>
/// The main window's content. Knows nothing about tray internals — it only
/// calls the orchestrator's unit-level operations.
/// </summary>
sealed class MainPanel(AppHost host) : Component
{
    public override Element Render()
    {
        var (trayOn, setTrayOn) = UseState(host.TrayActive);

        return Border(
            VStack(18,
                TextBlock("两个互不依赖的单元").FontSize(26).Bold(),
                TextBlock("主窗口与托盘各自独立：关掉一个不影响另一个，每个都可反复创建、销毁。"),
                TextBlock("托盘单元 = 隐藏宿主窗口（ActivateOnOpen=false，从未显示）+ UseTrayIcon 组件。"),
                TextBlock("两者都关闭后，进程自动退出——这是唯一的全局规则。"),

                ToggleSwitch(
                    trayOn,
                    on =>
                    {
                        if (on) host.EnableTray(); else host.DisableTray();
                        setTrayOn(on);
                    },
                    header: "托盘单元",
                    onContent: "启用",
                    offContent: "停用"),

                Button("销毁主窗口（托盘不受影响）", host.CloseMainWindow),
                TextBlock("重新打开主窗口：右键托盘图标 → 打开主窗口。"),

                TextBlock($"主窗口创建次数：{host.MainWindowCreations}    托盘单元创建次数：{host.TrayUnitCreations}"))
            .Padding(24));
    }
}
