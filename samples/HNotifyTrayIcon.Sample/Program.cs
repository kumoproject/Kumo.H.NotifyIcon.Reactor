using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// The call sites below are written exactly like Reactor preview15's tray API;
// the only differences are the HNotify* type names (HNotifyApp / HNotifyTrayIcon /
// HNotifyWindowIcon) standing in for ReactorApp / ReactorTrayIcon / WindowIcon,
// and the tray being backed by H.NotifyIcon (no native Shell_NotifyIcon code
// inside this app; the menu is the backend's own menu system, not preview15's
// Reactor-rendered flyout).

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

ReactorApp.Run(_ =>
{
    // WinUI's default DispatcherShutdownMode (OnLastWindowClose) makes the XAML
    // runtime quit the message pump when the last window closes — that is why
    // ShutdownPolicy.Explicit alone could not keep the process alive. Flip the
    // platform switch so Explicit behaves as documented: only ReactorApp.Exit()
    // ends the app. (Available since Windows App SDK 1.8.)
    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

    var mainWindowKey = WindowKey.Of("main-window");
    ReactorWindow? mainWindow = null;
    HNotifyTrayIcon? tray = null; // assigned below, before any window can open

    void OpenMainWindow()
    {
        var existing = ReactorApp.FindWindow(mainWindowKey);
        if (existing is not null)
        {
            mainWindow = existing;
            existing.Show();
            existing.Activate();
            return;
        }

        var opened = ReactorApp.OpenWindow(
            new WindowSpec
            {
                Title = "HNotify Tray Icon (H.NotifyIcon)",
                Width = 640,
                Height = 420,
                StartPosition = WindowStartPosition.CenterOnPrimary,
                Key = mainWindowKey,
            },
            // The handle is assigned right after OpenTrayIcon (below); a window
            // can only open later (click / startup), so it is never null here.
            () => new MainWindowContent(tray!));

        mainWindow = opened;
        opened.Closed += (_, _) =>
        {
            if (ReferenceEquals(mainWindow, opened))
                mainWindow = null;
        };
    }

    // Icon straight from the embedded resource — no sidecar .ico file, no temp
    // file. preview15 spells this WindowIcon.FromBytes; preview14 has no such
    // factory, so the library ships an identically shaped stand-in.
    using var iconStream =
        typeof(Program).Assembly.GetManifestResourceStream("HNotifyTrayIcon.Sample.app.ico")
        ?? throw new InvalidOperationException("Embedded icon resource 'HNotifyTrayIcon.Sample.app.ico' not found.");

    // The menu is H.NotifyIcon's own context menu, fed with Reactor's menu DSL
    // (Factories.MenuItem / MenuSeparator) and converted by the library into the
    // MenuFlyout the backend needs. --menu-second-window switches the backend
    // from the native Win32 popup to its window-hosted mode; icons render in the
    // window-hosted mode, the native popup carries text only.
    var trayHandle = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        HNotifyWindowIcon.FromBytes(iconStream),
        "HNotify Tray Icon",
        WindowKey.Of("main-tray"))
    {
        ContextMenu = HNotifyMenu.Create(
            MenuItem("重新打开主窗口", OpenMainWindow, icon: "Home"),
            MenuSeparator(),
            MenuItem("退出", () => ReactorApp.Exit())),
        MenuActivation = HNotifyMenuActivation.RightClick,
        ContextMenuMode = HasArgument("--menu-second-window")
            ? HNotifyContextMenuMode.SecondWindow
            : HNotifyContextMenuMode.PopupMenu,
    });
    tray = trayHandle;

    // Click/DoubleClick remain app-level events; the right-click menu is opened
    // by the backend itself (MenuActivation).
    trayHandle.Click += (_, _) => OpenMainWindow();
    trayHandle.DoubleClick += (_, _) => OpenMainWindow();

    if (!HasArgument("--silent"))
        OpenMainWindow();
});

bool HasArgument(string value) =>
    Environment.GetCommandLineArgs().Contains(value, StringComparer.OrdinalIgnoreCase);

/// <summary>
/// Main window content. Drives the tray icon through <see cref="HNotifyTrayIcon.Update"/>:
/// every action builds a full spec from the current one (record <c>with</c>), and
/// Update re-applies only the fields that actually changed.
/// </summary>
sealed class MainWindowContent(HNotifyTrayIcon tray) : Component
{
    public override Element Render()
    {
        var (tooltip, setTooltip) = UseState(tray.Spec.Tooltip);
        var (isVisible, setIsVisible) = UseState(tray.Spec.IsVisible);

        return Border(
            VStack(12,
                TextBlock("HNotify Tray Icon (H.NotifyIcon)").FontSize(28).Bold(),
                TextBlock("左键打开主窗口；右键弹出 H.NotifyIcon 原生菜单。"),
                TextBlock("Update 演示：下面每项操作都会基于当前 spec 造一份新 spec —— 只有变化的字段会被重放到托盘。"),

                Button("改 Tooltip（带时间戳）", () =>
                {
                    var next = tray.Spec with { Tooltip = $"HNotify Tray Icon — {DateTime.Now:HH:mm:ss}" };
                    tray.Update(next);
                    setTooltip(next.Tooltip);
                }),

                Button(
                    isVisible ? "隐藏图标（IsVisible = false）" : "显示图标（IsVisible = true）",
                    () =>
                    {
                        var next = tray.Spec with { IsVisible = !tray.Spec.IsVisible };
                        tray.Update(next);
                        setIsVisible(next.IsVisible);
                    })
                    .AutomationName("切换托盘图标的显示/隐藏"),

                TextBlock($"当前 Tooltip：{tooltip}    图标可见：{isVisible}"))
            .Padding(24));
    }
}
