namespace PeerCast.Nova.Foundation.Logging;

public interface INovaLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}
