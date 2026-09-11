using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

List<string> exArgs = ["--native-menu", "--menu-second-window"];

// The call sites below are written exactly like Reactor preview15's tray API;
// the only differences are the HNotify* type names (HNotifyApp / HNotifyTrayIcon /
// HNotifyWindowIcon) standing in for ReactorApp / ReactorTrayIcon / WindowIcon,
// and the tray being backed by H.NotifyIcon (no native Shell_NotifyIcon code
// inside this app, no flyout host window of Reactor's own).

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
            () => new MainWindowContent());

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

    // The H.NotifyIcon native menu is opt-in: --native-menu swaps the Reactor
    // flyout for the backend's own context menu; --menu-second-window renders it
    // in H.NotifyIcon's window-hosted mode instead of the native Win32 popup.
    var nativeMenu = HasArgument("--native-menu");

    var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        HNotifyWindowIcon.FromBytes(iconStream),
        "HNotify Tray Icon",
        WindowKey.Of("main-tray"))
    {
        // Reactor's own menu DSL (Factories.MenuItem / MenuSeparator), converted
        // by the library into the MenuFlyout H.NotifyIcon needs. Icons render in
        // the window-hosted mode; the native popup carries text only.
        ContextMenu = nativeMenu
            ? HNotifyMenu.Create(
                MenuItem("重新打开主窗口", OpenMainWindow, icon: "Home"),
                MenuSeparator(),
                MenuItem("退出", () => ReactorApp.Exit()))
            : null,
        MenuActivation = HNotifyMenuActivation.RightClick,
        ContextMenuMode = HasArgument("--menu-second-window")
            ? HNotifyContextMenuMode.SecondWindow
            : HNotifyContextMenuMode.PopupMenu,
    });

    tray.Click += (_, _) => OpenMainWindow();
    tray.DoubleClick += (_, _) => OpenMainWindow();
    tray.RightClick += (_, _) =>
    {
        // When the native menu is on, H.NotifyIcon opens it on right click
        // itself; showing the Reactor flyout here too would stack two menus.
        if (nativeMenu) return;

        tray.ShowFlyout(
            VStack(4,
                Button("重新打开主窗口", OpenMainWindow),
                Button("退出", () => ReactorApp.Exit()))
            .Width(240)
            .Padding(8));
    };

    if (!HasArgument("--silent"))
        OpenMainWindow();
});

bool HasArgument(string value) =>
    Environment.GetCommandLineArgs().ToList().Concat(exArgs).Contains(value, StringComparer.OrdinalIgnoreCase);

sealed class MainWindowContent : Component
{
    public override Element Render() => Border(
        VStack(12,
            TextBlock("HNotify Tray Icon (H.NotifyIcon)").FontSize(28).Bold(),
            TextBlock("左键打开主窗口，右键显示 Reactor flyout。"))
        .Padding(24));
}
