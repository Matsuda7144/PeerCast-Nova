using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal static class PlaybackFmp4HttpFixtureCommand
{
    public const string Argument = "--fmp4-http-fixture";
    private const string JoinAfterFirstFragment = "--join-after-first-fragment";
    private const int MaximumFixtureBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan FragmentInterval = TimeSpan.FromMilliseconds(500);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(args[1]) ||
            (args.Length == 3 && args[2] != JoinAfterFirstFragment))
        {
            Console.Error.WriteLine($"Usage: {Argument} <file> [{JoinAfterFirstFragment}]");
            return 2;
        }

        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var bytes = await ReadFixtureAsync(args[1], stop.Token).ConfigureAwait(false);
            var sessionId = new ChannelSessionId(Guid.NewGuid());
            await using var host = new PlaybackFmp4HttpStreamHost(new ChannelId(Guid.NewGuid()), sessionId);
            var offset = PublishNext(host, sessionId, bytes, 0);
            if (args.Length == 3)
            {
                offset = PublishNext(host, sessionId, bytes, offset);
            }

            if (offset == bytes.Length)
            {
                throw new InvalidDataException("The fixture has no media fragment after the selected start.");
            }

            Console.WriteLine($"FMP4_HTTP_URL {host.StreamUri}");
            if (!await host.WaitForClientAsync(stop.Token).ConfigureAwait(false))
            {
                return 1;
            }

            using var timer = new PeriodicTimer(FragmentInterval);
            while (offset < bytes.Length)
            {
                offset = PublishNext(host, sessionId, bytes, offset);
                if (offset < bytes.Length)
                {
                    await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false);
                }
            }

            if (!await host.CompleteAsync().WaitAsync(stop.Token).ConfigureAwait(false))
            {
                Console.Error.WriteLine("The local client did not receive a complete HTTP response.");
                return 1;
            }

            Console.WriteLine("FMP4_HTTP_FIXTURE_COMPLETE");
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Console.Error.WriteLine("The fMP4 fixture preview was stopped or reached its time limit.");
            return 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or SocketException)
        {
            Console.Error.WriteLine($"Could not serve the fMP4 fixture: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static async Task<byte[]> ReadFixtureAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        if (input.Length is <= 0 or > MaximumFixtureBytes)
        {
            throw new InvalidDataException("The fMP4 fixture must be between 1 byte and 16 MiB.");
        }

        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    private static int PublishNext(
        PlaybackFmp4HttpStreamHost host, ChannelSessionId sessionId, ReadOnlySpan<byte> bytes, int offset)
    {
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(bytes, offset, out var first) !=
            PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            first is null ||
            PlaybackIsoBmffBoxSequenceReader.ReadNext(bytes, first.NextOffset, out var second) !=
            PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            second is null ||
            !host.TryPublishSegment(sessionId, bytes[offset..second.NextOffset]))
        {
            throw new InvalidDataException($"Invalid fMP4 segment at byte {offset}.");
        }

        return second.NextOffset;
    }
}
