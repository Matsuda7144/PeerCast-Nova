namespace PeerCast.Nova.Network.Rtmp;

/// <summary>
/// Receives validated AVC and AAC-LC from a single OBS publishing session.
/// Calls are serialized. Configuration and sample bytes are borrowed for
/// the call and must be copied before any retention.
/// </summary>
public interface IObsRtmpEncodedMediaConsumer
{
    void Configure(ReadOnlySpan<byte> avcDecoderConfiguration, ReadOnlySpan<byte> aacAudioSpecificConfig);

    void AcceptAudio(uint timestampMilliseconds, ReadOnlySpan<byte> accessUnit);

    void AcceptVideo(
        uint decodeTimestampMilliseconds, int compositionOffsetMilliseconds,
        bool isIdr, ReadOnlySpan<byte> accessUnit);
}
