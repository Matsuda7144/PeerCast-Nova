using System.Globalization;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextRecordingHistoryRow(
    RecordingSessionTerminalFact Fact, string Result, string Channel, string Terminal,
    string Origin, string Filename, string Bytes, string Association, string Details)
{
    public Guid SessionId => Fact.Session.SessionId.Value;
}

internal sealed record GenesisRecordingHistoryPresentation(string Text, bool IsWarning, string WarningDetails = "");

internal static class GenesisRecordingHistoryListPresentation
{
    public static IReadOnlyList<GenesisNativeListColumnPresentation> Columns { get; } =
        Array.AsReadOnly(new GenesisNativeListColumnPresentation[]
        {
            new("結果", 150, GenesisChannelColumnAlignment.Left),
            new("チャンネル", 190, GenesisChannelColumnAlignment.Left),
            new("終端観測日時", 180, GenesisChannelColumnAlignment.Left),
            new("録画種別", 80, GenesisChannelColumnAlignment.Left),
            new("ファイル", 270, GenesisChannelColumnAlignment.Left),
            new("記録バイト数", 120, GenesisChannelColumnAlignment.Right),
            new("関連ファイル", 180, GenesisChannelColumnAlignment.Left),
        });

    public static string[] CreateRowValues(GenesisGuiNextRecordingHistoryRow row) =>
        [row.Result, row.Channel, row.Terminal, row.Origin, row.Filename, row.Bytes, row.Association];
}

/// <summary>Terminal facts and cached filesystem observations remain separate truth sources.</summary>
internal static class GenesisGuiNextRecordingHistoryProjection
{
    public const string UnavailableText = "録画履歴を確認できません";
    public const int FieldLimit = 4096;
    public const int DetailsLimit = 32768;
    private const string Truncation = "…（表示を省略）";

    public static string Bound(string? value, int limit = FieldLimit)
    {
        if (value is null) return "不明";
        if (value.Length <= limit) return value;
        var length = limit - Truncation.Length;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + Truncation;
    }

    public static IReadOnlyList<GenesisGuiNextRecordingHistoryRow> CreateRows(
        GenesisRecordingHistorySnapshot history, GenesisRecordingLibrarySnapshot library)
    {
        if (history.Entries.Count > GenesisRecordingHistoryReadModel.MaximumTerminalEntryCount ||
            history.Entries.Select(x => x.Session.SessionId.Value).Distinct().Count() != history.Entries.Count)
            throw new InvalidDataException("Invalid History identities.");
        var paths = new Dictionary<string, GenesisRecordingLibraryEntry>(GenesisRecordingLibraryKnownRootRegistry.PathComparer);
        foreach (var entry in library.Entries)
            if (Normalized(entry.Path) is { } path && !paths.TryAdd(path, entry))
                throw new InvalidDataException("Ambiguous Library path.");
        var pathCounts = new Dictionary<string, int>(GenesisRecordingLibraryKnownRootRegistry.PathComparer);
        foreach (var fact in history.Entries)
            if (Normalized(fact.WriteResult?.Path) is { } path)
                pathCounts[path] = pathCounts.GetValueOrDefault(path) + 1;
        // GetSnapshot already defines terminal UTC / Guid.CompareTo descending order.
        // Index paths once rather than normalizing the entire Library for each row.
        return Array.AsReadOnly(history.Entries.Select(fact => CreateRow(fact, library, paths, pathCounts)).ToArray());
    }

    private static string? Normalized(string? path) =>
        GenesisRecordingLibraryKnownRootRegistry.TryNormalize(path, out var value) ? value : null;

