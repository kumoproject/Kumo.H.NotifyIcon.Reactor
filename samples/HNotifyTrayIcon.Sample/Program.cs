using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// The call sites below are written exactly like Reactor preview15's tray API;
// the only difference is HNotifyApp/HNotifyTrayIcon instead of ReactorApp/ReactorTrayIcon,
// and the tray is backed by H.NotifyIcon (no native Shell_NotifyIcon code inside
// this app, no flyout host window of Reactor's own).

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

ReactorApp.Run(_ =>
{
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

    var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "app.ico")),
        "HNotify Tray Icon",
        WindowKey.Of("main-tray")));

    tray.Click += (_, _) => OpenMainWindow();
    tray.DoubleClick += (_, _) => OpenMainWindow();
    tray.RightClick += (_, _) => tray.ShowFlyout(
        VStack(4,
            Button("重新打开主窗口", OpenMainWindow),
            Button("退出", () => ReactorApp.Exit()))
        .Width(240)
        .Padding(8));

    if (!HasArgument("--silent"))
        OpenMainWindow();
});

static bool HasArgument(string value) =>
    Environment.GetCommandLineArgs().Contains(value, StringComparer.OrdinalIgnoreCase);

sealed class MainWindowContent : Component
{
    public override Element Render() => Border(
        VStack(12,
            TextBlock("HNotify Tray Icon (H.NotifyIcon)").FontSize(28).Bold(),
            TextBlock("左键打开主窗口，右键显示 Reactor flyout。"))
        .Padding(24));
}
