using System.Security.Cryptography;

namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackFfmpegExternalRuntimeManifestTests
{
    [Fact]
    public void Built_in_candidate_matches_pinned_inventory_and_is_approved_external()
    {
        var manifest = PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate;

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestOrigin.BuiltInCandidate,
            manifest.Origin);
        Assert.False(manifest.IsHostSupplied);
        Assert.Equal(
            PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority.ProjectReviewed,
            manifest.LicenseDeclarationAuthority);
        Assert.Equal(
            PlaybackFfmpegExternalRuntimeAuthorization.BuiltInApproved,
            manifest.Authorization);
        Assert.Equal(PlaybackFfmpegExternalRuntimeReview.Approved, manifest.Review);
        Assert.Equal(PlaybackFfmpegBackendCandidate.BuildId, manifest.BuildId);
        Assert.Equal(PlaybackFfmpegBackendCandidate.FfmpegVersion, manifest.Version);
        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            manifest.LicenseIdentifier);
        Assert.Equal(PlaybackDecoderBackendOrigin.ThirdParty, manifest.Provenance.Origin);
        Assert.Equal(
            PlaybackDecoderBackendDistribution.OptionalExternal,
            manifest.Provenance.Distribution);
        Assert.Equal(
            PlaybackDecoderBackendLicenseReview.Approved,
            manifest.Provenance.LicenseReview);
        Assert.True(manifest.Provenance.ContainsNativeCode);
        Assert.Equal(
            PlaybackDecoderBackendLicenseReview.Approved,
            PlaybackFfmpegBackendCandidate.Provenance.LicenseReview);
        Assert.Equal(
            PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate.Files,
            manifest.RuntimeInventory.Files);
        Assert.Equal(PlaybackFfmpegExternalRuntimeBuildConfiguration.Required, manifest.BuildConfiguration);
        Assert.Equal(5, manifest.Files.Count);
        Assert.Equal(10, manifest.RuntimeInventory.Files.Count);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, true)]
    public void Host_manifest_rejects_disallowed_build_configuration(
        bool gpl,
        bool nonfree,
        bool shared,
        bool @static)
    {
        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            new(gpl, nonfree, shared, @static),
            CreateFiles(CreateLibraryBytes()));

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.ConfigurationRejected,
            creation.Result);
        Assert.Null(creation.Manifest);
    }

    [Fact]
    public async Task Host_manifest_is_host_declared_attested_and_unreviewed()
    {
        var bytes = CreateLibraryBytes();
        var creation = CreateHostManifest(bytes);
        var manifest = Assert.IsType<PlaybackFfmpegExternalRuntimeManifest>(creation.Manifest);

        Assert.Equal(PlaybackFfmpegExternalRuntimeManifestResult.Accepted, creation.Result);
        Assert.True(manifest.IsHostSupplied);
        Assert.Equal(
            PlaybackFfmpegExternalRuntimeLicenseDeclarationAuthority.HostDeclared,
            manifest.LicenseDeclarationAuthority);
        Assert.Equal(
            PlaybackFfmpegExternalRuntimeAuthorization.HostAttested,
            manifest.Authorization);
        Assert.Equal(PlaybackFfmpegExternalRuntimeReview.Unreviewed, manifest.Review);
        Assert.Equal(
            PlaybackDecoderBackendLicenseReview.Pending,
            manifest.Provenance.LicenseReview);
        Assert.Equal("host.ffmpeg.8.1.modified", manifest.BuildId);
        Assert.All(manifest.Files, static file => Assert.Equal(64, file.Sha256.Length));

        var verifiedStreams = CreateStreams(manifest, bytes);
        try
        {
            Assert.Equal(
                PlaybackDecoderBackendRuntimeInventoryVerification.Verified,
                await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                    manifest.RuntimeInventory,
                    verifiedStreams));
        }
        finally
        {
            await DisposeStreamsAsync(verifiedStreams);
        }

        var changed = bytes.ToDictionary(static entry => entry.Key, static entry => entry.Value.ToArray());
        changed[PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec][0] ^= 0x7f;
        var changedStreams = CreateStreams(manifest, changed);
        try
        {
            Assert.Equal(
                PlaybackDecoderBackendRuntimeInventoryVerification.DigestMismatch,
                await PlaybackDecoderBackendRuntimeInventoryVerifier.VerifyAsync(
                    manifest.RuntimeInventory,
                    changedStreams));
        }
        finally
        {
            await DisposeStreamsAsync(changedStreams);
        }
    }

    [Fact]
    public async Task Host_attested_runtime_requires_explicit_policy_before_runtime_access()
    {
        var manifest = Assert.IsType<PlaybackFfmpegExternalRuntimeManifest>(
            CreateHostManifest(CreateLibraryBytes()).Manifest);
        var deniedPolicy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowNativeCode = true,
            AllowBundled = false,
            AllowHostAttestedRuntime = false,
        };
        var allowedPolicy = deniedPolicy with { AllowHostAttestedRuntime = true };

        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.HostAttestedRuntimeNotAllowed,
            deniedPolicy.EvaluateHostAttestedRuntime(manifest.Provenance));
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.None,
            allowedPolicy.EvaluateHostAttestedRuntime(manifest.Provenance));
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.LicenseNotApproved,
            allowedPolicy.Evaluate(manifest.Provenance));

        await using var factory = new PlaybackFfmpegExternalRuntimeGenerationFactory(
            deniedPolicy,
            manifest);
        var result = await factory.CreateAsync(
            "this-path-must-not-be-inspected",
            sourceSession: null,
            output: null,
            decoderGeneration: 0,
            discontinuityGeneration: 0);

        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected,
            result.Result);
        Assert.Null(result.Generation);
        Assert.Equal(
            PlaybackProductionDecoderGenerationFactoryResult.PolicyRejected,
            factory.Snapshot.Result);
    }

    [Theory]
    [InlineData("GPL-3.0-or-later")]
    [InlineData("LicenseRef-NonFree")]
    [InlineData("LGPL-2.1-or-later")]
    [InlineData("")]
    public void Host_manifest_rejects_nonrequired_declared_license_classes(string licenseIdentifier)
    {
        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            licenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            CreateFiles(CreateLibraryBytes()));

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.LicenseRejected,
            creation.Result);
        Assert.Null(creation.Manifest);
    }

    [Fact]
    public void Host_manifest_rejects_missing_role()
    {
        var files = CreateFiles(CreateLibraryBytes()).ToList();
        files.RemoveAt(files.Count - 1);

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.InvalidFileCount,
            creation.Result);
        Assert.Null(creation.Manifest);
    }

    [Fact]
    public void Host_manifest_rejects_duplicate_role()
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[^1] = files[0] with
        {
            FileName = "swscale-9.dll",
            AbiMajor = 9,
        };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.DuplicateRole,
            creation.Result);
        Assert.Null(creation.Manifest);
    }

    [Fact]
    public void Host_manifest_rejects_undefined_role()
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[0] = files[0] with
        {
            Role = (PlaybackFfmpegExternalRuntimeLibraryRole)999,
        };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.UndefinedRole,
            creation.Result);
        Assert.Null(creation.Manifest);
    }

    [Theory]
    [InlineData("../avcodec-62.dll")]
    [InlineData("renamed-codec.dll")]
    [InlineData("AVCODEC-62.DLL")]
    public void Host_manifest_rejects_unrecognizable_or_path_bearing_file_name(string fileName)
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[0] = files[0] with { FileName = fileName };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.FileNameRejected,
            creation.Result);
    }

    [Fact]
    public void Host_manifest_rejects_wrong_abi_major()
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[0] = files[0] with { AbiMajor = 61 };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.AbiMajorRejected,
            creation.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Host_manifest_rejects_nonpositive_file_length(long length)
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[0] = files[0] with { Length = length };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.FileLengthRejected,
            creation.Result);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    public void Host_manifest_rejects_invalid_digest(string digest)
    {
        var files = CreateFiles(CreateLibraryBytes()).ToArray();
        files[0] = files[0] with { Sha256 = digest };

        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1",
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            files);

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.DigestRejected,
            creation.Result);
    }

    [Theory]
    [InlineData("contains path/separator")]
    [InlineData("contains:command")]
    [InlineData("contains url?query")]
    public void Host_manifest_rejects_unbounded_identity_syntax(string buildId)
    {
        var creation = PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            buildId,
            "8.1-custom",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            CreateFiles(CreateLibraryBytes()));

        Assert.Equal(
            PlaybackFfmpegExternalRuntimeManifestResult.InvalidBuildId,
            creation.Result);
    }

    [Fact]
    public void Reviewed_external_manifest_requires_explicit_third_party_and_native_policy()
    {
        var manifest = PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate;
        var approvedPolicy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = true,
            AllowNativeCode = true,
            AllowBundled = false,
        };
        var rejectedPolicy = new PlaybackDecoderBackendPolicy
        {
            AllowThirdParty = false,
            AllowNativeCode = true,
            AllowBundled = false,
        };

        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.None,
            approvedPolicy.Evaluate(manifest.Provenance));
        Assert.Equal(
            PlaybackDecoderBackendPolicyRejection.ThirdPartyNotAllowed,
            rejectedPolicy.Evaluate(manifest.Provenance));
    }

    [Fact]
    public void Manifest_public_contract_contains_no_runtime_path_or_acquisition_instruction()
    {
        var propertyNames = typeof(PlaybackFfmpegExternalRuntimeManifest)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, static name =>
            name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Directory", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Command", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Script", StringComparison.OrdinalIgnoreCase));
    }

    private static PlaybackFfmpegExternalRuntimeManifestCreation CreateHostManifest(
        Dictionary<PlaybackFfmpegExternalRuntimeLibraryRole, byte[]> bytes) =>
        PlaybackFfmpegExternalRuntimeManifest.CreateHostSupplied(
            "host.ffmpeg.8.1.modified",
            "8.1-custom+local1",
            PlaybackFfmpegExternalRuntimeManifest.RequiredDeclaredLicenseIdentifier,
            PlaybackFfmpegExternalRuntimeBuildConfiguration.Required,
            CreateFiles(bytes));

    private static IEnumerable<PlaybackFfmpegExternalRuntimeFile> CreateFiles(
        Dictionary<PlaybackFfmpegExternalRuntimeLibraryRole, byte[]> bytes)
    {
        yield return File(
            PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec,
            "avcodec-62.dll",
            62,
            bytes);
        yield return File(
            PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat,
            "avformat-62.dll",
            62,
            bytes);
        yield return File(
            PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil,
            "avutil-60.dll",
            60,
            bytes);
        yield return File(
            PlaybackFfmpegExternalRuntimeLibraryRole.SwResample,
            "swresample-6.dll",
            6,
            bytes);
        yield return File(
            PlaybackFfmpegExternalRuntimeLibraryRole.SwScale,
            "swscale-9.dll",
            9,
            bytes);
    }

    private static PlaybackFfmpegExternalRuntimeFile File(
        PlaybackFfmpegExternalRuntimeLibraryRole role,
        string fileName,
        int abiMajor,
        Dictionary<PlaybackFfmpegExternalRuntimeLibraryRole, byte[]> bytes)
    {
        var content = bytes[role];
        return new(
            role,
            fileName,
            abiMajor,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)));
    }

    private static Dictionary<PlaybackFfmpegExternalRuntimeLibraryRole, byte[]>
        CreateLibraryBytes() => new()
        {
            [PlaybackFfmpegExternalRuntimeLibraryRole.AvCodec] = [1, 2, 3, 4],
            [PlaybackFfmpegExternalRuntimeLibraryRole.AvFormat] = [5, 6, 7],
            [PlaybackFfmpegExternalRuntimeLibraryRole.AvUtil] = [8, 9],
            [PlaybackFfmpegExternalRuntimeLibraryRole.SwResample] = [10, 11, 12, 13, 14],
            [PlaybackFfmpegExternalRuntimeLibraryRole.SwScale] = [15, 16, 17, 18, 19, 20],
        };

    private static Dictionary<string, Stream> CreateStreams(
        PlaybackFfmpegExternalRuntimeManifest manifest,
        Dictionary<PlaybackFfmpegExternalRuntimeLibraryRole, byte[]> bytes) =>
        manifest.Files.ToDictionary(
            static file => file.FileName,
            file => (Stream)new MemoryStream(bytes[file.Role].ToArray()),
            StringComparer.OrdinalIgnoreCase);

    private static async ValueTask DisposeStreamsAsync(
        Dictionary<string, Stream> streams)
    {
        foreach (var stream in streams.Values)
        {
            await stream.DisposeAsync();
        }
    }
}
