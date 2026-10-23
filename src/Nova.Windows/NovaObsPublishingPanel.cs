namespace PeerCast.Nova.Windows;

public enum NovaObsPublishingTone
{
    Normal,
    Active,
    Warning,
    Special,
}

/// <summary>Immutable presentation values; never owns the publisher or OBS.</summary>
public sealed record NovaObsPublishingPanel(
    long Generation,
    Guid SessionId,
    long Revision,
    bool IsTerminal,
    string Server,
    string PublishName,
    string ListenerText,
    string StateText,
    string DiagnosticText,
    string TechnicalDetails,
    string? HttpStreamUrl,
    NovaObsPublishingTone Tone);
