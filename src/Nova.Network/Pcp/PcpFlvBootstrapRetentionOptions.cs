namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Bounds the FLV bootstrap retained for a late local-player join.
/// </summary>
public sealed record PcpFlvBootstrapRetentionOptions
{
    public const int MaximumComponentBytesHardLimit = 16 * 1024 * 1024;
    public const int MaximumTotalBytesHardLimit = 16 * 1024 * 1024;

    public int MaximumMetadataTagBytes { get; init; } = 1024 * 1024;

    public int MaximumConfigurationTagBytes { get; init; } = 256 * 1024;

    public int MaximumTotalRetainedBytes { get; init; } = 2 * 1024 * 1024;

    public void Validate()
    {
        ValidateComponent(MaximumMetadataTagBytes, nameof(MaximumMetadataTagBytes));
        ValidateComponent(
            MaximumConfigurationTagBytes,
            nameof(MaximumConfigurationTagBytes));

        if (MaximumTotalRetainedBytes is < 13 or > MaximumTotalBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumTotalRetainedBytes),
                MaximumTotalRetainedBytes,
                $"The total FLV bootstrap bound must be between 13 bytes and {MaximumTotalBytesHardLimit} bytes.");
        }
    }

    private static void ValidateComponent(int value, string name)
    {
        if (value is < 1 or > MaximumComponentBytesHardLimit)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"The FLV bootstrap component bound must be between 1 byte and {MaximumComponentBytesHardLimit} bytes.");
        }
    }
}
