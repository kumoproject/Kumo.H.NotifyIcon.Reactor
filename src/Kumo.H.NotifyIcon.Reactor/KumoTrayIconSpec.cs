using Microsoft.UI.Reactor;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Immutable declarative description of a system-tray icon.
/// <para>
/// This is the H.NotifyIcon-backed mirror of Reactor v0.1.0-preview.15's
/// <c>Microsoft.UI.Reactor.TrayIconSpec</c>; hand it to
/// <see cref="KumoApp.OpenTrayIcon"/> to register, and to
/// <see cref="KumoTrayIcon.Update"/> to diff and re-apply only changed fields.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Icon sources.</b> Two flavours are accepted, matching what the backend
/// (H.NotifyIcon) and the compile-time dependency (Reactor preview14) can
/// actually carry:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="WindowIcon"/> (surface-identical to preview15):
/// <see cref="WindowIcon.FromPath"/> pointing at an <c>.ico</c> file.
/// <c>ms-appx:///</c> resources are resolved to a file on disk when possible
/// (unpackaged: next to the executable; packaged: inside the install folder)
/// and otherwise skipped with a diagnostic, exactly like Reactor's native
/// tray icon.
/// </item>
/// <item>
/// <see cref="System.Drawing.Icon"/> (Kumo extension, H.NotifyIcon native):
/// this is the escape hatch for icons that live in memory, e.g. one extracted
/// from an embedded resource. Reactor preview15 covers the same scenario with
/// <c>WindowIcon.FromBytes</c> / <c>FromRgba</c>, which do not exist in
/// preview14 — and this library is compiled against preview14.
/// </item>
/// </list>
/// <para>
/// Prefer the <see cref="WindowIcon"/> constructor for call sites that should
/// move to the real Reactor preview15 tray API unchanged.
/// </para>
/// </remarks>
public sealed record KumoTrayIconSpec
{
    /// <summary>
    /// Creates a spec from a Reactor <see cref="WindowIcon"/> source
    /// (filesystem path, or a resource that can be resolved to one).
    /// </summary>
    /// <param name="icon">Icon bitmap source. Must not be <c>null</c>.</param>
    /// <param name="tooltip">User-visible tooltip and accessible name.</param>
    /// <param name="key">Optional stable identity for reuse and shell lookups.</param>
    /// <param name="isVisible">Whether the icon is shown after registration.</param>
    public KumoTrayIconSpec(
        WindowIcon icon,
        string tooltip,
        WindowKey? key = null,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(tooltip);

        Icon = icon;
        Tooltip = tooltip;
        Key = key;
        IsVisible = isVisible;
    }

    /// <summary>
    /// Creates a spec from an in-memory <see cref="System.Drawing.Icon"/>
    /// (Kumo extension over preview15's spec, backed natively by H.NotifyIcon).
    /// </summary>
    /// <param name="drawingIcon">
    /// Icon instance owned by the caller. The tray handle clones it before
    /// applying, so the caller keeps ownership.
    /// </param>
    /// <param name="tooltip">User-visible tooltip and accessible name.</param>
    /// <param name="key">Optional stable identity for reuse and shell lookups.</param>
    /// <param name="isVisible">Whether the icon is shown after registration.</param>
    public KumoTrayIconSpec(
        System.Drawing.Icon drawingIcon,
        string tooltip,
        WindowKey? key = null,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(drawingIcon);
        ArgumentNullException.ThrowIfNull(tooltip);

        DrawingIcon = drawingIcon;
        Tooltip = tooltip;
        Key = key;
        IsVisible = isVisible;
    }

    /// <summary>
    /// Reactor <see cref="WindowIcon"/> source, or <c>null</c> when the spec was
    /// created from a <see cref="System.Drawing.Icon"/>.
    /// </summary>
    public WindowIcon? Icon { get; init; }

    /// <summary>
    /// In-memory icon source, or <c>null</c> when the spec was created from a
    /// <see cref="WindowIcon"/>. Kumo extension over preview15.
    /// </summary>
    public System.Drawing.Icon? DrawingIcon { get; init; }

    /// <summary>User-visible tooltip that surfaces as the icon's accessible name.</summary>
    public string Tooltip { get; init; }

    /// <summary>Optional stable identity for hook reuse and <see cref="KumoApp.FindTrayIcon"/> lookups.</summary>
    public WindowKey? Key { get; init; }

    /// <summary>Whether the icon is currently shown.</summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// Convenience factory for the in-memory icon constructor
    /// (same result as <c>new KumoTrayIconSpec(drawingIcon, tooltip, key, isVisible)</c>).
    /// </summary>
    public static KumoTrayIconSpec FromDrawingIcon(
        System.Drawing.Icon drawingIcon,
        string tooltip,
        WindowKey? key = null,
        bool isVisible = true)
        => new(drawingIcon, tooltip, key, isVisible);
}