    private static GenesisGuiNextRecordingHistoryRow CreateRow(RecordingSessionTerminalFact fact,
        GenesisRecordingLibrarySnapshot library, Dictionary<string, GenesisRecordingLibraryEntry> paths,
        Dictionary<string, int> pathCounts)
    {
        var result = fact.Kind switch
        {
            RecordingSinkSessionLifecycleKind.Completed => fact.WriteResult?.CompletionKind switch
            {
                RecordingFileWriteCompletionKind.EndOfStream => "完了（入力終了）",
                RecordingFileWriteCompletionKind.Stopped => "完了（停止）",
                _ => throw new InvalidDataException("Invalid writer outcome."),
            },
            RecordingSinkSessionLifecycleKind.NoDecodableVideoStart => "映像開始なし",
            RecordingSinkSessionLifecycleKind.Faulted => "失敗",
            _ => throw new InvalidDataException("Invalid terminal outcome."),
        };
        var origin = fact.Session.Origin switch
        {
            RecordingSessionOrigin.Manual => "手動",
            RecordingSessionOrigin.AutomaticRule => "自動",
            _ => throw new InvalidDataException("Invalid recording origin."),
        };
        if (fact.AdmissionObservedAtUtc == default || fact.TerminalObservedAtUtc == default)
            throw new InvalidDataException("Invalid observation time.");
        var path = Normalized(fact.WriteResult?.Path);
        var entry = path is null ? null : paths.GetValueOrDefault(path);
        var linked = entry?.HistoryFacts.Any(x => x.Session.SessionId.Value == fact.Session.SessionId.Value) == true;
        var association = library.State == GenesisRecordingLibraryObservationState.NotObservedYet
            ? "未確認（Library未更新）"
            : entry is null ? "関連未確認（snapshot対象外）"
            : !linked ? "パス一致・履歴未反映"
            : entry.Availability switch
            {
                GenesisRecordingLibraryAvailability.Available => "利用可能（Library観測）",
                GenesisRecordingLibraryAvailability.Missing => "見つかりません（Library観測）",
                _ => "確認できません（Library観測）",
            };
        var channel = string.IsNullOrWhiteSpace(fact.ChannelName) ? "不明" : Bound(fact.ChannelName);
        var bytes = fact.WriteResult is { } write ? Bytes(write.BytesWritten) : "—";
        var samePathCount = path is null ? 0 : pathCounts.GetValueOrDefault(path);
        var details = new List<string>
        {
            "SessionId: " + fact.Session.SessionId.Value.ToString("N"),
            "ChannelId: " + fact.Session.ChannelId.Value.ToString("N"),
            "チャンネル: " + channel, "由来: " + origin,
            "RuleId: " + (fact.Session.AutomaticRuleId?.ToString("D") ?? "なし（手動）"),
            "受入観測日時: " + Utc(fact.AdmissionObservedAtUtc, precise: true),
            "終端観測日時: " + Utc(fact.TerminalObservedAtUtc, precise: true),
            "terminal Kind: " + fact.Kind,
            "writer completion: " + (fact.WriteResult?.CompletionKind.ToString() ?? "情報なし"),
            "確定パス: " + (fact.WriteResult is null ? "確定ファイル情報なし" : Bound(fact.WriteResult.Path)),
            "録画時の記録バイト数: " + bytes,
            "StreamType: " + Bound(fact.StreamType),
            "RequestedFileExtension（要求値・メディア未検証）: " + Bound(fact.RequestedFileExtension),
            "故障段階: " + (fact.Fault?.Stage.ToString() ?? "情報なし"),
            "ExceptionType: " + (fact.Fault is null ? "情報なし" : Bound(fact.Fault.ExceptionType)),
            "関連ファイル: " + association,
            "同パス参照の履歴: " + samePathCount.ToString(CultureInfo.InvariantCulture) + " 件",
            "Library revision（独立したcached観測）: " + library.Revision.ToString(CultureInfo.InvariantCulture),
            "Library coverage: " + (library.State == GenesisRecordingLibraryObservationState.NotObservedYet ? "未観測"
                : library.IsSnapshotTruncated || library.HasTruncatedRoots || library.ExcludedHistoryPathCount > 0
                    ? "一部のみ・snapshot対象外あり" : "snapshot保持範囲"),
            "Library対象外パス数: " + library.ExcludedHistoryPathCount.ToString(CultureInfo.InvariantCulture),
            "Library観測失敗: " + (entry?.Failure.ToString() ?? "情報なし"),
            "Libraryの履歴欠落: " + library.HistoryHasGap,
        };
        if (entry?.FileMetadata is { } metadata)
        {
            details.Add("Library観測サイズ: " + Bytes(metadata.Length));
            details.Add("Library観測更新日時: " + Utc(metadata.LastWriteTimeUtc, precise: true));
            details.Add("Library観測作成日時: " + (metadata.CreatedTimeUtc is { } created ? Utc(created, true) : "不明"));
            details.Add("Library観測属性: " + metadata.Attributes);
            if (fact.WriteResult is { } writer && metadata.Length != writer.BytesWritten)
                details.Add("現在の観測サイズは録画時の記録と異なります");
        }
        else details.Add("Library観測ファイル情報: 情報なし");
        return new(fact, result, channel, Utc(fact.TerminalObservedAtUtc), origin,
            fact.WriteResult is null ? "確定ファイル情報なし" : Bound(Path.GetFileName(fact.WriteResult.Path)),
            bytes, association, Bound(string.Join("\r\n", details), DetailsLimit));
    }

