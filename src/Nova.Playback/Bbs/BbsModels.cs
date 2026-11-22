namespace PeerCast.Nova.Playback.Bbs;

public enum BbsBoardKind
{
    Shitaraba,
    Jpnkn,
}

public sealed record BbsTarget(
    BbsBoardKind Kind,
    string BaseUrl,
    string? Category,
    string Board,
    string? ThreadKey)
{
    public string BoardUrl => Kind switch
    {
        BbsBoardKind.Shitaraba => $"{BaseUrl}/{Category}/{Board}/",
        BbsBoardKind.Jpnkn => $"{BaseUrl}/{Board}/",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public string SubjectUrl => $"{BoardUrl}subject.txt";

    public string? ThreadUrl => string.IsNullOrEmpty(ThreadKey)
        ? null
        : Kind switch
        {
            BbsBoardKind.Shitaraba => $"{BaseUrl}/bbs/read.cgi/{Category}/{Board}/{ThreadKey}/",
            BbsBoardKind.Jpnkn => $"{BaseUrl}/test/read.cgi/{Board}/{ThreadKey}/",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };

    public string? GetRawModeUrl(int fromNumber)
    {
        if (Kind != BbsBoardKind.Shitaraba || string.IsNullOrEmpty(ThreadKey))
        {
            return null;
        }

        var start = Math.Max(1, fromNumber);
        return $"{BaseUrl}/bbs/rawmode.cgi/{Category}/{Board}/{ThreadKey}/{start}-";
    }

    public string? GetDatUrl()
    {
        if (Kind != BbsBoardKind.Jpnkn || string.IsNullOrEmpty(ThreadKey))
        {
            return null;
        }

        return $"{BaseUrl}/{Board}/dat/{ThreadKey}.dat";
    }

    public string? GetWriteUrl()
    {
        if (string.IsNullOrEmpty(ThreadKey))
        {
            return null;
        }

        return Kind switch
        {
            BbsBoardKind.Shitaraba => $"{BaseUrl}/bbs/write.cgi/{Category}/{Board}/{ThreadKey}/",
            BbsBoardKind.Jpnkn => $"{BaseUrl}/test/bbs.cgi",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
    }

    public BbsTarget WithThread(string threadKey) => this with { ThreadKey = threadKey };
}

public sealed record BbsThreadSummary(
    string Key,
    string Title,
    int PostCount);

public sealed record BbsPost(
    int Number,
    string Name,
    string Mail,
    string Timestamp,
    string Id,
    string Body,
    string? ThreadTitle = null)
{
    public string FormatHeader()
    {
        var mailPart = string.IsNullOrEmpty(Mail) ? "[]" : $"[{Mail}]";
        var idPart = string.IsNullOrEmpty(Id) ? string.Empty : $" ID:{Id}";
        return $"{Number}: {Name} {mailPart} {Timestamp}{idPart}";
    }

    public string FormatCompactSingleLine(int maxBodyLength = 80)
    {
        var singleLine = Body
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
        if (singleLine.Length > maxBodyLength)
        {
            singleLine = string.Concat(singleLine.AsSpan(0, maxBodyLength), "…");
        }

        return $">>{Number} {singleLine}";
    }
}

public sealed record BbsPostRequest(
    string Name,
    string Mail,
    string Message);

public sealed record BbsFetchState(
    int ExistingPostCount,
    string? LastModified,
    long ExistingDatByteLength)
{
    public static BbsFetchState Initial { get; } = new(0, null, 0);
}

public sealed record BbsFetchResult(
    IReadOnlyList<BbsPost> AddedPosts,
    bool ResetExisting,
    bool NotModified,
    string? ThreadTitle,
    BbsFetchState NextState);

public sealed record BbsPostResult(
    bool Succeeded,
    string? ErrorMessage = null);

public sealed record BbsSessionSnapshot(
    BbsTarget Target,
    string? ThreadKey,
    string ThreadTitle,
    int PostCount,
    IReadOnlyList<BbsThreadSummary> Threads,
    IReadOnlyList<BbsPost> Posts,
    string? StatusMessage,
    bool IsLoading)
{
    public string FormatThreadBarText()
    {
        if (!string.IsNullOrWhiteSpace(ThreadTitle))
        {
            return $"[ {ThreadTitle} ] ({PostCount})";
        }

        if (!string.IsNullOrWhiteSpace(StatusMessage))
        {
            return $"[ {StatusMessage} ]";
        }

        return IsLoading ? "[ 掲示板を読み込み中... ]" : "[ スレッド未選択 ]";
    }
}
