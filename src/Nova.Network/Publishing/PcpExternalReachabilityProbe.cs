using System.Net;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

public sealed record PcpExternalReachabilityProbeResult(
    bool Succeeded,
    string? ExternalAddress,
    bool? PortReachable,
    string? FailureReason);

/// <summary>
/// Tracker への PCP HELO / OLEH ハンドシェイクを使って公開 IPv4 アドレスと待受ポートの外部到達性を検証する。
/// </summary>
public static class PcpExternalReachabilityProbe
{
    public static async Task<PcpExternalReachabilityProbeResult> ProbeAsync(
        string trackerEndpoint,
        int listenPort,
        Guid? sessionId = null,
        Guid? broadcastId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseEndpoint(trackerEndpoint, out var parsed) || parsed is null)
        {
            return new(false, null, null, "Tracker Endpoint が無効です。");
        }

        if (listenPort is < 0 or > 65535)
        {
            return new(false, null, null, "ポート番号が無効です。");
        }

        var sid = sessionId is { } s && s != Guid.Empty ? s : Guid.NewGuid();
        var bcid = PcpAnnounceOwner.NormalizeOrCreateBroadcastId(broadcastId);
        return await ProbeCoreAsync(
            parsed,
            listenPort,
            sid,
            bcid,
            new PcpAnnounceProductionConnector(),
            static (host, token) => Dns.GetHostAddressesAsync(host, token),
            timeout ?? TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PcpExternalReachabilityProbeResult> ProbeCoreAsync(
        PcpAnnounceEndpoint trackerEndpoint,
        int listenPort,
        Guid sessionId,
        Guid broadcastId,
        IPcpAnnounceConnector connector,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);

        IPcpAnnounceConnection? connection = null;
        try
        {
            var resolved = await ResolveEndpointAsync(trackerEndpoint, resolve, budget.Token).ConfigureAwait(false);
            connection = await connector.ConnectAsync(resolved, budget.Token).ConfigureAwait(false);

            var preamble = new byte[PcpPreamble.Size];
            PcpPreamble.Write(preamble);
            var helo = CreateProbeHelo(listenPort, sessionId, broadcastId);
            var payload = preamble.Concat(PcpAnnounceDocument.EncodeAtom(helo)).ToArray();
            await connection.WriteAsync(payload, budget.Token).ConfigureAwait(false);

            var atom = await connection.ReadAsync(budget.Token).ConfigureAwait(false);
            if (atom.Name == PcpKnownAtomNames.Quit)
            {
                return new(false, null, null, "Tracker から切断されました。");
            }

            var oleh = PcpAnnounceCodec.ParseOleh(atom);
            var externalText = oleh.RemoteAddress?.ToString();
            var externalAddress = PublishingIpv4.IsPublicAddress(externalText) ? externalText : null;
            bool? portReachable = listenPort > 0
                ? oleh.RemotePort is > 0
                : null;

            try
            {
                await connection.WriteAsync(PcpAnnounceDocument.EncodeAtom(PcpAnnounceCodec.CreateQuit()), budget.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 正常結果を取得済みなら QUIT 送信失敗は無視する。
            }

            return new(externalAddress is not null || portReachable is not null, externalAddress, portReachable, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PcpAnnounceBannedException)
        {
            return new(false, null, null, "Tracker により拒否されました。");
        }
        catch (TimeoutException)
        {
            return new(false, null, null, "Tracker への接続がタイムアウトしました。");
        }
        catch (OperationCanceledException)
        {
            return new(false, null, null, "Tracker への接続がタイムアウトしました。");
        }
        catch (Exception exception)
        {
            return new(false, null, null, exception.Message);
        }
        finally
        {
            if (connection is not null)
            {
                try { await connection.CloseAsync().ConfigureAwait(false); }
                catch { connection.Abort(); }
            }
        }
    }

    private static bool TryParseEndpoint(string value, out PcpAnnounceEndpoint? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            parsed = PcpAnnounceEndpoint.Parse(value.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<IPEndPoint> ResolveEndpointAsync(
        PcpAnnounceEndpoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(endpoint.Uri.Host, out var literal))
        {
            return new IPEndPoint(new IPAddress(literal.GetAddressBytes()), endpoint.Port);
        }

        var addresses = await resolve(endpoint.Uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        var ipv4 = addresses.Where(static a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
        if (ipv4.Length == 0)
        {
            throw new InvalidDataException("Tracker の IPv4 アドレスを解決できませんでした。");
        }

        return new IPEndPoint(new IPAddress(ipv4[0].GetAddressBytes()), endpoint.Port);
    }

    private static PcpAtom CreateProbeHelo(int listenPort, Guid sessionId, Guid broadcastId)
    {
        if (listenPort > 0)
        {
            return PcpAnnounceCodec.CreateHelo(
                "PeerCastNova/ReachabilityProbe",
                sessionId,
                broadcastId,
                checked((ushort)listenPort),
                requestPing: true);
        }

        var helo = PcpHeloCodec.CreateHelo("PeerCastNova/ReachabilityProbe", sessionId, openPort: null, pingPort: null);
        return PcpAtom.Parent(
            helo.Name,
            [.. helo.Children, PcpAtom.Leaf(PcpAnnounceCodec.BroadcastId, PcpWireValueCodec.EncodeGuid(broadcastId))]);
    }
}
