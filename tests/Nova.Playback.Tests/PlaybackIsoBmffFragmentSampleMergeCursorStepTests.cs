using Result = PeerCast.Nova.Playback.PlaybackIsoBmffFragmentSampleMergeCursorStepResult;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffFragmentSampleMergeCursorStepTests
{
    [Fact]
    public void Step_ProjectsExactSampleAndSuccessor()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(3);
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            1);

        var step = Step(cursor);

        Assert.Same(cursor, step.Cursor);
        Assert.Equal(1, step.Index);
        Assert.Equal(source.Samples[1], step.Sample);
        Assert.Same(source, step.NextCursor.Source);
        Assert.Equal(2, step.NextCursor.NextIndex);
        Assert.False(step.NextCursor.IsComplete);
    }

    [Fact]
    public void Step_FinalSampleCreatesCompleteSuccessor()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(2);
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            1);

        var step = Step(cursor);

        Assert.Equal(source.Samples[1], step.Sample);
        Assert.True(step.NextCursor.IsComplete);
        Assert.Equal(0, step.NextCursor.RemainingCount);
    }

    [Fact]
    public void Step_ReportsCompletedCursorWithoutStep()
    {
        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(1);
        var cursor = PlaybackIsoBmffFragmentSampleMergeCursorTests.Project(
            source,
            1);

        Assert.Equal(
            Result.Completed,
            PlaybackIsoBmffFragmentSampleMergeCursorStepper.Step(
                cursor,
                out var step));
        Assert.Null(step);
    }

    [Fact]
    public void Step_RejectsMissingAndInvalidCursor()
    {
        Assert.Equal(
            Result.InvalidInput,
            PlaybackIsoBmffFragmentSampleMergeCursorStepper.Step(
                null,
                out var missingStep));
        Assert.Null(missingStep);

        var source = PlaybackIsoBmffFragmentSampleMergeCursorTests.Merge(1);
        var invalid = new PlaybackIsoBmffFragmentSampleMergeCursor(source, 2);
        Assert.Equal(
            Result.InvalidCursor,
            PlaybackIsoBmffFragmentSampleMergeCursorStepper.Step(
                invalid,
                out var invalidStep));
        Assert.Null(invalidStep);
    }

    internal static PlaybackIsoBmffFragmentSampleMergeCursorStep Step(
        PlaybackIsoBmffFragmentSampleMergeCursor cursor)
    {
        Assert.Equal(
            Result.Stepped,
            PlaybackIsoBmffFragmentSampleMergeCursorStepper.Step(
                cursor,
                out var step));
        return Assert.IsType<PlaybackIsoBmffFragmentSampleMergeCursorStep>(step);
    }
}
