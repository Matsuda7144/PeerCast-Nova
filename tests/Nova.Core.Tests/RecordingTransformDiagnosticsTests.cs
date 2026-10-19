using System.Collections.Concurrent;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Core.Tests;

public sealed class RecordingTransformDiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Nova-transform-diagnostics-tests", Guid.NewGuid().ToString("N"));

    public static TheoryData<RecordingTransformThrowSite> Sites => new(
        Enum.GetValues<RecordingTransformThrowSite>().Where(site => site != RecordingTransformThrowSite.Unknown));

    [Theory]
    [MemberData(nameof(Sites))]
    public void TypedSitesPreserveExceptionAndNeverInspectMessage(RecordingTransformThrowSite site)
    {
        const string privateMessage = "pcp://private-tracker/secret payload file/path";
        var exception = new RecordingStreamTransformException(privateMessage, site);
        Assert.IsAssignableFrom<IOException>(exception);
        Assert.Equal(privateMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(site, exception.Diagnostic.Site);
        Assert.Equal((int)site <= 26 ? RecordingTransformPhase.DecodableStart : RecordingTransformPhase.TimestampNormalization,
            exception.Diagnostic.Phase);
        Assert.NotEqual(RecordingTransformFailureReason.Unknown, exception.Diagnostic.Reason);
        Assert.Equal(RecordingFailureStage.StreamTransform,
            RecordingFaultDescriptor.FromException(RecordingFailureStage.StreamTransform, exception).Stage);
        Assert.Equal("RecordingStreamTransformException",
            RecordingFaultDescriptor.FromException(exception).ExceptionType);
    }

    [Fact]
    public void UnknownSiteAndLegacyConstructorRemainUnknownRegardlessOfMessage()
    {
        Assert.Equal(default, new RecordingStreamTransformException("The FLV signature is invalid.").Diagnostic);
        Assert.Equal(default, new RecordingStreamTransformException("secret", (RecordingTransformThrowSite)999).Diagnostic);
        Assert.Equal(43, Enum.GetValues<RecordingTransformThrowSite>().Length);
        Assert.Equal(42, Sites.Count);
    }

    [Theory]
    [InlineData(false, 0, RecordingTransformThrowSite.DecodableStartSignature, RecordingTransformFailureReason.InvalidFlvHeader)]
    [InlineData(true, 0, RecordingTransformThrowSite.TimestampNormalizationSignature, RecordingTransformFailureReason.InvalidFlvHeader)]
    [InlineData(false, 1, RecordingTransformThrowSite.DecodableStartVersionOrFlags, RecordingTransformFailureReason.InvalidFlvVersion)]
    [InlineData(true, 1, RecordingTransformThrowSite.TimestampNormalizationVersionOrFlags, RecordingTransformFailureReason.InvalidFlvVersion)]
    [InlineData(false, 2, RecordingTransformThrowSite.DecodableStartReadExactlyTruncated, RecordingTransformFailureReason.TruncatedInput)]
    [InlineData(true, 2, RecordingTransformThrowSite.TimestampNormalizationReadExactlyTruncated, RecordingTransformFailureReason.TruncatedInput)]
    [InlineData(false, 3, RecordingTransformThrowSite.DecodableStartInitialPreviousTagSize, RecordingTransformFailureReason.InvalidTagFraming)]
    [InlineData(true, 3, RecordingTransformThrowSite.TimestampNormalizationInitialPreviousTagSize, RecordingTransformFailureReason.InvalidTagFraming)]
    public async Task RealTransformThrowsCarryFixedSite(bool normalize, int corruption,
        RecordingTransformThrowSite site, RecordingTransformFailureReason reason)
    {
        byte[] input = [0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0];
        if (corruption == 0) input[0] = 0;
        if (corruption == 1) input[3] = 2;
        if (corruption == 2) input = [0x46];
        if (corruption == 3) input[12] = 1;
        await using Stream transform = normalize
            ? new FlvRecordingTimestampNormalizingStream(new MemoryStream(input))
            : new FlvRecordingDecodableStartStream(new MemoryStream(input));
        var exception = await Assert.ThrowsAsync<RecordingStreamTransformException>(async () =>
            await transform.CopyToAsync(Stream.Null));
        Assert.Equal(site, exception.Diagnostic.Site);
        Assert.Equal(reason, exception.Diagnostic.Reason);
    }

    [Fact]
    public async Task ConcurrentSinkSessionsCorrelateAdmissionFailureTerminalWithActualIdentity()
    {
        var observer = new CaptureObserver();
        var terminal = new TerminalObserver(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new RecordingFileSink(new SyntheticSources(release.Task), new Paths(root),
            terminalObserver: terminal, diagnosticObserver: observer);
        var first = await sink.StartAsync(Request(), default);
        var second = await sink.StartAsync(Request(), default);
        Assert.NotEqual(first.Session!.SessionId, second.Session!.SessionId);
        // These admission observations exist before either source can fail.
        Assert.Equal(2, observer.Events.Count);
        Assert.All(observer.Events, e => Assert.Equal(RecordingDiagnosticEventKind.WriterAdmitted, e.EventKind));
        release.SetResult();
        await terminal.Complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopFaultedAsync(sink, first.Session);
        await StopFaultedAsync(sink, second.Session);
        Assert.Equal(6, observer.Events.Count);
        foreach (var session in new[] { first.Session, second.Session })
        {
            var events = observer.Events.Where(e => e.ActualSessionId == session.SessionId.Value).ToArray();
            Assert.Equal(new[] { RecordingDiagnosticEventKind.WriterAdmitted, RecordingDiagnosticEventKind.TransformFailed,
                RecordingDiagnosticEventKind.Terminal }, events.Select(e => e.EventKind));
            Assert.Equal(RecordingTransformThrowSite.DecodableStartSignature, events[1].ThrowSiteId);
            Assert.Equal(RecordingFailureStage.StreamTransform, events[1].FaultStage);
            Assert.Equal(RecordingDiagnosticExceptionType.RecordingStreamTransformException, events[1].SafeExceptionType);
            Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, events[2].TerminalKind);
            var fact = Assert.Single(terminal.Facts, f => f.Session == session);
            Assert.Equal(RecordingSinkSessionLifecycleKind.Faulted, fact.Kind);
            Assert.Null(fact.WriteResult);
            Assert.Equal(RecordingFailureStage.StreamTransform, fact.Fault!.Stage);
            Assert.Equal("RecordingStreamTransformException", fact.Fault.ExceptionType);
        }
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledOrThrowingObserverPreservesFaultAndCleanup(bool throwing)
    {
        var terminal = new TerminalObserver(1);
        var sink = new RecordingFileSink(new SyntheticSources(Task.CompletedTask), new Paths(root),
            terminalObserver: terminal, diagnosticObserver: throwing ? new ThrowingObserver() : null);
        var started = await sink.StartAsync(Request(), default);
        await terminal.Complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopFaultedAsync(sink, started.Session!);
        var fact = Assert.Single(terminal.Facts);
        Assert.Equal(started.Session, fact.Session);
        Assert.Null(fact.WriteResult);
        Assert.Equal(RecordingFailureStage.StreamTransform, fact.Fault!.Stage);
        Assert.Empty(Directory.GetFiles(root));
    }

    private static async Task StopFaultedAsync(RecordingFileSink sink, RecordingSinkSession session)
    {
        var result = await sink.StopAsync(new RecordingSinkStopRequest(session), default);
        Assert.Equal(RecordingSinkStopResultKind.Faulted, result.Kind);
        Assert.False(sink.TryGetStatus(session, out _));
    }

    private static RecordingSinkStartRequest Request() => RecordingSinkStartRequest.Manual(
        new RecordingCandidate(new ChannelId(Guid.NewGuid()), new ChannelMetadataSnapshot(name: "private channel")));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OptionalObservationPreservesSuccessAndNoDecodableStart(bool noStart, bool enabled)
    {
        // AVC end-of-sequence without an IDR enters the existing no-start path.
        // A header-only FLV instead takes the existing legacy fallback.
        byte[] payload = noStart ? [0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0,
            9, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0x17, 2, 0, 0, 0, 0, 0, 0, 16] : [1, 2, 3];
        var terminal = new TerminalObserver(1);
        var observer = new CaptureObserver();
        var sink = new RecordingFileSink(new FixedSource(payload), new Paths(root, noStart ? ".flv" : ".bin"),
            terminalObserver: terminal, diagnosticObserver: enabled ? observer : null);
        var started = await sink.StartAsync(Request(), default);
        await terminal.Complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await sink.StopAsync(new RecordingSinkStopRequest(started.Session!), default);
        var fact = Assert.Single(terminal.Facts);
        Assert.Null(fact.Fault);
        Assert.Equal(noStart ? RecordingSinkSessionLifecycleKind.NoDecodableVideoStart : RecordingSinkSessionLifecycleKind.Completed, fact.Kind);
        Assert.Equal(noStart ? RecordingSinkStopResultKind.NoDecodableVideoStart : RecordingSinkStopResultKind.Stopped, stopped.Kind);
        if (noStart)
        {
            Assert.Null(fact.WriteResult);
            Assert.Empty(Directory.GetFiles(root));
        }
        else
        {
            Assert.Equal(payload, await File.ReadAllBytesAsync(fact.WriteResult!.Path));
            Assert.Equal(3, fact.WriteResult.BytesWritten);
        }
        Assert.Equal(enabled ? 2 : 0, observer.Events.Count);
        Assert.DoesNotContain(observer.Events, e => e.EventKind == RecordingDiagnosticEventKind.TransformFailed);
    }

    [Fact]
    public async Task CancellationBeforeAdmissionCreatesNoDiagnosticOrHistory()
    {
        var observer = new CaptureObserver();
        var terminal = new TerminalObserver(1);
        var sink = new RecordingFileSink(new FixedSource([1]), new Paths(root),
            terminalObserver: terminal, diagnosticObserver: observer);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sink.StartAsync(Request(), cancelled.Token));
        Assert.Empty(observer.Events);
        Assert.Empty(terminal.Facts);
        Assert.False(Directory.Exists(root));
    }

    private sealed class FixedSource(byte[] payload) : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new MemoryStream(payload));
    }

    private sealed class Paths(string root, string extension = ".flv") : IRecordingFilePathProvider
    {
        public string GetPath(RecordingSinkStartRequest request, RecordingSessionId sessionId) =>
            Path.Combine(root, sessionId.Value.ToString("N") + extension);
    }

    private sealed class SyntheticSources(Task release) : IRecordingStreamSourceProvider
    {
        public ValueTask<Stream> OpenReadAsync(RecordingSinkStartRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new GatedInput(release));
    }

    private sealed class GatedInput(Task release) : MemoryStream(new byte[13])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await release.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class CaptureObserver : IRecordingDiagnosticObserver
    {
        public ConcurrentQueue<RecordingDiagnosticEvent> Events { get; } = new();
        public bool TryObserve(in RecordingDiagnosticEvent diagnostic)
        {
            Events.Enqueue(diagnostic);
            return true;
        }
    }

    private sealed class ThrowingObserver : IRecordingDiagnosticObserver
    {
        public bool TryObserve(in RecordingDiagnosticEvent diagnostic) => throw new IOException("private diagnostic failure");
    }

    private sealed class TerminalObserver(int count) : IRecordingSessionTerminalObserver
    {
        public ConcurrentQueue<RecordingSessionTerminalFact> Facts { get; } = new();
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ObserveTerminal(RecordingSessionTerminalFact fact)
        {
            Facts.Enqueue(fact);
            if (Facts.Count == count) Complete.TrySetResult();
        }
        public void RecordPublicationFailure() => throw new InvalidOperationException("Unexpected History gap.");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }
}
