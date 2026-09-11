using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Component-style tray (UseTrayIcon): the icon is owned by a component and is
// closed automatically when that component unmounts. On top of the SecondWindow
// native menu (hosted by a window of H.NotifyIcon's own making — non-blocking,
// full XAML incl. icons), the window demonstrates three common tray-app
// switches: tray resident on/off, live text updates (i18n), and what closing
// the main window does.

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

ReactorApp.Run(_ =>
{
    // Let ShutdownPolicy.Explicit actually take effect: WinUI otherwise quits
    // the message loop when the last window closes. (Windows App SDK 1.8+)
    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

    var windowKey = WindowKey.Of("main-window");
    var host = new HostFlags();

    var window = ReactorApp.OpenWindow(
        new WindowSpec
        {
            Title = "HNotify Component Sample (UseTrayIcon)",
            Width = 680,
            Height = 500,
            StartPosition = WindowStartPosition.CenterOnPrimary,
            Key = windowKey,
        },
        () => new TrayHostComponent(windowKey, host));

    window.Closing += (_, e) =>
    {
        if (e.Reason != WindowCloseReason.UserClosed) return;

        // Common tray-app rule: with no resident tray icon, closing the window
        // exits the app; with one, the switch decides hide-and-stay vs exit.
        host.ExitOnWindowClose = host.CloseExits || !host.TrayEnabled;
        if (host.ExitOnWindowClose) return; // let it close; Closed ends the process

        e.Cancel = true;
        window.Hide();
    };

    // A cancelled close never raises Closed, so reaching here means the close
    // proceeded — under Explicit policy we own the actual process exit.
    window.Closed += (_, _) =>
    {
        if (host.ExitOnWindowClose) ReactorApp.Exit();
    };
});

/// <summary>
/// State shared between the component (which owns it) and the window close
/// handlers (which live outside the component tree).
/// </summary>
sealed class HostFlags
{
    public bool TrayEnabled { get; set; } = true;
    public bool CloseExits { get; set; }
    public bool ExitOnWindowClose { get; set; }
}

/// <summary>
/// Owns the tray icon through <c>HNotifyComponent.UseTrayIcon</c>. Function
/// components get the same hook via <c>context.UseTrayIcon(spec)</c>.
/// </summary>
sealed class TrayHostComponent : HNotifyComponent
{
    private readonly WindowKey _windowKey;
    private readonly HostFlags _host;

    // The icon is built once: rebuilding byte[] → Icon on every render would
    // defeat the spec diffing that the hook's Update() call performs.
    private readonly System.Drawing.Icon _icon = HNotifyWindowIcon.FromBytes(LoadEmbeddedIcon());

    private HNotifyTrayIconSpec? _spec;
    private bool _specEnglish;
    private bool _specEnabled;
    private bool _hasSpec;

    public TrayHostComponent(WindowKey windowKey, HostFlags host)
    {
        _windowKey = windowKey;
        _host = host;
    }

    public override Element Render()
    {
        var (english, setEnglish) = UseState(false);
        var (trayEnabled, setTrayEnabled) = UseState(true);
        var (closeExits, setCloseExits) = UseState(false);

        // Window close handlers run outside the tree; keep their copy in sync.
        UseEffect(() =>
        {
            _host.TrayEnabled = trayEnabled;
            _host.CloseExits = closeExits;
        }, trayEnabled, closeExits);

        // Opened on first render, closed automatically on unmount. Rebuilt spec
        // flows through the hook's Update() — only changed fields re-apply.
        var tray = UseTrayIcon(BuildSpec(english, trayEnabled));

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            EventHandler onClick = (_, _) => ShowMainWindow();
            tray.Click += onClick;
            return () => tray.Click -= onClick;
        }, tray ?? (object)"no-tray");

        string T(string zh, string en) => english ? en : zh;

        return Border(
            VStack(18,
                TextBlock(T("组件方式托盘（UseTrayIcon）", "Component-style tray (UseTrayIcon)")).FontSize(26).Bold(),
                TextBlock(T(
                    "托盘图标归本组件所有；右键菜单为 SecondWindow 模式（独立窗口承载，非阻塞）。",
                    "The tray icon is owned by this component; the right-click menu uses SecondWindow mode (its own window, non-blocking).")),

                ToggleSwitch(
                    trayEnabled,
                    setTrayEnabled,
                    header: T("托盘图标", "Tray icon"),
                    onContent: T("常驻", "On"),
                    offContent: T("关闭", "Off")),
                TextBlock(T(
                    "关闭后图标从通知区域消失（改 spec 的 IsVisible，经 Update 重放到 shell）。",
                    "Turning it off removes the icon from the notification area (spec IsVisible, re-applied through Update).")),

                ToggleSwitch(
                    english,
                    setEnglish,
                    header: T("界面 / 托盘文案", "UI / tray text"),
                    onContent: "EN",
                    offContent: "中文"),
                TextBlock(T(
                    "切换后 tooltip 与菜单文案即时更新（演示 Update 的增量重放）。",
                    "Toggling updates the tooltip and menu text live (Update re-applies only changed fields).")),

                ToggleSwitch(
                    closeExits,
                    setCloseExits,
                    header: T("关闭主窗口时", "Closing the main window"),
                    onContent: T("退出", "Exit"),
                    offContent: T("隐藏驻留", "Hide")),
                TextBlock(T(
                    "托盘关闭时，无论此开关如何，关窗都会退出进程。",
                    "With the tray icon off, closing the window exits regardless of this switch.")))
            .Padding(24));
    }

    private HNotifyTrayIconSpec BuildSpec(bool english, bool trayEnabled)
    {
        // Rebuild only when something the tray shows actually changed.
        if (_hasSpec && _specEnglish == english && _specEnabled == trayEnabled) return _spec!;
        (_specEnglish, _specEnabled, _hasSpec) = (english, trayEnabled, true);

        var tooltip = english ? "HNotify Component Tray" : "HNotify 组件托盘";
        _spec = new HNotifyTrayIconSpec(_icon, tooltip, WindowKey.Of("component-tray"))
        {
            IsVisible = trayEnabled,
            ContextMenu = HNotifyMenu.Create(
                MenuItem(english ? "Show window" : "显示主窗口", ShowMainWindow, icon: "Home"),
                MenuSeparator(),
                MenuItem(english ? "Exit" : "退出", () => ReactorApp.Exit())),
            MenuActivation = HNotifyMenuActivation.RightClick,
            ContextMenuMode = HNotifyContextMenuMode.SecondWindow,
        };
        return _spec;
    }

    private void ShowMainWindow()
    {
        var window = ReactorApp.FindWindow(_windowKey);
        window?.Show();
        window?.Activate();
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
