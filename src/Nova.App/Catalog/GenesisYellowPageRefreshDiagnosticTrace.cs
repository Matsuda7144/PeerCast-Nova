using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace PeerCast.Nova.App;

internal enum GenesisYellowPageRefreshTrigger
{
    Initial,
    Manual,
    Automatic,
}

internal sealed record GenesisYellowPageRefreshDiagnosticContext(
    GenesisYellowPageRefreshDiagnosticTrace Trace,
    Guid RefreshRequestId,
    GenesisYellowPageRefreshTrigger Trigger,
    Guid? RefreshAttemptId = null,
    string? ProviderId = null,
    long? ConfigurationRevision = null)
{
    public GenesisYellowPageRefreshDiagnosticContext ForAttempt(
        Guid attemptId,
        long? configurationRevision) =>
        this with
        {
            RefreshAttemptId = attemptId,
            ProviderId = null,
            ConfigurationRevision = configurationRevision,
        };

    public GenesisYellowPageRefreshDiagnosticContext ForProvider(string providerId) =>
        this with { ProviderId = providerId };
}

internal static class GenesisYellowPageRefreshDiagnosticAmbient
{
    private static readonly AsyncLocal<GenesisYellowPageRefreshDiagnosticContext?> CurrentContext = new();

    public static GenesisYellowPageRefreshDiagnosticContext? Current => CurrentContext.Value;

    public static IDisposable Push(GenesisYellowPageRefreshDiagnosticContext? context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(
        GenesisYellowPageRefreshDiagnosticContext? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                CurrentContext.Value = previous;
            }
        }
    }
}

/// <summary>
/// Explicitly opted-in, bounded evidence for Yellow Page refresh causality.
/// Only fixed scalar fields are accepted; configured URLs and response bodies
/// are never part of this event schema.
/// </summary>
internal sealed class GenesisYellowPageRefreshDiagnosticTrace : IAsyncDisposable
{
    internal const string DirectoryEnvironmentVariable =
        "NOVA_YELLOW_PAGE_REFRESH_DIAGNOSTICS_DIRECTORY";
    internal const string FileName = "yellow-page-refresh-diagnostics.jsonl";
    internal const string ManifestFileName = "yellow-page-refresh-diagnostics-manifest.json";
    internal const int QueueCapacity = 256;
    internal const int MaximumRecords = 4096;
    internal const int MaximumFileBytes = 4 * 1024 * 1024;
    internal const int MaximumProviderMappings = MaximumRecords;
    private const int ClosingRecordReserveBytes = 512;
    private static readonly TimeSpan ShutdownFlushLimit = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Channel<GenesisYellowPageRefreshDiagnosticEvent> _queue;
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly Task _writer;
    private readonly int _maximumRecords;
    private readonly int _maximumFileBytes;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _providerIds = new(StringComparer.Ordinal);
    private long _sequence;
    private long _droppedEvents;
    private int _closed;
    private int _writerFailed;

