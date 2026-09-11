using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Per-process WinUI window that hosts the reconciled content of a tray icon's
/// flyout. Borderless, non-resizable, hidden from the taskbar / Alt-Tab;
/// auto-dismisses on deactivation (light dismiss) or Escape.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors Reactor preview15's internal <c>TrayFlyoutHostWindow</c>: one
/// instance per process, lazily created on the first
/// <see cref="HNotifyTrayIcon.ShowFlyout"/> call, reused by every tray icon, with a
/// fresh <see cref="ReactorHost"/> per show so flyout components get a fresh
/// hook state on every invocation.
/// </para>
/// <para>
/// Unlike preview15 (fixed 280x360 DIPs), the window is sized to the measured
/// desired size of the Reactor content, clamped to the work area of the monitor
/// under the cursor.
/// </para>
/// </remarks>
internal sealed class HNotifyFlyoutHostWindow : IDisposable
{
    private const int FallbackWidthDip = 280;
    private const int FallbackHeightDip = 360;
    private const int ScreenMarginPx = 8;

    private const uint MonitorDefaultToNearest = 2;
    private const uint MdtEffectiveDpi = 0;

    private static HNotifyFlyoutHostWindow? s_instance;
    private static readonly object s_lock = new();

    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private ReactorHost? _host;
    private KeyboardAccelerator? _escapeAccelerator;
    private bool _disposed;
    private bool _isShowing;
    private bool _windowClosed;

    /// <summary>Acquire the per-process host, creating it on first call. UI-thread only.</summary>
    public static HNotifyFlyoutHostWindow GetOrCreate()
    {
        var existing = Volatile.Read(ref s_instance);
        if (existing is not null) return existing;

        lock (s_lock)
        {
            if (s_instance is not null) return s_instance;

            var instance = new HNotifyFlyoutHostWindow();
            Volatile.Write(ref s_instance, instance);
            return instance;
        }
    }

    /// <summary>Dismiss the currently visible flyout, if any. UI-thread only. Idempotent.</summary>
    public static void HideIfShown() => Volatile.Read(ref s_instance)?.Hide();

    private HNotifyFlyoutHostWindow()
    {
        _window = new Window { Title = "HNotifyTrayFlyout" };
        _appWindow = _window.AppWindow;

        try { _appWindow.IsShownInSwitchers = false; }
        catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout IsShownInSwitchers failed: {ex.Message}"); }

        try
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
                presenter.IsAlwaysOnTop = true;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout presenter setup failed: {ex.Message}"); }

        _window.Activated += OnActivated;

        // WinUI closes every window on Application.Exit. From that point on any
        // Window member access throws COMException ("The WinUI Desktop Window
        // object has already been closed"), so latch the state and never touch
        // the window again once this fires.
        _window.Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _windowClosed = true;
        _isShowing = false;

        // Window is gone — only the managed Reactor host needs tearing down, and
        // even that may throw from inside the window's teardown; DetachHost
        // swallows it.
        DetachHost();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_disposed || _windowClosed) return;

