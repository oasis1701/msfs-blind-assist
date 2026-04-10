using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Programs the PMDG 777 FMC using SimBrief OFP data via CDU key events.
///
/// Two-phase workflow:
///
///   Phase 1 — Initial programming (call ProgramAsync first time):
///     MENU → IDENT → POS INIT → enter origin, copy GPS position, insert IRS
///     RTE page → request SimBrief uplink → load payload → set fuel → select route
///     Wait for uplink, load, activate, EXEC
///     Navigate to DEP/ARR and pause for user to program SID/runway.
///
///   User manually programs SID and departure runway on DEP/ARR page.
///
///   Phase 2 — Performance programming (call ProgramAsync second time):
///     MENU → INIT_REF (lands on PERF INIT directly — route/IRS already filled) → load ZFW
///     THRUST LIM → set thrust rating, max climb, SEL OAT if available
///     TAKEOFF REF → enter flaps, accept V-speeds, send CG
///     LEGS → RTE DATA → load winds
///     VNAV → descent page → load winds
///     MENU → FS ACTIONS → DOORS → close all, arm all
///     Return to LEGS, announce complete.
///
/// Timing:
///   350 ms between each key press
///   400 ms extra delay for repeated characters
///   500 ms settle after LSK press
///   1 500 ms settle after page navigation
///   15 000 ms wait for ACARS uplinks
///   5 000 ms wait after "close all doors"
/// </summary>
public class FmcProgrammingService
{
    private readonly SimConnectManager _simConnect;

    private const int KeyDelay    = 350;
    private const int RepeatDelay = 400;
    private const int LskSettle   = 500;
    private const int PageSettle  = 1500;
    private const int UplinkWait  = 15000;
    private const int DoorWait    = 5000;

    // -----------------------------------------------------------------------
    // Phase tracking
    // -----------------------------------------------------------------------

    public enum ProgrammingPhase { NotStarted, WaitingForSidRwy, Complete }
    public ProgrammingPhase Phase { get; private set; } = ProgrammingPhase.NotStarted;

    public void Reset() => Phase = ProgrammingPhase.NotStarted;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public FmcProgrammingService(SimConnectManager simConnect)
    {
        _simConnect = simConnect;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// First call: runs Phase 1 (basic route + IRS).
    /// Second call (when Phase == WaitingForSidRwy): runs Phase 2 (perf, winds, doors).
    /// </summary>
    public async Task<FmcProgramResult> ProgramAsync(SimBriefOFP ofp,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!_simConnect.IsConnected)
            return FmcProgramResult.Error("SimConnect not connected.");

        if (Phase == ProgrammingPhase.WaitingForSidRwy)
            return await RunPhase2Async(ofp, progress, ct);

        Phase = ProgrammingPhase.NotStarted;
        return await RunPhase1Async(ofp, progress, ct);
    }

    // -----------------------------------------------------------------------
    // Phase 1: IRS alignment + route uplink
    // -----------------------------------------------------------------------

