using System.Diagnostics;
using Microsoft.UI.Reactor;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Application-level registry for <see cref="HNotifyTrayIcon"/> handles — the
/// H.NotifyIcon-backed peer of Reactor v0.1.0-preview.15's
/// <c>ReactorApp</c> tray surface (<c>OpenTrayIcon</c> / <c>FindTrayIcon</c> /
/// <c>TrayIcons</c> / <c>TrayIconOpened</c> / <c>TrayIconClosed</c>).
/// </summary>
/// <remarks>
/// <para>
/// This registry is intentionally independent from Reactor's internal tray
/// registry: Reactor preview14's <c>ShutdownPolicy</c> only counts Reactor's
/// own surfaces, so an app whose lifetime is "tray only" should set
/// <see cref="ReactorApp.ShutdownPolicy"/> to
/// <see cref="ShutdownPolicy.Explicit"/> and exit via
/// <see cref="ReactorApp.Exit"/>.
/// </para>
/// </remarks>
public static class HNotifyApp
{
    private static HNotifyTrayIcon[] _trayIcons = global::System.Array.Empty<HNotifyTrayIcon>();

    /// <summary>
    /// Snapshot of every open <see cref="HNotifyTrayIcon"/>. Copy-on-write;
    /// safe to read from any thread.
    /// </summary>
    public static IReadOnlyList<HNotifyTrayIcon> TrayIcons => Volatile.Read(ref _trayIcons);

    /// <summary>Fires on the UI thread when a <see cref="HNotifyTrayIcon"/> opens.</summary>
    public static event EventHandler<HNotifyTrayIcon>? TrayIconOpened;

    /// <summary>Fires on the UI thread when a <see cref="HNotifyTrayIcon"/> closes.</summary>
    public static event EventHandler<HNotifyTrayIcon>? TrayIconClosed;

    /// <summary>
    /// Open a system-tray icon. UI-thread only. The returned handle stays alive
    /// until <see cref="HNotifyTrayIcon.Close"/> / <see cref="HNotifyTrayIcon.Dispose"/>
    /// is called or the process exits.
    /// </summary>
    public static HNotifyTrayIcon OpenTrayIcon(HNotifyTrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(OpenTrayIcon));

        var icon = new HNotifyTrayIcon(spec);
        RegisterTrayIcon(icon);
        try
        {
            icon.RegisterWithShell();
        }
        catch
        {
            UnregisterTrayIcon(icon);
            try { icon.Dispose(); } catch { /* best effort */ }
            throw;
        }

        return icon;
    }

    /// <summary>Look up an open tray icon by <see cref="WindowKey"/>.</summary>
    public static HNotifyTrayIcon? FindTrayIcon(WindowKey key)
    {
        var snapshot = TrayIcons;
        for (var i = 0; i < snapshot.Count; i++)
        {
            var candidate = snapshot[i];
            if (candidate.Key is { } k && k.Equals(key)) return candidate;
        }

        return null;
    }

    // Copy-on-write add. UI-thread only — reads can happen anywhere.
    internal static void RegisterTrayIcon(HNotifyTrayIcon icon)
    {
        var current = Volatile.Read(ref _trayIcons);
        var next = new HNotifyTrayIcon[current.Length + 1];
        global::System.Array.Copy(current, next, current.Length);
        next[current.Length] = icon;
        Volatile.Write(ref _trayIcons, next);

        try { TrayIconOpened?.Invoke(null, icon); }
        catch (Exception ex) { Debug.WriteLine($"[HNotify] TrayIconOpened threw: {ex.Message}"); }
    }

    internal static void UnregisterTrayIcon(HNotifyTrayIcon icon)
    {
        var current = Volatile.Read(ref _trayIcons);
        var index = global::System.Array.IndexOf(current, icon);
        if (index < 0)
        {
            // Already gone (e.g. double dispose) — keep the snapshot intact.
            return;
        }

        var next = new HNotifyTrayIcon[current.Length - 1];
        if (index > 0) global::System.Array.Copy(current, 0, next, 0, index);
        if (index < current.Length - 1)
            global::System.Array.Copy(current, index + 1, next, index, current.Length - index - 1);
        Volatile.Write(ref _trayIcons, next);

        try { TrayIconClosed?.Invoke(null, icon); }
        catch (Exception ex) { Debug.WriteLine($"[HNotify] TrayIconClosed threw: {ex.Message}"); }
    }
}
