using System.Drawing;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// Temporary stand-in for Reactor preview15's <c>WindowIcon.FromBytes</c>, which
/// does not exist in preview14 (this library's compile dependency).
/// </summary>
/// <remarks>
/// <para>
/// Returns the <see cref="System.Drawing.Icon"/> flavour that
/// <see cref="HNotifyTrayIconSpec"/> accepts, so a call site reads the same as
/// preview15's <c>new TrayIconSpec(WindowIcon.FromBytes(...), ...)</c>. When the
/// app moves to preview15, only the factory name changes:
/// </para>
/// <code>
/// // now (preview14 + this library)
/// new HNotifyTrayIconSpec(HNotifyWindowIcon.FromBytes(bytes), "My App", key);
///
/// // later (preview15)
/// new TrayIconSpec(WindowIcon.FromBytes(bytes), "My App", key);
/// </code>
/// <para>
/// Only encoded <c>.ico</c> bytes are supported — the backend is
/// <see cref="System.Drawing.Icon"/>. preview15's <c>FromBytes</c> additionally
/// understands other encodings. When the icon already exists as a
/// <see cref="System.Drawing.Icon"/> (e.g. extracted from a shell handle), use
/// <see cref="HNotifyTrayIconSpec.FromDrawingIcon"/> instead.
/// </para>
/// </remarks>
public static class HNotifyWindowIcon
{
    /// <summary>
    /// Creates a tray-ready icon from raw <c>.ico</c> bytes (embedded resource,
    /// <c>File.ReadAllBytes</c>, downloaded buffer, …).
    /// </summary>
    /// <param name="data">Encoded <c>.ico</c> image. Must not be empty.</param>
    /// <returns>
    /// A fresh <see cref="System.Drawing.Icon"/> owned by the caller. The tray
    /// clones it before applying, so disposing it after opening the tray icon is
    /// safe.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> is empty, or is not a valid <c>.ico</c> image.
    /// </exception>
    public static Icon FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            throw new ArgumentException("Icon data is empty.", nameof(data));

        try
        {
            // System.Drawing.Icon keeps a reference to the stream it is built
            // from (it can read from it lazily when the handle is first
            // requested), so this MemoryStream is intentionally never disposed:
            // no temp file, no file handle — just the byte buffer we were given.
            return new Icon(new MemoryStream(data, writable: false));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"{data.Length} bytes could not be parsed as an .ico image. " +
                "System.Drawing.Icon (the H.NotifyIcon backend) only accepts .ico data.",
                nameof(data), ex);
        }
    }

    /// <summary>
    /// Convenience overload for resource / file streams: copies the stream and
    /// hands the bytes to <see cref="FromBytes(byte[])"/>. The stream can be
    /// disposed as soon as this method returns.
    /// </summary>
    public static Icon FromBytes(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray());
    }
}
