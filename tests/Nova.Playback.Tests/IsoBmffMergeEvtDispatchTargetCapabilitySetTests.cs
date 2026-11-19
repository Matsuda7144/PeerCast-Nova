using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetTests
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
            Result.Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetFactory
                .TryCreate([video, audio], out var capabilitySet));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySet>(
                capabilitySet);
        Assert.Collection(
            created.Capabilities,
            first =>
            {
                Assert.Equal(video, first.Declaration);
                Assert.Equal(video.TargetId, first.TargetId);
                Assert.Equal(video.MediaKind, first.MediaKind);
                Assert.Equal(
                    video.MaximumPayloadByteCount,
                    first.MaximumPayloadByteCount);
            },
            second => Assert.Equal(audio, second.Declaration));
    }

    [Fact]
    public void TryCreate_RejectsMissingAndExcessiveDeclarations()
    {
        AssertResult(Result.MissingDeclaration, null);
        AssertResult(Result.MissingDeclaration, []);
        AssertResult(
            Result.DeclarationLimitExceeded,
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
            Result.InvalidTargetId,
            [Declaration(Guid.Empty, PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            Result.UnsupportedMediaKind,
            [Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1)]);
        AssertResult(
            Result.InvalidMaximumPayloadByteCount,
            [Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 0)]);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateTargetOrMediaKind()
    {
        var targetId = Guid.NewGuid();
        AssertResult(
            Result.DuplicateTargetId,
            [
                Declaration(targetId, PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(targetId, PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            Result.DuplicateMediaKind,
            [
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
            ]);
    }

    private static void AssertResult(
        Result expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilitySetFactory
                .TryCreate(declarations, out var capabilitySet));
        Assert.Null(capabilitySet);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchTargetCapabilityDeclaration
            Declaration(
                Guid targetId,
                PlaybackIsoBmffHandlerKind mediaKind,
                int maximumPayloadByteCount) =>
        new(targetId, mediaKind, maximumPayloadByteCount);
}
