namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffGaSpecificConfigCompositionResult
{
    Composed,
    MissingPrefix,
    MissingProgramConfigElement,
    UnexpectedProgramConfigElement,
    ProgramConfigElementSourceMismatch,
    NeedMoreData,
}

public static class PlaybackIsoBmffGaSpecificConfigComposer
{
    public static PlaybackIsoBmffGaSpecificConfigCompositionResult Compose(
        PlaybackIsoBmffGaSpecificConfigPrefix? prefix,
        PlaybackIsoBmffProgramConfigElement? programConfigElement,
        out PlaybackIsoBmffGaSpecificConfig? config)
    {
        config = null;
        if (prefix is null)
        {
            return PlaybackIsoBmffGaSpecificConfigCompositionResult.MissingPrefix;
        }

        var startBitLength = prefix.ConsumedBitLength;
        if (prefix.RequiresProgramConfigElement)
        {
            if (programConfigElement is null)
            {
                return PlaybackIsoBmffGaSpecificConfigCompositionResult
                    .MissingProgramConfigElement;
            }

            if (!ReferenceEquals(
                    programConfigElement.Header.GaSpecificConfigPrefix,
                    prefix))
            {
                return PlaybackIsoBmffGaSpecificConfigCompositionResult
                    .ProgramConfigElementSourceMismatch;
            }

            startBitLength = programConfigElement.ConsumedBitLength;
        }
        else if (programConfigElement is not null)
        {
            return PlaybackIsoBmffGaSpecificConfigCompositionResult
                .UnexpectedProgramConfigElement;
        }

        var reader = new BitReader(
            prefix.AudioSpecificConfigHeader.ConfigurationBytes.Span,
            startBitLength);
        byte? layerNumber = null;
        if (prefix.AudioObjectType is 6 or 20)
        {
            if (!reader.TryRead(3, out var layerNumberValue))
            {
                return PlaybackIsoBmffGaSpecificConfigCompositionResult.NeedMoreData;
            }

            layerNumber = checked((byte)layerNumberValue);
        }

        byte? numberOfSubFrames = null;
        ushort? layerLength = null;
        bool? sectionDataResilienceFlag = null;
        bool? scalefactorDataResilienceFlag = null;
        bool? spectralDataResilienceFlag = null;
        bool? extensionFlag3 = null;
        if (prefix.ExtensionFlag)
        {
            if (prefix.AudioObjectType == 22)
            {
                if (!reader.TryRead(5, out var subFrames) ||
                    !reader.TryRead(11, out var length))
                {
                    return PlaybackIsoBmffGaSpecificConfigCompositionResult
                        .NeedMoreData;
                }

                numberOfSubFrames = checked((byte)subFrames);
                layerLength = checked((ushort)length);
            }

            if (prefix.AudioObjectType is 17 or 19 or 20 or 23)
            {
                if (!reader.TryReadBoolean(out var section) ||
                    !reader.TryReadBoolean(out var scalefactor) ||
                    !reader.TryReadBoolean(out var spectral))
                {
                    return PlaybackIsoBmffGaSpecificConfigCompositionResult
                        .NeedMoreData;
                }

                sectionDataResilienceFlag = section;
                scalefactorDataResilienceFlag = scalefactor;
                spectralDataResilienceFlag = spectral;
            }

            if (!reader.TryReadBoolean(out var flag3))
            {
                return PlaybackIsoBmffGaSpecificConfigCompositionResult.NeedMoreData;
            }

            extensionFlag3 = flag3;
        }

        config = new(
            prefix.AudioSpecificConfigHeader,
            prefix.ExplicitSbrPsExtension,
            prefix.AudioObjectType,
            prefix.FrameLengthFlag,
            prefix.DependsOnCoreCoder,
            prefix.CoreCoderDelay,
            prefix.ExtensionFlag,
            layerNumber,
            numberOfSubFrames,
            layerLength,
            sectionDataResilienceFlag,
            scalefactorDataResilienceFlag,
            spectralDataResilienceFlag,
            extensionFlag3,
            reader.Position);
        return PlaybackIsoBmffGaSpecificConfigCompositionResult.Composed;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _payload;
        internal BitReader(ReadOnlySpan<byte> payload, int position)
        {
            _payload = payload;
            Position = position;
        }

        internal int Position { get; private set; }
        internal bool TryReadBoolean(out bool value)
        {
            var result = TryRead(1, out var raw);
            value = raw != 0;
            return result;
        }

        internal bool TryRead(int count, out uint value)
        {
            value = 0;
            if (count is < 1 or > 32 || count > (_payload.Length * 8L) - Position)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var bit = Position + index;
                value = (value << 1) |
                    (uint)((_payload[bit / 8] >> (7 - (bit % 8))) & 1);
            }

            Position += count;
            return true;
        }
    }
}
