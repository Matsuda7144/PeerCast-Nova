using System.Buffers.Binary;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackIsoBmffEditDurationReconcilerTests
{
    [Fact]
    public void Reconcile_MatchingKnownDurationProjectsTrackIdentity()
    {
        var result = PlaybackIsoBmffEditDurationReconciler.Reconcile(
            ParseEditList(2, 3),
            ParseTrackHeader(7, 5),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult.Reconciled,
            result);
        Assert.Equal(
            new PlaybackIsoBmffReconciledEditDuration(7, 5, 2),
            reconciliation);
    }

    [Fact]
    public void Reconcile_EmptyListMatchesZeroTrackDuration()
    {
        var result = PlaybackIsoBmffEditDurationReconciler.Reconcile(
            ParseEditList(),
            ParseTrackHeader(1, 0),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult.Reconciled,
            result);
        Assert.Equal((ulong)0, reconciliation?.DurationUnits);
        Assert.Equal(0, reconciliation?.EditCount);
    }

    [Fact]
    public void Reconcile_MismatchedDurationFailsClosed()
    {
        var result = PlaybackIsoBmffEditDurationReconciler.Reconcile(
            ParseEditList(4),
            ParseTrackHeader(1, 5),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult.DurationMismatch,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_UnknownTrackDurationIsNotInferred()
    {
        var result = PlaybackIsoBmffEditDurationReconciler.Reconcile(
            ParseEditList(4),
            ParseTrackHeader(1, uint.MaxValue),
            out var reconciliation);

        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult
                .UnknownTrackDuration,
            result);
        Assert.Null(reconciliation);
    }

    [Fact]
    public void Reconcile_MissingInputsAreDistinct()
    {
        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult.MissingEditList,
            PlaybackIsoBmffEditDurationReconciler.Reconcile(
                null,
                ParseTrackHeader(1, 0),
                out var missingList));
        Assert.Null(missingList);

        Assert.Equal(
            PlaybackIsoBmffEditDurationReconciliationResult.MissingTrackHeader,
            PlaybackIsoBmffEditDurationReconciler.Reconcile(
                ParseEditList(),
                null,
                out var missingHeader));
        Assert.Null(missingHeader);
    }

    private static PlaybackIsoBmffEditListBox ParseEditList(
        params uint[] durations)
    {
        var box = new byte[16 + (durations.Length * 12)];
        WriteBoxHeader(box, 0x656C7374);
        BinaryPrimitives.WriteUInt32BigEndian(
            box.AsSpan(12, 4),
            (uint)durations.Length);
        for (var index = 0; index < durations.Length; index++)
        {
            var offset = 16 + (index * 12);
            BinaryPrimitives.WriteUInt32BigEndian(
                box.AsSpan(offset, 4),
                durations[index]);
            BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(offset + 4, 4), 0);
            BinaryPrimitives.WriteInt16BigEndian(box.AsSpan(offset + 8, 2), 1);
        }

        Assert.Equal(
            PlaybackIsoBmffEditListBoxParseResult.Parsed,
            PlaybackIsoBmffEditListBoxParser.Parse(box, out var editList));
        return Assert.IsType<PlaybackIsoBmffEditListBox>(editList);
    }

    private static PlaybackIsoBmffTrackHeaderIdentityBox ParseTrackHeader(
        uint trackId,
        uint duration)
    {
        var box = new byte[92];
        WriteBoxHeader(box, 0x746B6864);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(20, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(28, 4), duration);
        Assert.Equal(
            PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed,
            PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
                box,
                out var trackHeader));
        return Assert.IsType<PlaybackIsoBmffTrackHeaderIdentityBox>(trackHeader);
    }

    private static void WriteBoxHeader(byte[] box, uint type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(0, 4),
            (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4, 4), type);
    }
}
