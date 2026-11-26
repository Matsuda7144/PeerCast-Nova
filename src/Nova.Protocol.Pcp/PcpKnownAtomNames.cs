namespace PeerCast.Nova.Protocol.Pcp;

public static class PcpKnownAtomNames
{
    public static readonly PcpAtomName Helo = PcpAtomName.FromAscii("helo");
    public static readonly PcpAtomName Oleh = PcpAtomName.FromAscii("oleh");
    public static readonly PcpAtomName Okay = PcpAtomName.FromAscii("ok");
    public static readonly PcpAtomName Quit = PcpAtomName.FromAscii("quit");
    public static readonly PcpAtomName Host = PcpAtomName.FromAscii("host");
    public static readonly PcpAtomName Broadcast = PcpAtomName.FromAscii("bcst");
    public static readonly PcpAtomName BroadcastChannelId = PcpAtomName.FromAscii("cid");
    public static readonly PcpAtomName Channel = PcpAtomName.FromAscii("chan");

    public static readonly PcpAtomName ChannelId = PcpAtomName.FromAscii("id");
    public static readonly PcpAtomName ChannelPacket = PcpAtomName.FromAscii("pkt");
    public static readonly PcpAtomName ChannelInfo = PcpAtomName.FromAscii("info");
    public static readonly PcpAtomName ChannelTrack = PcpAtomName.FromAscii("trck");

    public static readonly PcpAtomName Type = PcpAtomName.FromAscii("type");
    public static readonly PcpAtomName Position = PcpAtomName.FromAscii("pos");
    public static readonly PcpAtomName Data = PcpAtomName.FromAscii("data");
    public static readonly PcpAtomName ChannelPacketContinuation =
        PcpAtomName.FromAscii("cont");
    public static readonly PcpAtomName StreamType = PcpAtomName.FromAscii("styp");
    public static readonly PcpAtomName Name = PcpAtomName.FromAscii("name");
    public static readonly PcpAtomName Genre = PcpAtomName.FromAscii("gnre");
    public static readonly PcpAtomName Description = PcpAtomName.FromAscii("desc");
    public static readonly PcpAtomName Comment = PcpAtomName.FromAscii("cmnt");
    public static readonly PcpAtomName Url = PcpAtomName.FromAscii("url");
    public static readonly PcpAtomName StreamExtension = PcpAtomName.FromAscii("sext");
    public static readonly PcpAtomName Bitrate = PcpAtomName.FromAscii("bitr");
    public static readonly PcpAtomName Title = PcpAtomName.FromAscii("titl");
    public static readonly PcpAtomName Album = PcpAtomName.FromAscii("albm");
    public static readonly PcpAtomName Creator = PcpAtomName.FromAscii("crea");

    public static readonly PcpAtomName HeloAgent = PcpAtomName.FromAscii("agnt");
    public static readonly PcpAtomName HeloSessionId = PcpAtomName.FromAscii("sid");
    public static readonly PcpAtomName HeloPort = PcpAtomName.FromAscii("port");
    public static readonly PcpAtomName HeloPing = PcpAtomName.FromAscii("ping");
    public static readonly PcpAtomName HeloRemoteIp = PcpAtomName.FromAscii("rip");
    public static readonly PcpAtomName HeloVersion = PcpAtomName.FromAscii("ver");

    public static readonly PcpAtomName Root = PcpAtomName.FromAscii("root");
    public static readonly PcpAtomName RootUpdateInterval = PcpAtomName.FromAscii("uint");
    public static readonly PcpAtomName RootCheckVersion = PcpAtomName.FromAscii("chkv");
    public static readonly PcpAtomName RootUpdate = PcpAtomName.FromAscii("upd");
    public static readonly PcpAtomName RootNext = PcpAtomName.FromAscii("next");

    public static readonly PcpAtomName Push = PcpAtomName.FromAscii("push");
    public static readonly PcpAtomName Cout = PcpAtomName.FromAscii("cout");
    public static readonly PcpAtomName PushIp = PcpAtomName.FromAscii("ip");
    public static readonly PcpAtomName PushPort = PcpAtomName.FromAscii("port");
    public static readonly PcpAtomName PushChannel = PcpAtomName.FromAscii("chan");
    public static readonly PcpAtomName PushRelay = PcpAtomName.FromAscii("rly");
}
