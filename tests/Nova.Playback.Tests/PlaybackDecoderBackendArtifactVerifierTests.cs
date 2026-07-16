using System.Security.Cryptography;
using System.Text;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackDecoderBackendArtifactVerifierTests
{
    [Fact]
    public async Task Verify_ExactDigestPassesWithoutClosingStream()
    {
        var bytes = Encoding.ASCII.GetBytes("external-backend-artifact");
        await using var stream = new MemoryStream(bytes);
        var manifest = CreateManifest(bytes);

        var result = await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
            stream,
            manifest,
            bytes.Length);

        Assert.Equal(PlaybackDecoderBackendArtifactVerification.Verified, result);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task Verify_DigestMismatchReturnsFixedResult()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        var manifest = CreateManifest([3, 2, 1]);

        var result = await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
            stream,
            manifest);

        Assert.Equal(PlaybackDecoderBackendArtifactVerification.DigestMismatch, result);
    }

    [Fact]
    public async Task Verify_SeekableOversizeRejectsBeforeReading()
    {
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var manifest = CreateManifest([1, 2, 3, 4]);

        var result = await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
            stream,
            manifest,
            maximumArtifactBytes: 3);

        Assert.Equal(PlaybackDecoderBackendArtifactVerification.SizeLimitExceeded, result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Verify_CancellationIsObserved()
    {
        await using var stream = new CancelingReadStream();
        var manifest = CreateManifest([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PlaybackDecoderBackendArtifactVerifier.VerifyAsync(
                stream,
                manifest,
                cancellationToken: cancellation.Token));
    }

    private static PlaybackDecoderBackendArtifactManifest CreateManifest(byte[] bytes)
    {
        return new PlaybackDecoderBackendArtifactManifest(
            "example.decoder",
            "1.0.0",
            "LGPL-2.1-or-later",
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private sealed class CancelingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromCanceled<int>(cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
