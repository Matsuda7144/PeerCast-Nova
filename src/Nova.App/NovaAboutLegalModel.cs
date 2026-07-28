using System.Reflection;
using System.Text;

namespace PeerCast.Nova.App;

public sealed record NovaAboutLegalModel
{
    internal NovaAboutLegalModel(
        string productName,
        string projectLicense,
        string externalRuntimeName,
        string externalRuntimeDistribution,
        string notice,
        string provenance,
        string runtimeReplacement)
    {
        ProductName = productName;
        ProjectLicense = projectLicense;
        ExternalRuntimeName = externalRuntimeName;
        ExternalRuntimeDistribution = externalRuntimeDistribution;
        Notice = notice;
        Provenance = provenance;
        RuntimeReplacement = runtimeReplacement;
    }

    public string ProductName { get; }

    public string ProjectLicense { get; }

    public string ExternalRuntimeName { get; }

    public string ExternalRuntimeDistribution { get; }

    public string Notice { get; }

    public string Provenance { get; }

    public string RuntimeReplacement { get; }
}

public enum NovaAboutLegalMaterialResult
{
    None,
    Loaded,
    Rejected,
}

public sealed record NovaAboutLegalMaterial
{
    private const int MaximumDocumentCharacters = 16_384;
    private const int MaximumTotalCharacters = 32_768;
    private const string NoticeResource =
        "PeerCast.Nova.App.Legal.FFmpeg.NOTICE.md";
    private const string ProvenanceResource =
        "PeerCast.Nova.App.Legal.FFmpeg.PROVENANCE.md";
    private const string RuntimeReplacementResource =
        "PeerCast.Nova.App.Legal.FFmpeg.RUNTIME-REPLACEMENT.md";

    private NovaAboutLegalMaterial(
        NovaAboutLegalMaterialResult result,
        NovaAboutLegalModel? model)
    {
        Result = result;
        Model = model;
    }

    public NovaAboutLegalMaterialResult Result { get; }

    public NovaAboutLegalModel? Model { get; }

    public static NovaAboutLegalMaterial Load() =>
        Load(typeof(NovaAboutLegalMaterial).Assembly);

    internal static NovaAboutLegalMaterial Load(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!TryRead(assembly, NoticeResource, out var notice) ||
            !TryRead(assembly, ProvenanceResource, out var provenance) ||
            !TryRead(assembly, RuntimeReplacementResource, out var replacement) ||
            notice.Length + provenance.Length + replacement.Length >
                MaximumTotalCharacters)
        {
            return new(NovaAboutLegalMaterialResult.Rejected, null);
        }

        return new(
            NovaAboutLegalMaterialResult.Loaded,
            new(
                "PeerCast Nova",
                "Apache License 2.0",
                "FFmpeg",
                "Optional external shared runtime; not bundled by PeerCast Nova",
                notice,
                provenance,
                replacement));
    }

    private static bool TryRead(
        Assembly assembly,
        string resourceName,
        out string value)
    {
        try
        {
            value = string.Empty;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null || stream.Length <= 0 ||
                stream.Length > MaximumDocumentCharacters)
            {
                return false;
            }

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1_024,
                leaveOpen: false);
            value = reader.ReadToEnd();
            return value.Length is > 0 and <= MaximumDocumentCharacters;
        }
        catch (DecoderFallbackException)
        {
            value = string.Empty;
            return false;
        }
        catch (IOException)
        {
            value = string.Empty;
            return false;
        }
    }
}
