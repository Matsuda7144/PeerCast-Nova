using System.Security;
using System.Security.Cryptography;
using System.Text;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingRuleSnapshotStatus
{
    Available,
    MissingFile,
    Invalid,
    UnsupportedVersion,
    Unavailable,
    Cancelled,
}

internal enum GenesisRecordingRuleRepositoryFailureKind
{
    None,
    Malformed,
    TooLarge,
    UnsupportedVersion,
    Io,
    Permission,
    NoPath,
    Cancelled,
    ShuttingDown,
}

internal enum GenesisRecordingRuleRevisionKind
{
    Missing,
    Sha256,
}

internal readonly record struct GenesisRecordingRuleRevision(
    GenesisRecordingRuleRevisionKind Kind,
    string Value)
{
    public static GenesisRecordingRuleRevision Missing { get; } =
        new(GenesisRecordingRuleRevisionKind.Missing, "missing:v1");

    public static GenesisRecordingRuleRevision FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new(
            GenesisRecordingRuleRevisionKind.Sha256,
            $"sha256:{hash}");
    }
}

internal sealed record GenesisRecordingRuleRepositorySnapshot(
    GenesisRecordingRuleSnapshotStatus Status,
    RecordingRuleSet? Rules,
    GenesisRecordingRuleRevision? Revision,
    GenesisRecordingRuleRepositoryFailureKind FailureKind)
{
    public bool IsAvailable => Status is
        GenesisRecordingRuleSnapshotStatus.Available or
        GenesisRecordingRuleSnapshotStatus.MissingFile;
}

internal enum GenesisRecordingRuleRepositoryWriteStatus
{
    Applied,
    Conflict,
    Invalid,
    PersistenceFailed,
    Cancelled,
    Unavailable,
}

internal sealed record GenesisRecordingRuleRepositoryWriteResult(
    GenesisRecordingRuleRepositoryWriteStatus Status,
    GenesisRecordingRuleRepositorySnapshot? Snapshot,
    GenesisRecordingRuleRepositoryFailureKind FailureKind)
{
    public bool Succeeded => Status == GenesisRecordingRuleRepositoryWriteStatus.Applied;
}

/// <summary>
/// Application-owned read and serialized write boundary for recording rules.
/// The caller supplies a snapshot as the optimistic revision for each save;
/// production composition should own one instance for the application lifetime.
/// </summary>
internal sealed class GenesisRecordingRuleRepository : IAsyncDisposable
{
    private static readonly RecordingRuleSet EmptyRuleSet = new([]);

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly object lifecycleGate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource shutdownCancellation = new();
    private readonly TaskCompletionSource operationsDrained = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposeCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string? path;
    private readonly Func<string, RecordingRuleSet, CancellationToken, ValueTask> saveAsync;
    private int activeOperations;
    private bool isShuttingDown;

