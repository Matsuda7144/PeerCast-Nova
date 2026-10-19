namespace PeerCast.Nova.App;

public enum GenesisChannelSortDirection
{
    Ascending,
    Descending,
}

public sealed record GenesisChannelSort(
    GenesisChannelColumn Column,
    GenesisChannelSortDirection Direction);

internal static class GenesisChannelListOrdering
{
    public static IReadOnlyList<GenesisChannelItem> Apply(
        IEnumerable<GenesisChannelItem> channels,
        GenesisChannelSort? sort)
    {
        ArgumentNullException.ThrowIfNull(channels);
        var snapshot = channels.ToArray();
        if (sort is null || snapshot.Length < 2)
        {
            return Array.AsReadOnly(snapshot);
        }

        return Array.AsReadOnly(
            snapshot.OrderBy(
                    static channel => channel,
                    new ChannelComparer(sort))
                .ToArray());
    }

    private sealed class ChannelComparer : IComparer<GenesisChannelItem>
    {
        private readonly GenesisChannelSort _sort;

        public ChannelComparer(GenesisChannelSort sort)
        {
            _sort = sort;
        }

        public int Compare(GenesisChannelItem? left, GenesisChannelItem? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            return _sort.Column switch
            {
                GenesisChannelColumn.ChannelName => CompareText(left.Name, right.Name),
                GenesisChannelColumn.Details =>
                    CompareText(left.DetailsText, right.DetailsText),
                GenesisChannelColumn.ListenerRelay => CompareListenerRelay(left, right),
                GenesisChannelColumn.Bitrate =>
                    CompareNullable(left.BitrateKbps, right.BitrateKbps),
                GenesisChannelColumn.Uptime => CompareNullable(left.Uptime, right.Uptime),
                GenesisChannelColumn.Type => CompareText(left.TypeText, right.TypeText),
                GenesisChannelColumn.YellowPage =>
                    CompareText(left.YellowPageText, right.YellowPageText),
                _ => 0,
            };
        }

        private int CompareListenerRelay(
            GenesisChannelItem left,
            GenesisChannelItem right)
        {
            var listenerComparison = CompareNullable(left.Listeners, right.Listeners);
            return listenerComparison != 0
                ? listenerComparison
                : CompareNullable(left.Relays, right.Relays);
        }

        private int CompareText(string left, string right) =>
            ApplyDirection(StringComparer.CurrentCultureIgnoreCase.Compare(left, right));

        private int CompareNullable<T>(T? left, T? right)
            where T : struct, IComparable<T>
        {
            if (left is null && right is null)
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            return ApplyDirection(left.Value.CompareTo(right.Value));
        }

        private int ApplyDirection(int comparison) =>
            _sort.Direction == GenesisChannelSortDirection.Descending
                ? -comparison
                : comparison;
    }
}
