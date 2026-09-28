using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingSinkContractTests
{
    private static readonly ChannelId TestChannelId = new(
        Guid.ParseExact(
            "531DC8DFC7FB42928AC2C0A626517A87",
            "N"));

    [Fact]
    public void RecordingSessionId_EmptyIdentifier_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new RecordingSessionId(Guid.Empty));
    }

    [Fact]
    public void RecordingSessionId_ToString_UsesStableUppercaseNFormat()
    {
        var id = new RecordingSessionId(
            Guid.ParseExact(
                "11111111aaaaaaaa22222222bbbbbbbb",
                "N"));

        Assert.Equal(
            "11111111AAAAAAAA22222222BBBBBBBB",
            id.ToString());
    }

    [Fact]
    public void StartRequest_EmptyRuleIdentifier_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new RecordingSinkStartRequest(
                CreateCandidate(),
                Guid.Empty));
    }

    [Fact]
    public void ManualStartRequest_HasExplicitOriginAndNoRuleIdentifier()
    {
        var request = RecordingSinkStartRequest.Manual(CreateCandidate());

        Assert.Equal(RecordingSessionOrigin.Manual, request.Origin);
        Assert.Null(request.RuleId);
        Assert.Equal(Guid.Empty, request.SelectedRuleId);
    }

    [Fact]
    public void SinkSession_EmptyRuleIdentifier_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new RecordingSinkSession(
                new RecordingSessionId(Guid.NewGuid()),
                TestChannelId,
                Guid.Empty));
    }

    [Fact]
    public void ManualSinkSession_HasExplicitOriginAndNoAutomaticRule()
    {
        var session = RecordingSinkSession.Manual(
            new RecordingSessionId(Guid.NewGuid()),
            TestChannelId);

        Assert.Equal(RecordingSessionOrigin.Manual, session.Origin);
        Assert.Null(session.AutomaticRuleId);
        Assert.Equal(Guid.Empty, session.StartedByRuleId);
    }

    [Fact]
    public void StartResult_StartedAndAlreadyRunning_RequireSession()
    {
        Assert.Throws<ArgumentNullException>(
            () => RecordingSinkStartResult.Started(null!));
        Assert.Throws<ArgumentNullException>(
            () => RecordingSinkStartResult.AlreadyRunning(null!));
    }

    [Fact]
    public void StartResult_ExpectedOutcomesExposeStableSessionContract()
    {
        var session = CreateSession();

        var started = RecordingSinkStartResult.Started(session);
        var existing =
            RecordingSinkStartResult.AlreadyRunning(session);
        var rejected = RecordingSinkStartResult.Rejected;

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            started.Kind);
        Assert.Same(session, started.Session);

        Assert.Equal(
            RecordingSinkStartResultKind.AlreadyRunning,
            existing.Kind);
        Assert.Same(session, existing.Session);

        Assert.Equal(
            RecordingSinkStartResultKind.Rejected,
            rejected.Kind);
        Assert.Null(rejected.Session);
    }

    [Fact]
    public void StopResult_ExpectedOutcomesAreExplicit()
    {
        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            RecordingSinkStopResult.Stopped.Kind);
        Assert.Equal(
            RecordingSinkStopResultKind.AlreadyStopped,
            RecordingSinkStopResult.AlreadyStopped.Kind);
        Assert.Equal(
            RecordingSinkStopResultKind.Rejected,
            RecordingSinkStopResult.Rejected.Kind);
    }

    [Fact]
    public async Task RecordingSink_InterfaceCarriesTypedRequestsAndResults()
    {
        var session = CreateSession();
        var sink = new TestRecordingSink(session);
        var ruleId = Guid.NewGuid();

        var start = await sink.StartAsync(
            new RecordingSinkStartRequest(
                CreateCandidate(),
                ruleId),
            CancellationToken.None);

        var stop = await sink.StopAsync(
            new RecordingSinkStopRequest(
                Assert.IsType<RecordingSinkSession>(
                    start.Session)),
            CancellationToken.None);

        Assert.Equal(
            RecordingSinkStartResultKind.Started,
            start.Kind);
        Assert.Equal(
            RecordingSinkStopResultKind.Stopped,
            stop.Kind);
        Assert.Equal(1, sink.StartCallCount);
        Assert.Equal(1, sink.StopCallCount);
    }

    private static RecordingCandidate CreateCandidate()
    {
        return new RecordingCandidate(
            TestChannelId,
            new ChannelMetadataSnapshot(
                name: "Channel",
                genre: "Games",
                bitrateKbps: 2000));
    }

    private static RecordingSinkSession CreateSession()
    {
        return new RecordingSinkSession(
            new RecordingSessionId(Guid.NewGuid()),
            TestChannelId,
            Guid.NewGuid());
    }

    private sealed class TestRecordingSink(
        RecordingSinkSession session) : IRecordingSink
    {
        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public ValueTask<RecordingSinkStartResult> StartAsync(
            RecordingSinkStartRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            StartCallCount++;

            return ValueTask.FromResult(
                RecordingSinkStartResult.Started(session));
        }

        public ValueTask<RecordingSinkStopResult> StopAsync(
            RecordingSinkStopRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            StopCallCount++;

            return ValueTask.FromResult(
                RecordingSinkStopResult.Stopped);
        }
    }
}
