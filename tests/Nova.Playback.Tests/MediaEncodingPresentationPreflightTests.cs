using System.Buffers.Binary;
using System.Text;

namespace PeerCast.Nova.Playback.Tests;

public sealed class MediaEncodingPresentationPreflightTests
{
    private static readonly string[] InitializationBoxTypes = ["ftyp", "moov"];
    private static readonly string[] FragmentBoxTypes = ["moof", "mdat"];

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void SplitterAcceptsTheExplicitFragmentInventory(int fragmentCount)
    {
        // Layout-only boxes: no retained encoded configuration or media bytes.
        var input = CreateLayout(fragmentCount);
        var (initialization, fragments) =
            PlaybackFmp4PhysicalPresentationAcceptanceTests.SplitFixture(input, fragmentCount);
        Assert.Equal(16, initialization.Length);
        Assert.Equal(fragmentCount, fragments.Count);
        Assert.All(fragments, fragment => Assert.Equal(16, fragment.Length));
        Assert.Equal(input, initialization.Concat(fragments.SelectMany(static bytes => bytes)).ToArray());
    }

    [Fact]
    public void LegacyFixtureDefaultStillRequiresTwoFragments()
    {
        Assert.Equal(2,
            PlaybackFmp4PhysicalPresentationAcceptanceTests.SplitFixture(CreateLayout(2)).MediaFragments.Count);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            PlaybackFmp4PhysicalPresentationAcceptanceTests.SplitFixture(CreateLayout(4)));
    }

    [Fact]
    public void SplitterRejectsMissingMdat()
    {
        var input = CreateLayout(4);
        Encoding.ASCII.GetBytes("free").CopyTo(input, input.Length - 4);
        Assert.Throws<InvalidDataException>(() =>
            PlaybackFmp4PhysicalPresentationAcceptanceTests.SplitFixture(input, 4));
    }

    private static byte[] CreateLayout(int fragmentCount)
    {
        var types = InitializationBoxTypes
            .Concat(Enumerable.Range(0, fragmentCount).SelectMany(static _ => FragmentBoxTypes))
            .ToArray();
        var bytes = new byte[types.Length * 8];
        for (var index = 0; index < types.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index * 8), 8);
            Encoding.ASCII.GetBytes(types[index]).CopyTo(bytes, index * 8 + 4);
        }

        return bytes;
    }
}
