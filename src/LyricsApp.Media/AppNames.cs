using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace LyricsApp.Media;

/// <summary>Turns a player's application id into the name the Start menu shows for it.</summary>
public static class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string Get(string app) => Cache.GetOrAdd(app, Resolve);

    private static string Resolve(string app)
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                && AppInfo.GetFromAppUserModelId(app)?.DisplayInfo.DisplayName is { Length: > 0 } name) return name;
        }
        catch (Exception)
        {
            // Not a packaged app.
        }
        try
        {
            if (FromStartMenu(app) is { Length: > 0 } name) return name;
        }
        catch (Exception)
        {
            // Not in the Start menu either (many desktop players report their file name as the id).
        }
        return Fallback(app);
    }

    /// <summary>Desktop programs: the name of the Start menu entry that carries this id.</summary>
    private static string? FromStartMenu(string app)
    {
        var shellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + app, IntPtr.Zero, ref shellItem, out var item) != 0 || item is null) return null;
        try
        {
            const uint NormalDisplay = 0;
            if (item.GetDisplayName(NormalDisplay, out var text) != 0 || text == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(text);
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId, out IShellItem? item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem? parent);
        [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
    }

    /// <summary>A readable name from the id alone: "Player.exe" → "Player", "Vendor.Player_abc!App" → "Player".</summary>
    public static string Fallback(string app)
    {
        var name = app.Trim();
        var bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];
        var underscore = name.LastIndexOf('_');
        if (bang > 0 && underscore > 0) name = name[..underscore];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        else if (bang > 0 && name.LastIndexOf('.') is var dot and > 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return name.Length == 0 ? app : name;
    }
}
