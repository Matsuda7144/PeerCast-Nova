using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;
using static PeerCast.Nova.Network.Tests.PcpDownstreamPacerTests;

namespace PeerCast.Nova.Network.Tests;

public sealed partial class PcpDownstreamChildSessionTests
{
    [Fact]
    public async Task PacingWaitLongerThanWriteTimeoutPreservesPacketAndMakesProgress()
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(8, clock);
        var writer = new PacedWriter();
        var result = await PacingScenario(pacing, writer, async (fanout, _, _, token) =>
        {
            await fanout.ConsumeAsync(CreatePacket(new byte[600]), token);
            for (var i = 0; i < 3; i++)
            {
                await Until(() => pacing.WaitingRequests == 1 && clock.ActiveTimerCount >= 2);
                var before = writer.Bytes;
                clock.Advance(TimeSpan.FromSeconds(1)); // 10x the network write timeout, exclusively in pacing.
                await Until(() => writer.Bytes > before);
            }
            await Until(() => fanout.Evidence.DeliveredPacketCount == 1);
        });
        Assert.Equal(PcpDownstreamChildEndReason.QuitReceived, result.EndReason);
        Assert.Equal(600 + PcpChannelPacketWireWriter.PrefixLength, writer.Bytes);
        Assert.Equal(1, result.DeliveredPacketCount);
        Assert.Equal(0, pacing.WaitingRequests);
    }

    [Fact]
    public async Task ActualWriteStallStillTimesOutWithPacingEnabled()
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(1000, clock);
        var writer = new PacedWriter { Stall = true };
        var result = await PacingScenario(pacing, writer, async (fanout, _, _, token) =>
        {
            await fanout.ConsumeAsync(CreatePacket([1]), token);
            await writer.Entered.Task.WaitAsync(token);
            clock.Advance(TimeSpan.FromMilliseconds(100));
        }, quit: false);
        Assert.Equal(PcpDownstreamChildEndReason.WriteTimedOut, result.EndReason);
        Assert.Equal(0, result.DeliveredPacketCount);
        Assert.Equal(0, pacing.WaitingRequests);
    }

    [Theory]
    [InlineData(0, PcpDownstreamChildEndReason.Stopped)]
    [InlineData(1, PcpDownstreamChildEndReason.PacingLimited)]
    [InlineData(2, PcpDownstreamChildEndReason.PacingLimited)]
    [InlineData(3, PcpDownstreamChildEndReason.QuitReceived)]
    [InlineData(4, PcpDownstreamChildEndReason.ConnectionEnded)]
    [InlineData(5, PcpDownstreamChildEndReason.Stopped)]
    public async Task PacingCancellationBacklogAndPacketDeadlineRemainFinite(int action, PcpDownstreamChildEndReason expected)
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(8, clock);
        await pacing.AcquireMediaAsync(pacing.BurstBytes, default);
        var writer = new PacedWriter();
        var result = await PacingScenario(pacing, writer, async (fanout, stop, client, token) =>
        {
            await fanout.ConsumeAsync(CreatePacket([1]), token);
            await Until(() => pacing.WaitingRequests == 1 && clock.ActiveTimerCount >= 2);
            if (action == 0) stop.Cancel();
            else if (action == 1) await fanout.ConsumeAsync(CreatePacket([2]), token);
            else if (action == 2) clock.Advance(PcpDownstreamPacer.PacketDeadline);
            else if (action == 3) await client.WriteAsync(EncodeTerminal(PcpKnownAtomNames.Quit), token);
            else if (action == 4) await client.DisposeAsync();
            else await fanout.SuspendForRecoveryAsync(TimeSpan.FromSeconds(5), TimeProvider.System, token);
        }, quit: false, onePacketQueue: true);
        Assert.Equal(expected, result.EndReason);
        Assert.True(result.CleanupCompleted);
        Assert.Equal(0, writer.Bytes);
        Assert.Equal(0, pacing.WaitingRequests);
    }

    [Fact]
    public async Task FailedWriteDoesNotRecycleItsIssuedBudget()
    {
        var clock = new ManualNatTimeProvider();
        var pacing = new PcpDownstreamPacer(1000, clock);
        var result = await PacingScenario(pacing, new PacedWriter { Fail = true },
            async (fanout, _, _, token) => await fanout.ConsumeAsync(CreatePacket([1]), token), quit: false);
        Assert.Equal(PcpDownstreamChildEndReason.WriteFailed, result.EndReason);
        Assert.Equal(0, result.DeliveredPacketCount);
        using var stop = new CancellationTokenSource();
        var next = pacing.AcquireMediaAsync(pacing.BurstBytes, stop.Token).AsTask();
        Assert.False(next.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        Assert.Equal(0, pacing.WaitingRequests);
    }

    private static async Task<PcpDownstreamChildResult> PacingScenario(PcpDownstreamPacer pacing,
        PacedWriter writer,
        Func<PcpRelayFanout, CancellationTokenSource, TcpTransportConnection, CancellationToken, Task> action,
        bool quit = true, bool onePacketQueue = false)
    {
        var transport = CreateTransportOptions();
        await using var listener = CreateListener(transport);
        await using var fanout = new PcpRelayFanout(new() { MaxPendingPacketsPerChild = onePacketQueue ? 1 : 8 });
        listener.Start();
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var run = AcceptAndRunAsync(listener, fanout, new() { WriteTimeout = TimeSpan.FromMilliseconds(100) },
            stop.Token, writer, pacing: pacing);
        await using var client = await new TcpTransportConnector(transport).ConnectAsync(listener.LocalEndPoint, timeout.Token);
        var reader = PipeReader.Create(client.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            Assert.Equal(PcpCompatibilityOutcome.Accepted,
                (await NegotiateAsync(client, reader, listener.LocalEndPoint, ChannelId.Value, timeout.Token)).Outcome);
            await action(fanout, stop, client, timeout.Token);
            if (quit) await client.WriteAsync(EncodeTerminal(PcpKnownAtomNames.Quit), timeout.Token);
            return await run.WaitAsync(timeout.Token);
        }
        finally { stop.Cancel(); await reader.CompleteAsync(); }
    }

    private sealed class PacedWriter : IPcpDownstreamPacketWriter
    {
        private int _bytes;
        internal int Bytes => Volatile.Read(ref _bytes);
        internal bool Stall;
        internal bool Fail;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (Fail) throw new IOException("Controlled unknown partial write");
            if (Stall) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            Interlocked.Add(ref _bytes, buffer.Length);
        }
    }

    private sealed class PacingAdmission(PcpDownstreamPacer pacing) : IPcpDownstreamRelayAdmission, IPcpDownstreamRelayPacing
    {
        public ChannelId ChannelId => PcpDownstreamChildSessionTests.ChannelId;
        public PcpDownstreamPacer Pacing => pacing;
        public bool TryReserve(long connectionId, [NotNullWhen(true)] out IDisposable? reservation, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); reservation = new Lease(); return true; }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
