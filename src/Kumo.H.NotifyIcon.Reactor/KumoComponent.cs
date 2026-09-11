using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Base class that adds the H.NotifyIcon-backed mirror of Reactor
/// v0.1.0-preview.15's <c>Component.UseTrayIcon(TrayIconSpec)</c> convenience to
/// class components.
/// </summary>
/// <remarks>
/// <para>
/// Reactor's own protected <c>Component.UseTrayIcon</c> cannot be extended from
/// another assembly, so this library provides the same shape as a base class.
/// Function components can use
/// <see cref="KumoRenderContextExtensions.UseTrayIcon"/> on their
/// <see cref="RenderContext"/> instead.
/// </para>
/// <code>
/// sealed class MyComponent : KumoComponent
/// {
///     public override Element Render()
///     {
///         var tray = UseTrayIcon(new KumoTrayIconSpec(
///             WindowIcon.FromPath(iconPath), "My App", WindowKey.Of("my-tray")));
///
///         UseEffect(() =&gt;
///         {
///             if (tray is null) return () =&gt; { };
///             EventHandler onRightClick = (_, _) =&gt; tray.ShowFlyout(menu);
///             tray.RightClick += onRightClick;
///             return () =&gt; tray.RightClick -= onRightClick;
///         }, tray ?? (object)"no-tray");
///
///         return TextBlock("Tray ready");
///     }
/// }
/// </code>
/// </remarks>
public abstract class KumoComponent : Component
{
    /// <summary>Stable non-null effect dependency used when the icon failed to open.</summary>
    private static readonly object NoTrayIconSentinel = new();

    /// <summary>
    /// Open (or reuse-by-<see cref="KumoTrayIconSpec.Key"/>) a system-tray icon
    /// scoped to this component; the icon closes on unmount.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when no UI dispatcher has been captured or the icon
    /// cannot be created — null-check before subscribing to events.
    /// </remarks>
    protected KumoTrayIcon? UseTrayIcon(KumoTrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var handleRef = UseRef<KumoTrayIcon?>(null);

        if (handleRef.Current is null)
        {
            if (spec.Key is { } key)
            {
                handleRef.Current = KumoApp.FindTrayIcon(key);
            }

            if (handleRef.Current is null && ReactorApp.UIDispatcher is not null)
            {
                try
                {
                    handleRef.Current = KumoApp.OpenTrayIcon(spec);
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException)
                {
                    handleRef.Current = null;
                }
            }
        }

        var icon = handleRef.Current;
        var iconDependency = (object?)icon ?? NoTrayIconSentinel;

        UseEffect(() =>
        {
            if (icon is null) return;
            try { icon.Update(spec); } catch { /* best effort */ }
        }, iconDependency, spec);

        UseEffect(() =>
        {
            return () =>
            {
                try { handleRef.Current?.Close(); } catch { /* best effort */ }
            };
        });

        return icon;
    }
}
