using System.Collections.ObjectModel;

namespace PeerCast.Nova.App;

internal enum GenesisYellowPageSettingsMutationOutcome
{
    Applied,
    DuplicateId,
    NotFound,
    Invalid,
    PersistenceFailed,
}

internal sealed record GenesisYellowPageSettingsMutationResult(
    GenesisYellowPageSettingsMutationOutcome Outcome,
    string? ErrorMessage)
{
    public bool Succeeded => Outcome == GenesisYellowPageSettingsMutationOutcome.Applied;
}

internal sealed record GenesisYellowPageSettingsDraft(
    string? Id,
    string? Name,
    string? IndexUrl,
    string? FallbackTrackerUrl,
    bool IsEnabled,
    string? Namespace);

/// <summary>
/// Framework-neutral editing contract for the application-owned Yellow Page
/// configuration. The Yellow Page id is the stable identity: edits replace the
/// metadata for that id rather than silently creating a different provider.
/// Successful mutations are durably persisted when the shared configuration has
/// an application persistence boundary; failed persistence rolls memory back.
/// </summary>
internal sealed class GenesisYellowPageSettingsModel
{
    private const string DuplicateIdMessage = "A Yellow Page with this id already exists.";
    private const string NotFoundMessage = "The Yellow Page was not found.";
    private const string InvalidIdMessage = "Enter a valid Yellow Page id.";
    private const string StableIdMessage = "The Yellow Page id cannot be changed while editing.";
    private const string InvalidNameMessage = "Enter a valid Yellow Page name.";
    private const string InvalidIndexUrlMessage =
        "Enter a valid HTTP or HTTPS Yellow Page index URL.";
    private const string InvalidTrackerUrlMessage =
        "Enter a valid pcp:// Yellow Page fallback tracker URL.";
    private const string InvalidNamespaceMessage = "Enter a valid Yellow Page namespace.";
    private const string InvalidDefinitionMessage = "The Yellow Page settings are invalid.";
    private const string PersistenceFailedMessage =
        "The Yellow Page settings could not be saved. No changes were applied.";

    private readonly GenesisYellowPageConfiguration _configuration;

    public GenesisYellowPageSettingsModel(GenesisYellowPageConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public ReadOnlyCollection<GenesisYellowPageDefinition> Entries =>
        Array.AsReadOnly(_configuration.Definitions.ToArray());

    public GenesisYellowPageSettingsMutationResult Add(
        GenesisYellowPageSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var created = TryCreateDefinition(draft);
        if (created.Result is not null)
        {
            return created.Result;
        }

        var before = _configuration.Definitions;
        if (!_configuration.Add(created.Definition!))
        {
            return Failure(
                GenesisYellowPageSettingsMutationOutcome.DuplicateId,
                DuplicateIdMessage);
        }

        return PersistOrRollback(before);
    }

    public GenesisYellowPageSettingsMutationResult Edit(
        string stableId,
        GenesisYellowPageSettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string normalizedStableId;
        try
        {
            normalizedStableId = NormalizeStableId(stableId);
        }
        catch (ArgumentException)
        {
            return Invalid(InvalidIdMessage);
        }

        if (!string.Equals(
                normalizedStableId,
                draft.Id?.Trim(),
                StringComparison.Ordinal))
        {
            return Invalid(StableIdMessage);
        }

        var created = TryCreateDefinition(draft);
        if (created.Result is not null)
        {
            return created.Result;
        }

        var before = _configuration.Definitions;
        if (!_configuration.Replace(created.Definition!))
        {
            return Failure(
                GenesisYellowPageSettingsMutationOutcome.NotFound,
                NotFoundMessage);
        }

        if (before.SequenceEqual(_configuration.Definitions))
        {
            return Applied();
        }

        return PersistOrRollback(before);
    }

    public GenesisYellowPageSettingsMutationResult Remove(string id)
    {
        var before = _configuration.Definitions;
        try
        {
            if (!_configuration.Remove(id))
            {
                return Failure(
                    GenesisYellowPageSettingsMutationOutcome.NotFound,
                    NotFoundMessage);
            }
        }
        catch (ArgumentException)
        {
            return Invalid(InvalidIdMessage);
        }

        return PersistOrRollback(before);
    }

    public GenesisYellowPageSettingsMutationResult SetEnabled(string id, bool isEnabled)
    {
        var before = _configuration.Definitions;
        try
        {
            if (!_configuration.SetEnabled(id, isEnabled))
            {
                return Failure(
                    GenesisYellowPageSettingsMutationOutcome.NotFound,
                    NotFoundMessage);
            }
        }
        catch (ArgumentException)
        {
            return Invalid(InvalidIdMessage);
        }

        if (before.SequenceEqual(_configuration.Definitions))
        {
            return Applied();
        }

        return PersistOrRollback(before);
    }

    private GenesisYellowPageSettingsMutationResult PersistOrRollback(
        IReadOnlyList<GenesisYellowPageDefinition> before)
    {
        if (_configuration.TryPersistCurrentSnapshot())
        {
            return Applied();
        }

        _configuration.RestoreSnapshot(before);
        return Failure(
            GenesisYellowPageSettingsMutationOutcome.PersistenceFailed,
            PersistenceFailedMessage);
    }

    private static DefinitionCreation TryCreateDefinition(
        GenesisYellowPageSettingsDraft draft)
    {
        if (!Uri.TryCreate(draft.IndexUrl?.Trim(), UriKind.Absolute, out var indexUri))
        {
            return new(null, Invalid(InvalidIndexUrlMessage));
        }

        Uri? fallbackTrackerUri = null;
        if (!string.IsNullOrWhiteSpace(draft.FallbackTrackerUrl) &&
            !Uri.TryCreate(
                draft.FallbackTrackerUrl.Trim(),
                UriKind.Absolute,
                out fallbackTrackerUri))
        {
            return new(null, Invalid(InvalidTrackerUrlMessage));
        }

        try
        {
            return new(
                new GenesisYellowPageDefinition(
                    draft.Id!,
                    draft.Name!,
                    indexUri,
                    fallbackTrackerUri,
                    draft.IsEnabled,
                    draft.Namespace),
                null);
        }
        catch (ArgumentException exception)
        {
            return new(null, Invalid(MapValidationMessage(exception.ParamName)));
        }
    }

    private static string MapValidationMessage(string? parameterName) => parameterName switch
    {
        "id" => InvalidIdMessage,
        "name" => InvalidNameMessage,
        "indexUri" => InvalidIndexUrlMessage,
        "fallbackTrackerUri" => InvalidTrackerUrlMessage,
        "yellowPageNamespace" => InvalidNamespaceMessage,
        _ => InvalidDefinitionMessage,
    };

    private static string NormalizeStableId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var normalized = id.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Yellow Page id is required.", nameof(id));
        }

        return normalized;
    }

    private static GenesisYellowPageSettingsMutationResult Applied() =>
        new(GenesisYellowPageSettingsMutationOutcome.Applied, ErrorMessage: null);

    private static GenesisYellowPageSettingsMutationResult Invalid(string message) =>
        Failure(GenesisYellowPageSettingsMutationOutcome.Invalid, message);

    private static GenesisYellowPageSettingsMutationResult Failure(
        GenesisYellowPageSettingsMutationOutcome outcome,
        string message) =>
        new(outcome, message);

    private sealed record DefinitionCreation(
        GenesisYellowPageDefinition? Definition,
        GenesisYellowPageSettingsMutationResult? Result);
}