    private async Task<FmcProgramResult> RunPhase1Async(SimBriefOFP ofp,
        IProgress<string>? progress, CancellationToken ct)
    {
        // 1. MENU — ensure we start from the top-level menu
        progress?.Report("Navigating to menu...");
        await SendKey("MENU", ct);
        await Delay(PageSettle, ct);

        // 2. L1 — IDENT page
        progress?.Report("IDENT page...");
        await SendKey("L1", ct);
        await Delay(PageSettle, ct);

        // 3. R6 — POS INIT page
        progress?.Report("POS INIT page...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 4. Enter origin airport at 2L (reference airport for IRS alignment)
        if (!string.IsNullOrWhiteSpace(ofp.OriginIcao))
        {
            progress?.Report($"Entering origin: {ofp.OriginIcao}...");
            await TypeText(ofp.OriginIcao, ct);
            await SendKey("L2", ct);
            await Delay(LskSettle, ct);
        }

        // 5. NEXT PAGE — POS INIT page 2 (GPS position)
        progress?.Report("POS INIT page 2...");
        await SendKey("NEXT_PAGE", ct);
        await Delay(PageSettle, ct);

        // 6. L3 — copy GPS position into scratchpad
        progress?.Report("Copying GPS position...");
        await SendKey("L3", ct);
        await Delay(LskSettle, ct);

        // 7. PREV PAGE — back to POS INIT page 1
        progress?.Report("Back to POS INIT page 1...");
        await SendKey("PREV_PAGE", ct);
        await Delay(PageSettle, ct);

        // 8. R5 — insert GPS position into IRS
        progress?.Report("Inserting position into IRS...");
        await SendKey("R5", ct);
        await Delay(LskSettle, ct);

        // 9. RTE — route page
        progress?.Report("Route page...");
        await SendKey("RTE", ct);
        await Delay(PageSettle, ct);

        // 10. L3 — ON REQUEST (opens ACARS uplink options)
        progress?.Report("On request...");
        await SendKey("L3", ct);
        await Delay(LskSettle, ct);

        // 11. L2 — request from SimBrief
        progress?.Report("Requesting route from SimBrief...");
        await SendKey("L2", ct);
        await Delay(LskSettle, ct);

        // 12. L5 — load payload
        progress?.Report("Loading payload...");
        await SendKey("L5", ct);
        await Delay(LskSettle, ct);

        // 13. R5 — set fuel
        progress?.Report("Setting fuel...");
        await SendKey("R5", ct);
        await Delay(LskSettle, ct);

        // 14. R6 — select this route
        progress?.Report("Selecting route...");
        await SendKey("R6", ct);
        await Delay(LskSettle, ct);

        // 15. Wait 15 seconds for route uplink to arrive
        progress?.Report("Waiting for route uplink (15 sec)...");
        await Delay(UplinkWait, ct);

        // 16. L4 — load the route
        progress?.Report("Loading route...");
        await SendKey("L4", ct);
        await Delay(LskSettle, ct);

        // 17. Wait 15 more seconds for route to process
        progress?.Report("Waiting for route to load (15 sec)...");
        await Delay(UplinkWait, ct);

        // 18. R6 — activate route
        progress?.Report("Activating route...");
        await SendKey("R6", ct);
        await Delay(LskSettle, ct);

        // 19. EXEC — execute
        progress?.Report("Executing...");
        await SendKey("EXEC", ct);
        await Delay(PageSettle, ct);

        // 20. DEP/ARR — navigate so user can see the page to program SID/runway
        progress?.Report("DEP/ARR page — program SID and runway...");
        await SendKey("DEP_ARR", ct);
        await Delay(PageSettle, ct);

        Phase = ProgrammingPhase.WaitingForSidRwy;

        var result = new FmcProgramResult { Success = true };
        result.WaitingForUser  = true;
        result.WaitingMessage  = "Please program runway and SID, then press Program FMC again to complete.";
        return result;
    }

    // -----------------------------------------------------------------------
    // Phase 2: performance init, winds, doors
    // -----------------------------------------------------------------------

    private async Task<FmcProgramResult> RunPhase2Async(SimBriefOFP ofp,
        IProgress<string>? progress, CancellationToken ct)
    {
        var result = new FmcProgramResult { Success = true };

        // 1. MENU — start from a known state
        progress?.Report("Navigating to menu...");
        await SendKey("MENU", ct);
        await Delay(PageSettle, ct);

        // 2. INIT REF — goes directly to PERF INIT at this point (route/IRS already
        //    filled in Phase 1, so the CDU opens PERF INIT rather than IDENT)
        progress?.Report("PERF INIT page...");
        await SendKey("INIT_REF", ct);
        await Delay(PageSettle, ct);

        // 3. R5 — accept PERF INIT uplink
        progress?.Report("Accepting PERF INIT uplink...");
        await SendKey("R5", ct);
        await Delay(LskSettle, ct);

        // 4. L3 twice with 2 s gap — set ZFW
        progress?.Report("Setting ZFW...");
        await SendKey("L3", ct);
        await Delay(2000, ct);
        await SendKey("L3", ct);
        await Delay(LskSettle, ct);
        result.ProgrammedFields.Add("ZFW: loaded");

        // 5. R6 — THRUST LIM page
        progress?.Report("THRUST LIM page...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 6. SEL OAT — type assumed temp and send to L1 (must be before selecting rating)
        if (!string.IsNullOrWhiteSpace(ofp.TakeoffAssumedTemp))
        {
            string tempStr = FormatAssumedTemp(ofp.TakeoffAssumedTemp);
            progress?.Report($"SEL OAT: {tempStr}°C...");
            await TypeText(tempStr, ct);
            await SendKey("L1", ct);
            await Delay(LskSettle, ct);
            result.ProgrammedFields.Add($"SEL OAT: {tempStr}°C");
        }

        // 7. Select thrust rating: TO → L2, TO1 → L3, TO2 → L4
        string thrustKey = GetThrustRatingKey(ofp.TakeoffEngineRating);
        string ratingLabel = string.IsNullOrWhiteSpace(ofp.TakeoffEngineRating) ? "TO" : ofp.TakeoffEngineRating.Trim().ToUpperInvariant();
        progress?.Report($"Thrust rating: {ratingLabel}...");
        await SendKey(thrustKey, ct);
        await Delay(LskSettle, ct);
        result.ProgrammedFields.Add($"Thrust rating: {ratingLabel}");

        // 8. R2 — CLB (max climb)
        progress?.Report("CLB...");
        await SendKey("R2", ct);
        await Delay(LskSettle, ct);

        // 9. R6 — TAKEOFF REF page (V-speeds + CG)
        progress?.Report("TAKEOFF REF page...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 10. Enter flap setting at L1
        if (!string.IsNullOrWhiteSpace(ofp.TakeoffFlaps))
        {
            progress?.Report($"Takeoff flaps: {ofp.TakeoffFlaps}...");
            await TypeText(ofp.TakeoffFlaps, ct);
            await SendKey("L1", ct);
            await Delay(LskSettle, ct);
            result.ProgrammedFields.Add($"Takeoff flaps: {ofp.TakeoffFlaps}");
        }

        // 11. R1, R2, R3 — accept V1, Vr, V2 (2 s between each)
        progress?.Report("Accepting V1...");
        await SendKey("R1", ct);
        await Delay(2000, ct);
        progress?.Report("Accepting Vr...");
        await SendKey("R2", ct);
        await Delay(2000, ct);
        progress?.Report("Accepting V2...");
        await SendKey("R3", ct);
        await Delay(LskSettle, ct);
        result.ProgrammedFields.Add("V-speeds: accepted");

        // 12. L3 twice — send CG
        progress?.Report("Sending CG...");
        await SendKey("L3", ct);
        await Delay(LskSettle, ct);
        await SendKey("L3", ct);
        await Delay(LskSettle, ct);
        result.ProgrammedFields.Add("CG: loaded");

        // 13. LEGS — legs page
        progress?.Report("LEGS page...");
        await SendKey("LEGS", ct);
        await Delay(PageSettle, ct);

        // 15. R6 — RTE DATA page
        progress?.Report("RTE DATA page...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 16. R6 — load wind data
        progress?.Report("Loading wind data...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 17. VNAV — VNAV page (starts at CLIMB, page 1)
        progress?.Report("VNAV page...");
        await SendKey("VNAV", ct);
        await Delay(PageSettle, ct);

        // 18. NEXT PAGE → CRUISE (page 2)
        await SendKey("NEXT_PAGE", ct);
        await Delay(PageSettle, ct);

        // 19. NEXT PAGE → DESCENT (page 3 — last sub-page)
        progress?.Report("VNAV DESCENT page...");
        await SendKey("NEXT_PAGE", ct);
        await Delay(PageSettle, ct);

        // 20. R5 — open wind load option
        progress?.Report("Descent winds...");
        await SendKey("R5", ct);
        await Delay(LskSettle, ct);

        // 21. L6 — load
        await SendKey("L6", ct);
        await Delay(LskSettle, ct);
        result.ProgrammedFields.Add("Descent winds: loaded");

        // 22. MENU
        progress?.Report("Navigating to menu...");
        await SendKey("MENU", ct);
        await Delay(PageSettle, ct);

        // 23. R6 — FS ACTIONS
        progress?.Report("FS Actions...");
        await SendKey("R6", ct);
        await Delay(PageSettle, ct);

        // 24. L3 — DOORS
        progress?.Report("Doors page...");
        await SendKey("L3", ct);
        await Delay(PageSettle, ct);

        // 25. L1 — close all doors
        progress?.Report("Closing all doors...");
        await SendKey("L1", ct);
        await Delay(DoorWait, ct);

        // 26. L2 — arm all doors
        progress?.Report("Arming all doors...");
        await SendKey("L2", ct);
        await Delay(LskSettle, ct);

        // 27. LEGS — return to legs page
        progress?.Report("Returning to LEGS page...");
        await SendKey("LEGS", ct);
        await Delay(PageSettle, ct);

        Phase = ProgrammingPhase.Complete;
        result.CompletionMessage = "FMC programming complete.";
        return result;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Strip any decimal portion from an assumed OAT string so we type a clean integer (e.g. "55.0" → "55").</summary>
    private static string FormatAssumedTemp(string raw)
    {
        string s = raw.Trim();
        int dot = s.IndexOf('.');
        return dot >= 0 ? s[..dot] : s;
    }

    /// <summary>Map SimBrief engine rating string to the THRUST LIM page softkey.</summary>
    private static string GetThrustRatingKey(string engineRating)
    {
        string r = engineRating.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        return r switch
        {
            "TO1" => "L3",
            "TO2" => "L4",
            _     => "L2",  // "TO" or empty → default TO
        };
    }

    // -----------------------------------------------------------------------
    // CDU key sending
    // -----------------------------------------------------------------------

    private string? _previousKey;

    private async Task SendKey(string eventSuffix, CancellationToken ct)
    {
        string eventName = $"EVT_CDU_L_{eventSuffix}";
        if (!PMDG777Definition.EventIds.TryGetValue(eventName, out int evId)) return;
        _simConnect.SendPMDGEvent(eventName, (uint)evId, 1);
        await Delay(KeyDelay, ct);
    }

    private async Task TypeText(string text, CancellationToken ct)
    {
        _previousKey = null;
        foreach (char c in text.ToUpperInvariant())
        {
            ct.ThrowIfCancellationRequested();

            string? suffix = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                >= '0' and <= '9' => c.ToString(),
                '.'               => "DOT",
                '/'               => "SLASH",
                ' '               => "SPACE",
                '-' or '+'        => "PLUS_MINUS",
                _                 => null
            };

            if (suffix == null) continue;

            if (suffix == _previousKey)
                await Delay(RepeatDelay, ct);

            await SendKey(suffix, ct);
            _previousKey = suffix;
        }
        _previousKey = null;
    }

    private static Task Delay(int ms, CancellationToken ct) => Task.Delay(ms, ct);

    private static FmcProgramResult Error(string msg)
        => new FmcProgramResult { Success = false, ErrorMessage = msg };
}

/// <summary>Result of an FMC programming phase.</summary>
public class FmcProgramResult
{
    public bool    Success          { get; set; }
    public string? ErrorMessage     { get; set; }

    /// <summary>True when Phase 1 completes; user must program SID/runway before Phase 2.</summary>
    public bool    WaitingForUser   { get; set; }
    /// <summary>Message to announce when waiting for the user.</summary>
    public string? WaitingMessage   { get; set; }
    /// <summary>Message to announce on full completion.</summary>
    public string? CompletionMessage { get; set; }

    public List<string> ProgrammedFields { get; } = new();
    public List<string> SkippedFields    { get; } = new();

    public static FmcProgramResult Error(string msg)
        => new FmcProgramResult { Success = false, ErrorMessage = msg };

    public string BuildSummary()
    {
        if (!Success && ErrorMessage != null) return $"FMC programming failed: {ErrorMessage}";
        if (WaitingForUser) return WaitingMessage ?? "Waiting for manual programming.";
        if (CompletionMessage != null) return CompletionMessage;

        var sb = new System.Text.StringBuilder("FMC programmed. ");
        if (ProgrammedFields.Count > 0)
            sb.Append($"Entered: {string.Join(", ", ProgrammedFields)}. ");
        if (SkippedFields.Count > 0)
            sb.Append($"Manual: {string.Join("; ", SkippedFields)}.");
        return sb.ToString();
    }
}
