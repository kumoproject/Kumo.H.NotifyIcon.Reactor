using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// H.NotifyIcon-backed mirror of Reactor v0.1.0-preview.15's
/// <c>RenderContext.UseTrayIcon(TrayIconSpec)</c> hook.
/// </summary>
public static class KumoRenderContextExtensions
{
    /// <summary>Stable non-null effect dependency used when the icon failed to open.</summary>
    private static readonly object NoTrayIconSentinel = new();

    /// <summary>
    /// Open (or reuse-by-<see cref="KumoTrayIconSpec.Key"/>) a system-tray icon
    /// scoped to the calling render context. The icon closes automatically on
    /// unmount — that is the only difference from
    /// <see cref="KumoApp.OpenTrayIcon"/>, which is app-scoped and keeps the
    /// icon alive until an explicit <see cref="KumoTrayIcon.Close"/>.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when no UI dispatcher has been captured (test
    /// contexts) or when the icon cannot be created — callers should null-check
    /// before subscribing to events. Identity-stable across re-renders so the
    /// same handle wires through subsequent effects.
    /// </remarks>
    public static KumoTrayIcon? UseTrayIcon(this RenderContext context, KumoTrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spec);

        var handleRef = context.UseRef<KumoTrayIcon?>(null);

        // Open on first render; reuse on subsequent renders if a key match
        // already exists. The hook owns the lifetime — the cleanup effect below
        // closes the icon on unmount.
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
                    // Shell unavailable in test/headless contexts.
                    handleRef.Current = null;
                }
            }
        }

        var icon = handleRef.Current;
        var iconDependency = (object?)icon ?? NoTrayIconSentinel;

        // Spec-change diff: re-apply only when the value-equal record changes.
        // The hook is always called — never from inside a branch — so hook order
        // stays stable across renders even when the open failed.
        context.UseEffect(() =>
        {
            if (icon is null) return;
            try { icon.Update(spec); } catch { /* best effort */ }
        }, iconDependency, spec);

        // Component-scoped lifetime — close on unmount. Read the handle through
        // the ref inside the cleanup so a late-bound icon (e.g. a dispatcher
        // captured after the first render) still gets closed.
        context.UseEffect(() =>
        {
            return () =>
            {
                try { handleRef.Current?.Close(); } catch { /* best effort */ }
            };
        });

        return icon;
    }
}
