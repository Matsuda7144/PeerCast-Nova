using PeerCast.Nova.Foundation.Logging;

namespace PeerCast.Nova.Foundation.Tests;

public sealed class FileNovaLoggerTests
{
    [Fact]
    public void Log_WritesTimestampLevelMessageAndException()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PeerCastNova-FileLogger-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "nova.log");
            var logger = new FileNovaLogger(path);

            logger.Log(LogLevel.Error, "Startup failed.", new InvalidOperationException("boom"));

            var content = File.ReadAllText(path);
            Assert.Contains("[Error] Startup failed.", content, StringComparison.Ordinal);
            Assert.Contains("InvalidOperationException: boom", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Log_WithBlankMessage_ThrowsArgumentException()
    {
        var logger = new FileNovaLogger(Path.Combine(Path.GetTempPath(), "unused.log"));
        Assert.Throws<ArgumentException>(() => logger.Log(LogLevel.Information, " "));
    }

    [Fact]
    public void Log_RotatesFileWhenExceedingMaximumBytes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"PeerCastNova-FileLoggerRotate-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "nova.log");
            var logger = new FileNovaLogger(path, maximumFileBytes: 1024);

            var payload = new string('A', 600);
            logger.Log(LogLevel.Information, payload);
            logger.Log(LogLevel.Information, payload);
            logger.Log(LogLevel.Information, "after-rotation");

            Assert.True(File.Exists(path + ".1"));
            var active = File.ReadAllText(path);
            Assert.Contains("after-rotation", active, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
