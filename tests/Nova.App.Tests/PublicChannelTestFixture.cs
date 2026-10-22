using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.App.Tests;

/// <summary>Only fake stores, in-memory media and managed loopback port-0 peers.</summary>
internal sealed class PublicChannelTestFixture : IAsyncDisposable
{
    public static readonly TcpTransportOptions Transport = new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(2),
        MaxConcurrentConnections = 32,
    };
    public MemoryConnectionStore Store { get; }
    public GenesisGuiNextApplicationState State { get; }
    public GenesisGuiNextPublicChannelComposition Channels => State.PublicChannels;

    private PublicChannelTestFixture(MemoryConnectionStore store, GenesisGuiNextApplicationState state)
    {
        Store = store;
        State = state;
    }

    public static async Task<PublicChannelTestFixture> CreateAsync(GenesisGuiNextConnectionPreferences? preferences = null)
    {
        var store = new MemoryConnectionStore(preferences ?? GenesisGuiNextConnectionPreferences.Default);
        return new(store, await LoadAsync(store));
    }

    public static ValueTask<GenesisGuiNextApplicationState> LoadAsync(MemoryConnectionStore store) =>
        GenesisGuiNextApplicationState.LoadAsync(new FavoriteStore(), new NotificationStore(),
            DefaultGenesisYellowPageConfigurationStore.Instance, DefaultGenesisGuiNextPlaybackPreferencesStore.Instance,
            store, new SilentSound());

    public ValueTask DisposeAsync() => State.DisposeAsync();

    public async Task<Channel> BroadcastAsync(CancellationToken token, ChannelId? id = null,
        PcpDownstreamChildOptions? childOptions = null)
    {
        var fanout = new PcpRelayFanout();
        try
        {
            var owner = await Channels.PrepareBroadcastAsync(id ?? new(Guid.NewGuid()), new(Guid.NewGuid()),
                fanout, new(IPAddress.Loopback, 0), Transport, childOptions, token);
            var run = owner.RunAsync(token);
            return new(owner, run, fanout, null);
        }
        catch { await fanout.DisposeAsync(); throw; }
    }

    public async Task<Channel> ViewingAsync(CancellationToken token, ChannelId? id = null)
    {
        // The real downstream server provides a held synthetic upstream PCP
        // handshake, so exclusion tests cover an actual upstream session.
        var channelId = id ?? new(Guid.NewGuid());
        var fanout = new PcpRelayFanout();
        var upstream = new PcpDownstreamRelayServer(new(IPAddress.Loopback, 0), fanout,
            channelId, Guid.NewGuid(), Transport);
        var upstreamRun = upstream.RunAsync(token);
        try
        {
            var source = new GenesisChannelItem("Synthetic upstream",
                $"pcp://127.0.0.1:{upstream.LocalEndPoint.Port}/channel/{channelId.Value:N}");
            var owner = await Channels.PrepareViewingAsync(source, new(IPAddress.Loopback, 0),
                new PcpRelayCoordinatorOptions
                {
                    DownstreamTransport = Transport,
                    UpstreamSession = new()
                    {
                        MaxReconnectAttempts = 0,
                        Stream = new() { ReadIdleTimeout = TimeSpan.FromSeconds(20) },
                    },
                }, token);
            var run = owner.RunAsync(token);
            Assert.Equal(PcpRelayCoordinatorReadinessOutcome.Ready,
                await owner.ViewingRuntime!.WaitForReadinessAsync(token));
            await UntilAsync(() => fanout.ActiveChildCount == 1, token);
            await fanout.ConsumeAsync(new PcpChannelPacket(channelId, new(Guid.NewGuid()),
                0, 1, 100, 100, new byte[] { 1 }, discontinuity: null), token);
            await owner.ViewingRuntime.FirstPacketAccepted.WaitAsync(token);
            return new(owner, run, fanout, upstream, upstreamRun);
        }
        catch
        {
            await upstream.DisposeAsync();
            await upstreamRun;
            await fanout.DisposeAsync();
            throw;
        }
    }

    public static async Task UntilAsync(Func<bool> predicate, CancellationToken token)
    {
        while (!predicate()) await Task.Delay(10, token);
    }

    internal sealed class Channel(GenesisGuiNextPublicChannelComposition.Owner owner, Task run,
        PcpRelayFanout producerFanout, PcpDownstreamRelayServer? upstream, Task? upstreamRun = null) : IAsyncDisposable
    {
        public GenesisGuiNextPublicChannelComposition.Owner Owner { get; } = owner;
        public Task Run { get; } = run;
        public PcpRelayFanout ProducerFanout { get; } = producerFanout;
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Owner.DisposeAsync();
                await Run;
            }
            finally
            {
                if (upstream is not null) await upstream.DisposeAsync();
                if (upstreamRun is not null) await upstreamRun;
                await ProducerFanout.DisposeAsync();
            }
        }
    }

    internal sealed class Peer(TcpTransportConnection connection, PipeReader reader,
        PcpRelayHandshakeResult handshake) : IAsyncDisposable
    {
        private int _disposed;
        public PcpRelayHandshakeResult Handshake { get; } = handshake;
        public ValueTask QuitAsync(CancellationToken token)
        {
            var writer = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(writer,
                PcpAtom.Leaf(PcpKnownAtomNames.Quit, PcpWireValueCodec.EncodeUInt32(0)));
            return connection.WriteAsync(writer.WrittenMemory, token);
        }
        public static async Task<Peer> ConnectAsync(GenesisGuiNextPublicChannelComposition.Owner owner,
            CancellationToken token)
        {
            var endpoint = owner.LocalEndPoint;
            var connection = await new TcpTransportConnector(Transport).ConnectAsync("127.0.0.1", endpoint.Port, token);
            var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
            try
            {
                var result = await PcpRelayHandshakeClient.NegotiateAsync(connection, reader, "127.0.0.1", endpoint.Port,
                    owner.ChannelId.Value, Guid.NewGuid(), 0, 1,
                    new PcpRelayHandshakeSettings(32768, 65536, 16, "PeerCastNova/Test", PcpHeloCodec.LegacyServantVersion), token);
                return new Peer(connection, reader, result);
            }
            catch { await reader.CompleteAsync(); await connection.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { await reader.CompleteAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }

    internal sealed class MemoryConnectionStore(GenesisGuiNextConnectionPreferences preferences) : IGenesisGuiNextConnectionPreferencesStore
    {
        public GenesisGuiNextConnectionPreferences Value { get; private set; } = preferences;
        public int LoadCount { get; private set; }
        public ValueTask<GenesisGuiNextConnectionPreferences> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return ValueTask.FromResult(Value);
        }
        public ValueTask SaveAsync(GenesisGuiNextConnectionPreferences preferences, CancellationToken cancellationToken = default)
        {
            Value = preferences;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FavoriteStore : IGenesisFavoriteChannelStore
    {
        public ValueTask<IReadOnlyList<GenesisFavoriteChannel>> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<GenesisFavoriteChannel>>([]);
        public ValueTask SaveAsync(IReadOnlyList<GenesisFavoriteChannel> favorites, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class NotificationStore : IGenesisChannelNotificationPreferenceStore
    {
        public ValueTask<GenesisChannelNotificationPreferences> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GenesisChannelNotificationPreferences.Default);
        public ValueTask SaveAsync(GenesisChannelNotificationPreferences preferences, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
    private sealed class SilentSound : IGenesisSystemNotificationSoundPlayer
    {
        public ValueTask PlayAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
