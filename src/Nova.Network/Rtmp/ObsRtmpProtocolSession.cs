using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PeerCast.Nova.Network.Rtmp;

internal sealed class ObsRtmpProtocolSession(
    Stream stream,
    ObsRtmpIngestOptions options,
    ObsFlvPublishingSession publishing,
    ObsRtmpIngressAdmission admission,
    ObsRtmpIngressObservation? observation = null)
{
    private bool _connected;
    private bool _created;
    private bool _published;

    public async Task<ObsRtmpIngestEndReason> RunAsync(CancellationToken cancellationToken)
    {
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, admission.ReadStop);
        await HandshakeAsync(reads.Token).ConfigureAwait(false);
        var reader = new RtmpChunkReader(stream, options,
            (count, token) => SendUInt32Async(3, count, token));
        while (true)
        {
            var message = await reader.ReadAsync(reads.Token).ConfigureAwait(false);
            observation?.MessageDecoded?.Invoke(message);
            // Reading a message does not admit it. The shared gate decides
            // whether this complete message or the close request won the race.
            if (!admission.TryAdmit())
            {
                // A forced stop that won before this boundary is cancellation,
                // even when local admission was also closed.
                cancellationToken.ThrowIfCancellationRequested();
                return ObsRtmpIngestEndReason.Quiesced;
            }
            observation?.MessageAdmitted?.Invoke(message);
            try
            {
                if (message.Type is >= 1 and <= 6)
                {
                    await ControlAsync(reader, message, cancellationToken).ConfigureAwait(false);
                }
                else if (message.Type == 20)
                {
                    if (await CommandAsync(message, cancellationToken).ConfigureAwait(false))
                    {
                        return ObsRtmpIngestEndReason.PublisherStopped;
                    }
                }
                else if (message.Type is 8 or 9 or 18)
                {
                    if (!_published || message.StreamId != 1)
                    {
                        throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                            ObsRtmpRejectionReason.MediaOutsidePublishedStream, "RTMP media requires the active published stream.");
                    }

                    await publishing.AcceptAsync(message, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                        ObsRtmpRejectionReason.UnsupportedMessageType, "Unsupported RTMP message type.");
                }
            }
            catch (InvalidDataException exception)
            {
                ObsRtmpRejection.Enrich(exception, ObsRtmpRejection.ForMessage(message));
                throw;
            }
        }
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        var client = new byte[1537];
        await ReadExactlyAsync(client, cancellationToken).ConfigureAwait(false);
        if (client[0] != 3)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpHandshake,
                ObsRtmpRejectionReason.UnsupportedHandshakeVersion, "Only unencrypted RTMP version 3 is supported.");
        }

        var response = new byte[3073];
        response[0] = 3;
        RandomNumberGenerator.Fill(response.AsSpan(9, 1528));
        // S1 version stays zero, selecting the simple handshake fallback in OBS.
        client.AsSpan(1).CopyTo(response.AsSpan(1537));
        await WriteAsync(response, cancellationToken).ConfigureAwait(false);
        var echo = new byte[1536];
        await ReadExactlyAsync(echo, cancellationToken).ConfigureAwait(false);
        if (!echo.AsSpan(0, 4).SequenceEqual(response.AsSpan(1, 4)) ||
            !echo.AsSpan(8).SequenceEqual(response.AsSpan(9, 1528)))
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpHandshake,
                ObsRtmpRejectionReason.InvalidHandshakeEcho, "Invalid RTMP handshake echo.");
        }
    }

    private async ValueTask ControlAsync(
        RtmpChunkReader reader, RtmpMessage message, CancellationToken cancellationToken)
    {
        var bytes = message.Payload;
        if (message.StreamId != 0)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpControlMessage,
                ObsRtmpRejectionReason.InvalidMessageStreamId, "RTMP protocol control must use message stream zero.");
        }

        switch (message.Type)
        {
            case 1 when bytes.Length == 4:
                reader.SetChunkBytes(BinaryPrimitives.ReadUInt32BigEndian(bytes));
                break;
            case 2 when bytes.Length == 4:
                reader.Abort(BinaryPrimitives.ReadUInt32BigEndian(bytes));
                break;
            case 3 when bytes.Length == 4:
                break;
            case 5 when bytes.Length == 4:
                var window = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                if (window == 0)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpControlMessage,
                        ObsRtmpRejectionReason.ZeroAcknowledgementWindow, "RTMP acknowledgement window cannot be zero.");
                }

                reader.AcknowledgementWindow = window;
                break;
            case 6 when bytes.Length == 5 && bytes[4] <= 2:
                break;
            case 4 when bytes.Length >= 2:
                var eventType = BinaryPrimitives.ReadUInt16BigEndian(bytes);
                if (eventType == 6 && bytes.Length == 6)
                {
                    var pong = bytes.ToArray();
                    pong[1] = 7;
                    await SendAsync(4, 0, pong, cancellationToken).ConfigureAwait(false);
                }
                else if (eventType != 3 || bytes.Length != 10)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpControlMessage,
                        ObsRtmpRejectionReason.UnsupportedUserControlEvent, "Unsupported RTMP user-control event.");
                }

                break;
            default:
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.RtmpControlMessage,
                    ObsRtmpRejectionReason.MalformedControlMessage, "Invalid RTMP control message.");
        }
    }

    private async ValueTask<bool> CommandAsync(RtmpMessage message, CancellationToken cancellationToken)
    {
        var context = ObsRtmpRejection.ForMessage(message);
        try
        {
            var values = RtmpAmf0.Decode(message.Payload);
            if (values.Count > 0 && values[0] is string token)
                context = context with { CommandOrEvent = ObsRtmpRejection.Token(token) };
            if (values.Count > 1 && values[1] is double transaction && double.IsFinite(transaction))
                context = context with { TransactionId = transaction };
            return await CommandCoreAsync(message, values, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            ObsRtmpRejection.Enrich(exception, context);
            throw;
        }
    }

    private async ValueTask<bool> CommandCoreAsync(
        RtmpMessage message, List<object?> values, CancellationToken cancellationToken)
    {
        if (values.Count < 3 || values[0] is not string name ||
            values[1] is not double transaction || transaction < 0)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfCommand,
                ObsRtmpRejectionReason.MalformedCommandEnvelope, "Invalid RTMP command envelope.");
        }

        if (name == "connect")
        {
            if (_connected || message.StreamId != 0 || values.Count != 3 ||
                values[2] is not Dictionary<string, object?> properties ||
                !properties.TryGetValue("app", out var application) ||
                !Equals(application, options.Application) ||
                (properties.TryGetValue("objectEncoding", out var encoding) && !Equals(encoding, 0.0)))
            {
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.ConnectCommand,
                    ObsRtmpRejectionReason.InvalidConnect, "Unsupported RTMP connect request.");
            }

            await SendUInt32Async(5, 2_500_000, cancellationToken).ConfigureAwait(false);
            byte[] bandwidth = [0, 0x26, 0x25, 0xA0, 2];
            await SendAsync(6, 0, bandwidth, cancellationToken).ConfigureAwait(false);
            await SendCommandAsync(0, cancellationToken, "_result", transaction,
                new Dictionary<string, object?> { ["fmsVer"] = "FMS/3,0,1,123", ["capabilities"] = 31.0 },
                Status("NetConnection.Connect.Success")).ConfigureAwait(false);
            _connected = true;
            return false;
        }

        if (!_connected || values[2] is not null)
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                ObsRtmpRejectionReason.CommandOutOfSequence, "RTMP command is out of sequence.");
        }

        switch (name)
        {
            case "releaseStream":
            case "FCPublish":
                RequireStreamName(message, values, streamId: 0);
                if (_published)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                        ObsRtmpRejectionReason.DuplicatePublish, "A second publish is unsupported.");
                }

                await SendCommandAsync(0, cancellationToken, "_result", transaction, null, null)
                    .ConfigureAwait(false);
                break;
            case "createStream":
                if (_created || message.StreamId != 0 || values.Count != 3)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.CreateStreamCommand,
                        ObsRtmpRejectionReason.InvalidCreateStream, "Only one RTMP stream is supported.");
                }

                await SendCommandAsync(0, cancellationToken, "_result", transaction, null, 1.0)
                    .ConfigureAwait(false);
                _created = true;
                break;
            case "publish":
                RequireStreamName(message, values, streamId: 1, expectedCount: 5);
                if (!_created || _published || !Equals(values[4], "live"))
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.PublishCommand,
                        ObsRtmpRejectionReason.InvalidPublish, "Only one live publisher is supported.");
                }

                byte[] begin = [0, 0, 0, 0, 0, 1];
                await SendAsync(4, 0, begin, cancellationToken).ConfigureAwait(false);
                await SendCommandAsync(1, cancellationToken, "onStatus", 0.0, null,
                    Status("NetStream.Publish.Start")).ConfigureAwait(false);
                _published = true;
                break;
            case "_checkbw":
                if (message.StreamId != 0 || values.Count != 3)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfCommand,
                        ObsRtmpRejectionReason.InvalidBandwidthCheck, "Invalid RTMP bandwidth check.");
                }

                await SendCommandAsync(0, cancellationToken, "_result", transaction, null, 0.0)
                    .ConfigureAwait(false);
                break;
            case "FCUnpublish":
                RequireStreamName(message, values, streamId: 0);
                if (!_published)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                        ObsRtmpRejectionReason.PublisherNotActive, "RTMP publisher is not active.");
                }

                return true;
            case "deleteStream":
                if (!_published || message.StreamId != 0 || values.Count != 4 || !Equals(values[3], 1.0))
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                        ObsRtmpRejectionReason.InvalidDeleteStream, "Invalid RTMP stream deletion.");
                }

                return true;
            case "closeStream":
                if (!_published || message.StreamId != 1 || values.Count != 3)
                {
                    throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.SessionPolicy,
                        ObsRtmpRejectionReason.InvalidCloseStream, "Invalid RTMP stream close.");
                }

                return true;
            default:
                throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.AmfCommand,
                    ObsRtmpRejectionReason.UnsupportedCommand, "Unsupported RTMP command.");
        }

        return false;
    }

    private static Dictionary<string, object?> Status(string code) => new()
    {
        ["level"] = "status",
        ["code"] = code,
        ["description"] = "Accepted.",
        ["objectEncoding"] = 0.0,
    };

    private void RequireStreamName(RtmpMessage message, List<object?> values, uint streamId, int expectedCount = 4)
    {
        if (message.StreamId != streamId || values.Count != expectedCount ||
            !Equals(values[3], options.StreamName))
        {
            throw ObsRtmpRejection.Create(ObsRtmpRejectionStage.PublishCommand,
                ObsRtmpRejectionReason.InvalidPublishRoute, "RTMP application or stream is outside the configured single stream.");
        }
    }

    private ValueTask SendCommandAsync(uint streamId, CancellationToken cancellationToken, params object?[] values) =>
        SendAsync(20, streamId, RtmpAmf0.Encode(values), cancellationToken);

    private ValueTask SendUInt32Async(byte type, uint value, CancellationToken cancellationToken)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return SendAsync(type, 0, bytes, cancellationToken);
    }

    private async ValueTask SendAsync(
        byte type, uint streamId, byte[] bytes, CancellationToken cancellationToken)
    {
        // Keep the default outbound chunk size 128. Formats 0 and 3 suffice for responses.
        var header = new byte[12];
        header[0] = type <= 6 ? (byte)2 : (byte)3;
        RtmpChunkReader.WriteUInt24(header.AsSpan(4), (uint)bytes.Length);
        header[7] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), streamId);
        await WriteAsync(header, cancellationToken).ConfigureAwait(false);
        for (var offset = 0; offset < bytes.Length;)
        {
            if (offset > 0)
            {
                byte[] continuation = [(byte)(header[0] | 0xC0)];
                await WriteAsync(continuation, cancellationToken).ConfigureAwait(false);
            }

            var count = Math.Min(128, bytes.Length - offset);
            await WriteAsync(bytes.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }
    }

    private async ValueTask ReadExactlyAsync(Memory<byte> bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.IoTimeout);
        try
        {
            await stream.ReadExactlyAsync(bytes, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RTMP handshake deadline expired.");
        }
    }

    private async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.IoTimeout);
        try
        {
            await stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RTMP response deadline expired.");
        }
    }
}
