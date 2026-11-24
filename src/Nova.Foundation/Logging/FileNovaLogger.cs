using System.Text;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.Foundation.Logging;

/// <summary>
/// サイズ上限付きのローテーション対応ファイルロガー。
/// ディスク障害や権限不足でアプリ本体を落とさないよう、書き込み時の I/O 例外は内部で安全に吸収する。
/// </summary>
public sealed class FileNovaLogger : INovaLogger
{
    public const long DefaultMaximumFileBytes = 2L * 1024 * 1024;

    private static readonly Lazy<FileNovaLogger> DefaultInstance = new(
        static () => new FileNovaLogger(NovaAppDataPaths.GetDefaultLogFilePath()));

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly long _maximumFileBytes;

    public FileNovaLogger(string filePath, long maximumFileBytes = DefaultMaximumFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (maximumFileBytes < 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFileBytes),
                maximumFileBytes,
                "Maximum log file size must be at least 1024 bytes.");
        }

        _filePath = filePath;
        _maximumFileBytes = maximumFileBytes;
    }

    public static FileNovaLogger Default => DefaultInstance.Value;

    public string FilePath => _filePath;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var builder = new StringBuilder();
        builder.Append('[').Append(DateTimeOffset.UtcNow.ToString("O")).Append("] [")
            .Append(level).Append("] ").AppendLine(message);
        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        var entry = builder.ToString();
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded();
                File.AppendAllText(_filePath, entry, Encoding.UTF8);
            }
            catch
            {
                // ロギング失敗で呼び出し元の正常系・終了処理を壊さない。
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var info = new FileInfo(_filePath);
        if (info.Length < _maximumFileBytes)
        {
            return;
        }

        var backupPath = _filePath + ".1";
        File.Move(_filePath, backupPath, overwrite: true);
    }
}
