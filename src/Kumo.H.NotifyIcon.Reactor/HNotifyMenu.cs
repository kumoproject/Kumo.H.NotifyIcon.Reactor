using System.Windows.Input;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Kumo.H.NotifyIcon.Reactor;

/// <summary>
/// When the H.NotifyIcon native context menu opens on its own, mirroring the
/// backend's <c>MenuActivation</c> values. (HNotify extension — preview15's
/// tray API has no native-menu concept.)
/// </summary>
public enum HNotifyMenuActivation
{
    /// <summary>Never auto-open; show the menu from code via <c>HNotifyTrayIcon.ShowContextMenu</c>.</summary>
    None,

    /// <summary>Open on a left click.</summary>
    LeftClick,

    /// <summary>Open on a right click (the default).</summary>
    RightClick,

    /// <summary>Open on a double click.</summary>
    DoubleClick,

    /// <summary>Open on a left or right click.</summary>
    LeftOrRightClick,

    /// <summary>Open on a left click or a double click.</summary>
    LeftOrDoubleClick,

    /// <summary>Open on a middle click.</summary>
    MiddleClick,

    /// <summary>Open on any click.</summary>
    All,
}

/// <summary>
/// How H.NotifyIcon renders the native context menu. (HNotify extension.)
/// </summary>
public enum HNotifyContextMenuMode
{
    /// <summary>
    /// The <see cref="Microsoft.UI.Xaml.Controls.MenuFlyout"/> items are converted
    /// into a native Win32 popup menu drawn by the shell (<c>TrackPopupMenuEx</c>).
    /// This is H.NotifyIcon's default. Item actions dispatch through
    /// <c>ICommand</c> only — <c>MenuFlyoutItem.Click</c> handlers never run.
    /// </summary>
    PopupMenu,

    /// <summary>
    /// H.NotifyIcon hosts the <see cref="Microsoft.UI.Xaml.Controls.MenuFlyout"/>
    /// in a dedicated window of its own (full XAML menu, standard Click/Command
    /// dispatch). Creates one extra window for the process; H.NotifyIcon marks
    /// this mode as still in preview.
    /// </summary>
    SecondWindow,
}

/// <summary>
/// Theme of the native popup menu. (HNotify extension.)
/// </summary>
public enum HNotifyContextMenuTheme
{
    /// <summary>Windows' default native menu appearance.</summary>
    Default,

    /// <summary>Follow the current Windows app theme (the default).</summary>
    System,

    /// <summary>Force a light native menu where supported.</summary>
    Light,

    /// <summary>Force a dark native menu where supported.</summary>
    Dark,
}

/// <summary>
/// Converts Reactor's menu DSL — <c>MenuItem()</c>, <c>MenuSeparator()</c>,
/// <c>MenuSubItem()</c>, <c>ToggleMenuItem()</c>, <c>RadioMenuItem()</c> from
/// <c>Microsoft.UI.Reactor.Factories</c> — into the
/// <see cref="Microsoft.UI.Xaml.Controls.MenuFlyout"/> that
/// <see cref="HNotifyTrayIconSpec.ContextMenu"/> hands to H.NotifyIcon.
/// (HNotify extension.)
/// </summary>
/// <remarks>
/// <para>
/// Reusing Reactor's DSL keeps tray-menu call sites identical to Reactor menu
/// code elsewhere: <c>HNotifyMenu.Create(MenuItem("退出", Exit), MenuSeparator(), ...)</c>.
/// Item actions are wired through <see cref="System.Windows.Input.ICommand"/>
/// (not <c>Click</c>) because that is the only dispatch path
/// <see cref="HNotifyContextMenuMode.PopupMenu"/> honours; window-hosted mode
/// executes the command too, so one wiring works everywhere.
/// </para>
/// <para>
/// <b>Translation limits</b> (imposed by H.NotifyIcon's menu rendering): icons
/// ARE converted — the <c>MenuItem(..., icon:)</c> string (a <c>Symbol</c> enum
/// name or a Segoe Fluent glyph) and every public <c>IconData</c> shape — but
/// only <see cref="HNotifyContextMenuMode.SecondWindow"/> mode renders them;
/// the native popup carries text / enabled / checked only. Keyboard
/// accelerators, access keys and descriptions are not mapped;
/// <c>ToggleMenuItem</c> / <c>RadioMenuItem</c> keep their initial checked
/// state, but their callbacks only fire in window-hosted mode, and the native
/// popup never writes the checked state back.
/// </para>
/// </remarks>
public static partial class HNotifyMenu
{
    /// <summary>
    /// Converts Reactor menu items (<c>MenuItem()</c> and friends) into a
    /// <see cref="Microsoft.UI.Xaml.Controls.MenuFlyout"/> for
    /// <see cref="HNotifyTrayIconSpec.ContextMenu"/>.
    /// </summary>
    public static WinUI.MenuFlyout Create(params MenuFlyoutItemBase[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var menu = new WinUI.MenuFlyout();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            menu.Items.Add(Convert(item));
        }
        return menu;
    }

    /// <summary>
    /// Convenience overload for Reactor's <c>MenuItems(...)</c> container element.
    /// </summary>
    public static WinUI.MenuFlyout Create(MenuFlyoutContentElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Create(content.Items);
    }

