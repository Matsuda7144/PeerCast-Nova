using System.Buffers.Binary;

namespace PeerCast.Nova.App.Tests;

public sealed class PublishingBroadcastIdStoreTests
{
    [Fact]
    public async Task LoadOrCreateOwner_PersistsAndRestoresNormalizedBroadcastId()
    {
        var root = Directory.CreateTempSubdirectory("nova-bcid-store-");
        try
        {
            var filePath = Path.Combine(root.FullName, "publishing-bcid.txt");

            Guid firstId;
            await using (var firstOwner = SystemGenesisGuiNextApplicationState.PublishingBroadcastIdStore.LoadOrCreateOwner(filePath))
            {
                firstId = firstOwner.BroadcastId;
                Assert.NotEqual(Guid.Empty, firstId);
                Assert.True(File.Exists(filePath));
                var persistedText = File.ReadAllText(filePath).Trim();
                Assert.Equal(firstId.ToString("N").ToUpperInvariant(), persistedText);
            }

            await using (var secondOwner = SystemGenesisGuiNextApplicationState.PublishingBroadcastIdStore.LoadOrCreateOwner(filePath))
            {
                Assert.Equal(firstId, secondOwner.BroadcastId);
            }

            File.WriteAllText(filePath, "not-a-valid-guid");
            await using (var recoveredOwner = SystemGenesisGuiNextApplicationState.PublishingBroadcastIdStore.LoadOrCreateOwner(filePath))
            {
                Assert.NotEqual(Guid.Empty, recoveredOwner.BroadcastId);
                var bytes = new byte[16];
                Assert.True(recoveredOwner.BroadcastId.TryWriteBytes(bytes, bigEndian: true, out _));
                Assert.Equal((byte)0, bytes[0]);
                _ = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                Assert.Equal(
                    recoveredOwner.BroadcastId.ToString("N").ToUpperInvariant(),
                    File.ReadAllText(filePath).Trim());
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
