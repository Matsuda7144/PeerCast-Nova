using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingAnnounceIsolationTests
{
    [Fact]
    public void ProductionApplicationHasNoReferenceToAnnounceWorkerOwnerOrTransport()
    {
        // Check actual compiled references: GUI save, YP refresh and Publishing Start
        // cannot create/use the new foundation through a production application call site.
        using var file = File.OpenRead(typeof(PublishingChannelPreferences).Assembly.Location);
        using var image = new PEReader(file);
        var metadata = image.GetMetadataReader();
        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            Assert.False(metadata.GetString(type.Name).StartsWith("PcpAnnounce", StringComparison.Ordinal),
                "Production application references the test-only announce foundation.");
        }
        Assert.False(typeof(PcpAnnounceOwner).IsPublic);
        Assert.False(typeof(PcpAnnounceLoopbackConnector).IsPublic);
        Assert.DoesNotContain(typeof(PcpAnnounceOwner).Assembly.GetCustomAttributesData(), a =>
            a.AttributeType.Name == "InternalsVisibleToAttribute" &&
            a.ConstructorArguments.Any(v => string.Equals(v.Value as string, "Nova.App", StringComparison.Ordinal)));
    }
}
