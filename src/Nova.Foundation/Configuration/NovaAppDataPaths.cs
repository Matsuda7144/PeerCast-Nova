namespace PeerCast.Nova.Foundation.Configuration;

/// <summary>
/// PeerCast Nova が利用するローカルアプリケーションデータのパス解決を集約する。
/// </summary>
public static class NovaAppDataPaths
{
    public const string PrimaryDirectoryName = "PeerCast Nova";

    public static string GetLocalApplicationDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? AppContext.BaseDirectory
            : localAppData;
    }

    public static string GetPrimaryDataDirectory() =>
        Path.Combine(GetLocalApplicationDataRoot(), PrimaryDirectoryName);

    public static string GetLegacySettingsDirectory() =>
        Path.Combine(GetLocalApplicationDataRoot(), "PeerCast", "Nova");

    public static string GetLogsDirectory() =>
        Path.Combine(GetPrimaryDataDirectory(), "logs");

    public static string GetDefaultLogFilePath() =>
        Path.Combine(GetLogsDirectory(), "nova.log");
}
