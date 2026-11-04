namespace PeerCast.Nova.App;

// COLORREF (BGR), shared by the Phase 1 Win32 surfaces.
internal static class GenesisGuiNextDesign
{
    internal const int Background = 0x00201708;
    internal const int Sidebar = 0x00301C09;
    internal const int Panel = 0x0033230D;
    internal const int Input = 0x00422B10;
    internal const int List = 0x002E1F0B;
    internal const int Header = 0x003C2910;
    internal const int Border = 0x006D4920;
    internal const int Separator = 0x00463016;
    internal const int Selected = 0x00C65B08;
    internal const int Hover = 0x005B3512;
    internal const int Accent = 0x00FFBF00;
    internal const int Text = 0x00FFF7EC;
    internal const int SecondaryText = 0x00CBB69A;
    internal const int Success = 0x0075D942;
    internal const int Recording = 0x006660EE;
    internal const int Warning = 0x004ABCF5;
    internal const int Special = 0x00DA8AB6;
    internal const int BodyFontHeight = 14;
    internal const int RowImageHeight = 26;
    internal static int RowBackground(bool selected, bool hot) => selected ? Selected : hot ? Hover : List;
    internal static int NavigationBackground(bool selected, bool hot) => selected ? Selected : hot ? Hover : Sidebar;
}