    /// <summary>Converts a single Reactor menu item (recursively for sub-menus).</summary>
    public static WinUI.MenuFlyoutItemBase Convert(MenuFlyoutItemBase item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item switch
        {
            MenuFlyoutItemData data => CreateItem(data),
            MenuFlyoutSeparatorData => new WinUI.MenuFlyoutSeparator(),
            MenuFlyoutSubItemData data => CreateSubItem(data),
            ToggleMenuFlyoutItemData data => CreateToggle(data),
            RadioMenuFlyoutItemData data => CreateRadio(data),
            _ => throw new NotSupportedException(
                $"{item.GetType().Name} is not supported by the H.NotifyIcon context-menu conversion."),
        };
    }

    private static WinUI.MenuFlyoutItem CreateItem(MenuFlyoutItemData data)
    {
        var item = new WinUI.MenuFlyoutItem
        {
            Text = data.Text,
            IsEnabled = data.IsEnabled,
        };

        if (ResolveIcon(data.IconElement, data.Icon) is { } icon)
            item.Icon = icon;

        if (data.OnClick is not null)
        {
            // Command, not Click — see the remarks on this class.
            item.Command = new DelegatingCommand(data.OnClick);
        }
        return item;
    }

    private static WinUI.MenuFlyoutSubItem CreateSubItem(MenuFlyoutSubItemData data)
    {
        var item = new WinUI.MenuFlyoutSubItem { Text = data.Text };

        if (ResolveIcon(data.IconElement, data.Icon) is { } icon)
            item.Icon = icon;

        foreach (var child in data.Items)
        {
            ArgumentNullException.ThrowIfNull(child);
            item.Items.Add(Convert(child));
        }
        return item;
    }

    private static WinUI.ToggleMenuFlyoutItem CreateToggle(ToggleMenuFlyoutItemData data)
    {
        var item = new WinUI.ToggleMenuFlyoutItem
        {
            Text = data.Text,
            IsChecked = data.IsChecked,
        };

        if (ResolveIcon(data.IconElement, data.Icon) is { } icon)
            item.Icon = icon;

        if (data.OnIsCheckedChanged is not null)
        {
            // Click-based on purpose: PopupMenu mode neither raises Click nor
            // writes IsChecked back, so state round-tripping is window-mode only.
            item.Click += (sender, _) =>
                data.OnIsCheckedChanged(((WinUI.ToggleMenuFlyoutItem)sender!).IsChecked);
        }
        return item;
    }

    private static WinUI.RadioMenuFlyoutItem CreateRadio(RadioMenuFlyoutItemData data)
    {
        var item = new WinUI.RadioMenuFlyoutItem
        {
            Text = data.Text,
            GroupName = data.GroupName,
            IsChecked = data.IsChecked,
        };

        if (ResolveIcon(data.IconElement, data.Icon) is { } icon)
            item.Icon = icon;

        if (data.OnClick is not null)
        {
            item.Click += (_, _) => data.OnClick();
        }
        return item;
    }

    // Reactor's own IconResolver is internal, so its IconData → IconElement
    // projection is mirrored here with the same semantics: IconData wins over
    // the string; a string is either a Symbol enum name or a Segoe Fluent glyph.
    private static WinUI.IconElement? ResolveIcon(IconData? iconData, string? icon)
    {
        if (iconData is not null)
        {
            return iconData switch
            {
                SymbolIconData data => ResolveIconString(data.Symbol) ?? new WinUI.SymbolIcon(WinUI.Symbol.Placeholder),
                FontIconData data => CreateFontIcon(data),
                BitmapIconData data => new WinUI.BitmapIcon { UriSource = data.Source, ShowAsMonochrome = data.ShowAsMonochrome },
                PathIconData data => CreatePathIcon(data),
                ImageIconData data => new WinUI.ImageIcon { Source = new BitmapImage(data.Source) },
                _ => null,
            };
        }

        return icon is null ? null : ResolveIconString(icon);
    }

    private static WinUI.IconElement? ResolveIconString(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return null;

        if (Enum.TryParse<WinUI.Symbol>(symbol, ignoreCase: true, out var parsed))
            return new WinUI.SymbolIcon(parsed);

        return new WinUI.FontIcon
        {
            Glyph = symbol,
            FontFamily = ResolveSymbolFontFamily(),
        };
    }

    private static FontFamily ResolveSymbolFontFamily()
    {
        if (Application.Current?.Resources is { } resources
            && resources.TryGetValue("SymbolThemeFontFamily", out var value)
            && value is FontFamily themeFont)
        {
            return themeFont;
        }
        return new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    }

    private static WinUI.FontIcon CreateFontIcon(FontIconData data)
    {
        var icon = new WinUI.FontIcon { Glyph = data.Glyph };
        if (data.FontFamily is not null) icon.FontFamily = new FontFamily(data.FontFamily);
        if (data.FontSize.HasValue) icon.FontSize = data.FontSize.Value;
        return icon;
    }

    private static WinUI.PathIcon CreatePathIcon(PathIconData data)
    {
        var icon = new WinUI.PathIcon();
        if (XamlReader.Load(
            $"<Geometry xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>{data.Data}</Geometry>")
            is Geometry geometry)
        {
            icon.Data = geometry;
        }
        return icon;
    }

    private sealed partial class DelegatingCommand(Action onClick) : ICommand
    {
        // The backend only calls Execute; no consumer needs a CanExecute changed
        // notification, so the event has no backing storage (custom accessors also
        // keep CS0067 quiet for a field-like event nobody raises).
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => onClick();
    }
}
