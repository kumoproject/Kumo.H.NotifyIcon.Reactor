using Microsoft.UI.Reactor;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// UI-thread validation for the tray surface, mirroring Reactor's internal
/// <c>ThreadAffinity.ThrowIfNotOnUIThread</c> contract: every mutator on
/// <see cref="HNotifyTrayIcon"/> must run on the UI thread captured by
/// <see cref="ReactorApp.UIDispatcher"/>.
/// </summary>
internal static class HNotifyThreading
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the current thread is
    /// not the Reactor UI thread.
    /// </summary>
    public static void ThrowIfNotOnUiThread(string memberName)
    {
        var dispatcher = ReactorApp.UIDispatcher;
        if (dispatcher is null)
        {
            throw new InvalidOperationException(
                $"{memberName} must be called on the Reactor UI thread. " +
                "Open tray icons from the ReactorApp.Run startup callback (or any UI-thread code " +
                "that runs after it) so ReactorApp.UIDispatcher has been captured.");
        }

        if (!dispatcher.HasThreadAccess)
        {
            throw new InvalidOperationException(
                $"{memberName} must be called on the UI thread.");
        }
    }
}
