using System.Diagnostics;
using Microsoft.UI.Reactor;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Translates a <see cref="HNotifyTrayIconSpec"/> icon source into the
/// <see cref="System.Drawing.Icon"/> instance H.NotifyIcon actually consumes.
/// </summary>
/// <remarks>
/// Mirrors Reactor preview15's <c>ReactorTrayIcon.LoadHIcon</c> diagnostics:
/// resource (<c>ms-appx:///</c>) icons are resolved to a file when one exists
/// and otherwise skipped with a debug diagnostic rather than crashing the icon.
/// </remarks>
internal static class HNotifyIconResolver
{
    private const string MsAppxPrefix = "ms-appx:///";

    /// <summary>
    /// Resolves (and clones) the icon to hand to the shell, or <c>null</c> when
    /// the source produced none. The caller owns the returned instance.
    /// </summary>
    public static System.Drawing.Icon? Resolve(HNotifyTrayIconSpec spec)
    {
        try
        {
            if (spec.DrawingIcon is not null)
            {
                // Clone so H.NotifyIcon (which disposes the Icon it replaces /
                // owns) never mutates the caller's instance.
                return (System.Drawing.Icon)spec.DrawingIcon.Clone();
            }

            var windowIcon = spec.Icon;
            if (windowIcon is null) return null;

            var path = ResolvePath(windowIcon);
            if (path is null || !File.Exists(path))
            {
                Debug.WriteLine(
                    "[HNotify] Tray icon: no file found for icon source " +
                    $"'{windowIcon.Source}'. Ship a sidecar .ico next to the app, or pass a " +
                    "System.Drawing.Icon (HNotifyTrayIconSpec.FromDrawingIcon).");
                return null;
            }

            return new System.Drawing.Icon(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HNotify] Tray icon load failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns the absolute filesystem path for a <see cref="WindowIcon"/>, or
    /// <c>null</c> when it is a resource URI that cannot be resolved to a file.
    /// </summary>
    private static string? ResolvePath(WindowIcon icon)
    {
        var source = icon.Source;
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (!icon.IsResource) return source;

        var relative = source.StartsWith(MsAppxPrefix, StringComparison.OrdinalIgnoreCase)
            ? source[MsAppxPrefix.Length..]
            : source;
        relative = relative.Replace('/', Path.DirectorySeparatorChar);

        // Unpackaged apps resolve ms-appx to the output directory.
        var unpackaged = Path.Combine(AppContext.BaseDirectory, relative);
        if (File.Exists(unpackaged)) return unpackaged;

        // Packaged apps resolve ms-appx to the package install location.
        try
        {
            var installed = global::Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            var packaged = Path.Combine(installed, relative);
            if (File.Exists(packaged)) return packaged;
        }
        catch
        {
            // No package identity — not a packaged app.
        }

        // Nothing exists; report the unpackaged candidate so the diagnostic is
        // actionable and return it so the caller logs File.Exists == false.
        return unpackaged;
    }
}
