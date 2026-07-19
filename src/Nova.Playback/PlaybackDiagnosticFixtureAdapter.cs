namespace PeerCast.Nova.Playback;

public enum PlaybackDiagnosticFixtureAdapterResult
{
    Completed,
    InvalidConfiguration,
    FixtureRejected,
    Faulted,
    Cancelled,
}

public readonly record struct PlaybackDiagnosticEncodedPacket(
    BoundedDiagnosticPlaybackCodec Codec,
    long PresentationTimestamp,
    long Duration,
    ReadOnlyMemory<byte> Payload);

public sealed record PlaybackDiagnosticEncodedInput(
    PlaybackDiagnosticEncodedPacket Video,
    PlaybackDiagnosticEncodedPacket Audio);

/// <summary>
/// Diagnostic-only adapter from the bounded 0.0.8e fixture into the encoded
/// packet shape required by the RuntimeDecode phase. It performs no native call,
/// decoder activation, retry, seeking, or packet loop.
/// </summary>
public static class PlaybackDiagnosticFixtureAdapter
{
    public static PlaybackDiagnosticFixtureAdapterResult TryLoad(
        Func<Stream>? streamFactory,
        CancellationToken cancellationToken,
        out PlaybackDiagnosticEncodedInput? input)
    {
        input = null;
        if (streamFactory is null)
            return PlaybackDiagnosticFixtureAdapterResult.InvalidConfiguration;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = streamFactory();
            if (stream is null)
                return PlaybackDiagnosticFixtureAdapterResult.InvalidConfiguration;

            cancellationToken.ThrowIfCancellationRequested();
            var result = BoundedDiagnosticPlaybackFixtureFormat.TryRead(stream, out var fixture);
            if (result != BoundedDiagnosticPlaybackFixtureResult.Completed || fixture is null)
                return PlaybackDiagnosticFixtureAdapterResult.FixtureRejected;

            cancellationToken.ThrowIfCancellationRequested();
            input = new(
                new(
                    fixture.Video.Codec,
                    fixture.Video.PresentationTimestamp,
                    fixture.Video.Duration,
                    fixture.Video.Payload),
                new(
                    fixture.Audio.Codec,
                    fixture.Audio.PresentationTimestamp,
                    fixture.Audio.Duration,
                    fixture.Audio.Payload));
            return PlaybackDiagnosticFixtureAdapterResult.Completed;
        }
        catch (OperationCanceledException)
        {
            return PlaybackDiagnosticFixtureAdapterResult.Cancelled;
        }
        catch
        {
            return PlaybackDiagnosticFixtureAdapterResult.Faulted;
        }
    }
}