    public GenesisRecordingRuleRepository(
        string? path,
        Func<string, RecordingRuleSet, CancellationToken, ValueTask>? saveAsync = null)
    {
        this.path = string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path);
        this.saveAsync = saveAsync ?? RecordingRuleFileStore.SaveAsync;
    }

    public static GenesisRecordingRuleRepository CreateSystem() =>
        new(GenesisRecordingRuleFilePath.GetSystemPath());

    public async ValueTask<GenesisRecordingRuleRepositorySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var operation = TryBeginOperation(cancellationToken);
        if (operation is null)
        {
            return CancelledSnapshot(GenesisRecordingRuleRepositoryFailureKind.ShuttingDown);
        }

        using (operation)
        {
            try
            {
                await operationGate.WaitAsync(operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancelledSnapshot(GetCancellationFailureKind());
            }

            try
            {
                return await LoadSnapshotCoreAsync(operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CancelledSnapshot(GetCancellationFailureKind());
            }
            finally
            {
                operationGate.Release();
            }
        }
    }

    public async ValueTask<GenesisRecordingRuleRepositoryWriteResult> SaveAsync(
        GenesisRecordingRuleRepositorySnapshot expectedSnapshot,
        RecordingRuleSet proposedRules,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        ArgumentNullException.ThrowIfNull(proposedRules);

        if (!expectedSnapshot.IsAvailable ||
            expectedSnapshot.Rules is null ||
            expectedSnapshot.Revision is null)
        {
            return WriteResult(
                GenesisRecordingRuleRepositoryWriteStatus.Unavailable,
                expectedSnapshot,
                expectedSnapshot.FailureKind);
        }

        var operation = TryBeginOperation(cancellationToken);
        if (operation is null)
        {
            return WriteResult(
                GenesisRecordingRuleRepositoryWriteStatus.Cancelled,
                expectedSnapshot,
                GenesisRecordingRuleRepositoryFailureKind.ShuttingDown);
        }

        using (operation)
        {
            byte[] proposedBytes;
            try
            {
                proposedBytes = StrictUtf8.GetBytes(
                    RecordingRuleJsonCodec.Serialize(proposedRules));
            }
            catch (EncoderFallbackException)
            {
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Invalid,
                    expectedSnapshot,
                    GenesisRecordingRuleRepositoryFailureKind.Malformed);
            }
            catch (ArgumentException)
            {
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Invalid,
                    expectedSnapshot,
                    GenesisRecordingRuleRepositoryFailureKind.Malformed);
            }

            if (proposedBytes.Length > RecordingRuleFileStore.MaximumFileByteCount)
            {
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Invalid,
                    expectedSnapshot,
                    GenesisRecordingRuleRepositoryFailureKind.TooLarge);
            }

            try
            {
                await operationGate.WaitAsync(operation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Cancelled,
                    expectedSnapshot,
                    GetCancellationFailureKind());
            }

            try
            {
                if (path is null)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.Unavailable,
                        expectedSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.NoPath);
                }

                var currentSnapshot = await LoadSnapshotCoreAsync(operation.Token)
                    .ConfigureAwait(false);

                if (!currentSnapshot.IsAvailable || currentSnapshot.Revision is null)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.Unavailable,
                        currentSnapshot,
                        currentSnapshot.FailureKind);
                }

                if (currentSnapshot.Revision != expectedSnapshot.Revision)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.Conflict,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.None);
                }

                // This detects external changes observed before the write decision.
                // The filesystem exposes no compare-and-swap here, so a non-cooperating
                // external writer can still race between this check and replacement.
                try
                {
                    await saveAsync(path, proposedRules, operation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.Cancelled,
                        currentSnapshot,
                        GetCancellationFailureKind());
                }
                catch (UnauthorizedAccessException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.Permission);
                }
                catch (SecurityException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.Permission);
                }
                catch (IOException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.Io);
                }
                catch (NotSupportedException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.PersistenceFailed,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.Io);
                }
                catch (InvalidOperationException)
                {
                    return WriteResult(
                        GenesisRecordingRuleRepositoryWriteStatus.Invalid,
                        currentSnapshot,
                        GenesisRecordingRuleRepositoryFailureKind.TooLarge);
                }

                var savedSnapshot = new GenesisRecordingRuleRepositorySnapshot(
                    GenesisRecordingRuleSnapshotStatus.Available,
                    proposedRules,
                    GenesisRecordingRuleRevision.FromBytes(proposedBytes),
                    GenesisRecordingRuleRepositoryFailureKind.None);
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Applied,
                    savedSnapshot,
                    GenesisRecordingRuleRepositoryFailureKind.None);
            }
            catch (OperationCanceledException)
            {
                return WriteResult(
                    GenesisRecordingRuleRepositoryWriteStatus.Cancelled,
                    null,
                    GetCancellationFailureKind());
            }
            finally
            {
                operationGate.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        var startDisposal = false;
        lock (lifecycleGate)
        {
            if (!isShuttingDown)
            {
                isShuttingDown = true;
                if (activeOperations == 0)
                {
                    operationsDrained.TrySetResult();
                }

                startDisposal = true;
            }
        }

        if (startDisposal)
        {
            shutdownCancellation.Cancel();
            _ = CompleteDisposalAsync();
        }

        return new ValueTask(disposeCompletion.Task);
    }

    private async Task<GenesisRecordingRuleRepositorySnapshot> LoadSnapshotCoreAsync(
        CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return UnavailableSnapshot(GenesisRecordingRuleRepositoryFailureKind.NoPath);
        }

        byte[] bytes;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length > RecordingRuleFileStore.MaximumFileByteCount)
            {
                return InvalidSnapshot(
                    GenesisRecordingRuleRepositoryFailureKind.TooLarge,
                    revision: null);
            }

            if (stream.Length == 0)
            {
                bytes = [];
            }
            else
            {
                bytes = new byte[checked((int)stream.Length)];
                try
                {
                    await stream.ReadExactlyAsync(bytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    return UnavailableSnapshot(
                        GenesisRecordingRuleRepositoryFailureKind.Io);
                }

                if (stream.Length != bytes.Length)
                {
                    return UnavailableSnapshot(
                        GenesisRecordingRuleRepositoryFailureKind.Io);
                }
            }
        }
        catch (FileNotFoundException)
        {
            return MissingSnapshot();
        }
        catch (DirectoryNotFoundException)
        {
            return MissingSnapshot();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return UnavailableSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Permission);
        }
        catch (SecurityException)
        {
            return UnavailableSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Permission);
        }
        catch (IOException)
        {
            return UnavailableSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Io);
        }

        var revision = GenesisRecordingRuleRevision.FromBytes(bytes);
        if (bytes.Length == 0)
        {
            return InvalidSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Malformed,
                revision);
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return InvalidSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Malformed,
                revision);
        }

        try
        {
            var rules = RecordingRuleJsonCodec.Deserialize(json);
            return new(
                GenesisRecordingRuleSnapshotStatus.Available,
                rules,
                revision,
                GenesisRecordingRuleRepositoryFailureKind.None);
        }
        catch (NotSupportedException)
        {
            return new(
                GenesisRecordingRuleSnapshotStatus.UnsupportedVersion,
                null,
                revision,
                GenesisRecordingRuleRepositoryFailureKind.UnsupportedVersion);
        }
        catch (FormatException)
        {
            return InvalidSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Malformed,
                revision);
        }
        catch (ArgumentException)
        {
            return InvalidSnapshot(
                GenesisRecordingRuleRepositoryFailureKind.Malformed,
                revision);
        }
    }

    private OperationScope? TryBeginOperation(CancellationToken cancellationToken)
    {
        lock (lifecycleGate)
        {
            if (isShuttingDown)
            {
                return null;
            }

            activeOperations++;
            try
            {
                return new OperationScope(
                    this,
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        shutdownCancellation.Token));
            }
            catch
            {
                activeOperations--;
                throw;
            }
        }
    }

    private void EndOperation()
    {
        lock (lifecycleGate)
        {
            activeOperations--;
            if (isShuttingDown && activeOperations == 0)
            {
                operationsDrained.TrySetResult();
            }
        }
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            await operationsDrained.Task.ConfigureAwait(false);
            operationGate.Dispose();
            shutdownCancellation.Dispose();
            disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            disposeCompletion.TrySetException(exception);
        }
    }

    private static GenesisRecordingRuleRepositorySnapshot MissingSnapshot() =>
        new(
            GenesisRecordingRuleSnapshotStatus.MissingFile,
            EmptyRuleSet,
            GenesisRecordingRuleRevision.Missing,
            GenesisRecordingRuleRepositoryFailureKind.None);

    private static GenesisRecordingRuleRepositorySnapshot InvalidSnapshot(
        GenesisRecordingRuleRepositoryFailureKind failureKind,
        GenesisRecordingRuleRevision? revision) =>
        new(
            GenesisRecordingRuleSnapshotStatus.Invalid,
            null,
            revision,
            failureKind);

    private static GenesisRecordingRuleRepositorySnapshot UnavailableSnapshot(
        GenesisRecordingRuleRepositoryFailureKind failureKind) =>
        new(
            GenesisRecordingRuleSnapshotStatus.Unavailable,
            null,
            null,
            failureKind);

    private static GenesisRecordingRuleRepositorySnapshot CancelledSnapshot(
        GenesisRecordingRuleRepositoryFailureKind failureKind) =>
        new(
            GenesisRecordingRuleSnapshotStatus.Cancelled,
            null,
            null,
            failureKind);

    private static GenesisRecordingRuleRepositoryWriteResult WriteResult(
        GenesisRecordingRuleRepositoryWriteStatus status,
        GenesisRecordingRuleRepositorySnapshot? snapshot,
        GenesisRecordingRuleRepositoryFailureKind failureKind) =>
        new(status, snapshot, failureKind);

    private GenesisRecordingRuleRepositoryFailureKind GetCancellationFailureKind()
    {
        lock (lifecycleGate)
        {
            return isShuttingDown
                ? GenesisRecordingRuleRepositoryFailureKind.ShuttingDown
                : GenesisRecordingRuleRepositoryFailureKind.Cancelled;
        }
    }

    private sealed class OperationScope(
        GenesisRecordingRuleRepository owner,
        CancellationTokenSource cancellationSource) : IDisposable
    {
        private GenesisRecordingRuleRepository? owner = owner;
        private CancellationTokenSource? cancellationSource = cancellationSource;

        public CancellationToken Token =>
            cancellationSource?.Token ?? new CancellationToken(canceled: true);

        public void Dispose()
        {
            Interlocked.Exchange(ref cancellationSource, null)?.Dispose();
            Interlocked.Exchange(ref owner, null)?.EndOperation();
        }
    }
}

internal static class GenesisRecordingRuleFilePath
{
    private const string FileName = "recording-rules.json";

    public static string? GetSystemPath() => Resolve(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string? Resolve(string? localApplicationData) =>
        string.IsNullOrWhiteSpace(localApplicationData)
            ? null
            : Path.Combine(
                localApplicationData,
                SystemGenesisGuiNextApplicationState.ApplicationDirectoryName,
                FileName);
}
