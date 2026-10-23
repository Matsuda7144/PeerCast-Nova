using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Tests;

/// <summary>Independent test-side wire writer; does not use the production encoders.</summary>
internal sealed class ObsRtmpTestClient : IDisposable
{
    private readonly TcpClient _client = new(AddressFamily.InterNetwork);
    private NetworkStream _stream = null!;
    private RtmpChunkReader _responses = null!;
    private int _chunkBytes = 128;
    private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(10));

    public async Task ConnectAsync(IPEndPoint endpoint)
    {
        await _client.ConnectAsync(endpoint, _deadline.Token);
        _stream = _client.GetStream();
        byte[] hello = new byte[1537];
        hello[0] = 3;
        hello[5] = 0x80; // OBS may propose a complex handshake; zero-version S1 selects fallback.
        for (var i = 9; i < hello.Length; i++)
        {
            hello[i] = (byte)i;
        }

        await _stream.WriteAsync(hello, _deadline.Token);
        byte[] server = new byte[3073];
        // OBS reads S0/S1, sends the simple C2 echo, then reads S2.
        await _stream.ReadExactlyAsync(server.AsMemory(0, 1537), _deadline.Token);
        Assert.Equal(3, server[0]);
        Assert.Equal(new byte[4], server[5..9]); // Zero S1 version selects fallback.
        var echo = server.AsSpan(1, 1536).ToArray();
        echo[4] = 0x12; // time2 need not echo S1's zero field.
        await _stream.WriteAsync(echo, _deadline.Token);
        await _stream.ReadExactlyAsync(server.AsMemory(1537), _deadline.Token);
        Assert.Equal(hello.AsSpan(1).ToArray(), server.AsSpan(1537).ToArray());
        _responses = new RtmpChunkReader(_stream, new ObsRtmpIngestOptions(), (_, _) => ValueTask.CompletedTask);
    }

    public async Task PublishAsync(string application = "live", string streamName = "nova")
    {
        await SendCommandAsync(0, "connect", 1.0, new Dictionary<string, object?>
        {
            ["app"] = application,
            ["flashVer"] = "FMLE/3.0 (compatible; test)",
            ["objectEncoding"] = 0.0,
        });
        Assert.Equal(5, (await ReadAsync()).Type);
        Assert.Equal(6, (await ReadAsync()).Type);
        var connected = await ReadAsync();
        Assert.Equal(20, connected.Type);
        Assert.Contains("NetConnection.Connect.Success", Encoding.UTF8.GetString(connected.Payload));

        await SendCommandAsync(0, "releaseStream", 2.0, null, streamName);
        Assert.Equal(20, (await ReadAsync()).Type);
        await SendCommandAsync(0, "FCPublish", 3.0, null, streamName);
        Assert.Equal(20, (await ReadAsync()).Type);
        await SendCommandAsync(0, "createStream", 4.0, null);
        var created = RtmpAmf0.Decode((await ReadAsync()).Payload);
        Assert.Equal(1.0, created[3]);
        await SendCommandAsync(1, "publish", 0.0, null, streamName, "live");
        Assert.Equal(4, (await ReadAsync()).Type);
        var published = await ReadAsync();
        Assert.Equal(1U, published.StreamId);
        Assert.Contains("NetStream.Publish.Start", Encoding.UTF8.GetString(published.Payload));
    }

    public Task SendCommandAsync(uint streamId, params object?[] values) =>
        SendAsync(20, streamId, 0, Command(values), id: 3);

    public async Task SendAsync(byte type, uint streamId, uint timestamp, byte[] payload, int id = 6)
    {
        await _stream.WriteAsync(WireMessage(type, streamId, timestamp, payload, id, _chunkBytes), _deadline.Token);
    }

    public async Task SetChunkBytesAsync(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        await SendAsync(1, 0, 0, bytes, id: 2);
        _chunkBytes = value;
    }

    public async Task<RtmpMessage> ReadAsync() => await _responses.ReadAsync(_deadline.Token);

    public async Task SendWireAsync(byte[] wire) =>
        await _stream.WriteAsync(wire, _deadline.Token);

    public void EndInput() => _client.Client.Shutdown(SocketShutdown.Send);

    public Task SendCompressedAsync(
        byte type, uint delta, byte[] payload, int format = 1, int id = 4) =>
        SendWireAsync(WireCompressed(type, delta, payload, format, id, _chunkBytes));

    public static byte[] WireCompressed(
        byte type, uint delta, byte[] payload, int format, int id, int chunkBytes)
    {
        using var output = new MemoryStream();
        output.WriteByte((byte)((format << 6) | id));
        if (format <= 2)
        {
            Write24(output, Math.Min(delta, 0xFFFFFF));
        }

        if (format == 1)
        {
            Write24(output, (uint)payload.Length);
            output.WriteByte(type);
        }

        for (var offset = 0; offset < payload.Length;)
        {
            if (offset > 0)
            {
                output.WriteByte((byte)(0xC0 | id));
            }

            if (delta >= 0xFFFFFF)
            {
                Write32(output, delta);
            }

            var length = Math.Min(chunkBytes, payload.Length - offset);
            output.Write(payload, offset, length);
            offset += length;
        }

        return output.ToArray();
    }

    public void Dispose()
    {
        _client.Dispose();
        _deadline.Dispose();
    }

    public static byte[] WireMessage(
        byte type, uint streamId, uint timestamp, byte[] payload, int id = 6, int chunkBytes = 128)
    {
        using var output = new MemoryStream();
        output.WriteByte((byte)id);
        Write24(output, Math.Min(timestamp, 0xFFFFFF));
        Write24(output, (uint)payload.Length);
        output.WriteByte(type);
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(streamId);
        }

        for (var offset = 0; offset < payload.Length;)
        {
            if (offset > 0)
            {
                output.WriteByte((byte)(0xC0 | id));
            }

            if (timestamp >= 0xFFFFFF)
            {
                Write32(output, timestamp);
            }

            var length = Math.Min(chunkBytes, payload.Length - offset);
            output.Write(payload, offset, length);
            offset += length;
        }

        return output.ToArray();
    }

    public static byte[] Command(params object?[] values)
    {
        using var output = new MemoryStream();
        foreach (var value in values)
        {
            WriteValue(output, value);
        }

        return output.ToArray();
    }

    private static void WriteValue(Stream output, object? value)
    {
        switch (value)
        {
            case null:
                output.WriteByte(5);
                break;
            case string text:
                output.WriteByte(2);
                WriteString(output, text);
                break;
            case double number:
                output.WriteByte(0);
                var bytes = BitConverter.GetBytes(number);
                Array.Reverse(bytes);
                output.Write(bytes);
                break;
            case bool boolean:
                output.WriteByte(1);
                output.WriteByte(boolean ? (byte)1 : (byte)0);
                break;
            case Dictionary<string, object?> properties:
                output.WriteByte(3);
                foreach (var (key, property) in properties)
                {
                    WriteString(output, key);
                    WriteValue(output, property);
                }

                output.Write([0, 0, 9]);
                break;
            default:
                throw new InvalidOperationException("Unsupported test AMF type.");
        }
    }

    private static void WriteString(Stream output, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        output.WriteByte((byte)(bytes.Length >> 8));
        output.WriteByte((byte)bytes.Length);
        output.Write(bytes);
    }

    private static void Write24(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static void Write32(Stream output, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
}
