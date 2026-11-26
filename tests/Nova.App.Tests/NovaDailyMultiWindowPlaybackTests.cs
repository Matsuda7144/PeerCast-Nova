using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaDailyMultiWindowPlaybackTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly Guid Channel1Guid = Guid.ParseExact("531DC8DFC7FB42928AC2C0A626517A87", "N");
    private static readonly Guid Channel2Guid = Guid.ParseExact("642ED9E0D80C53A39BD3D1B737628B98", "N");
    private static readonly byte[] FlvHeader = [(byte)'F', (byte)'L', (byte)'V', 1, 5, 0, 0, 0, 9, 0, 0, 0, 0];

    [Fact]
    public async Task PlayAdditionalAsync_AllowsConcurrentPlaybackWindowsAndClosesIndividually()
    {
        using var root = new TempRoot();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var release1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var upstream1 = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        await using var upstream2 = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        upstream1.Start();
        upstream2.Start();

        var server1 = RunFlvUpstreamAsync(upstream1, Channel1Guid, release1.Task, timeout.Token);
        var server2 = RunFlvUpstreamAsync(upstream2, Channel2Guid, release2.Task, timeout.Token);

        var composition = new PcpRecordingRuntimeComposition(root.Path);
        var player1 = new ControlledLauncher();
        var player2 = new ControlledLauncher();
        var index = 0;

        await using var controller = new NovaDailyHttpFlvPlaybackController(
            () => Interlocked.Increment(ref index) == 1 ? player1 : player2,
            static _ => { },
            static _ => { },
            composition,
            new GenesisSharedPcpPlaybackRuntime(composition));

        try
        {
            var ch1 = new GenesisChannelItem("ch1", $"pcp://127.0.0.1:{upstream1.LocalEndPoint.Port}/channel/{Channel1Guid:N}");
            var ch2 = new GenesisChannelItem("ch2", $"pcp://127.0.0.1:{upstream2.LocalEndPoint.Port}/channel/{Channel2Guid:N}");

            await controller.PlayAsync(ch1, timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(1, controller.ActivePlaybackCount);
            Assert.Equal(0, player1.DisposeCount);

            await controller.PlayAdditionalAsync(ch2, timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(2, controller.ActivePlaybackCount);
            Assert.Equal(0, player1.DisposeCount);
            Assert.Equal(0, player2.DisposeCount);

            player2.Complete();
            while (controller.ActivePlaybackCount > 1)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(15, timeout.Token);
            }

            Assert.Equal(1, controller.ActivePlaybackCount);
            Assert.Equal(0, player1.DisposeCount);
            Assert.Equal(1, player2.DisposeCount);

            await controller.StopAsync().WaitAsync(timeout.Token);
            Assert.Equal(0, controller.ActivePlaybackCount);
            Assert.Equal(1, player1.DisposeCount);
        }
        finally
        {
            release1.TrySetResult();
            release2.TrySetResult();
            await Task.WhenAll(server1, server2).WaitAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task GuiNextController_KeepExistingSessions_UsesPlayAdditionalAndPreservesPlayingSnapshot()
    {
        var daily = new DualModeDailyController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var source1 = $"pcp://tracker.example:7144/channel/{Channel1Guid:N}";
        var source2 = $"pcp://tracker.example:7144/channel/{Channel2Guid:N}";

        var first = await controller.PlayAsync(new PlaybackSessionRequest(source1, "Channel 1"));
        Assert.Equal(PlaybackSessionCommandResult.Completed, first.Result);
        Assert.Equal(PlaybackSessionState.Playing, controller.Snapshot.State);
        Assert.Equal(1, daily.PlayCount);

        var second = await controller.PlayAsync(
            new PlaybackSessionRequest(source2, "Channel 2") { KeepExistingSessions = true });
        Assert.Equal(PlaybackSessionCommandResult.Completed, second.Result);
        Assert.Equal(1, daily.PlayCount);
        Assert.Equal(1, daily.PlayAdditionalCount);
        Assert.Equal(source2, daily.AdditionalPlayedChannel?.Source);
        Assert.Equal(PlaybackSessionState.Playing, controller.Snapshot.State);
        Assert.Equal(source1, controller.Snapshot.Source);
    }

    [Fact]
    public async Task PlaySourceInNewWindowAndDoubleClick_BothLaunchAdditionalWindowsWhilePlaying()
    {
        var daily = new DualModeDailyController();
        await using var sessionController = new GuiNextDailyPlaybackSessionController(daily);
        var model = new GenesisMainViewModel(sessionController);
        var ch1 = new GenesisChannelItem("Channel 1", $"pcp://tracker.example:7144/channel/{Channel1Guid:N}");
        var ch2 = new GenesisChannelItem("Channel 2", $"pcp://tracker.example:7144/channel/{Channel2Guid:N}");
        var ch3 = new GenesisChannelItem("Channel 3", $"pcp://tracker.example:7144/channel/{Guid.ParseExact("753FE0F1E91D64B4ACE4E2C848739C09", "N"):N}");
        model.ReplaceChannels([ch1, ch2, ch3]);
        var interaction = new GenesisMainWindowInteraction(model);
        GenesisGuiNextNativeTabRow[] rows = [new(ch1, ch1), new(ch2, ch2), new(ch3, ch3)];

        Assert.True(await interaction.ActivateChannelAsync(ch1));
        Assert.Equal(PlaybackSessionState.Playing, model.PlaybackState);
        Assert.False(model.CanPlay);
        Assert.True(model.CanPlayInNewWindow);
        Assert.Equal(1, daily.PlayCount);
        Assert.Equal(0, daily.PlayAdditionalCount);

        await interaction.PlaySourceInNewWindowAsync(ch2.Source);
        Assert.Equal(1, daily.PlayCount);
        Assert.Equal(1, daily.PlayAdditionalCount);
        Assert.Equal(ch2.Source, daily.AdditionalPlayedChannel?.Source);
        Assert.Equal(PlaybackSessionState.Playing, model.PlaybackState);

        Assert.True(await GenesisChannelListActivation.ActivateAsync(-3, 2, rows, interaction, available: true));
        Assert.Equal(1, daily.PlayCount);
        Assert.Equal(2, daily.PlayAdditionalCount);
        Assert.Equal(ch3.Source, daily.AdditionalPlayedChannel?.Source);
    }

    [Fact]
    public async Task ClosingFirstWindowWhileAdditionalRemains_RaisesPlaybackEndedOnlyAfterLastCloses()
    {
        using var root = new TempRoot();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var release1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var upstream1 = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        await using var upstream2 = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        upstream1.Start();
        upstream2.Start();

        var server1 = RunFlvUpstreamAsync(upstream1, Channel1Guid, release1.Task, timeout.Token);
        var server2 = RunFlvUpstreamAsync(upstream2, Channel2Guid, release2.Task, timeout.Token);

        var composition = new PcpRecordingRuntimeComposition(root.Path);
        var player1 = new ControlledLauncher();
        var player2 = new ControlledLauncher();
        var index = 0;

        await using var controller = new NovaDailyHttpFlvPlaybackController(
            () => Interlocked.Increment(ref index) == 1 ? player1 : player2,
            static _ => { },
            static _ => { },
            composition,
            new GenesisSharedPcpPlaybackRuntime(composition));
        var endedCount = 0;
        controller.PlaybackEnded += _ => Interlocked.Increment(ref endedCount);

        try
        {
            var ch1 = new GenesisChannelItem("ch1", $"pcp://127.0.0.1:{upstream1.LocalEndPoint.Port}/channel/{Channel1Guid:N}");
            var ch2 = new GenesisChannelItem("ch2", $"pcp://127.0.0.1:{upstream2.LocalEndPoint.Port}/channel/{Channel2Guid:N}");

            await controller.PlayAsync(ch1, timeout.Token).WaitAsync(timeout.Token);
            await controller.PlayAdditionalAsync(ch2, timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(2, controller.ActivePlaybackCount);

            player1.Complete();
            while (controller.ActivePlaybackCount > 1)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(15, timeout.Token);
            }

            Assert.Equal(1, controller.ActivePlaybackCount);
            Assert.Equal(1, player1.DisposeCount);
            Assert.Equal(0, player2.DisposeCount);
            Assert.Equal(0, Volatile.Read(ref endedCount));

            player2.Complete();
            while (controller.ActivePlaybackCount > 0 || Volatile.Read(ref endedCount) == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(15, timeout.Token);
            }

            Assert.Equal(0, controller.ActivePlaybackCount);
            Assert.Equal(1, player2.DisposeCount);
            Assert.Equal(1, Volatile.Read(ref endedCount));
        }
        finally
        {
            release1.TrySetResult();
            release2.TrySetResult();
            await Task.WhenAll(server1, server2).WaitAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task PlayAdditionalAsync_FromStoppedState_RaisesPlaybackEndedWhenClosed()
    {
        using var root = new TempRoot();
        using var timeout = new CancellationTokenSource(TestTimeout);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var upstream = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        upstream.Start();
        var server = RunFlvUpstreamAsync(upstream, Channel1Guid, release.Task, timeout.Token);

        var composition = new PcpRecordingRuntimeComposition(root.Path);
        var player = new ControlledLauncher();
        await using var controller = new NovaDailyHttpFlvPlaybackController(
            () => player,
            static _ => { },
            static _ => { },
            composition,
            new GenesisSharedPcpPlaybackRuntime(composition));
        var endedCount = 0;
        controller.PlaybackEnded += _ => Interlocked.Increment(ref endedCount);

        try
        {
            var ch = new GenesisChannelItem("ch1", $"pcp://127.0.0.1:{upstream.LocalEndPoint.Port}/channel/{Channel1Guid:N}");
            await controller.PlayAdditionalAsync(ch, timeout.Token).WaitAsync(timeout.Token);
            Assert.Equal(1, controller.ActivePlaybackCount);

            player.Complete();
            while (controller.ActivePlaybackCount > 0 || Volatile.Read(ref endedCount) == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(15, timeout.Token);
            }

            Assert.Equal(0, controller.ActivePlaybackCount);
            Assert.Equal(1, Volatile.Read(ref endedCount));
        }
        finally
        {
            release.TrySetResult();
            await server.WaitAsync(timeout.Token);
        }
    }

    [Fact]
    public void ContextMenuCommand_IncludesPlayInNewWindowWithoutChangingExistingIds()
    {
        Assert.Equal(5, (int)GenesisChannelContextMenuCommand.ManualSourceUrl);
        Assert.Equal(11, (int)GenesisChannelContextMenuCommand.PlayInNewWindow);
    }

    private static async Task RunFlvUpstreamAsync(
        TcpTransportListener listener,
        Guid channelGuid,
        Task release,
        CancellationToken cancellationToken)
    {
        await using var connection = await listener.AcceptAsync(cancellationToken);
        var reader = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            _ = await PcpHttpHeaderBlockReader.ReadRequestAsync(reader, 32 * 1024, cancellationToken);
            await connection.WriteAsync(
                Encoding.ASCII.GetBytes(
                    "HTTP/1.0 200 OK\r\nServer: PeerCastStation/Test\r\nContent-Type:application/x-peercast-pcp\r\n\r\n"),
                cancellationToken);
            await using var atoms = new PcpAtomPipeMessageReader(reader);
            _ = PcpHeloCodec.ParseHelo(await atoms.ReadNextAsync(cancellationToken));

            var reply = new ArrayBufferWriter<byte>();
            PcpAtomTreeCodec.Write(reply, PcpHeloCodec.CreateOleh("PeerCastStation/Test", Guid.NewGuid()));
            PcpAtomTreeCodec.Write(reply, PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)));
            PcpAtomTreeCodec.Write(
                reply,
                PcpAtom.Parent(
                    PcpKnownAtomNames.Channel,
                    PcpAtom.Leaf(PcpKnownAtomNames.ChannelId, PcpWireValueCodec.EncodeGuid(channelGuid)),
                    PcpAtom.Parent(
                        PcpKnownAtomNames.ChannelPacket,
                        PcpAtom.Leaf(PcpKnownAtomNames.Type, PcpWireValueCodec.EncodeUInt32(0x64616568)),
                        PcpAtom.Leaf(PcpKnownAtomNames.Position, PcpWireValueCodec.EncodeUInt32(0)),
                        PcpAtom.Leaf(PcpKnownAtomNames.Data, FlvHeader))));
            await connection.WriteAsync(reply.WrittenMemory, cancellationToken);
            await release.WaitAsync(cancellationToken);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    private sealed class ControlledLauncher : IDailyPlaybackPlayerLauncher, IDailyPlaybackPlayerLifetime
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public Task Completion => _completion.Task;
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void ValidateConfiguration() { }

        public ValueTask<IDailyPlaybackPlayerLifetime> LaunchAsync(
            DailyPlaybackPlayerSource source,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IDailyPlaybackPlayerLifetime>(this);

        public void Complete() => _completion.TrySetResult();

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            _completion.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DualModeDailyController : INovaDailyPlaybackController
    {
        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded;
        public int PlayCount { get; private set; }
        public int PlayAdditionalCount { get; private set; }
        public GenesisChannelItem? AdditionalPlayedChannel { get; private set; }

        public Task PlayAsync(GenesisChannelItem channel, CancellationToken cancellationToken)
        {
            PlayCount++;
            return Task.CompletedTask;
        }

        public Task PlayAdditionalAsync(GenesisChannelItem channel, CancellationToken cancellationToken)
        {
            PlayAdditionalCount++;
            AdditionalPlayedChannel = channel;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            PlaybackEnded?.Invoke(new(string.Empty, false));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nova-multiwindow-tests-" + Guid.NewGuid().ToString("N"));

        public TempRoot() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
