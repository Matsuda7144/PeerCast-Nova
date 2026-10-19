using System.IO.Pipelines;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Reads exactly one bounded downstream relay HTTP header through the caller-owned
/// PipeReader and returns privacy-safe routing information. The reader remains
/// caller-owned so bytes already buffered after the header stay available to the
/// PCP handshake instead of being discarded during routing.
/// </summary>
internal static class PcpDownstreamRelayRequestReader
{
    public static async ValueTask<PcpDownstreamRelayRequestValidation> ReadAsync(
        PipeReader reader,
        int maxHeaderBytes,
        int expectedPcpVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
            reader,
            maxHeaderBytes,
            cancellationToken).ConfigureAwait(false);

        return PcpDownstreamRelayRequestValidator.Validate(
            request,
            expectedPcpVersion);
    }
}
