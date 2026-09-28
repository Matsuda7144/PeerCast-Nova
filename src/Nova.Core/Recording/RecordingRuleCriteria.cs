using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Conjunctive match criteria for one recording rule.
/// All configured conditions must match.
/// </summary>
/// <remarks>
/// Use <see cref="MatchAll"/> when a rule is intentionally global. The
/// parameterized constructor rejects an otherwise empty criteria set so a
/// catch-all rule cannot be created accidentally.
/// </remarks>
public sealed record RecordingRuleCriteria
{
    private RecordingRuleCriteria()
    {
        IsMatchAll = true;
    }

    public static RecordingRuleCriteria MatchAll { get; } = new();

    public RecordingRuleCriteria(
        ChannelId? channelId = null,
        RecordingTextPattern? name = null,
        RecordingTextPattern? genre = null,
        RecordingTextPattern? description = null,
        RecordingTextPattern? contentType = null,
        RecordingTextPattern? streamType = null,
        RecordingTextPattern? comment = null,
        int? minimumBitrateKbps = null,
        int? maximumBitrateKbps = null)
    {
        if (channelId is null &&
            name is null &&
            genre is null &&
            description is null &&
            contentType is null &&
            streamType is null &&
            comment is null &&
            minimumBitrateKbps is null &&
            maximumBitrateKbps is null)
        {
            throw new ArgumentException(
                "Use RecordingRuleCriteria.MatchAll for an intentional catch-all rule.");
        }

        if (minimumBitrateKbps is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumBitrateKbps));
        }

        if (maximumBitrateKbps is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBitrateKbps));
        }

        if (minimumBitrateKbps.HasValue &&
            maximumBitrateKbps.HasValue &&
            minimumBitrateKbps > maximumBitrateKbps)
        {
            throw new ArgumentException(
                "Minimum bitrate cannot exceed maximum bitrate.");
        }

        ChannelId = channelId;
        Name = name;
        Genre = genre;
        Description = description;
        ContentType = contentType;
        StreamType = streamType;
        Comment = comment;
        MinimumBitrateKbps = minimumBitrateKbps;
        MaximumBitrateKbps = maximumBitrateKbps;
        IsMatchAll = false;
    }

    public bool IsMatchAll { get; }

    public ChannelId? ChannelId { get; }

    public RecordingTextPattern? Name { get; }

    public RecordingTextPattern? Genre { get; }

    public RecordingTextPattern? Description { get; }

    public RecordingTextPattern? ContentType { get; }

    public RecordingTextPattern? StreamType { get; }

    public RecordingTextPattern? Comment { get; }

    public int? MinimumBitrateKbps { get; }

    public int? MaximumBitrateKbps { get; }

    internal bool Matches(RecordingCandidate candidate)
    {
        return Evaluate(candidate, failures: null);
    }

    internal IReadOnlyList<RecordingRuleCriterion> GetFailedCriteria(
        RecordingCandidate candidate)
    {
        var failures = new List<RecordingRuleCriterion>();
        _ = Evaluate(candidate, failures);
        return failures.ToArray();
    }

    private bool Evaluate(
        RecordingCandidate candidate,
        List<RecordingRuleCriterion>? failures)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (IsMatchAll)
        {
            return true;
        }

        var metadata = candidate.Metadata;
        var matches = true;

        if (ChannelId is not null &&
            ChannelId != candidate.ChannelId)
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.ChannelId);
            matches = false;
        }

        if (Name is not null &&
            !Name.IsMatch(metadata.Name))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.Name);
            matches = false;
        }

        if (Genre is not null &&
            !Genre.IsMatch(metadata.Genre))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.Genre);
            matches = false;
        }

        if (Description is not null &&
            !Description.IsMatch(metadata.Description))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.Description);
            matches = false;
        }

        if (ContentType is not null &&
            !ContentType.IsMatch(metadata.ContentType))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.ContentType);
            matches = false;
        }

        if (StreamType is not null &&
            !StreamType.IsMatch(metadata.StreamType))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.StreamType);
            matches = false;
        }

        if (Comment is not null &&
            !Comment.IsMatch(metadata.Comment))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.Comment);
            matches = false;
        }

        if (MinimumBitrateKbps is { } minimum &&
            (!metadata.BitrateKbps.HasValue ||
             metadata.BitrateKbps.Value < minimum))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.MinimumBitrateKbps);
            matches = false;
        }

        if (MaximumBitrateKbps is { } maximum &&
            (!metadata.BitrateKbps.HasValue ||
             metadata.BitrateKbps.Value > maximum))
        {
            if (failures is null)
            {
                return false;
            }

            failures.Add(RecordingRuleCriterion.MaximumBitrateKbps);
            matches = false;
        }

        return matches;
    }
}
