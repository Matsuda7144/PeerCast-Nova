namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Copies one externally supplied recording stream into a single file.
/// A stop cancellation finalizes bytes already received; other failures leave
/// no final target behind.
/// </summary>
public static class RecordingStreamFileWriter
{
    private const int BufferSize = 64 * 1024;

    public static ValueTask<RecordingFileWriteResult> WriteAsync(
        Stream source,
        string path,
        CancellationToken stoppingToken)
    {
        return WriteCoreAsync(
            source,
            path,
            failureObserver: null,
            stoppingToken);
    }

    internal static ValueTask<RecordingFileWriteResult> WriteAsync(
        Stream source,
        string path,
        Action<RecordingFailureStage, Exception> failureObserver,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(failureObserver);
        return WriteCoreAsync(
            source,
            path,
            failureObserver,
            stoppingToken);
    }

    private static async ValueTask<RecordingFileWriteResult> WriteCoreAsync(
        Stream source,
        string path,
        Action<RecordingFailureStage, Exception>? failureObserver,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The recording source stream must be readable.",
                nameof(source));
        }

        stoppingToken.ThrowIfCancellationRequested();

        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                "The recording file path must resolve to a directory.",
                nameof(path));
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception exception)
        {
            ReportFailure(
                failureObserver,
                RecordingFailureStage.FileInitialize,
                exception);
            throw;
        }

        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.partial");

        long bytesWritten = 0;
        var completionKind =
            RecordingFileWriteCompletionKind.EndOfStream;

        try
        {
            FileStream output;

            try
            {
                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);
            }
            catch (Exception exception)
            {
                ReportFailure(
                    failureObserver,
                    RecordingFailureStage.FileInitialize,
                    exception);
                throw;
            }

            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];

                while (true)
                {
                    int read;

                    try
                    {
                        read = await source.ReadAsync(
                            buffer.AsMemory(),
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        completionKind =
                            RecordingFileWriteCompletionKind.Stopped;
                        break;
                    }
                    catch (RecordingStreamTransformException exception)
                    {
                        ReportFailure(
                            failureObserver,
                            RecordingFailureStage.StreamTransform,
                            exception);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(
                            failureObserver,
                            RecordingFailureStage.SourceRead,
                            exception);
                        throw;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    try
                    {
                        await output.WriteAsync(
                            buffer.AsMemory(0, read),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(
                            failureObserver,
                            RecordingFailureStage.FileWrite,
                            exception);
                        throw;
                    }

                    bytesWritten += read;
                }

                try
                {
                    await output.FlushAsync(
                        CancellationToken.None).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                catch (Exception exception)
                {
                    ReportFailure(
                        failureObserver,
                        RecordingFailureStage.FileFinalize,
                        exception);
                    throw;
                }
            }

            try
            {
                File.Move(
                    temporaryPath,
                    fullPath,
                    overwrite: false);
            }
            catch (Exception exception)
            {
                ReportFailure(
                    failureObserver,
                    RecordingFailureStage.FileFinalize,
                    exception);
                throw;
            }

            return new RecordingFileWriteResult(
                fullPath,
                bytesWritten,
                completionKind);
        }
        finally
        {
            TryDeletePartialFile(temporaryPath);
        }
    }

    private static void ReportFailure(
        Action<RecordingFailureStage, Exception>? failureObserver,
        RecordingFailureStage stage,
        Exception exception)
    {
        try
        {
            failureObserver?.Invoke(stage, exception);
        }
        catch (Exception)
        {
            // A diagnostic observer must not replace the recording failure.
        }
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
