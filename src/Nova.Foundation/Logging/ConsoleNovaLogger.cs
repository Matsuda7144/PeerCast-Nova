namespace PeerCast.Nova.Foundation.Logging;

public sealed class ConsoleNovaLogger : INovaLogger
{
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var line = $"[{DateTimeOffset.UtcNow:O}] [{level}] {message}";
        Console.WriteLine(line);

        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }
}
