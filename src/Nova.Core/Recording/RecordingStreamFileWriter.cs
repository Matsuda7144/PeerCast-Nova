using System.Runtime.ExceptionServices;

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
        return WriteFinalizedResultAsync(
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
        return WriteFinalizedResultAsync(
            source,
            path,
            failureObserver,
            stoppingToken);
    }

    internal static ValueTask<RecordingFileWriteOutcome> WriteForFileSinkAsync(
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
            allowNoDecodableVideoStart: true,
            preserveWrittenBytesOnReadFailure: true,
            stoppingToken);
    }

    private static async ValueTask<RecordingFileWriteResult> WriteFinalizedResultAsync(
        Stream source,
        string path,
        Action<RecordingFailureStage, Exception>? failureObserver,
        CancellationToken stoppingToken)
    {
        var outcome = await WriteCoreAsync(
            source,
            path,
            failureObserver,
            allowNoDecodableVideoStart: false,
            preserveWrittenBytesOnReadFailure: false,
            stoppingToken).ConfigureAwait(false);
        return outcome.WriteResult ?? throw new InvalidOperationException(
            "The recording file writer did not produce a finalized file.");
    }

    private static async ValueTask<RecordingFileWriteOutcome> WriteCoreAsync(
        Stream source,
        string path,
        Action<RecordingFailureStage, Exception>? failureObserver,
        bool allowNoDecodableVideoStart,
        bool preserveWrittenBytesOnReadFailure,
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

        if (stoppingToken.IsCancellationRequested)
        {
            if (allowNoDecodableVideoStart &&
                source is IRecordingFileWriteOutcomeSource initialOutcomeSource &&
                initialOutcomeSource.NoDecodableVideoStart)
            {
                return RecordingFileWriteOutcome.NoDecodableVideoStart;
            }

            if (source is not IRecordingFileWriteOutcomeSource
                {
                    HasPendingDecodableStartOutput: true,
                })
            {
                stoppingToken.ThrowIfCancellationRequested();
            }
        }

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
        var drainingDecodableStartOutput = false;
        Exception? preservedReadFailure = null;
        var preservedReadFailureStage = RecordingFailureStage.SourceRead;

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
                    var readToken = drainingDecodableStartOutput
                        ? CancellationToken.None
                        : stoppingToken;

                    try
                    {
                        read = await source.ReadAsync(
                            buffer.AsMemory(),
                            readToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        completionKind =
                            RecordingFileWriteCompletionKind.Stopped;
                        drainingDecodableStartOutput =
                            source is IRecordingFileWriteOutcomeSource
                            {
                                HasPendingDecodableStartOutput: true,
                            };
                        if (!drainingDecodableStartOutput)
                        {
                            break;
                        }

                        continue;
                    }
                    catch (RecordingStreamTransformException exception)
                    {
                        if (preserveWrittenBytesOnReadFailure &&
                            bytesWritten > 0 &&
                            source is not IRecordingFileWriteOutcomeSource { NoDecodableVideoStart: true })
                        {
                            // すでに書き込み済みの有効な録画データがある場合は .partial を削除せず
                            // 確定保存したうえで障害を報告する。
                            preservedReadFailure = exception;
                            preservedReadFailureStage = RecordingFailureStage.StreamTransform;
                            break;
                        }

                        ReportFailure(
                            failureObserver,
                            RecordingFailureStage.StreamTransform,
                            exception);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        if (preserveWrittenBytesOnReadFailure &&
                            bytesWritten > 0 &&
                            source is not IRecordingFileWriteOutcomeSource { NoDecodableVideoStart: true })
                        {
                            // 上流切断などの途中障害でも、それまで書き込んだ録画データを
                            // 確定保存したうえで障害を報告する。
                            preservedReadFailure = exception;
                            preservedReadFailureStage = RecordingFailureStage.SourceRead;
                            break;
                        }

                        ReportFailure(
                            failureObserver,
                            RecordingFailureStage.SourceRead,
                            exception);
                        throw;
                    }

                    if (read == 0)
                    {
                        if (drainingDecodableStartOutput &&
                            source is IRecordingFileWriteOutcomeSource
                            {
                                HasPendingDecodableStartOutput: true,
                            })
                        {
                            var exception = new InvalidDataException(
                                "The accepted decodable start output ended before it was fully drained.");
                            ReportFailure(
                                failureObserver,
                                RecordingFailureStage.SourceRead,
                                exception);
                            throw exception;
                        }

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

                    if (drainingDecodableStartOutput &&
                        source is IRecordingFileWriteOutcomeSource
                        {
                            HasPendingDecodableStartOutput: false,
                        })
                    {
                        break;
                    }
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

            if (source is IRecordingFileWriteOutcomeSource outcomeSource &&
                outcomeSource.NoDecodableVideoStart)
            {
                if (allowNoDecodableVideoStart)
                {
                    return RecordingFileWriteOutcome.NoDecodableVideoStart;
                }

                throw new InvalidOperationException(
                    "A stream without a decodable video start cannot produce a finalized file.");
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

            if (preservedReadFailure is not null)
            {
                ReportFailure(
                    failureObserver,
                    preservedReadFailureStage,
                    preservedReadFailure);
                ExceptionDispatchInfo.Capture(preservedReadFailure).Throw();
            }

            var result = new RecordingFileWriteResult(
                fullPath,
                bytesWritten,
                completionKind);
            return RecordingFileWriteOutcome.Finalized(result);
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
