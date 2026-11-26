using System.Runtime.InteropServices;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class Win32AccessibilityAnnotationTests
{
    private const uint ObjectIdClient = 0xFFFFFFFCu;
    private const ushort VariantTypeInt4 = 3;
    private const uint WindowStylePopup = 0x80000000u;

    private static readonly Guid IAccessibleId = new("618736e0-3c3d-11cf-810c-00aa00389b71");

    [Fact]
    public void SetName_ReturnsFalseWhenHandleOrNameIsInvalid()
    {
        Assert.False(Win32AccessibilityAnnotation.SetName(0, "テスト"));
        Assert.False(Win32AccessibilityAnnotation.SetName(1, ""));
        Assert.False(Win32AccessibilityAnnotation.SetName(1, "   "));
    }

    [Fact]
    public void SetNameAndDescription_ReturnsFalseWhenHandleOrNameIsInvalid()
    {
        Assert.False(Win32AccessibilityAnnotation.SetNameAndDescription(0, "テスト", "補足", "ヘルプ"));
        Assert.False(Win32AccessibilityAnnotation.SetNameAndDescription(1, "", "補足", "ヘルプ"));
    }

    [Fact]
    public void Clear_ReturnsFalseWhenHandleIsZero()
    {
        Assert.False(Win32AccessibilityAnnotation.Clear(0));
    }

    [Fact]
    public void SetNameAndDescription_OnWindowsControlUpdatesAndClearsMsaaProperties()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? actualName = null;
        string? actualDescription = null;
        string? actualHelp = null;
        string? clearedDescription = null;
        string? clearedHelp = null;
        var setResult = false;
        var clearResult = false;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            nint hwnd = 0;
            nint accessiblePtr = 0;
            try
            {
                hwnd = CreateWindowExW(0, "EDIT", string.Empty, WindowStylePopup, 0, 0, 120, 24, 0, 0, 0, 0);
                Assert.NotEqual(0, hwnd);

                setResult = Win32AccessibilityAnnotation.SetNameAndDescription(
                    hwnd,
                    "テスト検索欄",
                    description: "補足説明",
                    help: "操作ヘルプ");
                Assert.True(setResult);

                var hr = AccessibleObjectFromWindow(hwnd, ObjectIdClient, in IAccessibleId, out accessiblePtr);
                Assert.Equal(0, hr);
                Assert.NotEqual(0, accessiblePtr);

                actualName = ReadAccessibleString(accessiblePtr, slotIndex: 10);
                actualDescription = ReadAccessibleString(accessiblePtr, slotIndex: 12);
                actualHelp = ReadAccessibleString(accessiblePtr, slotIndex: 15);

                _ = Marshal.Release(accessiblePtr);
                accessiblePtr = 0;

                clearResult = Win32AccessibilityAnnotation.Clear(hwnd);
                Assert.True(clearResult);

                hr = AccessibleObjectFromWindow(hwnd, ObjectIdClient, in IAccessibleId, out accessiblePtr);
                Assert.Equal(0, hr);
                Assert.NotEqual(0, accessiblePtr);

                clearedDescription = ReadAccessibleString(accessiblePtr, slotIndex: 12);
                clearedHelp = ReadAccessibleString(accessiblePtr, slotIndex: 15);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (accessiblePtr != 0)
                {
                    _ = Marshal.Release(accessiblePtr);
                }

                if (hwnd != 0)
                {
                    _ = DestroyWindow(hwnd);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }

        Assert.True(setResult);
        Assert.Equal("テスト検索欄", actualName);
        Assert.Equal("補足説明", actualDescription);
        Assert.Equal("操作ヘルプ", actualHelp);
        Assert.True(clearResult);
        Assert.Null(clearedDescription);
        Assert.Null(clearedHelp);
    }

    private static string? ReadAccessibleString(nint accessiblePtr, int slotIndex)
    {
        var vtable = Marshal.ReadIntPtr(accessiblePtr);
        var fnPtr = Marshal.ReadIntPtr(vtable, slotIndex * nint.Size);
        var getter = Marshal.GetDelegateForFunctionPointer<GetAccessibleStringDelegate>(fnPtr);
        var child = new VariantChild { vt = VariantTypeInt4, data1 = 0, data2 = 0 };
        var hr = getter(accessiblePtr, child, out var bstr);
        if (hr != 0 || bstr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VariantChild
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public nint data1;
        public nint data2;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAccessibleStringDelegate(nint self, VariantChild child, out nint bstr);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        uint idObject,
        in Guid riid,
        out nint ppvObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
