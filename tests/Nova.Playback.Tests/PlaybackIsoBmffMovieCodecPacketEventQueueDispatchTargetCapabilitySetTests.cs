namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetTests
{
    [Fact]
    public void TryCreate_OwnsBoundedCapabilityDeclarations()
    {
        var video = Declaration(
            Guid.NewGuid(),
            PlaybackIsoBmffHandlerKind.Video,
            1_024);
        var audio = Declaration(
            Guid.NewGuid(),
            PlaybackIsoBmffHandlerKind.Audio,
            2_048);

        Assert.Equal(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetFactory
                .TryCreate([video, audio], out var capabilitySet));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySet>(
                capabilitySet);
        Assert.Collection(
            created.Capabilities,
            first =>
            {
                Assert.Equal(video, first.Declaration);
                Assert.Equal(video.TargetId, first.TargetId);
                Assert.Equal(video.MediaKind, first.MediaKind);
                Assert.Equal(video.MaximumPayloadByteCount,
                    first.MaximumPayloadByteCount);
            },
            second => Assert.Equal(audio, second.Declaration));
    }

    [Fact]
    public void TryCreate_RejectsMissingAndExcessiveDeclarations()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .MissingDeclaration,
            null);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .MissingDeclaration,
            []);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .DeclarationLimitExceeded,
            [
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Audio, 1),
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1),
            ]);
    }

    [Fact]
    public void TryCreate_RejectsInvalidDeclarationFields()
    {
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .InvalidTargetId,
            [Declaration(Guid.Empty, PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .UnsupportedMediaKind,
            [Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1)]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .InvalidMaximumPayloadByteCount,
            [Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 0)]);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateTargetOrMediaKind()
    {
        var targetId = Guid.NewGuid();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .DuplicateTargetId,
            [
                Declaration(targetId, PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(targetId, PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
                .DuplicateMediaKind,
            [
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
            ]);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetCreationResult
            expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilitySetFactory
                .TryCreate(declarations, out var capabilitySet));
        Assert.Null(capabilitySet);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchTargetCapabilityDeclaration
        Declaration(
            Guid targetId,
            PlaybackIsoBmffHandlerKind mediaKind,
            int maximumPayloadByteCount) =>
        new(targetId, mediaKind, maximumPayloadByteCount);
}
