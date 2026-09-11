using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml.Controls;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Immutable declarative description of a system-tray icon.
/// <para>
/// This is the H.NotifyIcon-backed mirror of Reactor v0.1.0-preview.15's
/// <c>Microsoft.UI.Reactor.TrayIconSpec</c>; hand it to
/// <see cref="HNotifyApp.OpenTrayIcon"/> to register, and to
/// <see cref="HNotifyTrayIcon.Update"/> to diff and re-apply only changed fields.
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
/// <see cref="System.Drawing.Icon"/> (HNotify extension, H.NotifyIcon native):
/// this is the escape hatch for icons that live in memory, e.g. one extracted
/// from an embedded resource. Reactor preview15 covers the same scenario with
/// <c>WindowIcon.FromBytes</c> / <c>FromRgba</c>, which do not exist in
/// preview14 — use <see cref="HNotifyWindowIcon.FromBytes(byte[])"/> as the
/// temporary stand-in (or <see cref="FromDrawingIcon"/> when the icon already
/// is a <see cref="System.Drawing.Icon"/>).
/// </item>
/// </list>
/// <para>
/// Prefer the <see cref="WindowIcon"/> constructor for call sites that should
/// move to the real Reactor preview15 tray API unchanged.
/// </para>
/// </remarks>
public sealed record HNotifyTrayIconSpec
{
    /// <summary>
    /// Creates a spec from a Reactor <see cref="WindowIcon"/> source
    /// (filesystem path, or a resource that can be resolved to one).
    /// </summary>
    /// <param name="icon">Icon bitmap source. Must not be <c>null</c>.</param>
    /// <param name="tooltip">User-visible tooltip and accessible name.</param>
    /// <param name="key">Optional stable identity for reuse and shell lookups.</param>
    /// <param name="isVisible">Whether the icon is shown after registration.</param>
    public HNotifyTrayIconSpec(
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
    /// (HNotify extension over preview15's spec, backed natively by H.NotifyIcon).
    /// </summary>
    /// <param name="drawingIcon">
    /// Icon instance owned by the caller. The tray handle clones it before
    /// applying, so the caller keeps ownership.
    /// </param>
    /// <param name="tooltip">User-visible tooltip and accessible name.</param>
    /// <param name="key">Optional stable identity for reuse and shell lookups.</param>
    /// <param name="isVisible">Whether the icon is shown after registration.</param>
    public HNotifyTrayIconSpec(
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
    /// <see cref="WindowIcon"/>. HNotify extension over preview15.
    /// </summary>
    public System.Drawing.Icon? DrawingIcon { get; init; }

    /// <summary>User-visible tooltip that surfaces as the icon's accessible name.</summary>
    public string Tooltip { get; init; }

    /// <summary>Optional stable identity for hook reuse and <see cref="HNotifyApp.FindTrayIcon"/> lookups.</summary>
    public WindowKey? Key { get; init; }

    /// <summary>Whether the icon is currently shown.</summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// Convenience factory for the in-memory icon constructor
    /// (same result as <c>new HNotifyTrayIconSpec(drawingIcon, tooltip, key, isVisible)</c>).
    /// </summary>
    public static HNotifyTrayIconSpec FromDrawingIcon(
        System.Drawing.Icon drawingIcon,
        string tooltip,
        WindowKey? key = null,
        bool isVisible = true)
        => new(drawingIcon, tooltip, key, isVisible);

    // ── H.NotifyIcon native context menu (HNotify extensions) ──────────────
    // preview15's tray API has no menu concept (menus are Reactor flyouts shown
    // through ShowFlyout). These members surface H.NotifyIcon's *own* menu
    // system, which the backend renders itself. Build the flyout with
    // HNotifyMenu; the mode/theme fields map onto the backend's DPs 1:1.

    /// <summary>
    /// H.NotifyIcon's own context menu, rendered by the backend (HNotify
    /// extension; preview15 has no counterpart). <c>null</c> — the default —
    /// keeps the backend menu disabled and preserves the preview15 behavior.
    /// </summary>
    /// <remarks>
    /// Build it with <c>HNotifyMenu.Create(...)</c> from Reactor's menu DSL —
    /// <c>MenuItem()</c> / <c>MenuSeparator()</c> / <c>MenuSubItem()</c> etc. from
    /// <c>Microsoft.UI.Reactor.Factories</c>. Actions are wired through
    /// <c>ICommand</c> because in <see cref="HNotifyContextMenuMode.PopupMenu"/>
    /// mode the backend dispatches through the command only and never raises
    /// <c>MenuFlyoutItem.Click</c>.
    /// </remarks>
    public MenuFlyout? ContextMenu { get; init; }

    /// <summary>
    /// When the native context menu opens on its own
    /// (default <see cref="HNotifyMenuActivation.RightClick"/>). Only takes effect
    /// when <see cref="ContextMenu"/> is set;
    /// <see cref="HNotifyMenuActivation.None"/> means "show it from code instead"
    /// (<c>HNotifyTrayIcon.ShowContextMenu</c>). (HNotify extension)
    /// </summary>
    public HNotifyMenuActivation MenuActivation { get; init; } = HNotifyMenuActivation.RightClick;

    /// <summary>
    /// How the native menu is rendered
    /// (default <see cref="HNotifyContextMenuMode.PopupMenu"/>). (HNotify extension)
    /// </summary>
    public HNotifyContextMenuMode ContextMenuMode { get; init; } = HNotifyContextMenuMode.PopupMenu;

    /// <summary>
    /// Theme of the native popup menu
    /// (default <see cref="HNotifyContextMenuTheme.System"/>). (HNotify extension)
    /// </summary>
    public HNotifyContextMenuTheme ContextMenuTheme { get; init; } = HNotifyContextMenuTheme.System;

    /// <summary>
    /// Whether the menu closes when an item is chosen (default <c>true</c>). Only
    /// consulted by <see cref="HNotifyContextMenuMode.SecondWindow"/>. (HNotify extension)
    /// </summary>
    public bool CloseContextMenuOnItemClick { get; init; } = true;
}
