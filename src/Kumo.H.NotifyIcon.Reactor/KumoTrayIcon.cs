using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using HTaskbarIcon = global::H.NotifyIcon.TaskbarIcon;
using HMouseEvent = global::H.NotifyIcon.Core.MouseEvent;
using HMouseEventReceivedEventArgs = global::H.NotifyIcon.Core.MessageWindow.MouseEventReceivedEventArgs;
using HPopupActivationMode = global::H.NotifyIcon.Core.PopupActivationMode;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Top-level system-tray icon — the H.NotifyIcon-backed peer of Reactor
/// v0.1.0-preview.15's <c>Microsoft.UI.Reactor.ReactorTrayIcon</c>.
/// Created via <see cref="KumoApp.OpenTrayIcon"/>; provides the icon bitmap,
/// tooltip, and click events, and reconciles a Reactor <see cref="Element"/>
/// as its flyout via <see cref="ShowFlyout"/>.
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
/// hidden-window bookkeeping is needed here. The flyout is rendered by
/// <see cref="KumoFlyoutHostWindow"/>, a single borderless topmost window
/// reused per process (only one tray flyout can be open at a time, same as
/// preview15).
/// </para>
/// </remarks>
public sealed class KumoTrayIcon : IDisposable
{
    private static int s_nextId;

    private readonly string _id;
    private KumoTrayIconSpec _spec;
    private HTaskbarIcon? _taskbarIcon;
    private global::System.IDisposable? _mouseSubscription;
    private bool _registered;
    private bool _disposed;

    /// <summary>Stable id, e.g. <c>"kumo-tray-1"</c>. Allocated monotonically per process.</summary>
    public string Id => _id;

    /// <summary>Optional stable identity (from <see cref="KumoTrayIconSpec.Key"/>).</summary>
    public WindowKey? Key => _spec.Key;

    /// <summary>Last-applied <see cref="KumoTrayIconSpec"/> snapshot.</summary>
    public KumoTrayIconSpec Spec => _spec;

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
            KumoThreading.ThrowIfNotOnUiThread(nameof(Icon));
            if (_disposed || (ReferenceEquals(_spec.Icon, value) && _spec.DrawingIcon is null)) return;

