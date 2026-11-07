namespace PeerCast.Nova.Network.Transport;

// One observation boundary under both direct I/O and PipeReader. Never counts buffer consumption twice.
internal sealed class TransportObservationStream(Stream inner, PcpTransportCounter counter) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanTimeout => inner.CanTimeout;
    public override int ReadTimeout { get => inner.ReadTimeout; set => inner.ReadTimeout = value; }
    public override int WriteTimeout { get => inner.WriteTimeout; set => inner.WriteTimeout = value; }
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override int Read(byte[] buffer, int offset, int count)
    {
        try { var read = inner.Read(buffer, offset, count); counter.AddReceived(read); return read; }
        catch { counter.Invalidate(); throw; }
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        try { inner.Write(buffer, offset, count); counter.AddSent(count); }
        catch { counter.Invalidate(); throw; }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        try { var read = await inner.ReadAsync(buffer, token).ConfigureAwait(false); counter.AddReceived(read); return read; }
        catch { counter.Invalidate(); throw; }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        try { await inner.WriteAsync(buffer, token).ConfigureAwait(false); counter.AddSent(buffer.Length); }
        catch { counter.Invalidate(); throw; }
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { counter.Close(); inner.Dispose(); }
        base.Dispose(disposing);
    }
}