        // Light dismiss: clicking outside the flyout, pressing Escape (when it
        // deactivates the window), or invoking another window lands here. During
        // shutdown the window can already be closed by the time WinUI raises
        // this, so nothing may escape into the XAML event loop.
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isShowing)
        {
            try { Hide(); }
            catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout hide failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Mount the flyout content through a fresh <see cref="ReactorHost"/>,
    /// measure it, and show the window next to the cursor. UI-thread only.
    /// </summary>
    public void Show(Element flyoutContent)
    {
        if (_disposed || _windowClosed) return;
        ArgumentNullException.ThrowIfNull(flyoutContent);

        // Tear down the previous mount first so the flyout gets a fresh hook
        // state per invocation (same contract as preview15).
        DetachHost();

        _host = new ReactorHost(_window);
        _host.Mount(_ => flyoutContent);

        _isShowing = true;

        // The Reactor render pass is asynchronous, so the first layout pass is
        // enqueued behind it; a second pass after activation catches content
        // that only reports its final size once the window is live.
        EnqueueLayoutPass(activate: true);
    }

    /// <summary>Hide the flyout without disposing the cached host window. Idempotent.</summary>
    public void Hide()
    {
        if (!_isShowing) return;
        _isShowing = false;

        if (!_windowClosed)
        {
            try { _appWindow.Hide(); }
            catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout Hide failed: {ex.GetType().Name}: {ex.Message}"); }
        }

        // Dispose the per-show host so its hook-state cleanups run promptly; the
        // next Show creates a new host on the same window.
        DetachHost();
    }

    private void EnqueueLayoutPass(bool activate)
    {
        var queue = _window.DispatcherQueue;
        if (queue is null) return;

        queue.TryEnqueue(global::Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_disposed || _windowClosed || !_isShowing) return;

            SizeToContent(activate);
            if (activate) EnqueueLayoutPass(activate: false);
        });
    }

    private void SizeToContent(bool activate)
    {
        if (_windowClosed) return;

        try
        {
            if (activate) EnsureEscapeAccelerator();

            var (widthDip, heightDip) = MeasureContent();
            var rect = ComputeRect(widthDip, heightDip);
            _appWindow.MoveAndResize(rect);

            if (activate) _window.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HNotify] Tray flyout layout failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private (double Width, double Height) MeasureContent()
    {
        if (!TryGetWindowContent(out var content))
            return (FallbackWidthDip, FallbackHeightDip);

        content.Measure(new global::Windows.Foundation.Size(
            double.PositiveInfinity, double.PositiveInfinity));

        var desired = content.DesiredSize;
        var width = desired.Width > 0 ? desired.Width : FallbackWidthDip;
        var height = desired.Height > 0 ? desired.Height : FallbackHeightDip;
        return (width, height);
    }

    private void EnsureEscapeAccelerator()
    {
        if (_escapeAccelerator is not null) return;
        if (!TryGetWindowContent(out var root)) return;

        var accelerator = new KeyboardAccelerator { Key = VirtualKey.Escape };
        accelerator.Invoked += (_, args) =>
        {
            try { Hide(); }
            catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout escape-hide failed: {ex.Message}"); }
            args.Handled = true;
        };
        root.KeyboardAccelerators.Add(accelerator);
        _escapeAccelerator = accelerator;
    }

    private void DetachHost()
    {
        if (_escapeAccelerator is not null)
        {
            if (TryGetWindowContent(out var root))
            {
                try { root.KeyboardAccelerators.Remove(_escapeAccelerator); } catch { /* best effort */ }
            }
            _escapeAccelerator = null;
        }

        try { _host?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[HNotify] Tray flyout host dispose failed: {ex.GetType().Name}: {ex.Message}"); }
        _host = null;
    }

    /// <summary>
    /// Window.Content is only safe while the window is alive: after WinUI closes
    /// the window (Application.Exit, explicit Close) the getter throws
    /// <see cref="COMException"/> ("The WinUI Desktop Window object has already
    /// been closed."). Latches <see cref="_windowClosed"/> so later calls skip
    /// the window entirely.
    /// </summary>
    private bool TryGetWindowContent([NotNullWhen(true)] out FrameworkElement? content)
    {
        content = null;
        if (_disposed || _windowClosed) return false;

        try
        {
            content = _window.Content as FrameworkElement;
            return content is not null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            _windowClosed = true;
            return false;
        }
    }

    /// <summary>
    /// Places the flyout next to the cursor: to the bottom-right when it fits,
    /// otherwise flipped above/left, always clamped inside the work area of the
    /// monitor under the cursor. Sizes are DIPs, scaled to that monitor's DPI so
    /// mixed-DPI setups render consistently.
    /// </summary>
    private static RectInt32 ComputeRect(double widthDip, double heightDip)
    {
        TryGetCursorEnvironment(out var cursor, out var workArea, out var dpi);

        var widthPx = Math.Clamp(
            ToPixels(widthDip, dpi), 1, Math.Max(1, workArea.Width - ScreenMarginPx * 2));
        var heightPx = Math.Clamp(
            ToPixels(heightDip, dpi), 1, Math.Max(1, workArea.Height - ScreenMarginPx * 2));

        var x = cursor.X + widthPx <= workArea.X + workArea.Width ? cursor.X : cursor.X - widthPx;
        var y = cursor.Y + heightPx <= workArea.Y + workArea.Height ? cursor.Y : cursor.Y - heightPx;

        x = Math.Clamp(x, workArea.X + ScreenMarginPx,
            Math.Max(workArea.X + ScreenMarginPx, workArea.X + workArea.Width - widthPx - ScreenMarginPx));
        y = Math.Clamp(y, workArea.Y + ScreenMarginPx,
            Math.Max(workArea.Y + ScreenMarginPx, workArea.Y + workArea.Height - heightPx - ScreenMarginPx));

        return new RectInt32(x, y, widthPx, heightPx);
    }

    private static bool TryGetCursorEnvironment(
        out NativePoint cursor,
        out RectInt32 workArea,
        out uint dpi)
    {
        workArea = new RectInt32(0, 0, 1920, 1080);
        dpi = 96;

        if (!GetCursorPos(out cursor)) return false;

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        workArea = GetWorkAreaOrDefault(monitor);
        dpi = GetDpiForMonitorSafe(monitor);
        return true;
    }

    private static RectInt32 GetWorkAreaOrDefault(nint monitor)
    {
        if (monitor != 0)
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) return info.Work;
        }

        return new RectInt32(0, 0, 1920, 1080);
    }

    private static uint GetDpiForMonitorSafe(nint monitor)
    {
        if (monitor == 0) return 96;

        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX != 0
            ? dpiX
            : 96;
    }

    private static int ToPixels(double dip, uint dpi) =>
        Math.Max(1, (int)Math.Round(dip * dpi / 96.0));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isShowing = false;

        try { _window.Activated -= OnActivated; } catch { /* best effort */ }
        try { _window.Closed -= OnWindowClosed; } catch { /* best effort */ }

        DetachHost();

        if (!_windowClosed)
        {
            try { _window.Close(); } catch { /* best effort */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public RectInt32 Monitor;
        public RectInt32 Work;
        public uint Flags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, uint dpiType, out uint dpiX, out uint dpiY);
}
