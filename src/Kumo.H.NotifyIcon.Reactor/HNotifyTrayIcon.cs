using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HTaskbarIcon = global::H.NotifyIcon.TaskbarIcon;
using HMouseEvent = global::H.NotifyIcon.Core.MouseEvent;
using HMouseEventReceivedEventArgs = global::H.NotifyIcon.Core.MessageWindow.MouseEventReceivedEventArgs;
using HPopupActivationMode = global::H.NotifyIcon.Core.PopupActivationMode;
using HContextMenuMode = global::H.NotifyIcon.ContextMenuMode;
using HPopupMenuThemeMode = global::H.NotifyIcon.Core.PopupMenuThemeMode;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Top-level system-tray icon — the H.NotifyIcon-backed peer of Reactor
/// v0.1.0-preview.15's <c>Microsoft.UI.Reactor.ReactorTrayIcon</c>.
/// Created via <see cref="HNotifyApp.OpenTrayIcon"/>; provides the icon bitmap,
/// tooltip, click events, and the optional native context menu
/// (see <see cref="HNotifyTrayIconSpec.ContextMenu"/>).
/// </summary>
/// <remarks>
/// <para>
/// Public mutators must run on the UI thread captured by
/// <see cref="ReactorApp.UIDispatcher"/> (same contract as preview15).
/// Disposal is idempotent — a second <see cref="Close"/> / <see cref="Dispose"/>
/// call is a no-op.
/// </para>
/// <para>
/// <b>Backend differences vs. the native Reactor tray icon.</b> The icon is
/// created by H.NotifyIcon's <c>TaskbarIcon</c>, which owns its own hidden
/// message window and re-registers itself after an Explorer restart, so no
/// hidden-window bookkeeping is needed here. Menus are the backend's own (see
/// <see cref="ContextMenu"/>); the library ships no Reactor-rendered flyout
/// window.
/// </para>
/// </remarks>
public sealed partial class HNotifyTrayIcon : IDisposable
{
    private static int s_nextId;

    private readonly string _id;
    private HNotifyTrayIconSpec _spec;
    private HTaskbarIcon? _taskbarIcon;
    private MenuFlyout? _appliedContextMenu;
    private global::System.IDisposable? _mouseSubscription;
    private bool _registered;
    private bool _disposed;

    /// <summary>Stable id, e.g. <c>"hnotify-tray-1"</c>. Allocated monotonically per process.</summary>
    public string Id => _id;

    /// <summary>Optional stable identity (from <see cref="HNotifyTrayIconSpec.Key"/>).</summary>
    public WindowKey? Key => _spec.Key;

    /// <summary>Last-applied <see cref="HNotifyTrayIconSpec"/> snapshot.</summary>
    public HNotifyTrayIconSpec Spec => _spec;

    /// <summary>
    /// The icon bitmap source. Mutating triggers a shell re-apply.
    /// <c>null</c> when this handle was opened from a
    /// <see cref="System.Drawing.Icon"/> source (see <see cref="DrawingIcon"/>).
    /// </summary>
    public WindowIcon? Icon
    {
        get => _spec.Icon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(Icon));
            if (_disposed || (ReferenceEquals(_spec.Icon, value) && _spec.DrawingIcon is null)) return;

