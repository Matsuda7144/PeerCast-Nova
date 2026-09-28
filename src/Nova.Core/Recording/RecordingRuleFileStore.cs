using System.Text;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Bounded filesystem persistence for recording rule sets.
/// The caller owns path selection and lifecycle policy.
/// </summary>
public static class RecordingRuleFileStore
{
    public const int MaximumFileByteCount = 4 * 1024 * 1024;

    private const int BufferSize = 16 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async ValueTask SaveAsync(
        string path,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ruleSet);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                "Recording rule file path must resolve to a directory.",
                nameof(path));
        }

        var json = RecordingRuleJsonCodec.Serialize(ruleSet);
        byte[] bytes;

        try
        {
            bytes = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidOperationException(
                "Recording rule JSON could not be encoded as UTF-8.",
                exception);
        }

        if (bytes.Length > MaximumFileByteCount)
        {
            throw new InvalidOperationException(
                $"Recording rule JSON exceeds the {MaximumFileByteCount}-byte persistence limit.");
        }

        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.WriteAsync(
                    bytes,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(
                    cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(
                temporaryPath,
                fullPath,
                overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public static async ValueTask<RecordingRuleSet> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length <= 0)
        {
            throw new FormatException(
                "Recording rule file is empty.");
        }

        if (stream.Length > MaximumFileByteCount)
        {
            throw new FormatException(
                $"Recording rule file exceeds the {MaximumFileByteCount}-byte persistence limit.");
        }

        var bytes = new byte[checked((int)stream.Length)];

        try
        {
            await stream.ReadExactlyAsync(
                bytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            throw new IOException(
                "Recording rule file changed while it was being read.",
                exception);
        }

        string json;

        try
        {
            json = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException(
                "Recording rule file is not valid UTF-8.",
                exception);
        }

        return RecordingRuleJsonCodec.Deserialize(json);
    }

    public static async ValueTask<RecordingRuleSet?> LoadIfExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return await LoadAsync(
                path,
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void TryDeleteTemporaryFile(string path)
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
