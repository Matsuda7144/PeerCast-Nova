using PeerCast.Nova.Windows;

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
    internal const int CardTitleFontHeight = 20;
    internal const int DisabledText = 0x0093826C;
    internal static (int Background, int Border, int Text) CardButtonColors(
        bool play, bool recording, bool disabled, bool hot, bool pressed) => disabled
        ? (Panel, Separator, DisabledText)
        : (pressed ? Selected : hot ? Hover : play ? Selected : Input,
            recording ? Recording : play ? Accent : Border, Text);
    internal static (int Background, int Border, int Text) TopButtonColors(
        bool primary, bool disabled, bool hot, bool pressed, bool focused) => disabled
        ? (Panel, Separator, DisabledText)
        : (pressed ? Selected : hot ? Hover : primary ? 0x0055320F : Input,
            hot || focused || pressed ? Accent : Border, Text);
    internal static (int Background, int Border, int Text) RecordingButtonColors(
        bool selected, bool disabled, bool hot, bool pressed, bool focused) => disabled
        ? (Panel, Separator, DisabledText)
        : (pressed || selected ? Selected : hot ? Hover : Input,
            selected || hot || focused || pressed ? Accent : Border, Text);
    internal static int RecordingStatusColor(bool failed, bool active, bool transitioning) =>
        failed ? Warning : active || transitioning ? Recording : SecondaryText;
    internal static (int Background, int Border, int Text) PublishingButtonColors(
        bool start, bool interrupt, bool disabled, bool hot, bool pressed, bool focused)
    {
        var colors = TopButtonColors(start, disabled, hot, pressed, focused);
        return (colors.Background, interrupt && !disabled && !hot && !pressed && !focused ? Warning : colors.Border, colors.Text);
    }
    internal static int PublishingStatusColor(NovaObsPublishingTone tone) => tone switch
    {
        NovaObsPublishingTone.Active => Success,
        NovaObsPublishingTone.Warning => Warning,
        NovaObsPublishingTone.Special => Special,
        _ => SecondaryText,
    };
    internal static int RowBackground(bool selected, bool hot) => selected ? Selected : hot ? Hover : List;
    internal static int NavigationBackground(bool selected, bool hot) => selected ? Selected : hot ? Hover : Sidebar;
}