            _spec = _spec with { Icon = value, DrawingIcon = null };
            ApplyIcon();
        }
    }

    /// <summary>
    /// The in-memory icon bitmap source (HNotify extension over preview15).
    /// Mutating triggers a shell re-apply.
    /// </summary>
    public System.Drawing.Icon? DrawingIcon
    {
        get => _spec.DrawingIcon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(DrawingIcon));
            if (_disposed || ReferenceEquals(_spec.DrawingIcon, value)) return;

            _spec = _spec with { DrawingIcon = value, Icon = null };
            ApplyIcon();
        }
    }

    /// <summary>Tooltip text. Mutating triggers a shell re-apply.</summary>
    public string Tooltip
    {
        get => _spec.Tooltip;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(Tooltip));
            if (_disposed || string.Equals(_spec.Tooltip, value, StringComparison.Ordinal)) return;

            _spec = _spec with { Tooltip = value };
            _taskbarIcon?.ToolTipText = value;
        }
    }

    /// <summary>Whether the icon is currently shown. Mutating triggers a shell re-apply.</summary>
    public bool IsVisible
    {
        get => _spec.IsVisible;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(IsVisible));
            if (_disposed || _spec.IsVisible == value) return;

            _spec = _spec with { IsVisible = value };
            ApplyVisibility();
        }
    }

    // ── H.NotifyIcon native context menu (HNotify extensions) ──────────────

    /// <summary>
    /// H.NotifyIcon's own context menu (HNotify extension; preview15 has no
    /// counterpart). <c>null</c> disables it. Mutating re-applies the menu.
    /// </summary>
    public MenuFlyout? ContextMenu
    {
        get => _spec.ContextMenu;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(ContextMenu));
            if (_disposed || ReferenceEquals(_spec.ContextMenu, value)) return;

            _spec = _spec with { ContextMenu = value };
            ApplyMenu();
        }
    }

    /// <summary>
    /// When the native menu opens on its own. Only effective while
    /// <see cref="ContextMenu"/> is set. Mutating re-applies the menu.
    /// </summary>
    public HNotifyMenuActivation MenuActivation
    {
        get => _spec.MenuActivation;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(MenuActivation));
            if (_disposed || _spec.MenuActivation == value) return;

            _spec = _spec with { MenuActivation = value };
            ApplyMenu();
        }
    }

    /// <summary>How the native menu is rendered. Mutating re-applies the menu.</summary>
    public HNotifyContextMenuMode ContextMenuMode
    {
        get => _spec.ContextMenuMode;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(ContextMenuMode));
            if (_disposed || _spec.ContextMenuMode == value) return;

            _spec = _spec with { ContextMenuMode = value };
            ApplyMenu();
        }
    }

    /// <summary>Theme of the native popup menu. Mutating re-applies the menu.</summary>
    public HNotifyContextMenuTheme ContextMenuTheme
    {
        get => _spec.ContextMenuTheme;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(ContextMenuTheme));
            if (_disposed || _spec.ContextMenuTheme == value) return;

            _spec = _spec with { ContextMenuTheme = value };
            ApplyMenu();
        }
    }

    /// <summary>Whether the window-hosted menu closes on item click. Mutating re-applies the menu.</summary>
    public bool CloseContextMenuOnItemClick
    {
        get => _spec.CloseContextMenuOnItemClick;
        set
        {
            HNotifyThreading.ThrowIfNotOnUiThread(nameof(CloseContextMenuOnItemClick));
            if (_disposed || _spec.CloseContextMenuOnItemClick == value) return;

            _spec = _spec with { CloseContextMenuOnItemClick = value };
            ApplyMenu();
        }
    }

    /// <summary>Fires on the UI thread when the user left-clicks the icon (single click).</summary>
    public event EventHandler? Click;

    /// <summary>Fires on the UI thread when the user double-clicks the icon.</summary>
    public event EventHandler? DoubleClick;

    /// <summary>Fires on the UI thread when the user right-clicks the icon.</summary>
    public event EventHandler? RightClick;

    internal HNotifyTrayIcon(HNotifyTrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
        _id = $"hnotify-tray-{Interlocked.Increment(ref s_nextId)}";
    }

    /// <summary>
    /// Registers the icon with the shell. UI-thread only. Called once by
    /// <see cref="HNotifyApp.OpenTrayIcon"/>; subsequent state changes flow through
    /// <see cref="Update"/> or the property setters.
    /// </summary>
    internal void RegisterWithShell()
    {
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(RegisterWithShell));
        if (_registered) return;

        var taskbarIcon = new HTaskbarIcon();
        _taskbarIcon = taskbarIcon;

        // H.NotifyIcon keys shell state by Guid. The default id is derived from
        // the process path, so two icons in one process would collide; a spec
        // key maps to a stable per-icon id instead.
        if (_spec.Key is { } key)
        {
            taskbarIcon.CustomName = $"HNotify.TrayIcon.{key}";
        }

        // The backend's automatic tray popup stays off. The native context menu
        // is opt-in (spec.ContextMenu) and is applied by ApplyMenu: with no menu
        // attached the activation stays None, so no gesture pops anything on
        // its own.
        taskbarIcon.PopupActivation = HPopupActivationMode.None;
        ApplyMenu();

        taskbarIcon.ToolTipText = _spec.Tooltip ?? string.Empty;

        var icon = HNotifyIconResolver.Resolve(_spec);
        if (icon is not null) taskbarIcon.Icon = icon;

        // H.NotifyIcon disables its WinUI routed events by default
        // (https://github.com/HavenDV/H.NotifyIcon/issues/36), so subscribe to
        // the raw message-window stream instead and map it 1:1 onto preview15's
        // Click/DoubleClick/RightClick:
        //  * IconLeftMouseUp — a genuine single left click (H.NotifyIcon already
        //    suppresses the release that follows a double click).
        //  * IconDoubleClick — a left double click.
        //  * IconRightMouseUp — a right click.
        _mouseSubscription = taskbarIcon.TrayIcon.MessageWindow
            .SubscribeToMouseEventReceived(OnMouseEventReceived);

        // The TaskbarIcon never enters a XAML tree, so Loaded won't fire and the
        // icon must be created explicitly (efficiency mode off keeps the app out
        // of the background-throttled state).
        taskbarIcon.ForceCreate(enablesEfficiencyMode: false);

        // Apply the initial visibility only after creation: the backend only
        // carries NIS_HIDDEN into NIM_MODIFY, not into the initial NIM_ADD.
        ApplyVisibility();

        _registered = true;
    }

    private void OnMouseEventReceived(object? sender, HMouseEventReceivedEventArgs args)
    {
        switch (args.MouseEvent)
        {
            case HMouseEvent.IconLeftMouseUp:
                Click?.Invoke(this, EventArgs.Empty);
                break;

            case HMouseEvent.IconDoubleClick:
                DoubleClick?.Invoke(this, EventArgs.Empty);
                break;

            case HMouseEvent.IconRightMouseUp:
                RightClick?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// Show the native context menu at <paramref name="position"/> (screen
    /// coordinates in physical pixels, exactly what the backend's
    /// <c>TrackPopupMenuEx</c> expects). HNotify extension; mainly useful with
    /// <see cref="HNotifyMenuActivation.None"/>. UI-thread only. In
    /// <see cref="HNotifyContextMenuMode.PopupMenu"/> mode this enters the Win32
    /// menu loop and does not return until the menu closes (backend behavior).
    /// </summary>
    public void ShowContextMenu(System.Drawing.Point position)
    {
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(ShowContextMenu));
        if (_disposed || _taskbarIcon is null || _spec.ContextMenu is null) return;

        _taskbarIcon.ShowContextMenu(position);
    }

    /// <summary>
    /// Close the native context menu if it is open. HNotify extension. UI-thread
    /// only. Idempotent.
    /// </summary>
    public void CloseContextMenu()
    {
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(CloseContextMenu));
        if (_disposed || _taskbarIcon is null) return;

        _taskbarIcon.CloseContextMenu();
    }

    /// <summary>
    /// Diff <paramref name="next"/> against the current spec and re-apply only
    /// changed fields. UI-thread only.
    /// </summary>
    public void Update(HNotifyTrayIconSpec next)
    {
        ArgumentNullException.ThrowIfNull(next);
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(Update));
        ObjectDisposedException.ThrowIf(_disposed, this);

        var prev = _spec;
        _spec = next;
        if (Equals(prev, next)) return;

        if (!ReferenceEquals(prev.Icon, next.Icon) ||
            !ReferenceEquals(prev.DrawingIcon, next.DrawingIcon))
        {
            ApplyIcon();
        }

        if (!ReferenceEquals(prev.ContextMenu, next.ContextMenu) ||
            prev.MenuActivation != next.MenuActivation ||
            prev.ContextMenuMode != next.ContextMenuMode ||
            prev.ContextMenuTheme != next.ContextMenuTheme ||
            prev.CloseContextMenuOnItemClick != next.CloseContextMenuOnItemClick)
        {
            ApplyMenu();
        }

        if (!string.Equals(prev.Tooltip, next.Tooltip, StringComparison.Ordinal))
        {
            _taskbarIcon?.ToolTipText = next.Tooltip ?? string.Empty;
        }

        if (prev.IsVisible != next.IsVisible)
        {
            ApplyVisibility();
        }
    }

    /// <summary>
    /// Remove the icon from the tray and dispose. UI-thread only. Idempotent.
    /// </summary>
    public void Close() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        HNotifyThreading.ThrowIfNotOnUiThread(nameof(Dispose));
        _disposed = true;

        var taskbarIcon = _taskbarIcon;
        _taskbarIcon = null;
        _appliedContextMenu = null;

        try { _mouseSubscription?.Dispose(); } catch { /* best effort */ }
        _mouseSubscription = null;

        if (taskbarIcon is not null)
        {
            try
            {
                taskbarIcon.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HNotify] Tray icon dispose failed: {ex.Message}");
            }
        }

        _registered = false;
        HNotifyApp.UnregisterTrayIcon(this);
    }

    private void ApplyIcon()
    {
        if (_taskbarIcon is null) return;
        // Assigning Icon disposes the previous instance (H.NotifyIcon owns what
        // it holds), so the resolved clone must always be a fresh instance —
        // which HNotifyIconResolver guarantees.
        _taskbarIcon.Icon = HNotifyIconResolver.Resolve(_spec);
    }

    private void ApplyVisibility()
    {
        if (_taskbarIcon is null) return;
        _taskbarIcon.Visibility = _spec.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyMenu()
    {
        if (_taskbarIcon is null) return;

        var menu = _spec.ContextMenu;

        // H.NotifyIcon's SecondWindow mode owns a separate XAML window for
        // ContextFlyout. Explicitly tear that host down before replacing the
        // source flyout; assigning a new flyout directly from inside a
        // localized menu update can otherwise leave the old host attached to
        // the XAML application and crash during Application.Exit().
        bool replacingMenu = !ReferenceEquals(_appliedContextMenu, menu);
        if (replacingMenu && _taskbarIcon.ContextMenuMode == HContextMenuMode.SecondWindow)
        {
            _taskbarIcon.CloseContextMenu();
            _taskbarIcon.ContextMenuMode = HContextMenuMode.PopupMenu;
            _taskbarIcon.ContextFlyout = null;
        }

        // No menu attached → keep the backend from opening anything on its own,
        // exactly like before this extension existed.
        _taskbarIcon.MenuActivation = menu is null
            ? HPopupActivationMode.None
            : MapActivation(_spec.MenuActivation);
        _taskbarIcon.ContextFlyout = menu;
        _appliedContextMenu = menu;
        _taskbarIcon.ContextMenuMode = MapMode(_spec.ContextMenuMode);
        _taskbarIcon.ContextMenuThemeMode = MapTheme(_spec.ContextMenuTheme);
        _taskbarIcon.CloseContextMenuOnItemClick = _spec.CloseContextMenuOnItemClick;
    }

    // The public enums mirror the backend's values one-to-one; the mappings stay
    // explicit so the public API does not leak H.NotifyIcon types and a backend
    // enum change cannot silently change this library's meaning.
    private static HPopupActivationMode MapActivation(HNotifyMenuActivation activation)
    {
        return activation switch
        {
            HNotifyMenuActivation.None => HPopupActivationMode.None,
            HNotifyMenuActivation.LeftClick => HPopupActivationMode.LeftClick,
            HNotifyMenuActivation.RightClick => HPopupActivationMode.RightClick,
            HNotifyMenuActivation.DoubleClick => HPopupActivationMode.DoubleClick,
            HNotifyMenuActivation.LeftOrRightClick => HPopupActivationMode.LeftOrRightClick,
            HNotifyMenuActivation.LeftOrDoubleClick => HPopupActivationMode.LeftOrDoubleClick,
            HNotifyMenuActivation.MiddleClick => HPopupActivationMode.MiddleClick,
            HNotifyMenuActivation.All => HPopupActivationMode.All,
            _ => throw new ArgumentOutOfRangeException(nameof(activation), activation, "Unknown menu activation."),
        };
    }

    private static HContextMenuMode MapMode(HNotifyContextMenuMode mode)
    {
        return mode switch
        {
            HNotifyContextMenuMode.PopupMenu => HContextMenuMode.PopupMenu,
            HNotifyContextMenuMode.SecondWindow => HContextMenuMode.SecondWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown context menu mode."),
        };
    }

    private static HPopupMenuThemeMode MapTheme(HNotifyContextMenuTheme theme)
    {
        return theme switch
        {
            HNotifyContextMenuTheme.Default => HPopupMenuThemeMode.Default,
            HNotifyContextMenuTheme.System => HPopupMenuThemeMode.System,
            HNotifyContextMenuTheme.Light => HPopupMenuThemeMode.Light,
            HNotifyContextMenuTheme.Dark => HPopupMenuThemeMode.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown context menu theme."),
        };
    }
}