    private GenesisYellowPageRefreshDiagnosticTrace(
        Stream output,
        int maximumRecords,
        int maximumFileBytes,
        int queueCapacity,
        bool leaveOpen,
        Guid? runId = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecords);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, ClosingRecordReserveBytes + 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _output = output;
        _maximumRecords = maximumRecords;
        _maximumFileBytes = maximumFileBytes;
        _leaveOpen = leaveOpen;
        RunId = runId ?? Guid.NewGuid();
        _queue = Channel.CreateBounded<GenesisYellowPageRefreshDiagnosticEvent>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });
        _writer = Task.Run(WriteEventsAsync);
    }

    internal Guid RunId { get; }

    internal long DroppedEvents => Interlocked.Read(ref _droppedEvents);

    internal bool WriterFailed => Volatile.Read(ref _writerFailed) != 0;

    internal Task WriterCompletion => _writer;

    internal int ProviderMappingCount
    {
        get
        {
            lock (_stateGate)
            {
                return _providerIds.Count;
            }
        }
    }

    internal static GenesisYellowPageRefreshDiagnosticTrace? FromEnvironment(
        Func<string?>? readDirectory = null)
    {
        try
        {
            var path = readDirectory is null
                ? Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable)
                : readDirectory();
            return TryCreate(path);
        }
        catch
        {
            return null;
        }
    }

    internal static GenesisYellowPageRefreshDiagnosticTrace? TryCreate(string? newRunDirectory)
    {
        if (string.IsNullOrWhiteSpace(newRunDirectory) ||
            !Path.IsPathFullyQualified(newRunDirectory))
        {
            return null;
        }

        var path = Path.GetFullPath(newRunDirectory);
        if (Directory.Exists(path) || File.Exists(path) || IsUnderLocalApplicationData(path))
        {
            return null;
        }

        for (var ancestor = new DirectoryInfo(path); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (File.Exists(Path.Combine(ancestor.FullName, ".git")) ||
                Directory.Exists(Path.Combine(ancestor.FullName, ".git")) ||
                (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                return null;
            }
        }

        FileStream? output = null;
        try
        {
            Directory.CreateDirectory(path);
            var manifest = new
            {
                schemaVersion = 1,
                runId = Guid.NewGuid(),
                createdAtUtc = DateTimeOffset.UtcNow,
                environmentVariable = DirectoryEnvironmentVariable,
                eventFile = FileName,
                scope = "Opt-in Yellow Page refresh diagnostics; scalar metadata only; no URLs or response bodies.",
            };
            var runId = manifest.runId;
            using (var manifestStream = new FileStream(
                       Path.Combine(path, ManifestFileName),
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       1024,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
                manifestStream.Flush(true);
            }

            output = new FileStream(
                Path.Combine(path, FileName),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            return new GenesisYellowPageRefreshDiagnosticTrace(
                output,
                MaximumRecords,
                MaximumFileBytes,
                QueueCapacity,
                leaveOpen: false,
                runId);
        }
        catch
        {
            try
            {
                output?.Dispose();
            }
            catch
            {
                // Diagnostic setup is best effort.
            }

            return null;
        }
    }

    internal static GenesisYellowPageRefreshDiagnosticTrace CreateForTesting(
        Stream output,
        int maximumRecords = MaximumRecords,
        int maximumFileBytes = MaximumFileBytes,
        int queueCapacity = QueueCapacity,
        bool leaveOpen = true,
        Guid? runId = null) =>
        new(output, maximumRecords, maximumFileBytes, queueCapacity, leaveOpen, runId);

    internal bool TryRecord(
        string eventType,
        GenesisYellowPageRefreshDiagnosticContext? context = null,
        string? stage = null,
        string? outcome = null,
        int? enabledProviderCount = null,
        int? httpStatusCode = null,
        long? responseByteCount = null,
        long? durationMilliseconds = null,
        int? inputRowCount = null,
        int? parsedRowCount = null,
        int? skippedRowCount = null,
        int? projectedRowCount = null,
        string? exceptionType = null,
        string? failureStage = null,
        string? suppressionReason = null)
    {
        lock (_stateGate)
        {
            if (_closed != 0 || string.IsNullOrWhiteSpace(eventType))
            {
                return false;
            }

            if (WriterFailed)
            {
                Interlocked.Increment(ref _droppedEvents);
                return false;
            }

            var effectiveContext = context ?? GenesisYellowPageRefreshDiagnosticAmbient.Current;
            if (effectiveContext is not null &&
                !ReferenceEquals(effectiveContext.Trace, this))
            {
                effectiveContext = null;
            }

            if (!TryGetOpaqueProviderId(effectiveContext?.ProviderId, out var providerId))
            {
                // Dropping the whole event avoids claiming provider correlation
                // when the bounded mapping cannot represent this provider.
                Interlocked.Increment(ref _droppedEvents);
                return false;
            }

            var diagnostic = new GenesisYellowPageRefreshDiagnosticEvent(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                RunId,
                eventType,
                effectiveContext?.RefreshRequestId,
                effectiveContext?.RefreshAttemptId,
                effectiveContext?.Trigger.ToString(),
                providerId,
                effectiveContext?.ConfigurationRevision,
                stage,
                outcome,
                enabledProviderCount,
                httpStatusCode,
                responseByteCount,
                durationMilliseconds,
                inputRowCount,
                parsedRowCount,
                skippedRowCount,
                projectedRowCount,
                exceptionType is null ? null : Bound(exceptionType, 96),
                failureStage,
                suppressionReason);

            if (_queue.Writer.TryWrite(diagnostic))
            {
                return true;
            }

            Interlocked.Increment(ref _droppedEvents);
            return false;
        }
    }

    // Called only under _stateGate, together with the producer admission check.
    private bool TryGetOpaqueProviderId(string? providerId, out string? opaqueId)
    {
        opaqueId = null;
        if (providerId is null)
        {
            return true;
        }

        // Configuration IDs are untrusted text. Only run-local ordinals enter
        // the queue; the raw-to-opaque mapping never leaves this trace instance.
        if (!_providerIds.TryGetValue(providerId, out opaqueId))
        {
            if (_providerIds.Count >= MaximumProviderMappings)
            {
                return false;
            }

            opaqueId = "provider-" + (_providerIds.Count + 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            _providerIds.Add(providerId, opaqueId);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateGate)
        {
            if (_closed != 0)
            {
                return;
            }

            _closed = 1;
            _queue.Writer.TryComplete();
            // Queued events contain only opaque IDs. The writer does not use
            // this table, so it is safe to release it before bounded I/O drain.
            _providerIds.Clear();
        }

        try
        {
            await _writer.WaitAsync(ShutdownFlushLimit).ConfigureAwait(false);
        }
        catch
        {
            // Evidence flush is bounded and cannot hold refresh/session shutdown.
        }
    }

    private void FailWriter()
    {
        lock (_stateGate)
        {
            Interlocked.Exchange(ref _writerFailed, 1);
            _queue.Writer.TryComplete();
            _providerIds.Clear();
        }

        // The channel is closed to producers before this single reader drains
        // it. Every accepted but unwritten event is therefore counted once.
        while (_queue.Reader.TryRead(out _))
        {
            Interlocked.Increment(ref _droppedEvents);
        }
    }

    private async Task WriteEventsAsync()
    {
        var bytesWritten = 0;
        var recordsWritten = 0;
        try
        {
            await foreach (var diagnostic in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                byte[] bytes;
                try
                {
                    bytes = JsonSerializer.SerializeToUtf8Bytes(diagnostic, JsonOptions);
                }
                catch
                {
                    Interlocked.Increment(ref _droppedEvents);
                    continue;
                }

                var lineBytes = bytes.Length + 1;
                if (recordsWritten >= _maximumRecords - 1 ||
                    bytesWritten + lineBytes > _maximumFileBytes - ClosingRecordReserveBytes)
                {
                    Interlocked.Increment(ref _droppedEvents);
                    continue;
                }

                try
                {
                    await _output.WriteAsync(bytes).ConfigureAwait(false);
                    await _output.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
                    await _output.FlushAsync().ConfigureAwait(false);
                    bytesWritten += lineBytes;
                    recordsWritten++;
                }
                catch
                {
                    Interlocked.Increment(ref _droppedEvents);
                    FailWriter();
                    // The sink has failed: neither retry nor attempt a closing
                    // summary. DiagnosticsClosed is not guaranteed on I/O fault.
                    return;
                }
            }

            var summary = new GenesisYellowPageRefreshDiagnosticEvent(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                RunId,
                "DiagnosticsClosed",
                null,
                null,
                null,
                null,
                null,
                "Shutdown",
                WriterFailed
                    ? "WriterFailed"
                    : Interlocked.Read(ref _droppedEvents) > 0
                        ? "FlushedWithDrops"
                        : "Flushed",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                Interlocked.Read(ref _droppedEvents));
            var summaryBytes = JsonSerializer.SerializeToUtf8Bytes(summary, JsonOptions);
            if (!_leaveOpen &&
                recordsWritten < _maximumRecords &&
                bytesWritten + summaryBytes.Length + 1 <= _maximumFileBytes)
            {
                await _output.WriteAsync(summaryBytes).ConfigureAwait(false);
                await _output.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
                await _output.FlushAsync().ConfigureAwait(false);
            }
            else if (_leaveOpen && recordsWritten < _maximumRecords &&
                     bytesWritten + summaryBytes.Length + 1 <= _maximumFileBytes)
            {
                await _output.WriteAsync(summaryBytes).ConfigureAwait(false);
                await _output.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
                await _output.FlushAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            FailWriter();
        }
        finally
        {
            if (!_leaveOpen)
            {
                try
                {
                    await _output.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    FailWriter();
                }
            }
        }
    }

    private static bool IsUnderLocalApplicationData(string path)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localAppData));
        return path.StartsWith(
            string.Concat(root, Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Bound(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

internal sealed record GenesisYellowPageRefreshDiagnosticEvent(
    long Sequence,
    DateTimeOffset TimestampUtc,
    Guid RunId,
    string EventType,
    Guid? RefreshRequestId,
    Guid? RefreshAttemptId,
    string? Trigger,
    string? ProviderId,
    long? ConfigurationRevision,
    string? Stage,
    string? Outcome,
    int? EnabledProviderCount,
    int? HttpStatusCode,
    long? ResponseByteCount,
    long? DurationMilliseconds,
    int? InputRowCount,
    int? ParsedRowCount,
    int? SkippedRowCount,
    int? ProjectedRowCount,
    string? ExceptionType,
    string? FailureStage,
    string? SuppressionReason,
    long? DroppedEvents = null);