            _spec = _spec with { Icon = value, DrawingIcon = null };
            ApplyIcon();
        }
    }

    /// <summary>
    /// The in-memory icon bitmap source (Kumo extension over preview15).
    /// Mutating triggers a shell re-apply.
    /// </summary>
    public System.Drawing.Icon? DrawingIcon
    {
        get => _spec.DrawingIcon;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            KumoThreading.ThrowIfNotOnUiThread(nameof(DrawingIcon));
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
            KumoThreading.ThrowIfNotOnUiThread(nameof(Tooltip));
            if (_disposed || string.Equals(_spec.Tooltip, value, StringComparison.Ordinal)) return;

            _spec = _spec with { Tooltip = value };
            if (_taskbarIcon is not null) _taskbarIcon.ToolTipText = value;
        }
    }

    /// <summary>Whether the icon is currently shown. Mutating triggers a shell re-apply.</summary>
    public bool IsVisible
    {
        get => _spec.IsVisible;
        set
        {
            KumoThreading.ThrowIfNotOnUiThread(nameof(IsVisible));
            if (_disposed || _spec.IsVisible == value) return;

            _spec = _spec with { IsVisible = value };
            ApplyVisibility();
        }
    }

    /// <summary>Fires on the UI thread when the user left-clicks the icon (single click).</summary>
    public event EventHandler? Click;

    /// <summary>Fires on the UI thread when the user double-clicks the icon.</summary>
    public event EventHandler? DoubleClick;

    /// <summary>Fires on the UI thread when the user right-clicks the icon.</summary>
    public event EventHandler? RightClick;

    internal KumoTrayIcon(KumoTrayIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
        _id = $"kumo-tray-{Interlocked.Increment(ref s_nextId)}";
    }

    /// <summary>
    /// Registers the icon with the shell. UI-thread only. Called once by
    /// <see cref="KumoApp.OpenTrayIcon"/>; subsequent state changes flow through
    /// <see cref="Update"/> or the property setters.
    /// </summary>
    internal void RegisterWithShell()
    {
        KumoThreading.ThrowIfNotOnUiThread(nameof(RegisterWithShell));
        if (_registered) return;

        var taskbarIcon = new HTaskbarIcon();
        _taskbarIcon = taskbarIcon;

        // H.NotifyIcon keys shell state by Guid. The default id is derived from
        // the process path, so two icons in one process would collide; a spec
        // key maps to a stable per-icon id instead.
        if (_spec.Key is { } key)
        {
            taskbarIcon.CustomName = $"Kumo.H.NotifyIcon.Reactor.{key}";
        }

        // The library owns Click/DoubleClick/RightClick and ShowFlyout, so the
        // backend must not pop its own menu/popup on any mouse gesture.
        taskbarIcon.MenuActivation = HPopupActivationMode.None;
        taskbarIcon.PopupActivation = HPopupActivationMode.None;

        taskbarIcon.ToolTipText = _spec.Tooltip ?? string.Empty;

        var icon = KumoIconResolver.Resolve(_spec);
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
    /// Show a Reactor <see cref="Element"/> as the tray icon's flyout. UI-thread
    /// only. Light-dismisses when the user clicks outside it or presses Escape.
    /// </summary>
    public void ShowFlyout(Element flyoutContent)
    {
        ArgumentNullException.ThrowIfNull(flyoutContent);
        KumoThreading.ThrowIfNotOnUiThread(nameof(ShowFlyout));
        if (_disposed) return;

        KumoFlyoutHostWindow.GetOrCreate().Show(flyoutContent);
    }

    /// <summary>Dismiss the tray flyout. UI-thread only. Idempotent.</summary>
    public void HideFlyout()
    {
        KumoThreading.ThrowIfNotOnUiThread(nameof(HideFlyout));
        KumoFlyoutHostWindow.HideIfShown();
    }

    /// <summary>
    /// Diff <paramref name="next"/> against the current spec and re-apply only
    /// changed fields. UI-thread only.
    /// </summary>
    public void Update(KumoTrayIconSpec next)
    {
        ArgumentNullException.ThrowIfNull(next);
        KumoThreading.ThrowIfNotOnUiThread(nameof(Update));
        if (_disposed) throw new ObjectDisposedException(nameof(KumoTrayIcon));

        var prev = _spec;
        _spec = next;
        if (Equals(prev, next)) return;

        if (!ReferenceEquals(prev.Icon, next.Icon) ||
            !ReferenceEquals(prev.DrawingIcon, next.DrawingIcon))
        {
            ApplyIcon();
        }

        if (!string.Equals(prev.Tooltip, next.Tooltip, StringComparison.Ordinal))
        {
            if (_taskbarIcon is not null) _taskbarIcon.ToolTipText = next.Tooltip ?? string.Empty;
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
        KumoThreading.ThrowIfNotOnUiThread(nameof(Dispose));
        _disposed = true;

        var taskbarIcon = _taskbarIcon;
        _taskbarIcon = null;

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
                Debug.WriteLine($"[Kumo] Tray icon dispose failed: {ex.Message}");
            }
        }

        _registered = false;
        KumoApp.UnregisterTrayIcon(this);
    }

    private void ApplyIcon()
    {
        if (_taskbarIcon is null) return;
        // Assigning Icon disposes the previous instance (H.NotifyIcon owns what
        // it holds), so the resolved clone must always be a fresh instance —
        // which KumoIconResolver guarantees.
        _taskbarIcon.Icon = KumoIconResolver.Resolve(_spec);
    }

    private void ApplyVisibility()
    {
        if (_taskbarIcon is null) return;
        _taskbarIcon.Visibility = _spec.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
