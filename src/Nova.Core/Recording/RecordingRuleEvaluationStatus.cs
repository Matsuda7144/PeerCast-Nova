namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Diagnostic status for one recording rule evaluation.
/// </summary>
public enum RecordingRuleEvaluationStatus
{
    Disabled,
    NoMatch,
    Matched,
    Selected,
}
