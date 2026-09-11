# Kumo.H.NotifyIcon.Reactor

System-tray icons for [Microsoft.UI.Reactor](https://github.com/microsoft/microsoft-ui-reactor), backed entirely by [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (no Win32 `Shell_NotifyIcon` code of its own).

The API mirrors Reactor preview.15's tray surface — `TrayIconSpec` / `ReactorTrayIcon` / `ReactorApp.OpenTrayIcon` / `UseTrayIcon` — while the package compiles against **Microsoft.UI.Reactor 0.1.0-preview.14**. Backend: **H.NotifyIcon.WinUI 2.5.0-dev.2**. Target: `net10.0-windows10.0.26100.0`.

## Quick start

### Programmatic icon

```csharp
using Kumo.H.NotifyIcon.Reactor;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit; // tray-resident apps control their own exit

ReactorApp.Run(_ =>
{
    // Required for ShutdownPolicy.Explicit: WinUI otherwise quits when the last
    // window closes (Windows App SDK 1.8+).
    Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

    var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(
        WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "app.ico")),
        "My App",
        WindowKey.Of("main-tray"))
    {
        // The context menu is H.NotifyIcon's own, fed by Reactor's menu DSL.
        ContextMenu = HNotifyMenu.Create(
            MenuItem("Open", OpenMainWindow),
            MenuSeparator(),
            MenuItem("Exit", () => ReactorApp.Exit())),
        MenuActivation = HNotifyMenuActivation.RightClick,
    });

    tray.Click += (_, _) => OpenMainWindow();
});
```

### Component-scoped icon

```csharp
sealed class TrayHost : HNotifyComponent
{
    public override Element Render()
    {
        var tray = UseTrayIcon(new HNotifyTrayIconSpec(
            WindowIcon.FromPath(iconPath), "My App", WindowKey.Of("tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            EventHandler onClick = (_, _) => OpenMainWindow();
            tray.Click += onClick;
            return () => tray.Click -= onClick;
        }, tray ?? (object)"no-tray");

        return TextBlock("Tray ready");
    }
}
```

The icon opens on first render and closes when the component unmounts. Function components can use the `context.UseTrayIcon(spec)` extension instead (Reactor's hook analyzer requires such calls inside `Render` or a `Use*` method).

### Icon from memory

```csharp
// preview.15 spells this WindowIcon.FromBytes; this is the preview.14 stand-in,
// returning a caller-owned System.Drawing.Icon (.ico bytes only).
using var icon = HNotifyWindowIcon.FromBytes(iconBytes);
var tray = HNotifyApp.OpenTrayIcon(new HNotifyTrayIconSpec(icon, "My App", key));
```

## Notes

- **Exit**: Reactor's `ShutdownPolicy` does not count this library's icons. Use `ShutdownPolicy.Explicit` together with `Application.Current.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown` (Windows App SDK 1.8+), and end the app from a tray menu via `ReactorApp.Exit()`.
- **Threading**: every mutator (menu properties, `Update`, `ShowContextMenu` / `CloseContextMenu`, …) must run on the Reactor UI thread, or it throws `InvalidOperationException`.
- **Menus**: built with `HNotifyMenu.Create(...)` from the Reactor DSL (`MenuItem()` / `MenuSeparator()` / `MenuSubItem()` / `ToggleMenuItem()` / `RadioMenuItem()`). `ContextMenuMode.PopupMenu` (default) uses the native Win32 popup — text only; `SecondWindow` hosts the full XAML menu (icons render, one extra window) but H.NotifyIcon's preview mode retains ~60 KB per icon create/destroy cycle — prefer `PopupMenu` for icons that are recreated repeatedly.
- **Multiple icons**: give every spec a distinct `WindowKey` so each icon maps to its own shell GUID.

## Build

```powershell
dotnet build .\Kumo.H.NotifyIcon.Reactor.slnx

# ARM64 Native AOT publish (sample 2; EnableMsixTooling in the csproj works around
# WinAppSDK #6394 so the app's .pri/.xbf reach the publish output)
dotnet publish .\samples\HNotifyTrayIcon.ComponentSample\HNotifyTrayIcon.ComponentSample.csproj -c Release -p:Platform=ARM64

dotnet pack .\src\Kumo.H.NotifyIcon.Reactor\Kumo.H.NotifyIcon.Reactor.csproj -c Release
```

## Samples

- `samples/HNotifyTrayIcon.Sample` — programmatic setup; `--menu-second-window` switches to the window-hosted menu, `--silent` starts without the main window.
- `samples/HNotifyTrayIcon.ComponentSample` — component-scoped icon on a never-shown host window; the tray and the main window are fully independent units that can be created/destroyed repeatedly; also the Native AOT publishing sample.
