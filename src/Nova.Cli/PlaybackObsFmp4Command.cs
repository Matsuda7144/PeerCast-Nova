using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal static class PlaybackObsFmp4Command
{
    public const string Argument = "--fmp4-obs";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine($"Usage: Nova.Cli {Argument}");
            return 2;
        }

        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var channelId = new ChannelId(Guid.NewGuid());
            var sessionId = new ChannelSessionId(Guid.NewGuid());
            await using var output = new PlaybackFmp4HttpStreamHost(channelId, sessionId);
            using var publisher = new PlaybackObsFmp4Publisher(sessionId, output);
            var options = new ObsRtmpIngestOptions { Port = 0 };
            await using var ingest = ObsRtmpIngestHost.CreateForEncodedMedia(channelId, sessionId, publisher, options);
            ingest.Start(stop.Token);
            Console.WriteLine($"FMP4_OBS_SERVER rtmp://127.0.0.1:{ingest.LocalEndPoint.Port}/{options.Application}");
            Console.WriteLine($"FMP4_OBS_STREAM_KEY {options.StreamName}");
            Console.WriteLine($"FMP4_HTTP_URL {output.StreamUri}");

            var result = await ingest.Completion.ConfigureAwait(false);
            if (result.EndReason == ObsRtmpIngestEndReason.Cancelled && stop.IsCancellationRequested)
            {
                return 0;
            }

            if (result.EndReason != ObsRtmpIngestEndReason.PublisherStopped)
            {
                Console.Error.WriteLine(publisher.Rejection ?? $"OBS fMP4 ingest ended with {result.EndReason}.");
                return 1;
            }

            publisher.Complete();
            var httpCompleted = await output.CompleteAsync().WaitAsync(stop.Token).ConfigureAwait(false);
            Console.WriteLine($"FMP4_OBS_COMPLETE Fragments={publisher.PublishedFragments} HttpResponseComplete={httpCompleted}");
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or SocketException)
        {
            Console.Error.WriteLine($"Could not serve OBS fMP4: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