    public static GenesisRecordingHistoryPresentation Status(GenesisRecordingHistorySnapshot? history,
        GenesisRecordingHistoryDurabilitySnapshot? durability, bool unavailable)
    {
        var warnings = new List<string>();
        if (unavailable && history is not null) warnings.Add(UnavailableText);
        if (durability is null) warnings.Add("耐久性情報なし");
        else
        {
            if (durability.LoadStatus is not (GenesisRecordingHistoryLoadStatus.Available or GenesisRecordingHistoryLoadStatus.MissingFile))
                warnings.Add("履歴読込: " + durability.LoadStatus);
            if (durability.LastWriteStatus is GenesisRecordingHistoryWriteStatus.Failed or
                GenesisRecordingHistoryWriteStatus.Conflict or GenesisRecordingHistoryWriteStatus.Cancelled)
                warnings.Add("メタデータ保存: " + durability.LastWriteStatus);
            if (!durability.AutomaticPersistenceEnabled) warnings.Add("自動保存無効");
            if (durability.Pending) warnings.Add("メタデータ保存待ち（History＋KnownRoots）");
        }
        if (history?.EvictedEntryCount > 0) warnings.Add("保持範囲外 " + history.EvictedEntryCount.ToString(CultureInfo.InvariantCulture) + " 件");
        if (history?.PublicationFailureCount > 0) warnings.Add("履歴の記録失敗 " + history.PublicationFailureCount.ToString(CultureInfo.InvariantCulture) + " 件");
        var count = history is null ? UnavailableText : history.Entries.Count == 0
            ? "保持している録画履歴はありません"
            : "保持履歴 " + history.Entries.Count.ToString(CultureInfo.InvariantCulture) + " 件（上限4096件）";
        var summary = warnings.Count == 0 ? "" : " · " + warnings[0] + (warnings.Count > 1 ? "（ほか " + (warnings.Count - 1).ToString(CultureInfo.InvariantCulture) + " 項目）" : ""); return new(count + summary, unavailable || warnings.Count > 0, string.Join("\r\n", warnings));
    }

    public static string DurabilityDetails(GenesisRecordingHistoryDurabilitySnapshot? durability) =>
        durability is null ? "耐久性情報: 情報なし" :
        $"耐久性（History＋KnownRoots composite、個別行の保存証明ではありません）: load={durability.LoadStatus}, write={durability.LastWriteStatus}, failure={durability.Failure}, 自動保存={durability.AutomaticPersistenceEnabled}, persisted={durability.LastPersistedRevision}, latest={durability.LatestRevision}, pending={durability.Pending}";

    private static string Bytes(long value) => value.ToString("N0", CultureInfo.InvariantCulture) + " B";
    private static string Utc(DateTimeOffset value, bool precise = false) =>
        value.ToUniversalTime().ToString(precise ? "yyyy-MM-dd HH:mm:ss.fffffff 'UTC'" : "yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
