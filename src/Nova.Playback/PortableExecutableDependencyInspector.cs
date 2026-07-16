using System.Text;

namespace PeerCast.Nova.Playback;

internal sealed class PortableExecutableDependencyInspector :
    IPlaybackDecoderNativeDependencyInspector
{
    private const int MaximumSections = 96;
    private const int MaximumImports = 256;
    private const int MaximumDependencyNameBytes = 128;

    public bool TryGetDependencies(
        Stream image,
        out IReadOnlyList<string> dependencies)
    {
        dependencies = Array.Empty<string>();
        if (!image.CanRead || !image.CanSeek || image.Length < 256)
        {
            return false;
        }

        try
        {
            using var reader = new BinaryReader(image, Encoding.ASCII, leaveOpen: true);
            image.Position = 0;
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return false;
            }

            image.Position = 0x3c;
            var peOffset = reader.ReadUInt32();
            if (peOffset > image.Length - 24)
            {
                return false;
            }

            image.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return false;
            }

            _ = reader.ReadUInt16();
            var sectionCount = reader.ReadUInt16();
            if (sectionCount is 0 or > MaximumSections)
            {
                return false;
            }

            image.Position += 12;
            var optionalHeaderSize = reader.ReadUInt16();
            image.Position += 2;
            var optionalHeaderStart = image.Position;
            if (optionalHeaderStart + optionalHeaderSize > image.Length || optionalHeaderSize < 112)
            {
                return false;
            }

            var magic = reader.ReadUInt16();
            var dataDirectoryOffset = magic switch
            {
                0x10b => 96,
                0x20b => 112,
                _ => 0,
            };
            if (dataDirectoryOffset == 0 || optionalHeaderSize < dataDirectoryOffset + 16)
            {
                return false;
            }

            image.Position = optionalHeaderStart + dataDirectoryOffset + 8;
            var importRva = reader.ReadUInt32();
            var importSize = reader.ReadUInt32();
            if (importRva == 0 || importSize < 20)
            {
                return false;
            }

            image.Position = optionalHeaderStart + optionalHeaderSize;
            var sections = new Section[sectionCount];
            for (var index = 0; index < sections.Length; index++)
            {
                image.Position += 8;
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var rawOffset = reader.ReadUInt32();
                image.Position += 16;
                sections[index] = new Section(
                    virtualAddress,
                    virtualSize,
                    rawOffset,
                    rawSize);
            }

            if (!TryMapRva(importRva, sections, image.Length, out var importOffset))
            {
                return false;
            }

            var result = new List<string>();
            var descriptorCount = Math.Min(
                MaximumImports,
                checked((int)(importSize / 20)));
            for (var index = 0; index < descriptorCount; index++)
            {
                var descriptorOffset = importOffset + (index * 20L);
                if (descriptorOffset > image.Length - 20)
                {
                    return false;
                }

                image.Position = descriptorOffset;
                var originalFirstThunk = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                var nameRva = reader.ReadUInt32();
                var firstThunk = reader.ReadUInt32();
                if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
                {
                    dependencies = result.AsReadOnly();
                    return result.Count > 0;
                }

                if (!TryMapRva(nameRva, sections, image.Length, out var nameOffset) ||
                    !TryReadAsciiName(reader, nameOffset, out var name))
                {
                    return false;
                }

                result.Add(name);
            }

            return false;
        }
        catch (Exception exception) when (exception is
            EndOfStreamException or
            IOException or
            ArgumentException)
        {
            return false;
        }
    }

    private static bool TryMapRva(
        uint rva,
        IReadOnlyList<Section> sections,
        long imageLength,
        out long offset)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress)
            {
                var delta = rva - section.VirtualAddress;
                if (delta < section.VirtualSize && delta < section.RawSize)
                {
                    offset = section.RawOffset + delta;
                    return offset >= 0 && offset < imageLength;
                }
            }
        }

        offset = 0;
        return false;
    }

    private static bool TryReadAsciiName(
        BinaryReader reader,
        long offset,
        out string name)
    {
        reader.BaseStream.Position = offset;
        Span<byte> bytes = stackalloc byte[MaximumDependencyNameBytes];
        var length = 0;
        while (length < bytes.Length)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                name = Encoding.ASCII.GetString(bytes[..length]);
                return length > 0;
            }

            if (value > 0x7f)
            {
                name = string.Empty;
                return false;
            }

            bytes[length++] = value;
        }

        name = string.Empty;
        return false;
    }

    private readonly record struct Section(
        uint VirtualAddress,
        uint VirtualSize,
        uint RawOffset,
        uint RawSize);
}
