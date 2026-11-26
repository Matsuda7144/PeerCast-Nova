using System.Xml.Linq;

namespace PeerCast.Nova.App.Tests;

/// <summary>
/// リポジトリ内のソースファイルやプロジェクト定義を参照するテスト向け共通ヘルパー。
/// </summary>
internal static class RepositorySourceTestHelper
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PeerCast-Nova.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing PeerCast-Nova.sln was not found.");
    }

    public static string FindRepositoryFile(params string[] relativeParts)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine([root, .. relativeParts]);
        if (File.Exists(path))
        {
            return path;
        }

        if (relativeParts.Length == 3 &&
            string.Equals(relativeParts[0], "src", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relativeParts[1], "Nova.App", StringComparison.OrdinalIgnoreCase))
        {
            var appDirectory = Path.Combine(root, "src", "Nova.App");
            if (Directory.Exists(appDirectory))
            {
                var matches = Directory
                    .EnumerateFiles(appDirectory, relativeParts[2], SearchOption.AllDirectories)
                    .ToArray();
                if (matches.Length == 1)
                {
                    return matches[0];
                }

                if (matches.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Multiple Nova.App files matched '{relativeParts[2]}': {string.Join(", ", matches)}");
                }
            }
        }

        throw new FileNotFoundException($"Repository file was not found: {Path.Combine(relativeParts)}", path);
    }

    public static string ReadAppSource(string fileName) =>
        File.ReadAllText(FindRepositoryFile("src", "Nova.App", fileName));

    public static XDocument LoadProjectDocument(params string[] relativeParts) =>
        XDocument.Load(FindRepositoryFile(relativeParts));
}
