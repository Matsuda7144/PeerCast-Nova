namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Secondary, synchronous capture of immutable terminal facts. Implementations
/// must finish promptly and must not call recording commands or native UI.
/// Neither callback is allowed to change the recorder's result.
/// </summary>
public interface IRecordingSessionTerminalObserver
{
    void ObserveTerminal(RecordingSessionTerminalFact fact);

    /// <summary>Signals a history publication gap, not a recording fault.</summary>
    void RecordPublicationFailure();
}
