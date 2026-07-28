namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDispatcherBackedProductionGenerationTraceContractTests
{
    [Fact]
    public void Dispatcher_backed_generation_forwards_delivery_observation_capture()
    {
        var source = File.ReadAllText(FindSource());

        Assert.Contains(
            "captureDeliveryObservations: options.CaptureDeliveryObservations",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            Count(
                source,
                "captureDeliveryObservations: options.CaptureDeliveryObservations"));
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Nova.Playback",
                "PlaybackDispatcherBackedProductionGeneration.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the dispatcher-backed production generation source.");
    }

    private static int Count(string value, string expected)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(expected, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += expected.Length;
        }

        return count;
    }
}
