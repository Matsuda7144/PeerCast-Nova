namespace PeerCast.Nova.Core.Media;

/// <summary>
/// Bounded AAC-LC grammar shared with the fMP4 muxer. Accepts indexed or explicit
/// rates and only the SBR-absent sync extension, with exact zero byte alignment.
/// Callers apply their own rate, channel and frame-length profile.
/// </summary>
public static class AacLcAudioSpecificConfigParser
{
    public const int MaximumConfigurationBytes = 8;

    public static bool TryParse(
        ReadOnlySpan<byte> config,
        out int sampleRate,
        out int channels,
        out bool shortFrame)
    {
        sampleRate = 0;
        channels = 0;
        shortFrame = false;
        if (config.Length is < 2 or > MaximumConfigurationBytes)
        {
            return false;
        }

        var bitOffset = 0;
        if (!TryReadBits(config, ref bitOffset, 5, out var audioObjectType) ||
            audioObjectType != 2 ||
            !TryReadBits(config, ref bitOffset, 4, out var frequencyIndex))
        {
            return false;
        }
        if (frequencyIndex == 15)
        {
            if (!TryReadBits(config, ref bitOffset, 24, out var explicitRate))
            {
                return false;
            }

            sampleRate = checked((int)explicitRate);
        }
        else
        {
            ReadOnlySpan<int> rates = [96_000, 88_200, 64_000, 48_000,
                44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025,
                8_000, 7_350];
            if (frequencyIndex >= rates.Length)
            {
                return false;
            }

            sampleRate = rates[checked((int)frequencyIndex)];
        }

        if (!TryReadBits(config, ref bitOffset, 4, out var channelConfiguration))
        {
            return false;
        }

        channels = channelConfiguration switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,
            _ => 0,
        };
        if (channels == 0 || sampleRate == 0 ||
            !TryReadBits(config, ref bitOffset, 1, out var frameLengthFlag) ||
            !TryReadBits(config, ref bitOffset, 1, out var dependsOnCoreCoder) ||
            !TryReadBits(config, ref bitOffset, 1, out var extensionFlag) ||
            dependsOnCoreCoder != 0 || extensionFlag != 0)
        {
            return false;
        }

        shortFrame = frameLengthFlag != 0;

        if (bitOffset == config.Length * 8)
        {
            return true;
        }

        // The retained AAC-LC fixture carries the backward-compatible SBR
        // marker with sbrPresentFlag == 0. Preserve only that no-SBR form and
        // its seven zero bits at the end of the byte-sized ASC; reject every
        // other continuation without enabling SBR or PS.
        return IsSbrAbsentSyncExtension(config, bitOffset);
    }

    private static bool IsSbrAbsentSyncExtension(
        ReadOnlySpan<byte> config,
        int bitOffset)
    {
        if (config.Length * 8 - bitOffset != 24)
        {
            return false;
        }

        var extensionBitOffset = bitOffset;
        return TryReadBits(config, ref extensionBitOffset, 11, out var syncExtensionType) &&
            syncExtensionType == 0x2b7 &&
            TryReadBits(config, ref extensionBitOffset, 5, out var extensionAudioObjectType) &&
            extensionAudioObjectType == 5 &&
            TryReadBits(config, ref extensionBitOffset, 1, out var sbrPresentFlag) &&
            sbrPresentFlag == 0 &&
            TryReadBits(config, ref extensionBitOffset, 7, out var byteAlignmentBits) &&
            byteAlignmentBits == 0 &&
            extensionBitOffset == config.Length * 8;
    }

    private static bool TryReadBits(
        ReadOnlySpan<byte> bytes,
        ref int bitOffset,
        int count,
        out uint value)
    {
        value = 0;
        if (count is <= 0 or > 24 || bitOffset > bytes.Length * 8 - count)
        {
            return false;
        }

        for (var bit = 0; bit < count; bit++)
        {
            var absolute = bitOffset++;
            value = (value << 1) |
                (uint)((bytes[absolute / 8] >> (7 - absolute % 8)) & 1);
        }

        return true;
    }
}
