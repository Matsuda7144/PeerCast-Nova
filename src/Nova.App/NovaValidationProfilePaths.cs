namespace PeerCast.Nova.App;

/// <summary>Explicit application storage roots, not an OS security sandbox.</summary>
internal sealed class NovaValidationProfilePaths
{
    internal const string Marker = ".nova-validation-profile";
    internal const string MarkerValue = "Nova network-settings validation profile v1";
    private readonly StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal NovaValidationProfilePaths(string root, IReadOnlyList<string> productionRoots)
    {
        if (!IsAbsoluteRoot(root)) throw new ArgumentException("Invalid validation root.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (var production in productionRoots)
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(production));
            if (Contains(Root, path) || Contains(path, Root))
                throw new ArgumentException("Validation and production roots overlap.");
        }
        CheckNoReparse(Root);
    }

    internal string Root { get; }
    internal string Settings => Resolve("settings", "settings.json");
    internal string Favorites => Resolve("preferences", "favorites.json");
    internal string Notifications => Resolve("preferences", "notification-preferences.json");
    internal string YellowPages => Resolve("preferences", "yellow-pages.json");
    internal string Playback => Resolve("preferences", "playback-preferences.json");
    internal string History => Resolve("history", "recording-history.json");
    internal string Rules => Resolve("rules", "recording-rules.json");
    internal string Recordings => Resolve("recordings");
    internal string Diagnostics => Resolve("diagnostics");
    internal static string[] Directories => ["settings", "preferences", "history", "rules", "recordings", "diagnostics"];

    internal static bool IsAbsoluteRoot(string? root) =>
        GenesisRecordingOutputDirectorySettings.TryNormalize(root, out _) &&
        root == root!.Trim() && Path.GetFullPath(root) != Path.GetPathRoot(Path.GetFullPath(root)) &&
        !root!.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or "..") &&
        !(OperatingSystem.IsWindows() && root.StartsWith(@"\\", StringComparison.Ordinal));

    internal string Resolve(params string[] components)
    {
        var path = Path.Combine([Root, .. components]);
        Check(path);
        return path;
    }

    internal void Check(string path)
    {
        if (!IsAbsoluteRoot(path) || !Contains(Root, Path.GetFullPath(path)))
            throw new IOException("Validation path escaped its root.");
        CheckNoReparse(path);
    }

    internal void CheckAll()
    {
        Check(Root);
        foreach (var directory in Directories) _ = Resolve(directory);
        foreach (var path in new[] { Settings, Favorites, Notifications, YellowPages, Playback, History, Rules })
            Check(path);
        Check(History + ".lock");
        _ = Resolve(Marker);
        _ = Resolve(".validation.lock");
    }

    internal void CheckMetadata(GenesisRecordingMetadataSnapshot document)
    {
        foreach (var root in document.KnownRoots.Roots) Check(root.Path);
        foreach (var fact in document.History.Entries)
            if (fact.WriteResult is { } written) Check(written.Path);
    }

    private bool Contains(string root, string path) =>
        string.Equals(root, path, comparison) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, comparison);

    // Check ancestors as well as the leaf, including dangling symbolic links.
    // Recheck at application I/O boundaries. Other processes must not modify an
    // in-use profile: this is not protection against a hostile same-user actor.
    private static void CheckNoReparse(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
                throw new IOException("Validation profiles must be outside repositories.");
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Validation reparse paths are not allowed.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
