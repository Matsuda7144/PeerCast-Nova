namespace PeerCast.Nova.App;

internal static class GenesisChannelContextMenuDiagnostics
{
    [System.Diagnostics.Conditional("PEERCAST_NOVA_CONTEXT_MENU_TRACE")]
    public static void Write(string stage, string detail)
    {
        _ = stage;
        _ = detail;
    }
}
