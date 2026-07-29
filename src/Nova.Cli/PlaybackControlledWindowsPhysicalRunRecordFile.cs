using System.Text;

namespace PeerCast.Nova.Cli;

internal static class PlaybackControlledWindowsPhysicalRunRecordFile
{
    private const int MaximumRecordCharacterCount =
        PlaybackControlledWindowsAudioRenderEvidenceRecordCodec
            .MaximumRecordCharacterCount;
    private const int MaximumRecordByteCount =
        MaximumRecordCharacterCount * 4;

    internal static async ValueTask<string?> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaximumRecordByteCount)
            {
                return null;
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            return text.Length <= MaximumRecordCharacterCount
                ? text
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
