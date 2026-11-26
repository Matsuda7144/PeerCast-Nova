using System.Runtime.InteropServices;

namespace PeerCast.Nova.Windows;

/// <summary>
/// MSAA / UI Automation (IAccPropServices) を使ってネイティブ Win32 コントロールに
/// アクセシブル名・説明・操作ヘルプを動的に付与するヘルパー。
/// </summary>
public static class Win32AccessibilityAnnotation
{
    private const uint ObjectIdClient = 0xFFFFFFFCu;
    private const uint ChildIdSelf = 0u;
    private const uint ClsCtxInprocServer = 0x1u;

    private static readonly Guid ClsidAccPropServices = new("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7");
    private static readonly Guid IidIAccPropServices = new("6e26e776-04f0-495d-80e4-3330352e3169");
    private static readonly Guid PropIdAccName = new("608d3df8-8128-4aa7-a428-f55e49267291");
    private static readonly Guid PropIdAccDescription = new("4d48dfe4-bd3f-491f-a648-492d6f20c588");
    private static readonly Guid PropIdAccHelp = new("c831e11f-44db-4a99-9768-cb8f978b7231");

    public static bool SetName(nint hwnd, string name) =>
        SetNameAndDescription(hwnd, name);

    public static bool SetNameAndDescription(
        nint hwnd,
        string name,
        string? description = null,
        string? help = null)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0 || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        IAccPropServices? services = null;
        try
        {
            if (!TryCreateServices(out services) || services is null)
            {
                return false;
            }

            var hr = services.SetHwndPropStr(hwnd, ObjectIdClient, ChildIdSelf, PropIdAccName, name);
            if (hr < 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                _ = services.SetHwndPropStr(hwnd, ObjectIdClient, ChildIdSelf, PropIdAccDescription, description);
            }

            if (!string.IsNullOrWhiteSpace(help))
            {
                _ = services.SetHwndPropStr(hwnd, ObjectIdClient, ChildIdSelf, PropIdAccHelp, help);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (services is not null && Marshal.IsComObject(services))
            {
                _ = Marshal.ReleaseComObject(services);
            }
        }
    }

    public static bool Clear(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0)
        {
            return false;
        }

        IAccPropServices? services = null;
        try
        {
            if (!TryCreateServices(out services) || services is null)
            {
                return false;
            }

            var properties = new[] { PropIdAccName, PropIdAccDescription, PropIdAccHelp };
            var hr = services.ClearHwndProps(hwnd, ObjectIdClient, ChildIdSelf, properties, properties.Length);
            return hr >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (services is not null && Marshal.IsComObject(services))
            {
                _ = Marshal.ReleaseComObject(services);
            }
        }
    }

    private static bool TryCreateServices(out IAccPropServices? services)
    {
        services = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var clsid = ClsidAccPropServices;
        var iid = IidIAccPropServices;
        var hr = CoCreateInstance(ref clsid, 0, ClsCtxInprocServer, ref iid, out var instance);
        if (hr < 0 || instance is null)
        {
            return false;
        }

        services = instance as IAccPropServices;
        if (services is not null)
        {
            return true;
        }

        if (Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }

        return false;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [ComImport]
    [Guid("6e26e776-04f0-495d-80e4-3330352e3169")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccPropServices
    {
        [PreserveSig]
        int SetPropValue(
            nint pIDString,
            uint dwIDStringLen,
            Guid idProp,
            [MarshalAs(UnmanagedType.Struct)] object var);

        [PreserveSig]
        int SetPropServer(
            nint pIDString,
            uint dwIDStringLen,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] Guid[] paProps,
            int cProps,
            [MarshalAs(UnmanagedType.Interface)] object? pServer,
            int annoScope);

        [PreserveSig]
        int ClearProps(
            nint pIDString,
            uint dwIDStringLen,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] Guid[] paProps,
            int cProps);

        [PreserveSig]
        int SetHwndProp(
            nint hwnd,
            uint idObject,
            uint idChild,
            Guid idProp,
            [MarshalAs(UnmanagedType.Struct)] object var);

        [PreserveSig]
        int SetHwndPropStr(
            nint hwnd,
            uint idObject,
            uint idChild,
            Guid idProp,
            [MarshalAs(UnmanagedType.LPWStr)] string str);

        [PreserveSig]
        int SetHwndPropServer(
            nint hwnd,
            uint idObject,
            uint idChild,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] Guid[] paProps,
            int cProps,
            [MarshalAs(UnmanagedType.Interface)] object? pServer,
            int annoScope);

        [PreserveSig]
        int ClearHwndProps(
            nint hwnd,
            uint idObject,
            uint idChild,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] Guid[] paProps,
            int cProps);
    }
}
