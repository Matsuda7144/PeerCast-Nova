using System.Runtime.InteropServices;

namespace PeerCast.Nova.App;

internal static partial class Win32NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Point
    {
        public Win32Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Math.Max(0, Right - Left);
        public int Height => Math.Max(0, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32WindowClassEx
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32PaintStruct
    {
        public nint Dc;
        public int Erase;
        public Win32Rect Bounds;
        public int Restore;
        public int Incremental;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
        public uint Reserved7;
        public uint Reserved8;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32MonitorInfo
    {
        public uint Size;
        public Win32Rect Monitor;
        public Win32Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32TrackMouseEvent
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32InitCommonControlsEx
    {
        public uint Size;
        public uint Classes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ToolTip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32MinMaxInfo
    {
        public Win32Point Reserved;
        public Win32Point MaxSize;
        public Win32Point MaxPosition;
        public Win32Point MinTrackSize;
        public Win32Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public Win32Point MinPosition;
        public Win32Point MaxPosition;
        public Win32Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32WindowMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Win32Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32ScrollInfo
    {
        public uint Size;
        public uint Mask;
        public int Minimum;
        public int Maximum;
        public uint Page;
        public int Position;
        public int TrackPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32DrawItem
    {
        public uint ControlType;
        public uint ControlId;
        public uint ItemId;
        public uint ItemAction;
        public uint ItemState;
        public nint ControlWindow;
        public nint DeviceContext;
        public Win32Rect ItemRectangle;
        public nuint ItemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32NotifyHeader
    {
        public nint WindowFrom;
        public nuint ControlId;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32CustomDraw
    {
        public Win32NotifyHeader Header;
        public uint DrawStage;
        public nint DeviceContext;
        public Win32Rect ItemRectangle;
        public nuint ItemSpec;
        public uint ItemState;
        public nint ItemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32ListViewCustomDraw
    {
        public Win32CustomDraw Draw;
        public int TextColor;
        public int TextBackgroundColor;
        public int SubItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32TreeViewCustomDraw
    {
        public Win32CustomDraw Draw;
        public int TextColor;
        public int TextBackgroundColor;
        public int Level;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32ListViewNotify
    {
        public Win32NotifyHeader Header;
        public int Item;
        public int SubItem;
        public uint NewState;
        public uint OldState;
        public uint Changed;
        public Win32Point ActionPoint;
        public nint Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32ListViewKeyDown
    {
        public Win32NotifyHeader Header;
        public ushort VirtualKey;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32ListViewHitTest
    {
        public Win32Point Point;
        public uint Flags;
        public int Item;
        public int SubItem;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32ListViewColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public string? Text;
        public int TextMax;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32ListViewTextPointerColumn
    {
        public uint Mask;
        public int Format;
        public int Width;
        public nint Text;
        public int MaximumTextCharacters;
        public int SubItem;
        public int Image;
        public int Order;
        public int MinWidth;
        public int DefaultWidth;
        public int IdealWidth;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32ListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public string? Text;
        public int TextMax;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnsPointer;
        public nint ColumnFormatsPointer;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32ListViewTextPointerItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int MaximumTextCharacters;
        public int Image;
        public nint Parameter;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public nint ColumnIndices;
        public nint ColumnFormats;
        public int Group;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32HeaderItem
    {
        public uint Mask;
        public int Width;
        public nint Text;
        public nint Bitmap;
        public int TextCharacters;
        public int Format;
        public nint Parameter;
        public int Image;
        public int Order;
        public uint Type;
        public nint Filter;
        public uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32TreeHit
    {
        public int X;
        public int Y;
        public uint Flags;
        public nint Item;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32TreeItem
    {
        public uint Mask;
        public nint Handle;
        public uint State;
        public uint StateMask;
        public nint Text;
        public int TextMax;
        public int Image;
        public int SelectedImage;
        public int Children;
        public nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Win32TreeInsert
    {
        public nint Parent;
        public nint After;
        public Win32TreeItem Item;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32BrowseInfo
    {
        public nint Owner;
        public nint Root;
        public nint DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Title;
        public uint Flags;
        public nint Callback;
        public nint Data;
        public int Image;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Win32NonClientMetrics
    {
        public uint Size;
        public int BorderWidth;
        public int ScrollWidth;
        public int ScrollHeight;
        public int CaptionWidth;
        public int CaptionHeight;
        public GenesisUiLogFont CaptionFont;
        public int SmallCaptionWidth;
        public int SmallCaptionHeight;
        public GenesisUiLogFont SmallCaptionFont;
        public int MenuWidth;
        public int MenuHeight;
        public GenesisUiLogFont MenuFont;
        public GenesisUiLogFont StatusFont;
        public GenesisUiLogFont MessageFont;
        public int PaddedBorderWidth;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint Win32WindowProcedure(
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint Win32HookProcedure(
        int code,
        nuint wParam,
        nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool Win32EnumWindowsProcedure(
        nint window,
        nint lParam);
}
