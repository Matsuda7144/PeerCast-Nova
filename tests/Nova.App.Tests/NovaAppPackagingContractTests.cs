using System.Xml.Linq;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaAppPackagingContractTests
{
    private static readonly string[] ExpectedFfmpegLegalFiles =
    [
        "CANDIDATE-20260721-INVENTORY.txt",
        "COPYING.GPLv3",
        "COPYING.GPLv3.gz",
        "COPYING.GPLv3.README",
        "COPYING.LGPLv3",
        "ffmpeg-candidate-buildconf.txt",
        "ffmpeg-candidate-license.txt",
        "NOTICE.md",
        "NOVA-DISTRIBUTION-GUARD.md",
        "PROVENANCE.md",
        "RUNTIME-REPLACEMENT.md",
        "SHA256SUMS",
    ];

    private static readonly string[] InstallerSigningAndUpdateProperties =
    [
        "ApplicationVersion",
        "AssemblyOriginatorKeyFile",
        "BootstrapperEnabled",
        "GenerateBootstrapper",
        "Install",
        "InstallUrl",
        "ManifestCertificateThumbprint",
        "ManifestKeyFile",
        "PublishProtocol",
        "PublishUrl",
        "SignAssembly",
        "SignManifests",
        "UpdateEnabled",
        "UpdateMode",
        "UpdateUrl",
    ];

    [Fact]
    public void PortableProfileIsFrameworkDependentAndUntransformed()
    {
        var profile = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Properties",
            "PublishProfiles",
            "WindowsPortable.pubxml"));

        Assert.Equal("Release", Property(profile, "Configuration"));
        Assert.Equal("win-x64", Property(profile, "RuntimeIdentifier"));
        Assert.Equal("false", Property(profile, "SelfContained"));
        Assert.Equal("true", Property(profile, "UseAppHost"));
        Assert.Equal("false", Property(profile, "PublishSingleFile"));
        Assert.Equal("false", Property(profile, "PublishTrimmed"));
        Assert.Equal("false", Property(profile, "PublishReadyToRun"));
        Assert.Equal("none", Property(profile, "DebugType"));
        Assert.Equal("false", Property(profile, "DebugSymbols"));
    }

    [Fact]
    public void WindowsSelfContainedPublishProfileProducesSelfContainedWinX64WithPortableSymbols()
    {
        var profile = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Properties",
            "PublishProfiles",
            "WindowsSelfContained.pubxml"));

        Assert.Equal("Release", Property(profile, "Configuration"));
        Assert.Equal("win-x64", Property(profile, "RuntimeIdentifier"));
        Assert.Equal("true", Property(profile, "SelfContained"));
        Assert.Equal("true", Property(profile, "UseAppHost"));
        Assert.Equal("false", Property(profile, "PublishSingleFile"));
        Assert.Equal("false", Property(profile, "PublishTrimmed"));
        Assert.Equal("false", Property(profile, "PublishReadyToRun"));
        Assert.Equal("portable", Property(profile, "DebugType"));
        Assert.Equal("true", Property(profile, "DebugSymbols"));

        var declaredProperties = profile.Descendants()
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            InstallerSigningAndUpdateProperties,
            property => Assert.DoesNotContain(property, declaredProperties));
    }

    [Fact]
    public void AppPackageCopiesExactLegalAllowlist()
    {
        var project = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        var actual = project.Descendants("FfmpegLegalMaterial")
            .Select(element => MsBuildFileName(RequiredAttribute(element, "Include")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ExpectedFfmpegLegalFiles.Order(StringComparer.Ordinal),
            actual);
        Assert.All(
            ExpectedFfmpegLegalFiles,
            name => Assert.True(File.Exists(FindRepositoryFile(
                "THIRD-PARTY-LICENSES",
                "FFmpeg",
                name))));

        var linked = Assert.Single(
            project.Descendants("None"),
            element => string.Equals(
                RequiredAttribute(element, "Include"),
                "@(FfmpegLegalMaterial)",
                StringComparison.Ordinal));
        Assert.Equal(
            @"THIRD-PARTY-LICENSES\FFmpeg\%(Filename)%(Extension)",
            RequiredAttribute(linked, "Link"));
        Assert.Equal("PreserveNewest", Attribute(linked, "CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", Attribute(linked, "CopyToPublishDirectory"));
    }

    [Fact]
    public void AppPackageCopiesProjectLicenseAndNotice()
    {
        var project = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        var packageItems = project.Descendants("None")
            .Where(element => Attribute(element, "Link") is "LICENSE" or "NOTICE")
            .ToArray();

        Assert.Equal(2, packageItems.Length);
        Assert.All(packageItems, item =>
        {
            Assert.Equal("PreserveNewest", Attribute(item, "CopyToOutputDirectory"));
            Assert.Equal("PreserveNewest", Attribute(item, "CopyToPublishDirectory"));
        });
    }

    [Fact]
    public void LegalAllowlistContainsNoExternalRuntimePayload()
    {
        Assert.DoesNotContain(
            ExpectedFfmpegLegalFiles,
            name => Path.GetExtension(name) is ".dll" or ".exe" or ".zip");

        var projectText = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        Assert.DoesNotContain(@"FFmpeg\**\*", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimePack", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", projectText, StringComparison.Ordinal);
        Assert.Contains(
            "RemoveWindowsPortableDebugSymbols",
            projectText,
            StringComparison.Ordinal);
        Assert.Contains(
            "$(PublishDir)**\\*.pdb",
            projectText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PortableProfileDefinesNoInstallerSigningOrUpdateMechanism()
    {
        var profile = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Properties",
            "PublishProfiles",
            "WindowsPortable.pubxml"));
        var project = XDocument.Load(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        var declaredProperties = profile.Descendants()
            .Concat(project.Descendants())
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(
            InstallerSigningAndUpdateProperties,
            property => Assert.DoesNotContain(property, declaredProperties));
    }

    private static string Property(XDocument document, string name) =>
        Assert.Single(document.Descendants(name)).Value;

    private static string RequiredAttribute(XElement element, string name) =>
        Assert.IsType<XAttribute>(element.Attribute(name)).Value;

    private static string? Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value;

    private static string MsBuildFileName(string path) =>
        path.Replace('\\', '/').Split('/')[^1];

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }
}
