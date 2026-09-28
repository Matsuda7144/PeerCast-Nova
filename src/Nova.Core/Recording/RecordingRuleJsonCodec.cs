using System.Text.Json;
using System.Text.Json.Serialization;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Versioned JSON codec for recording rule sets.
/// Filesystem ownership intentionally remains outside this boundary.
/// </summary>
public static class RecordingRuleJsonCodec
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };

    public static string Serialize(RecordingRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        var document = new StorageDocument
        {
            Version = CurrentSchemaVersion,
            Rules = ruleSet.Rules
                .Select(ToStorageRule)
                .ToArray(),
        };

        return JsonSerializer.Serialize(
            document,
            SerializerOptions);
    }

    public static RecordingRuleSet Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        StorageDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StorageDocument>(
                json,
                SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "Recording rule JSON is malformed or contains unsupported fields.",
                exception);
        }

        if (document is null)
        {
            throw new FormatException(
                "Recording rule JSON does not contain a document.");
        }

        if (document.Version != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Recording rule schema version {document.Version} is not supported.");
        }

        if (document.Rules is null)
        {
            throw new FormatException(
                "Recording rule JSON must contain a rules array.");
        }

        try
        {
            return new RecordingRuleSet(
                document.Rules.Select(FromStorageRule));
        }
        catch (ArgumentException exception)
        {
            throw new FormatException(
                "Recording rule JSON contains an invalid rule.",
                exception);
        }
    }

    private static StorageRule ToStorageRule(RecordingRule rule)
    {
        return new StorageRule
        {
            Id = rule.Id.ToString("N"),
            Name = rule.Name,
            Action = ToStorageAction(rule.Action),
            Priority = rule.Priority,
            Enabled = rule.IsEnabled,
            Criteria = ToStorageCriteria(rule.Criteria),
        };
    }

    private static StorageCriteria ToStorageCriteria(
        RecordingRuleCriteria criteria)
    {
        return new StorageCriteria
        {
            MatchAll = criteria.IsMatchAll,
            ChannelId = criteria.ChannelId?.ToString(),
            Name = ToStoragePattern(criteria.Name),
            Genre = ToStoragePattern(criteria.Genre),
            Description = ToStoragePattern(criteria.Description),
            ContentType = ToStoragePattern(criteria.ContentType),
            StreamType = ToStoragePattern(criteria.StreamType),
            Comment = ToStoragePattern(criteria.Comment),
            MinimumBitrateKbps = criteria.MinimumBitrateKbps,
            MaximumBitrateKbps = criteria.MaximumBitrateKbps,
        };
    }

    private static StoragePattern? ToStoragePattern(
        RecordingTextPattern? pattern)
    {
        return pattern is null
            ? null
            : new StoragePattern
            {
                Value = pattern.Value,
                Mode = ToStorageMatchMode(pattern.Mode),
            };
    }

    private static RecordingRule FromStorageRule(StorageRule stored)
    {
        if (string.IsNullOrWhiteSpace(stored.Id) ||
            !Guid.TryParseExact(stored.Id, "N", out var id) ||
            id == Guid.Empty)
        {
            throw new ArgumentException(
                "Stored recording rule identifier is invalid.");
        }

        if (string.IsNullOrWhiteSpace(stored.Name))
        {
            throw new ArgumentException(
                "Stored recording rule name is required.");
        }

        if (stored.Criteria is null)
        {
            throw new ArgumentException(
                "Stored recording rule criteria are required.");
        }

        return new RecordingRule(
            id,
            stored.Name,
            FromStorageAction(stored.Action),
            FromStorageCriteria(stored.Criteria),
            stored.Priority,
            stored.Enabled);
    }

    private static RecordingRuleCriteria FromStorageCriteria(
        StorageCriteria stored)
    {
        var hasConfiguredCriteria =
            stored.ChannelId is not null ||
            stored.Name is not null ||
            stored.Genre is not null ||
            stored.Description is not null ||
            stored.ContentType is not null ||
            stored.StreamType is not null ||
            stored.Comment is not null ||
            stored.MinimumBitrateKbps is not null ||
            stored.MaximumBitrateKbps is not null;

        if (stored.MatchAll == true)
        {
            if (hasConfiguredCriteria)
            {
                throw new ArgumentException(
                    "Stored match-all criteria cannot also define conditions.");
            }

            return RecordingRuleCriteria.MatchAll;
        }

        if (stored.MatchAll == false && !hasConfiguredCriteria)
        {
            throw new ArgumentException(
                "Stored criteria must define a condition or set matchAll to true.");
        }

        // Schema v1 documents written before explicit MatchAll used an empty
        // criteria object for the same intentional catch-all semantics.
        if (stored.MatchAll is null && !hasConfiguredCriteria)
        {
            return RecordingRuleCriteria.MatchAll;
        }

        ChannelId? channelId = null;

        if (stored.ChannelId is not null)
        {
            if (!Guid.TryParseExact(
                    stored.ChannelId,
                    "N",
                    out var channelGuid) ||
                channelGuid == Guid.Empty)
            {
                throw new ArgumentException(
                    "Stored channel identifier is invalid.");
            }

            channelId = new ChannelId(channelGuid);
        }

        return new RecordingRuleCriteria(
            channelId,
            FromStoragePattern(stored.Name),
            FromStoragePattern(stored.Genre),
            FromStoragePattern(stored.Description),
            FromStoragePattern(stored.ContentType),
            FromStoragePattern(stored.StreamType),
            FromStoragePattern(stored.Comment),
            stored.MinimumBitrateKbps,
            stored.MaximumBitrateKbps);
    }

    private static RecordingTextPattern? FromStoragePattern(
        StoragePattern? stored)
    {
        if (stored is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(stored.Value))
        {
            throw new ArgumentException(
                "Stored recording text pattern is empty.");
        }

        return new RecordingTextPattern(
            stored.Value,
            FromStorageMatchMode(stored.Mode));
    }

    private static string ToStorageAction(
        RecordingRuleAction action)
    {
        return action switch
        {
            RecordingRuleAction.Record => "record",
            RecordingRuleAction.Ignore => "ignore",
            _ => throw new ArgumentOutOfRangeException(
                nameof(action)),
        };
    }

    private static RecordingRuleAction FromStorageAction(
        string? action)
    {
        return action switch
        {
            "record" => RecordingRuleAction.Record,
            "ignore" => RecordingRuleAction.Ignore,
            _ => throw new ArgumentException(
                "Stored recording rule action is invalid."),
        };
    }

    private static string ToStorageMatchMode(
        RecordingTextMatchMode mode)
    {
        return mode switch
        {
            RecordingTextMatchMode.Exact => "exact",
            RecordingTextMatchMode.Contains => "contains",
            RecordingTextMatchMode.Regex => "regex",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode)),
        };
    }

    private static RecordingTextMatchMode FromStorageMatchMode(
        string? mode)
    {
        return mode switch
        {
            "exact" => RecordingTextMatchMode.Exact,
            "contains" => RecordingTextMatchMode.Contains,
            "regex" => RecordingTextMatchMode.Regex,
            _ => throw new ArgumentException(
                "Stored recording text match mode is invalid."),
        };
    }

    private sealed class StorageDocument
    {
        public int Version { get; init; }

        public StorageRule[]? Rules { get; init; }
    }

    private sealed class StorageRule
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Action { get; init; }

        public int Priority { get; init; }

        public bool Enabled { get; init; }

        public StorageCriteria? Criteria { get; init; }
    }

    private sealed class StorageCriteria
    {
        public bool? MatchAll { get; init; }

        public string? ChannelId { get; init; }

        public StoragePattern? Name { get; init; }

        public StoragePattern? Genre { get; init; }

        public StoragePattern? Description { get; init; }

        public StoragePattern? ContentType { get; init; }

        public StoragePattern? StreamType { get; init; }

        public StoragePattern? Comment { get; init; }

        public int? MinimumBitrateKbps { get; init; }

        public int? MaximumBitrateKbps { get; init; }
    }

    private sealed class StoragePattern
    {
        public string? Value { get; init; }

        public string? Mode { get; init; }
    }
}
