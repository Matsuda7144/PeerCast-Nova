using System.Text.Json;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

/// <summary>Application v2 document with read-only v1 compatibility, separate from runtime objects.</summary>
internal static class GenesisRecordingHistoryJsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
    };

    public static GenesisRecordingHistorySnapshot Deserialize(byte[] bytes) => DeserializeDocument(bytes).History;

    public static GenesisRecordingMetadataSnapshot DeserializeDocument(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        ValidateProperties(json.RootElement);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var number))
            throw new JsonException("Missing version.");
        if (number is not 1 and not 2) throw new HistoryVersionException();
        if (!json.RootElement.TryGetProperty("entries", out var array) || array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > GenesisRecordingHistoryReadModel.MaximumTerminalEntryCount)
            throw new JsonException("Invalid entry bounds.");
        if (number == 2 && (!json.RootElement.TryGetProperty("knownRoots", out var roots) ||
            roots.ValueKind != JsonValueKind.Array || roots.GetArrayLength() > GenesisRecordingLibraryKnownRootRegistry.MaximumRootCount))
            throw new JsonException("Invalid root bounds.");
        var document = JsonSerializer.Deserialize<HistoryDocument>(bytes, Options) ?? throw new JsonException("Null document.");
        if (document.Entries is null || document.Entries.Length > GenesisRecordingHistoryReadModel.MaximumTerminalEntryCount ||
            document.EvictedEntryCount < 0 || document.PublicationFailureCount < 0 ||
            (document.EvictedEntryCount == 0) != (document.EvictedThrough is null))
            throw new JsonException("Invalid bounds.");
        var entries = document.Entries.Select(FromDto).ToArray();
        if (entries.Select(x => x.Session.SessionId).Distinct().Count() != entries.Length)
            throw new JsonException("Duplicate session.");
        if (document.EvictedThrough is { } boundary)
        {
            ValidateUtc(boundary.TerminalObservedAtUtc);
            if (boundary.SessionId == Guid.Empty || entries.Any(x =>
                x.TerminalObservedAtUtc < boundary.TerminalObservedAtUtc ||
                (x.TerminalObservedAtUtc == boundary.TerminalObservedAtUtc && x.Session.SessionId.Value.CompareTo(boundary.SessionId) <= 0)))
                throw new JsonException("Invalid retention boundary.");
        }
        // v1 unknown optional properties remain opaque, including knownRoots.
        var knownRoots = number == 1 ? Array.Empty<GenesisRecordingLibraryKnownRoot>() :
            json.RootElement.GetProperty("knownRoots").Deserialize<Root[]>(Options)!.Select(FromRootDto).ToArray();
        if (knownRoots.Select(x => x.Path).Distinct(GenesisRecordingLibraryKnownRootRegistry.PathComparer).Count() != knownRoots.Length)
            throw new JsonException("Duplicate root identity.");
        return new(new(0, Array.AsReadOnly(entries), document.EvictedEntryCount, document.PublicationFailureCount,
            document.EvictedThrough is { } b ? new(b.TerminalObservedAtUtc, b.SessionId) : null),
            new(0, Array.AsReadOnly(knownRoots.OrderBy(x => x.Path, GenesisRecordingLibraryKnownRootRegistry.PathComparer).ToArray())));
    }

    public static byte[] Serialize(GenesisRecordingHistorySnapshot snapshot) =>
        Serialize(new GenesisRecordingMetadataSnapshot(snapshot, new(0, Array.Empty<GenesisRecordingLibraryKnownRoot>())));

    public static byte[] Serialize(GenesisRecordingMetadataSnapshot snapshot)
    {
        if (snapshot.KnownRoots.Count > GenesisRecordingLibraryKnownRootRegistry.MaximumRootCount ||
            snapshot.KnownRoots.Roots.Any(x => x is null || !GenesisRecordingLibraryKnownRootRegistry.IsValidProvenance(x.Provenance)))
            throw new JsonException("Invalid root snapshot.");
        var document = new Document
        {
            SchemaVersion = 2,
            EvictedEntryCount = snapshot.History.EvictedEntryCount,
            PublicationFailureCount = snapshot.History.PublicationFailureCount,
            EvictedThrough = snapshot.History.EvictedThrough is { } boundary ? new Boundary
            { TerminalObservedAtUtc = boundary.TerminalObservedAtUtc, SessionId = boundary.SessionId } : null,
            Entries = snapshot.History.Entries.Select(ToDto).ToArray(),
            KnownRoots = snapshot.KnownRoots.Roots.OrderBy(x => x.Path, GenesisRecordingLibraryKnownRootRegistry.PathComparer)
                .Select(x => new Root
                {
                    Path = x.Path,
                    Provenance = Enum.GetValues<GenesisRecordingRootProvenance>()
                    .Where(p => x.Provenance.HasFlag(p)).Select(p => p.ToString()).ToArray()
                }).ToArray(),
        };
        using var stream = new LimitedStream();
        JsonSerializer.Serialize(stream, document, Options);
        var bytes = stream.ToArray();
        _ = Deserialize(bytes); // Never write a document the same contract rejects.
        return bytes;
    }

    private static GenesisRecordingLibraryKnownRoot FromRootDto(Root root)
    {
        if (root is null || !GenesisRecordingLibraryKnownRootRegistry.TryNormalize(root.Path, out var normalized) ||
            !string.Equals(root.Path, normalized, StringComparison.Ordinal) || root.Provenance is null || root.Provenance.Length is < 1 or > 3)
            throw new JsonException("Invalid known root.");
        var provenance = (GenesisRecordingRootProvenance)0;
        foreach (var name in root.Provenance)
        {
            var value = ParseEnum<GenesisRecordingRootProvenance>(name);
            if (provenance.HasFlag(value)) throw new JsonException("Duplicate provenance.");
            provenance |= value;
        }
        return new(root.Path, provenance);
    }

    private static Entry ToDto(RecordingSessionTerminalFact fact) => new()
    {
        SessionId = fact.Session.SessionId.Value,
        ChannelId = fact.Session.ChannelId.Value,
        ChannelName = fact.ChannelName,
        StreamType = fact.StreamType,
        RequestedFileExtension = fact.RequestedFileExtension,
        Origin = fact.Session.Origin.ToString(),
        AutomaticRuleId = fact.Session.AutomaticRuleId,
        AdmissionObservedAtUtc = fact.AdmissionObservedAtUtc,
        TerminalObservedAtUtc = fact.TerminalObservedAtUtc,
        Kind = fact.Kind.ToString(),
        WriteResult = fact.WriteResult is { } w ? new Writer
        { Path = w.Path, BytesWritten = w.BytesWritten, CompletionKind = w.CompletionKind.ToString() } : null,
        Fault = fact.Fault is { } f ? new Fault { Stage = f.Stage.ToString(), ExceptionType = f.ExceptionType } : null,
    };

    private static RecordingSessionTerminalFact FromDto(Entry entry)
    {
        if (entry is null) throw new JsonException("Null entry.");
        ValidateUtc(entry.AdmissionObservedAtUtc);
        ValidateUtc(entry.TerminalObservedAtUtc);
        var origin = ParseEnum<RecordingSessionOrigin>(entry.Origin);
        if ((origin == RecordingSessionOrigin.Manual && entry.AutomaticRuleId is not null) ||
            (origin == RecordingSessionOrigin.AutomaticRule && (entry.AutomaticRuleId is null || entry.AutomaticRuleId == Guid.Empty)))
            throw new JsonException("Invalid origin.");
        var session = origin == RecordingSessionOrigin.Manual
            ? RecordingSinkSession.Manual(new(entry.SessionId), new(entry.ChannelId))
            : new RecordingSinkSession(new(entry.SessionId), new(entry.ChannelId), entry.AutomaticRuleId!.Value);
        RecordingFileWriteResult? writer = null;
        if (entry.WriteResult is { } w)
        {
            // Lexical only. No stat/open/hash of the historical media path.
            if (!GenesisRecordingOutputDirectorySettings.TryNormalize(w.Path, out var normalized) ||
                !string.Equals(normalized, w.Path, StringComparison.Ordinal))
                throw new JsonException("Invalid final path.");
            writer = RecordingFileWriteResult.FromFinalizedFacts(w.Path, w.BytesWritten,
                ParseEnum<RecordingFileWriteCompletionKind>(w.CompletionKind));
        }
        RecordingFaultDescriptor? fault = null;
        if (entry.Fault is { } f)
        {
            if (string.IsNullOrWhiteSpace(f.ExceptionType) || f.ExceptionType.Length > 128 ||
                f.ExceptionType.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '`'))
                throw new JsonException("Invalid safe type.");
            fault = RecordingFaultDescriptor.FromSafeDetails(ParseEnum<RecordingFailureStage>(f.Stage), f.ExceptionType);
        }
        return new(session, entry.ChannelName, entry.StreamType, entry.RequestedFileExtension,
            entry.AdmissionObservedAtUtc, entry.TerminalObservedAtUtc,
            ParseEnum<RecordingSinkSessionLifecycleKind>(entry.Kind), writer, fault);
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        value is not null && Enum.TryParse<T>(value, out var result) && Enum.IsDefined(result) && value == result.ToString()
            ? result : throw new JsonException("Unknown enum.");

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value == default) throw new JsonException("Invalid UTC observation.");
        // UTC clock adjustments may reverse observation order; no media-duration inference.
    }

    private static void ValidateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate property.");
                ValidateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateProperties(item);
    }

    private class HistoryDocument
    {
        public required int SchemaVersion { get; init; }
        public required long EvictedEntryCount { get; init; }
        public required long PublicationFailureCount { get; init; }
        public required Boundary? EvictedThrough { get; init; }
        public required Entry[] Entries { get; init; }
    }
    private sealed class Document : HistoryDocument
    {
        public required Root[] KnownRoots { get; init; }
    }
    private sealed class Root
    {
        public required string Path { get; init; }
        public required string[] Provenance { get; init; }
    }
    private sealed class Boundary
    {
        public required DateTimeOffset TerminalObservedAtUtc { get; init; }
        public required Guid SessionId { get; init; }
    }
    private sealed class Entry
    {
        public required Guid SessionId { get; init; }
        public required Guid ChannelId { get; init; }
        public required string? ChannelName { get; init; }
        public required string? StreamType { get; init; }
        public required string? RequestedFileExtension { get; init; }
        public required string Origin { get; init; }
        public required Guid? AutomaticRuleId { get; init; }
        public required DateTimeOffset AdmissionObservedAtUtc { get; init; }
        public required DateTimeOffset TerminalObservedAtUtc { get; init; }
        public required string Kind { get; init; }
        public required Writer? WriteResult { get; init; }
        public required Fault? Fault { get; init; }
    }
    private sealed class Writer
    {
        public required string Path { get; init; }
        public required long BytesWritten { get; init; }
        public required string CompletionKind { get; init; }
    }
    private sealed class Fault
    {
        public required string Stage { get; init; }
        public required string ExceptionType { get; init; }
    }
    private sealed class LimitedStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > GenesisRecordingHistoryStore.MaximumByteCount) throw new HistorySizeException();
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > GenesisRecordingHistoryStore.MaximumByteCount) throw new HistorySizeException();
            // MemoryStream's subclass span fallback dispatches to Write(byte[]).
            // Call the non-virtual base array implementation to avoid recursion.
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
        }
    }
}

internal sealed class HistoryVersionException : Exception;
internal sealed class HistorySizeException : Exception;
