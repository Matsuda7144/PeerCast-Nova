using System.Text.Json;
using System.Threading.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>
/// Optional evidence, owned by the existing recording composition. Recorder
/// callbacks only try a bounded queue; all file I/O belongs to the worker.
/// </summary>
internal sealed class RecordingTransformDiagnosticFile : IRecordingDiagnosticObserver, IAsyncDisposable
{
    internal const string DirectoryEnvironmentVariable = "NOVA_RECORDING_TRANSFORM_DIAGNOSTICS_DIRECTORY";
    internal const string FileName = "recording-transform-diagnostics.jsonl";
    internal const int MaximumSessions = 256;
    internal const int MaximumRecords = 2048;
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int QueueCapacity = 128;
    private readonly object gate = new();
    private readonly Dictionary<Guid, int> observed = [];
    private readonly Channel<RecordingDiagnosticEvent> queue = Channel.CreateBounded<RecordingDiagnosticEvent>(
        new BoundedChannelOptions(QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private readonly int maximumFileBytes;
    private readonly int maximumRecords;
    private int accepted;
    private int closed;

    internal RecordingTransformDiagnosticFile(Stream output,
        int maximumFileBytes = MaximumFileBytes, int maximumRecords = MaximumRecords)
    {
        this.maximumFileBytes = maximumFileBytes;
        this.maximumRecords = maximumRecords;
        worker = Task.Run(() => WriteAsync(output));
    }

    internal static RecordingTransformDiagnosticFile? FromEnvironment(Func<string?>? readDirectory = null)
    {
        try
        {
            return TryCreate(readDirectory is null
                ? Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable)
                : readDirectory());
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static RecordingTransformDiagnosticFile? TryCreate(string? newRunDirectory)
    {
        if (string.IsNullOrWhiteSpace(newRunDirectory))
            return null;

        FileStream? output = null;
        try
        {
            if (!Path.IsPathFullyQualified(newRunDirectory))
                return null;
            var path = Path.GetFullPath(newRunDirectory);
            if (Directory.Exists(path) || File.Exists(path))
                return null;
            // Reject repository ancestors and junction/symlink ancestors. No
            // repository evidence or redirected production directory is allowed.
            for (var ancestor = new DirectoryInfo(path); ancestor is not null; ancestor = ancestor.Parent)
            {
                if (Directory.Exists(Path.Combine(ancestor.FullName, ".git")) ||
                    File.Exists(Path.Combine(ancestor.FullName, ".git")) ||
                    (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0))
                    return null;
            }
            Directory.CreateDirectory(path);
            output = new FileStream(Path.Combine(path, FileName), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            return new RecordingTransformDiagnosticFile(output);
        }
        catch (Exception)
        {
            try { output?.Dispose(); }
            catch (Exception) { }
            // Activation is best effort. Never overwrite or delete a directory,
            // and never turn an evidence failure into a recording/startup fault.
            return null;
        }
    }

    public bool TryObserve(in RecordingDiagnosticEvent diagnostic)
    {
        if (Volatile.Read(ref closed) != 0 || diagnostic.ActualSessionId == Guid.Empty ||
            !Enum.IsDefined(diagnostic.EventKind) || !Enum.IsDefined(diagnostic.TransformPhase) ||
            !Enum.IsDefined(diagnostic.ThrowSiteId) || !Enum.IsDefined(diagnostic.SafeReason) ||
            !Enum.IsDefined(diagnostic.FaultStage) || !Enum.IsDefined(diagnostic.SafeExceptionType) ||
            (diagnostic.TerminalKind is { } terminal && !Enum.IsDefined(terminal)))
            return false;

        // Drop on contention as well as overflow. No callback waits for I/O,
        // worker progress, or another recording's diagnostic admission.
        if (!Monitor.TryEnter(gate))
            return false;
        try
        {
            if (Volatile.Read(ref closed) != 0 || accepted >= maximumRecords)
                return false;
            var known = observed.TryGetValue(diagnostic.ActualSessionId, out var kinds);
            if (!known && observed.Count >= MaximumSessions)
                return false;
            var bit = 1 << (int)diagnostic.EventKind;
            // At most one admission, failure, and terminal per actual session.
            if ((kinds & bit) != 0 || !queue.Writer.TryWrite(diagnostic))
                return false;
            observed[diagnostic.ActualSessionId] = kinds | bit;
            accepted++;
            return true;
        }
        finally
        {
            Monitor.Exit(gate);
        }
    }

    private async Task WriteAsync(Stream output)
    {
        try
        {
            await using (output.ConfigureAwait(false))
            {
                var bytesWritten = 0;
                await foreach (var diagnostic in queue.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false))
                {
                    // Explicit allowlist: no exception/string/payload object can
                    // enter this schema. Enum values were validated at admission.
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        RecordingDiagnosticEvent.DiagnosticSchemaVersion,
                        EventKind = diagnostic.EventKind.ToString(),
                        diagnostic.ActualSessionId,
                        TransformPhase = diagnostic.TransformPhase.ToString(),
                        ThrowSiteId = diagnostic.ThrowSiteId.ToString(),
                        SafeReason = diagnostic.SafeReason.ToString(),
                        FaultStage = diagnostic.FaultStage.ToString(),
                        SafeExceptionType = diagnostic.SafeExceptionType.ToString(),
                        TerminalKind = diagnostic.TerminalKind?.ToString(),
                    });
                    if (bytesWritten + bytes.Length + 1 > maximumFileBytes)
                        break;
                    await output.WriteAsync(bytes, stopping.Token).ConfigureAwait(false);
                    await output.WriteAsync("\n"u8.ToArray(), stopping.Token).ConfigureAwait(false);
                    await output.FlushAsync(stopping.Token).ConfigureAwait(false);
                    bytesWritten += bytes.Length + 1;
                }
            }
        }
        catch (Exception)
        {
            // Cancellation, unavailable output and I/O faults stop evidence
            // only. No retries and no raw errors are emitted.
        }
        finally
        {
            Interlocked.Exchange(ref closed, 1);
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out _)) { }
            stopping.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref closed, 1);
        queue.Writer.TryComplete();
        // Drain normally, with a finite upper bound if storage stalls.
        try
        {
            stopping.CancelAfter(TimeSpan.FromSeconds(2));
            await worker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The worker owns its output even if a slow OS write outlives this
            // bounded wait. Recorder shutdown is never held by diagnostic I/O.
        }
    }
}
