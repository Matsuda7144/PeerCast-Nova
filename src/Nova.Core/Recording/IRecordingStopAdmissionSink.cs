namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Opt-in exact-session stop admission. Legacy StopAsync does not prove admission.
/// </summary>
public interface IRecordingStopAdmissionSink : IRecordingSink
{
    RecordingSinkStopAdmissionResult RequestStopAdmission(
        RecordingSinkStopRequest request,
        CancellationToken admissionToken = default);

    bool TryGetStopAdmission(
        RecordingSinkSession session,
        out RecordingSinkStopAdmission? admission);
}
