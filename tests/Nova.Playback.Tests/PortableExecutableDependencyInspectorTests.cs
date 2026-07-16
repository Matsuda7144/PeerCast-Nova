namespace PeerCast.Nova.Playback.Tests;

public sealed class PortableExecutableDependencyInspectorTests
{
    [Fact]
    public void TryGetDependencies_ReadsBoundedImportTableWithoutClosingStream()
    {
        using var image = CreateImage("kernel32.dll", includeTerminator: true);
        var inspector = new PortableExecutableDependencyInspector();

        var result = inspector.TryGetDependencies(image, out var dependencies);

        Assert.True(result);
        Assert.Equal(["kernel32.dll"], dependencies);
        Assert.True(image.CanRead);
    }

    [Fact]
    public void TryGetDependencies_RejectsImportTableWithoutBoundedTerminator()
    {
        using var image = CreateImage("unverified.dll", includeTerminator: false);
        var inspector = new PortableExecutableDependencyInspector();

        var result = inspector.TryGetDependencies(image, out var dependencies);

        Assert.False(result);
        Assert.Empty(dependencies);
    }

    private static MemoryStream CreateImage(string dependency, bool includeTerminator)
    {
        const int peOffset = 0x80;
        const int optionalHeaderSize = 0xf0;
        const int sectionRawOffset = 0x200;
        const int sectionRva = 0x1000;
        const int nameOffset = sectionRawOffset + 0x40;
        var bytes = new byte[0x400];
        using var image = new MemoryStream(bytes, writable: true);
        using (var writer = new BinaryWriter(image, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((ushort)0x5a4d);
            image.Position = 0x3c;
            writer.Write(peOffset);
            image.Position = peOffset;
            writer.Write(0x00004550u);
            writer.Write((ushort)0x8664);
            writer.Write((ushort)1);
            image.Position += 12;
            writer.Write((ushort)optionalHeaderSize);
            writer.Write((ushort)0x2022);
            var optionalHeaderStart = image.Position;
            writer.Write((ushort)0x20b);
            image.Position = optionalHeaderStart + 112 + 8;
            writer.Write((uint)sectionRva);
            writer.Write(includeTerminator ? 40u : 20u);
            image.Position = optionalHeaderStart + optionalHeaderSize;
            writer.Write(new byte[8]);
            writer.Write((uint)0x200);
            writer.Write((uint)sectionRva);
            writer.Write((uint)0x200);
            writer.Write((uint)sectionRawOffset);
            writer.Write(new byte[16]);
            image.Position = sectionRawOffset;
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)(sectionRva + 0x40));
            writer.Write(1u);
            image.Position = nameOffset;
            writer.Write(System.Text.Encoding.ASCII.GetBytes(dependency));
            writer.Write((byte)0);
        }

        return new MemoryStream(bytes, writable: false);
    }
}
