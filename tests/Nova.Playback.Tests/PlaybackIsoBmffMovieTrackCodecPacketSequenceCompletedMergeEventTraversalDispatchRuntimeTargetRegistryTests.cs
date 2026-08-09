using Result = PeerCast.Nova.Playback.PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryCreationResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryTests
{
    [Fact]
    public void TryCreate_OwnsOpaqueRuntimeRegistrations()
    {
        var video = Declaration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlaybackIsoBmffHandlerKind.Video,
            1);
        var audio = Declaration(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PlaybackIsoBmffHandlerKind.Audio,
            ulong.MaxValue);

        Assert.Equal(
            Result.Created,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryFactory
                .TryCreate([video, audio], out var registry));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistry>(
                registry);
        Assert.Collection(
            created.Registrations,
            first =>
            {
                Assert.Equal(video, first.Declaration);
                Assert.Equal(video.TargetId, first.TargetId);
                Assert.Equal(video.RuntimeInstanceId, first.RuntimeInstanceId);
                Assert.Equal(video.MediaKind, first.MediaKind);
                Assert.Equal(video.Generation, first.Generation);
            },
            second => Assert.Equal(audio, second.Declaration));
    }

    [Fact]
    public void TryCreate_RejectsMissingAndExcessiveDeclarations()
    {
        AssertResult(Result.MissingDeclaration, null);
        AssertResult(
            Result.DeclarationLimitExceeded,
            [
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Audio, 1),
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1),
            ]);
    }

    [Fact]
    public void TryCreate_RejectsInvalidFields()
    {
        AssertResult(
            Result.InvalidTargetId,
            [Declaration(Guid.Empty, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            Result.InvalidRuntimeInstanceId,
            [Declaration(Guid.NewGuid(), Guid.Empty, PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            Result.UnsupportedMediaKind,
            [Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1)]);
        AssertResult(
            Result.InvalidGeneration,
            [Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 0)]);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateIdentitiesAndMediaKind()
    {
        var targetId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        AssertResult(
            Result.DuplicateTargetId,
            [
                Declaration(targetId, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(targetId, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            Result.DuplicateRuntimeInstanceId,
            [
                Declaration(Guid.NewGuid(), runtimeId, PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), runtimeId, PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            Result.DuplicateMediaKind,
            [
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 2),
            ]);
    }

    private static void AssertResult(
        Result expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetRegistryFactory
                .TryCreate(declarations, out var registry));
        Assert.Null(registry);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalDispatchRuntimeTargetDeclaration
            Declaration(
                Guid targetId,
                Guid runtimeInstanceId,
                PlaybackIsoBmffHandlerKind mediaKind,
                ulong generation) =>
        new(targetId, runtimeInstanceId, mediaKind, generation);
}
