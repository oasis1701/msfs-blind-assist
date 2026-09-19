using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using static Microsoft.FlightSimulator.SimConnect.SimConnect;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// The simulator camera over SimConnect: one fixed data definition for <c>CAMERA STATE</c> and
/// the two halves of <c>CAMERA VIEW TYPE AND INDEX</c>, a one-shot read, and the write that moves
/// the camera. Backs <see cref="InstrumentViewSwitcher"/>, which AI display reads use to put a
/// particular instrument view in front of the capture (the MD-11's PFD/ND/EAD/SD/standby).
///
/// Live-verified on MSFS 2024 (2026-09-08): both SimVars are settable, the write applies within
/// a frame and the read reports it, from pilot, instrument and quickview cameras alike.
/// </summary>
public partial class SimConnectManager : ICameraViewIo
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CameraViewData
    {
        public double State;
        public double ViewType;
        public double ViewIndex;
    }

    // Each read goes out under its OWN request id — one of the CameraReadIdCount ids counting up
    // from REQUEST_CAMERA_VIEW, rotating — and only the answer to that id completes it (the
    // dispatch matches the range before its switch). A read abandoned by a timeout or a failed
    // request is forgotten at once, so its late answer lands on nobody; the single shared waiter
    // this replaces let that late answer complete the NEXT read. Released with null on
    // disconnect / aircraft switch (FailCameraViewRead).
    internal const int CameraReadIdCount = 8;
    private readonly CameraReadWaiters _cameraReads =
        new((int)DATA_REQUESTS.REQUEST_CAMERA_VIEW, CameraReadIdCount);

    /// <summary>
    /// Registers the camera definition with the other fixed definitions. Its own try/catch, like
    /// the GSX one beside it: a failure here degrades display reads to "capture the current
    /// view" and must not take the bulk registration down with it.
    /// </summary>
    private void RegisterCameraViewDefinition()
    {
        try
        {
            var sc = simConnect!;
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA STATE", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)0);
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA VIEW TYPE AND INDEX:0", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)1);
            sc.AddToDataDefinition(DATA_DEFINITIONS.DEF_CAMERA_VIEW, "CAMERA VIEW TYPE AND INDEX:1", "number",
                SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)2);
            sc.RegisterDataDefineStruct<CameraViewData>(DATA_DEFINITIONS.DEF_CAMERA_VIEW);
            Log.Debug("SimConnect", "Registered camera view definition");
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Camera view registration failed (display reads will capture the current view): {ex.Message}");
        }
    }

    /// <summary>
    /// One-shot read of the camera. Null when not connected, when the request cannot be issued,
    /// or when nothing arrives within <paramref name="timeoutMs"/> — never a stale value.
    /// </summary>
    public Task<CameraViewReading?> ReadCameraViewAsync(int timeoutMs)
    {
        if (!IsConnected || simConnect == null) return Task.FromResult<CameraViewReading?>(null);

        var (requestId, answer) = _cameraReads.Begin();
        if (requestId == CameraReadWaiters.NoRequestId)
        {
            // Every id in the range is still awaited (display reads run one at a time, so this is
            // not expected): a read without an id of its own could not be told from theirs.
            Log.Debug("SimConnect", "Camera view read skipped: every camera request id is still awaited");
            return answer;
        }

        try
        {
            simConnect.RequestDataOnSimObject((DATA_REQUESTS)requestId,
                DATA_DEFINITIONS.DEF_CAMERA_VIEW, SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Debug("SimConnect", $"Camera view request {requestId} failed: {ex.Message}");
            _cameraReads.Abandon(requestId);
            return Task.FromResult<CameraViewReading?>(null);
        }

        return AwaitCameraViewAsync(requestId, answer, timeoutMs);
    }

    // No ConfigureAwait(false): the caller (an AI display read on the UI thread) writes SimVars
    // right after this returns, and SimConnect calls stay on the UI thread in this app.
    private async Task<CameraViewReading?> AwaitCameraViewAsync(int requestId, Task<CameraViewReading?> answer, int timeoutMs)
    {
        try
        {
            return await answer.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (TimeoutException)
        {
            // A timed-out read is abandoned: the late answer to ITS id must not complete a later read.
            _cameraReads.Abandon(requestId);
            return null;
        }
    }

    /// <summary>
    /// Called from the dispatch for a request id in the camera range: completes the read that
    /// asked under <paramref name="requestId"/>, if it is still waiting. The late answer to an
    /// abandoned read completes nothing.
    /// </summary>
    private void CompleteCameraViewRead(int requestId, CameraViewData data)
    {
        var reading = new CameraViewReading(
            (int)Math.Round(data.State),
            (int)Math.Round(data.ViewType),
            (int)Math.Round(data.ViewIndex));
        if (!_cameraReads.Complete(requestId, reading))
            Log.Debug("SimConnect", $"Dropped a late camera view answer (request {requestId}): its read had already given up.");
    }

    /// <summary>A disconnect or aircraft switch means no delivery is coming: release every waiting read with null.</summary>
    private void FailCameraViewRead() => _cameraReads.FailAll();

    /// <summary>
    /// Moves the camera: the view type first, then the index within it (the order verified live —
    /// index alone only works within the current type).
    /// </summary>
    public void SetCameraView(int viewType, int viewIndex)
    {
        SetSimVar("CAMERA VIEW TYPE AND INDEX:0", viewType);
        SetSimVar("CAMERA VIEW TYPE AND INDEX:1", viewIndex);
    }

    Task<CameraViewReading?> ICameraViewIo.ReadAsync(int timeoutMs) => ReadCameraViewAsync(timeoutMs);

    void ICameraViewIo.Set(int viewType, int viewIndex) => SetCameraView(viewType, viewIndex);
}
