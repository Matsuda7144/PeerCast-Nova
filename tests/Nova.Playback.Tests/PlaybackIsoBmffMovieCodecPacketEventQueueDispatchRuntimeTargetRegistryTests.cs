namespace PeerCast.Nova.Playback.Tests;

public sealed class
    PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryTests
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
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .Created,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryFactory
                .TryCreate([video, audio], out var registry));
        var created = Assert.IsType<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistry>(
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
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .MissingDeclaration,
            null);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .DeclarationLimitExceeded,
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
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .InvalidTargetId,
            [Declaration(Guid.Empty, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .InvalidRuntimeInstanceId,
            [Declaration(Guid.NewGuid(), Guid.Empty, PlaybackIsoBmffHandlerKind.Video, 1)]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .UnsupportedMediaKind,
            [Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Other, 1)]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .InvalidGeneration,
            [Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 0)]);
    }

    [Fact]
    public void TryCreate_RejectsDuplicateIdentitiesAndMediaKind()
    {
        var targetId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .DuplicateTargetId,
            [
                Declaration(targetId, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(targetId, Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .DuplicateRuntimeInstanceId,
            [
                Declaration(Guid.NewGuid(), runtimeId, PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), runtimeId, PlaybackIsoBmffHandlerKind.Audio, 1),
            ]);
        AssertResult(
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
                .DuplicateMediaKind,
            [
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 1),
                Declaration(Guid.NewGuid(), Guid.NewGuid(), PlaybackIsoBmffHandlerKind.Video, 2),
            ]);
    }

    private static void AssertResult(
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryCreationResult
            expected,
        IReadOnlyList<
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration>?
            declarations)
    {
        Assert.Equal(
            expected,
            PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetRegistryFactory
                .TryCreate(declarations, out var registry));
        Assert.Null(registry);
    }

    private static
        PlaybackIsoBmffMovieCodecPacketEventQueueDispatchRuntimeTargetDeclaration
        Declaration(
            Guid targetId,
            Guid runtimeInstanceId,
            PlaybackIsoBmffHandlerKind mediaKind,
            ulong generation) =>
        new(targetId, runtimeInstanceId, mediaKind, generation);
}
