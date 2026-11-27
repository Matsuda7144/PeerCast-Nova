using System.Text.RegularExpressions;

namespace PeerCast.Nova.Foundation.Tests;

public sealed partial class RepositoryDocumentationSynchronizationTests
{
    [Fact]
    public void NumberedDocumentFiles_MatchTheirHeadings()
    {
        var root = FindRepositoryRoot();
        var issues = new List<string>();
        ValidateNumberedDocuments(root, "docs/adr", "ADR", issues);
        ValidateNumberedDocuments(root, "docs/reviews", "PCN", issues);

        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    [Fact]
    public void CoreRepositoryDocuments_ExistAndAreNonEmpty()
    {
        var root = FindRepositoryRoot();
        string[] requiredDocuments =
        [
            "README.md",
            "NAVIGATION.md",
            "ROADMAP.md",
            "CHANGELOG.md",
            "CONTRIBUTING.md",
        ];

        foreach (var relativePath in requiredDocuments)
        {
            var fullPath = Path.Combine(root, relativePath);
            Assert.True(File.Exists(fullPath), $"Required repository document is missing: {relativePath}");
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(fullPath)), $"Required repository document is empty: {relativePath}");
        }
    }

    [Fact]
    public void CurrentDocumentationReferences_ResolveToRepositoryFiles()
    {
        var root = FindRepositoryRoot();
        var issues = new List<string>();
        ValidateMarkdownLinks(root, "README.md", issues);
        ValidateMarkdownLinks(root, "NAVIGATION.md", issues);
        ValidateMarkdownLinks(root, "CHANGELOG.md", issues);
        ValidateRoadmapDocumentPaths(root, issues);

        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    private static void ValidateNumberedDocuments(
        string root,
        string relativeDirectory,
        string prefix,
        List<string> issues)
    {
        var directory = Path.Combine(
            root,
            relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        foreach (var path in Directory.EnumerateFiles(
            directory,
            $"{prefix}-*.md",
            SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var fileMatch = NumberedFileName().Match(fileName);
            if (!fileMatch.Success ||
                !string.Equals(
                    fileMatch.Groups["prefix"].Value,
                    prefix,
                    StringComparison.Ordinal))
            {
                issues.Add($"Invalid numbered document filename: {fileName}");
                continue;
            }

            var heading = ReadFirstNonemptyLine(path);
            var headingMatch = NumberedHeading().Match(heading);
            if (!headingMatch.Success ||
                !string.Equals(
                    headingMatch.Groups["prefix"].Value,
                    prefix,
                    StringComparison.Ordinal))
            {
                issues.Add($"Invalid heading in {fileName}: {heading}");
                continue;
            }

            if (!string.Equals(
                fileMatch.Groups["number"].Value,
                headingMatch.Groups["number"].Value,
                StringComparison.Ordinal))
            {
                issues.Add($"Filename/heading number mismatch in {fileName}");
            }
        }
    }

    private static void ValidateMarkdownLinks(
        string root,
        string relativePath,
        List<string> issues)
    {
        var documentPath = Path.Combine(root, relativePath);
        var documentDirectory = Path.GetDirectoryName(documentPath)!;
        var content = File.ReadAllText(documentPath);
        foreach (Match match in MarkdownDocumentLink().Matches(content))
        {
            var target = match.Groups["target"].Value;
            if (target.Contains("://", StringComparison.Ordinal))
            {
                continue;
            }

            var resolved = Path.GetFullPath(
                target.Replace('/', Path.DirectorySeparatorChar),
                documentDirectory);
            if (!File.Exists(resolved))
            {
                issues.Add($"Missing Markdown link target in {relativePath}: {target}");
            }
        }
    }

    private static void ValidateRoadmapDocumentPaths(
        string root,
        List<string> issues)
    {
        var content = File.ReadAllText(Path.Combine(root, "ROADMAP.md"));
        foreach (Match match in RoadmapDocumentPath().Matches(content))
        {
            var target = match.Groups["target"].Value;
            var resolved = Path.Combine(
                root,
                target.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(resolved))
            {
                issues.Add($"Missing ROADMAP document path: {target}");
            }
        }
    }

    private static string ReadFirstNonemptyLine(string path) =>
        File.ReadLines(path).First(line => !string.IsNullOrWhiteSpace(line));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PeerCast-Nova.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the PeerCast Nova repository root.");
    }

    [GeneratedRegex(
        "^(?<prefix>ADR|PCN)-(?<number>[0-9]{4})-.+\\.md$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberedFileName();

    [GeneratedRegex(
        "^# (?<prefix>ADR|PCN)-(?<number>[0-9]{4}):? (?<title>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeading();

    [GeneratedRegex(
        "\\[[^\\]]+\\]\\((?<target>[^)#]+\\.md)(?:#[^)]+)?\\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownDocumentLink();

    [GeneratedRegex(
        "`(?<target>docs/[^`]+\\.md)`",
        RegexOptions.CultureInvariant)]
    private static partial Regex RoadmapDocumentPath();
}
