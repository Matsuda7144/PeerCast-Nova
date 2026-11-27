using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

// Public DWM attributes only. Native caption buttons, hit testing and scrollbars stay owned by Windows.
internal sealed class GenesisGuiNextWindowChrome : IDisposable
{
    internal const int DarkMode = 20, CaptionColor = 35, TextColor = 36;
    private static readonly int[] Attributes = [DarkMode, CaptionColor, TextColor];
    private readonly Func<bool?> _highContrast;
    private readonly Func<int, int, bool> _write;
    private readonly Dictionary<int, int> _original = new();
    private bool _changed, _disposed;

    internal static GenesisGuiNextWindowChrome? TryCreate(nint window, bool modern)
    {
        // The documented support boundary for all three attributes is Windows 11 build 22000.
        if (!modern || window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return null;
        return new(ReadHighContrast, attribute => Read(window, attribute),
            (attribute, value) => Write(window, attribute, value));
    }

    internal GenesisGuiNextWindowChrome(Func<bool?> highContrast, Func<int, int?> read, Func<int, int, bool> write)
    {
        _highContrast = highContrast; _write = write;
        foreach (var attribute in Attributes)
            if (read(attribute) is { } value) _original[attribute] = value;
            // A new Nova main window has no custom caption colors. Windows can reject color
            // getters before the first setter; -1 is the documented reset-to-system value.
            else if (attribute != DarkMode) _original[attribute] = -1;
        Refresh();
    }

    internal void Refresh()
    {
        if (_disposed || _original.Count != Attributes.Length) return;
        // Unknown accessibility state is treated conservatively, just like high contrast.
        if (_highContrast() != false) { Restore(); return; }
        foreach (var (attribute, value) in new[] { (DarkMode, 1), (CaptionColor, GenesisGuiNextDesign.Panel), (TextColor, GenesisGuiNextDesign.Text) })
        {
            if (!_write(attribute, value)) { Restore(); return; }
            _changed = true;
        }
    }

    private void Restore()
    {
        if (!_changed) return;
        var restored = true;
        foreach (var attribute in Attributes) restored &= _write(attribute, _original[attribute]);
        if (restored) _changed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restore();
    }

    private static int? Read(nint window, int attribute)
    {
        try { return DwmGetWindowAttribute(window, attribute, out var value, sizeof(int)) >= 0 ? value : null; }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }
    private static bool Write(nint window, int attribute, int value)
    {
        try { return DwmSetWindowAttribute(window, attribute, ref value, sizeof(int)) >= 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
    private static bool? ReadHighContrast()
    {
        var state = new Win32HighContrast { Size = (uint)Marshal.SizeOf<Win32HighContrast>() };
        return SystemParametersInfoW(0x0042, state.Size, ref state, 0) ? (state.Flags & 1) != 0 : null;
    }
}
