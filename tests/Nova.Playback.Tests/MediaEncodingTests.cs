using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PeerCast.Nova.MediaEncoding;

namespace PeerCast.Nova.Playback.Tests;

public sealed class MediaEncodingTests
{
    [Theory]
    [InlineData(0, 48)]
    [InlineData(63, 48)]
    [InlineData(64, 47)]
    [InlineData(4098, 48)]
    public void H264ConfigurationRejectsInvalidDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new H264EncoderConfiguration(width, height));

    [Theory]
    [InlineData(0, 300000, 30)]
    [InlineData(121, 300000, 30)]
    [InlineData(30, 0, 30)]
    [InlineData(30, 300000, 0)]
    public void H264ConfigurationRejectsInvalidTimingOrRate(int fps, int bitRate, int interval) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new H264EncoderConfiguration(64, 48, fps, bitRate, interval));

    [Theory]
    [InlineData(44100, 2, 128000)]
    [InlineData(48000, 1, 128000)]
    [InlineData(48000, 2, 0)]
    public void AacConfigurationRejectsUnsupportedFormatOrRate(int rate, int channels, int bitRate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AacEncoderConfiguration(rate, channels, bitRate));

    [Fact]
    public void VideoInputCopiesCallerAndPublicMemory()
    {
        var source = new byte[64 * 48 * 3 / 2];
        var frame = new RawYuv420VideoFrame(64, 48, 0, source);
        source[0] = 1;
        Assert.True(MemoryMarshal.TryGetArray(frame.Data, out var exposed));
        exposed.Array![exposed.Offset] = 2;
        Assert.Equal(0, frame.Data.Span[0]);
    }

    [Fact]
    public void AudioInputCopiesCallerAndPublicMemory()
    {
        var source = new float[2048];
        var frame = new RawPcmAudioFrame(0, source);
        source[0] = 0.5f;
        Assert.True(MemoryMarshal.TryGetArray(frame.Samples, out var exposed));
        exposed.Array![exposed.Offset] = 0.8f;
        Assert.Equal(0, frame.Samples.Span[0]);
    }

    [Fact]
    public void RawFramesRejectInvalidLengthsTimestampsAndPcm()
    {
        Assert.Throws<ArgumentException>(() => new RawYuv420VideoFrame(64, 48, 0, new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawYuv420VideoFrame(64, 48, -1, new byte[4608]));
        Assert.Throws<ArgumentException>(() => new RawPcmAudioFrame(0, new float[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RawPcmAudioFrame(-1, new float[2048]));
        var samples = new float[2048];
        samples[0] = float.NaN;
        Assert.Throws<ArgumentException>(() => new RawPcmAudioFrame(0, samples));
        samples[0] = 2;
        Assert.Throws<ArgumentException>(() => new RawPcmAudioFrame(0, samples));
    }

    [Fact]
    public void EncodedOutputCopiesInputAndPublicMemoryAndRetainsPriming()
    {
        byte[] payload = [1, 2, 3];
        var sample = new MediaEncodedSample(-1024, -1024, 1024, false, payload);
        payload[0] = 9;
        Assert.True(MemoryMarshal.TryGetArray(sample.Payload, out var exposed));
        exposed.Array![exposed.Offset] = 8;
        Assert.Equal(1, sample.Payload.Span[0]);
        Assert.Equal(-1024, sample.DecodeTimestamp);
        Assert.Throws<ArgumentException>(() => new MediaEncodedSample(0, 0, 0, false, payload));
    }

    [Fact]
    public void AnnexBConfigurationAndPacketsAreExplicitlyNormalizedAndOwned()
    {
        // Artificial syntax-only vectors, unrelated to any retained media fixture.
        byte[] extradata = [0, 0, 0, 1, 0x67, 66, 0, 30, 0x80,
            0, 0, 1, 0x68, 0x80];
        var framing = AvcEncoderOutputFraming.Create(extradata);
        var configuration = framing.Configuration;
        extradata[5] = 99;
        Assert.Equal(66, configuration.Profile);
        Assert.Equal(4, configuration.NalLengthSize);
        Assert.Equal("Annex B", framing.NativePacketFraming);
        byte[] packet = [0, 0, 1, 0x65, 0x88, 0x84];
        Assert.Equal(new byte[] { 0, 0, 0, 3, 0x65, 0x88, 0x84 },
            framing.NormalizePacket(packet, isKeyFrame: true));
        Assert.True(MemoryMarshal.TryGetArray(configuration.ConfigurationRecord, out var exposed));
        exposed.Array![exposed.Offset] = 0;
        Assert.Equal(1, configuration.ConfigurationRecord.Span[0]);
        Assert.True(MemoryMarshal.TryGetArray(configuration.SequenceParameterSets[0], out var sps));
        sps.Array![sps.Offset] = 0;
        Assert.Equal(0x67, configuration.SequenceParameterSets[0].Span[0]);
    }

    [Fact]
    public void AvccConfigurationAndShortNalLengthsNormalizeToFour()
    {
        var original = AvcEncoderOutputFraming.Create(new byte[] {
            0, 0, 1, 0x67, 66, 0, 30, 0x80, 0, 0, 1, 0x68, 0x80 });
        var avcc = original.Configuration.ConfigurationRecord.ToArray();
        avcc[4] = 0xfc; // one-byte input lengths
        var framing = AvcEncoderOutputFraming.Create(avcc);
        var normalized = framing.NormalizePacket(new byte[] { 3, 0x65, 0x88, 0x84 }, true);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(normalized));
        Assert.Equal(0xff, framing.Configuration.ConfigurationRecord.Span[4]);
    }

    [Fact]
    public void FramingRejectsTruncationInconsistentKeyframeAndConfigurationChanges()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AvcEncoderOutputFraming.Create(new byte[] { 1, 66, 0, 30, 0xff, 0xe1, 0xff }));
        var framing = AvcEncoderOutputFraming.Create(new byte[] {
            0, 0, 1, 0x67, 66, 0, 30, 0x80, 0, 0, 1, 0x68, 0x80 });
        Assert.Throws<InvalidOperationException>(() =>
            framing.NormalizePacket(new byte[] { 0, 0, 1, 0x65, 0x80 }, false));
        Assert.Throws<InvalidOperationException>(() =>
            framing.NormalizePacket(new byte[] { 0, 0, 1, 0x67, 66, 0, 31, 0x80,
                0, 0, 1, 0x65, 0x80 }, true));
        Assert.Throws<InvalidOperationException>(() => framing.NormalizePacket(new byte[] { 5, 6 }, true));
        var avcc = framing.Configuration.ConfigurationRecord.ToArray();
        avcc[4] = 0xfe;
        Assert.Throws<InvalidOperationException>(() => AvcEncoderOutputFraming.Create(avcc));
    }

    [Fact]
    public void SyntheticSourcesAreDeterministicAndVideoChangesEachFrame()
    {
        Assert.Equal(SyntheticEncodingSource.Video(0).Data.ToArray(), SyntheticEncodingSource.Video(0).Data.ToArray());
        Assert.NotEqual(SyntheticEncodingSource.Video(0).Data.ToArray(), SyntheticEncodingSource.Video(1).Data.ToArray());
        Assert.Equal(SyntheticEncodingSource.Audio(0).Samples.ToArray(), SyntheticEncodingSource.Audio(0).Samples.ToArray());
        Assert.Contains(SyntheticEncodingSource.Audio(0).Samples.ToArray(), sample => Math.Abs(sample) > 0.1f);
    }
}

internal static class SyntheticEncodingSource
{
    public const int Width = 64;
    public const int Height = 48;
    public const int FramesPerSecond = 30;
    public const int VideoFrameCount = 96;
    public const int AudioFrameCount = 150; // 153600 samples = 3.2 seconds

    public static RawYuv420VideoFrame Video(int index)
    {
        var data = new byte[Width * Height * 3 / 2];
        Array.Fill(data, (byte)128, Width * Height, Width * Height / 2);
        var bar = index * 2 % Width;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                data[y * Width + x] = (byte)((x - bar + Width) % Width < 10
                    ? 235 : 32 + ((x / 8 + y / 8 + index / 3) % 2) * 80);
            }
        }

        return new(Width, Height, index, data);
    }

    public static RawPcmAudioFrame Audio(int index)
    {
        var samples = new float[2048];
        var pts = index * 1024L;
        for (var sample = 0; sample < 1024; sample++)
        {
            var value = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * (pts + sample) / 48_000));
            samples[sample * 2] = value;
            samples[sample * 2 + 1] = value;
        }

        return new(pts, samples);
    }
}
