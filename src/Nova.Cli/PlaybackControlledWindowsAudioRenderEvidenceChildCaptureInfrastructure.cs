using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PeerCast.Nova.Cli;

internal readonly record struct PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions(
    string RuntimeDirectory,
    string FixturePath,
    string FixtureSha256,
    string EvidenceRecordPath,
    string? TerminalRecordPath,
    string FailureRecordPath,
    int MaximumDecodeOperations,
    int MaximumDeliveryOperations,
    int FixturePacketBytes,
    int ObservationHoldMilliseconds);

internal interface IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
{
    ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        CancellationToken cancellationToken);
}

internal interface IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter
{
    ValueTask PromoteAsync(
        string destination,
        string content,
        CancellationToken cancellationToken);
}

internal sealed class SystemPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter :
    IPlaybackControlledWindowsAudioRenderEvidenceRecordPromoter
{
    public async ValueTask PromoteAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The record directory is unavailable.");
        var temporary = Path.Combine(
            directory,
            $".pcn-audio-render-capture-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                    temporary,
                    content,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, destination);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
            }
        }
    }
}

internal sealed class SystemPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor :
    IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor
{
    public async ValueTask<PlaybackControlledWindowsAudioChildProcessResult> ExecuteAsync(
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false,
        };
        AddArguments(startInfo, runnerAssemblyPath, options);
        return await SystemPlaybackControlledWindowsAudioChildProcessExecutor.ExecuteProcessAsync(
                startInfo,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void AddArguments(
        ProcessStartInfo startInfo,
        string runnerAssemblyPath,
        PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerAssemblyPath);
        Add(startInfo, runnerAssemblyPath);
        Add(startInfo, PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument);
        Add(startInfo, "--runtime-directory");
        Add(startInfo, options.RuntimeDirectory);
        Add(startInfo, "--controlled-fixture");
        Add(startInfo, options.FixturePath);
        Add(startInfo, "--fixture-sha256");
        Add(startInfo, options.FixtureSha256);
        Add(startInfo, "--output-kind");
        Add(startInfo, "audio");
        Add(startInfo, "--max-decode-operations");
        AddInvariant(startInfo, options.MaximumDecodeOperations);
        Add(startInfo, "--max-delivery-operations");
        AddInvariant(startInfo, options.MaximumDeliveryOperations);
        Add(startInfo, "--fixture-packet-bytes");
        AddInvariant(startInfo, options.FixturePacketBytes);
        Add(startInfo, "--observation-hold-ms");
        AddInvariant(startInfo, options.ObservationHoldMilliseconds);
    }

    private static void Add(ProcessStartInfo startInfo, string value) =>
        startInfo.ArgumentList.Add(value);

    private static void AddInvariant(ProcessStartInfo startInfo, int value) =>
        Add(startInfo, value.ToString(CultureInfo.InvariantCulture));
}
