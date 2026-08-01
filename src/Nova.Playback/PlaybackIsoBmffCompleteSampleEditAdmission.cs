using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffCompleteSampleEditAdmissionResult
{
    Admitted,
    MissingMovieOverlapSet,
    InvalidMovieOverlap,
    PartialSampleRejected,
}

public sealed class PlaybackIsoBmffCompleteSampleEditAdmission
{
    internal PlaybackIsoBmffCompleteSampleEditAdmission(
        PlaybackIsoBmffSamplePresentationTime sample,
        ReadOnlyCollection<PlaybackIsoBmffSampleEditMovieOverlap> appearances)
    {
        Sample = sample;
        Appearances = appearances;
    }

    public PlaybackIsoBmffSamplePresentationTime Sample { get; }

    public IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap> Appearances
    {
        get;
    }
}

public static class PlaybackIsoBmffCompleteSampleEditAdmissionPolicy
{
    public static PlaybackIsoBmffCompleteSampleEditAdmissionResult Admit(
        PlaybackIsoBmffSampleEditMovieOverlapSet? movieOverlapSet,
        out PlaybackIsoBmffCompleteSampleEditAdmission? admission)
    {
        admission = null;
        if (movieOverlapSet is null)
        {
            return PlaybackIsoBmffCompleteSampleEditAdmissionResult
                .MissingMovieOverlapSet;
        }

        var appearances = new List<PlaybackIsoBmffSampleEditMovieOverlap>();
        foreach (var overlap in movieOverlapSet.Overlaps)
        {
            if (overlap.Denominator == 0 ||
                overlap.MovieStartNumerator >=
                    overlap.MovieEndNumeratorExclusive)
            {
                return PlaybackIsoBmffCompleteSampleEditAdmissionResult
                    .InvalidMovieOverlap;
            }

            if (overlap.ContainsCompleteSample)
            {
                appearances.Add(overlap);
            }
        }

        if (appearances.Count == 0)
        {
            return PlaybackIsoBmffCompleteSampleEditAdmissionResult
                .PartialSampleRejected;
        }

        admission = new(movieOverlapSet.Sample, appearances.AsReadOnly());
        return PlaybackIsoBmffCompleteSampleEditAdmissionResult.Admitted;
    }
}
