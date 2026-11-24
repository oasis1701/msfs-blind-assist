using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.SimConnect.BBS757;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// The BlueBird 757 First Officer's state evaluator. Reads the SimConnect cache by the definition's
/// own keys (map controls by node, OnRequest, polled here each second by the First Officer window;
/// status rows and streamed rows on the 1 Hz batch). A field is NaN until every input it needs has
/// been read, never 0 (FO-20): an unread switch must never tick, untick or pass a guard.
///
/// Where the two engine variants differ (the fuel control switches, the Rolls-Royce-only EEC switches,
/// the idle N2, the Rolls-Royce cold and hot start rules), the variant comes from the aircraft TITLE
/// first (every BlueBird 757 title carries " PW" or " RR", <see cref="TitleVariant"/>), then from
/// BlueBird's own <c>L:B757_ENG_VARIANT</c>, believed only when it reads 1 (PW, measured): an unwritten
/// L:var reads 0 as well, so 0 says nothing. Without a title naming RR or the L:var reading 1, every
/// variant-dependent field is NaN.
/// </summary>
public sealed class Bbs757FoStateEvaluator : LVarStateEvaluator
{
    /// <summary>N2 below this reads stopped (measured on the PW: under 10 % about 43 s after fuel CUTOFF).</summary>
    public const double EngineStoppedN2 = 10.0;

    /// <summary>
    /// N2 at or above this is a lit engine past its starter: the start selector lines latch here (FOB-1). It sits
    /// below both idles and at the starter cut-out (measured PW: the selector released at about 52 %, idle 59.5 %;
    /// the Rolls-Royce's stock N2 is its N3, idle 50.5 % per its engines.cfg, starter cut-out 47 %).
    /// </summary>
    public const double EngineRunningN2 = 47.0;

    /// <summary>A starter is engaged only below this N2 (the manual: re-engage below 20 % N2, N3 on a Rolls-Royce).</summary>
    public const double StarterReengageN2 = 20.0;

    /// <summary>An engine is running (stabilized) at or above these: PW idle 59.5 % measured, Rolls-Royce 50.5 % (engines.cfg).</summary>
    public const double PwRunningN2 = 55.0, RrRunningN2 = 48.0;

    /// <summary>A Rolls-Royce cold start (fuel control RICH) at or below this EGT, °C (the manual).</summary>
    public const double RrRichEgtCelsius = 0.0;

    /// <summary>A Rolls-Royce is motored until its EGT is below this before fuel, °C (the manual's hot core).</summary>
    public const double RrHotStartEgtCelsius = 100.0;

    /// <summary>
    /// The fuel density the center tank's pounds use until BlueBird's own (<c>Bbs757Loadsheet.FuelDensityKey</c>) is read:
    /// the loadsheet's 6.7 lb/gal, so an unread density never leaves the tank unknown (the center pumps would never arm).
    /// </summary>
    public const double FallbackPoundsPerGallon = 6.7;

    /// <summary>
    /// The center tank in pounds, as the other Boeings' center pump policy and gate count it (plan 29): gallons times
    /// BlueBird's density, or <see cref="FallbackPoundsPerGallon"/> while that reads nothing usable; NaN with the gallons unread.
    /// </summary>
    internal static double CenterFuelLbs(double gallons, double poundsPerGallon)
    {
        if (double.IsNaN(gallons)) return double.NaN;
        double density = double.IsNaN(poundsPerGallon) || poundsPerGallon <= 0 ? FallbackPoundsPerGallon : poundsPerGallon;
        return gallons * density;
    }

    /// <summary>The center pumps belong on above the policy's empty threshold (1,000 lb), so no write ever meets the gate.</summary>
    private static bool CenterFuelWanted(double lbs) => lbs > CenterFuelPumpAutomation.OffThresholdLbs;

    private static readonly string[] CenterFuel = { K.CenterFuelGallons, Bbs757Loadsheet.FuelDensityKey };

    /// <summary>Gear legs: up below 5 %, down above 95 % (stock legs, 0..1 as "percent over 100").</summary>
    public const double GearUpBelow = 0.05, GearDownAbove = 0.95;

    // The passenger doors animate 0-100 and the cargo doors 0-200; closed at the ends the
    // definition's own door rows speak (Bbs757Annunciators: 5 and 10).
    public const double DoorClosedAt = 5, CargoDoorClosedAt = 10;

    /// <summary>The isolation switch's state var for the valve OPEN and CLOSED (measured 2026-10-27: 1 brought the right duct up to the left's pressure).</summary>
    public const double IsolationOpen = 1, IsolationClosed = 0;

    private static readonly string[] PollFields = BuildPollFields();

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    /// <summary>Every OnRequest map control and FO read a synthetic or a checklist line reads.</summary>
    internal static IReadOnlyList<string> PollFieldList => PollFields;

    private static string[] BuildPollFields() => new[]
    {
        K.Battery, K.StandbyPower, K.LeftBusTie, K.RightBusTie, K.LeftGenCont, K.RightGenCont, K.ApuGen,
        K.LeftUtilBus, K.RightUtilBus, K.ApuSelector,
        K.LeftYawDamper, K.RightYawDamper, K.LeftEec, K.RightEec, K.LeftLimiter, K.RightLimiter,
        K.Ignition, K.LeftStart, K.RightStart, K.EngineVariant, K.LeftEgt, K.RightEgt,
        K.LeftFuelPw, K.RightFuelPw, K.LeftFuelRr, K.RightFuelRr,
        K.LeftEngHyd, K.RightEngHyd, K.LeftElecHyd, K.C1ElecHyd, K.C2ElecHyd, K.RightElecHyd,
        K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump, K.CenterLeftPump, K.CenterRightPump,
        K.XfeedFwd, K.XfeedAft, K.CenterFuelGallons, Bbs757Loadsheet.FuelDensityKey,
        K.WingAntiIce, K.LeftEngAntiIce, K.RightEngAntiIce,
        K.BeaconLight, K.PositionLights, K.StrobeLights, K.TaxiLight, K.LeftLanding, K.RightLanding,
        K.LeftTurnoff, K.RightTurnoff, K.WingLights, K.LogoLights,
        K.SeatBelts, K.NoSmoking, K.EmergencyLights, K.Wipers,
        K.LeftPackSelector, K.RightPackSelector, K.Isolation, K.LeftEngBleed, K.RightEngBleed, K.ApuBleed,
        K.TrimAir, K.LeftRecirc, K.RightRecirc, K.PressMode,
        K.CaptainFd, K.FirstOfficerFd, K.AutothrottleArm, K.BankLimit, K.Autobrake,
        K.GearLever, K.ParkingBrake, K.TcasMode,
    }
    .Concat(K.IrsSwitches).Concat(K.WindowHeat).Concat(K.Heaters).Concat(K.TerrainAndWeather)
    .Distinct(StringComparer.Ordinal).ToArray();

    // ------------------------------------------------------------------ synthetic fields

    private sealed record Synth(string[] Inputs, Func<Func<string, double>, double> Compute);

    private static double B(bool b) => b ? 1 : 0;
    private static bool Known(params double[] v) => v.All(x => !double.IsNaN(x));
    internal static bool Pos(double v, double p) => !double.IsNaN(v) && Math.Abs(v - p) < 0.25;
    private static bool On(double v) => v > 0.5;

    /// <summary>A field computed from switch values: NaN unless every one was read.</summary>
    private static Synth Vars(Func<double[], bool> ok, params string[] keys)
        => new(keys, r =>
        {
            var v = keys.Select(r).ToArray();
            return Known(v) ? B(ok(v)) : double.NaN;
        });

    /// <summary>The pseudo-input carrying the variant the aircraft title names (1 PW, 0 RR, NaN neither).</summary>
    internal const string TitleVariantKey = "FO_TITLE_VARIANT";

    /// <summary>
    /// The variant a BlueBird 757 title names: 1 for " PW", 0 for " RR", NaN for none or both. Every title the package
    /// ships names one (checked against the installed package's aircraft.cfg files, 2026-10-28): "BlueBird 757-200 PW",
    /// "... PW AWE", "... PW NO CABIN", "... PW N/W", "... PW N/W AWE", "BlueBird 757-200 RR", "... RR AWE",
    /// "... RR N/W", "... RR N/W 4 Door" and "... RR Scimitar/W" (pinned by Bbs757FoStateEvaluatorTests).
    /// </summary>
    public static double TitleVariant(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return double.NaN;
        bool pw = System.Text.RegularExpressions.Regex.IsMatch(title, @"\bPW\b"),
             rr = System.Text.RegularExpressions.Regex.IsMatch(title, @"\bRR\b");
        return pw == rr ? double.NaN : pw ? 1 : 0;
    }

    /// <summary>
    /// 1 PW2000, 0 RB211: the title's variant when it names one. Without, BlueBird's L:var, but only its 1 (PW,
    /// measured): an L:var nobody has written reads 0 too, so a 0 is unknown (NaN), never Rolls-Royce. A Rolls-Royce
    /// is therefore known by its title alone.
    /// </summary>
    internal static double IsPw(Func<string, double> r)
    {
        double title = r(TitleVariantKey);
        if (!double.IsNaN(title)) return title;
        return Pos(r(K.EngineVariant), 1) ? 1 : double.NaN;
    }

    /// <summary>A field whose test depends on the variant: the PW test or the RR test, NaN unread.</summary>
    private static Synth ByVariant(string[] pwKeys, Func<double[], bool> pw, string[] rrKeys, Func<double[], bool> rr)
        => new(pwKeys.Concat(rrKeys).Append(K.EngineVariant).ToArray(), r =>
        {
            double variant = IsPw(r);
            if (double.IsNaN(variant)) return double.NaN;
            var keys = variant > 0.5 ? pwKeys : rrKeys;
            var v = keys.Select(r).ToArray();
            return Known(v) ? B((variant > 0.5 ? pw : rr)(v)) : double.NaN;
        });

    private static readonly string[] FuelPw = { K.LeftFuelPw, K.RightFuelPw };
    private static readonly string[] FuelRr = { K.LeftFuelRr, K.RightFuelRr };
    private static readonly string[] Eec = { K.LeftEec, K.RightEec, K.LeftLimiter, K.RightLimiter };
    private static readonly string[] Limiters = { K.LeftLimiter, K.RightLimiter };
    private static readonly string[] WingPumps = { K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump };
    private static readonly string[] CenterPumps = { K.CenterLeftPump, K.CenterRightPump };
    private static readonly string[] ElecHyd = { K.LeftElecHyd, K.C1ElecHyd, K.C2ElecHyd, K.RightElecHyd };
    private static readonly string[] EngHyd = { K.LeftEngHyd, K.RightEngHyd };
    private static readonly string[] Landing = { K.LeftLanding, K.RightLanding };
    private static readonly string[] Lever = { K.GearLever };

    /// <summary>
    /// The N2 at which a Pratt &amp; Whitney start takes fuel: about 25 % (the manual: "allow the core to
    /// reach a more robust value of approximately 25% N2"; 18 % is only the minimum).
    /// </summary>
    public const double PwFuelPointN2 = 25.0;

    /// <summary>
    /// The Rolls-Royce fuel point on the stock N2, which on the RB211 stands for N3 (its engines.cfg): the manual's
    /// "about 25 % N3", and the EGT below <see cref="RrHotStartEgtCelsius"/> (a hot core is motored first).
    /// </summary>
    public const double RrFuelPointN2 = 25.0;

    private static Synth FuelPoint(string n2Key, string egtKey) => new(new[] { n2Key, egtKey, K.EngineVariant }, r =>
    {
        double pw = IsPw(r), n2 = r(n2Key);
        if (double.IsNaN(pw) || double.IsNaN(n2)) return double.NaN;
        if (pw > 0.5) return B(n2 >= PwFuelPointN2);
        double egt = r(egtKey);
        return double.IsNaN(egt) ? double.NaN : B(n2 >= RrFuelPointN2 && egt < RrHotStartEgtCelsius);
    });

    /// <summary>The engines' running threshold by variant (<see cref="PwRunningN2"/>, <see cref="RrRunningN2"/>).</summary>
    private static Synth Running(params string[] n2Keys) => new(n2Keys.Append(K.EngineVariant).ToArray(), r =>
    {
        double pw = IsPw(r);
        var n2 = n2Keys.Select(r).ToArray();
        if (double.IsNaN(pw) || !Known(n2)) return double.NaN;
        double at = pw > 0.5 ? PwRunningN2 : RrRunningN2;
        return B(n2.All(x => x >= at));
    });

    /// <summary>The speedbrake lever (L:B757_FC_SPOILER_LEVER: 0 down, 10 armed, 100 up) in its ARMED band.</summary>
    public static bool SpeedbrakeIsArmed(double lever) => lever >= 5 && lever <= 15;

    /// <summary>The speedbrake lever DOWN (below the ARMED band).</summary>
    public static bool SpeedbrakeIsDown(double lever) => lever < 5;

    private static double Gear(Func<string, double> r, bool down)
    {
        double lever = r(K.GearLever);
        var legs = K.GearLegs.Select(r).ToArray();
        if (double.IsNaN(lever) || !Known(legs)) return double.NaN;
        return down
            ? B(Pos(lever, 1) && legs.All(l => l > GearDownAbove))
            : B(Pos(lever, 0.5) && legs.All(l => l < GearUpBelow));     // UP, THEN OFF
    }

    private static readonly Dictionary<string, Synth> Synthetics = new(StringComparer.Ordinal)
    {
        ["FO_VARIANT_PW"] = new(new[] { K.EngineVariant }, IsPw),

        // ---- engines (N2 fed by the window) ----
        ["FO_ENGINES_STOPPED"] = Vars(v => v.All(x => x < EngineStoppedN2), "FO_ENG1_N2", "FO_ENG2_N2"),
        // Both N2 delivered by the window: 1, else NaN (an engine start waits for it).
        ["FO_N2_READ"] = Vars(v => true, "FO_ENG1_N2", "FO_ENG2_N2"),
        ["FO_ENG1_RUNNING"] = Running("FO_ENG1_N2"),
        ["FO_ENG2_RUNNING"] = Running("FO_ENG2_N2"),
        ["FO_ENGINES_RUNNING"] = Running("FO_ENG1_N2", "FO_ENG2_N2"),

        // ---- the fuel point: BlueBird's start criteria (manual p. 146-148) ----
        ["FO_ENG1_FUEL_POINT"] = FuelPoint("FO_ENG1_N2", K.LeftEgt),
        ["FO_ENG2_FUEL_POINT"] = FuelPoint("FO_ENG2_N2", K.RightEgt),

        // ---- fuel control switches, by variant ----
        ["FO_FUEL_CONTROLS_CUTOFF"] = ByVariant(FuelPw, v => v.All(x => Pos(x, K.PwCutoff)),
                                                FuelRr, v => v.All(x => Pos(x, K.RrCutoff))),
        // RUN, or RICH on a Rolls-Royce (a cold start).
        ["FO_FUEL_CONTROLS_RUN"] = ByVariant(FuelPw, v => v.All(x => Pos(x, K.PwRun)),
                                             FuelRr, v => v.All(x => Pos(x, K.RrRun) || Pos(x, K.RrRich))),
        ["FO_FUEL_CONTROL_1_RUN"] = ByVariant(new[] { K.LeftFuelPw }, v => Pos(v[0], K.PwRun),
                                              new[] { K.LeftFuelRr }, v => Pos(v[0], K.RrRun) || Pos(v[0], K.RrRich)),
        ["FO_FUEL_CONTROL_2_RUN"] = ByVariant(new[] { K.RightFuelPw }, v => Pos(v[0], K.PwRun),
                                              new[] { K.RightFuelRr }, v => Pos(v[0], K.RrRun) || Pos(v[0], K.RrRich)),
        // A Rolls-Royce in RICH (a cold start): After Start returns it to RUN. A Pratt & Whitney has no RICH.
        ["FO_FUEL_CONTROLS_RICH"] = ByVariant(FuelPw, v => false, FuelRr, v => v.Any(x => Pos(x, K.RrRich))),
        // The engine limiter switches ON on both variants, and the EEC switches ON on a Rolls-Royce (a Pratt & Whitney has none).
        ["FO_ENGINE_CONTROLS_ON"] = new(Eec.Append(K.EngineVariant).ToArray(), r =>
        {
            double pw = IsPw(r);
            if (double.IsNaN(pw)) return double.NaN;
            var v = (pw > 0.5 ? Limiters : Eec).Select(r).ToArray();
            return Known(v) ? B(v.All(On)) : double.NaN;
        }),

        // ---- IRS ----
        ["FO_IRS_ALL_NAV"] = Vars(v => v.All(x => Pos(x, K.IrsNav)), K.IrsSwitches),
        ["FO_IRS_ALL_OFF"] = Vars(v => v.All(x => Pos(x, K.IrsOff)), K.IrsSwitches),
        // NAV with every ALIGN light out. Needs the battery ON: unpowered every light reads out.
        ["FO_IRS_ALIGNED"] = new(K.IrsSwitches.Concat(K.IrsAlignLights).Append(K.Battery).ToArray(), r =>
        {
            var sw = K.IrsSwitches.Select(r).ToArray();
            var lt = K.IrsAlignLights.Select(r).ToArray();
            double batt = r(K.Battery);
            if (!Known(sw) || !Known(lt) || double.IsNaN(batt)) return double.NaN;
            return B(On(batt) && sw.All(x => Pos(x, K.IrsNav)) && !lt.Any(On));
        }),

        // ---- doors ----
        ["FO_DOORS_CLOSED"] = new(K.Doors.Concat(K.CargoDoors).ToArray(), r =>
        {
            var pax = K.Doors.Select(r).ToArray();
            var cargo = K.CargoDoors.Select(r).ToArray();
            if (!Known(pax) || !Known(cargo)) return double.NaN;
            return B(pax.All(x => x <= DoorClosedAt) && cargo.All(x => x <= CargoDoorClosedAt));
        }),

        // ---- hydraulics ----
        // Before Start: engine pumps and the four electric pumps (R, C1, C2, L) ON.
        ["FO_HYD_PANEL_SET"] = Vars(v => v.All(On), EngHyd.Concat(ElecHyd).ToArray()),
        ["FO_ENG_HYD_PUMPS_ON"] = Vars(v => v.All(On), EngHyd),
        ["FO_ELEC_HYD_PUMPS_ON"] = Vars(v => v.All(On), ElecHyd),
        ["FO_ELEC_HYD_PUMPS_OFF"] = Vars(v => !v.Any(On), ElecHyd),

        // ---- fuel ----
        // Before Start: the four wing pumps ON, the center pumps ON only with fuel in the center
        // tank, both crossfeeds OFF (the manual: "Select the applicable pumps ON; leave crossfeed OFF").
        ["FO_FUEL_PANEL_SET"] = new(WingPumps.Concat(CenterPumps).Append(K.XfeedFwd).Append(K.XfeedAft)
                                     .Concat(CenterFuel).ToArray(), r =>
        {
            var wing = WingPumps.Select(r).ToArray();
            var ctr = CenterPumps.Select(r).ToArray();
            double xf = r(K.XfeedFwd), xa = r(K.XfeedAft);
            double lbs = CenterFuelLbs(r(K.CenterFuelGallons), r(Bbs757Loadsheet.FuelDensityKey));
            if (!Known(wing) || !Known(ctr) || !Known(xf, xa, lbs)) return double.NaN;
            bool centerWanted = CenterFuelWanted(lbs);
            return B(wing.All(On) && ctr.All(c => On(c) == centerWanted) && !On(xf) && !On(xa));
        }),
        ["FO_FUEL_PUMPS_OFF"] = Vars(v => !v.Any(On), WingPumps.Concat(CenterPumps).ToArray()),
        ["FO_CROSSFEEDS_OFF"] = Vars(v => !v.Any(On), K.XfeedFwd, K.XfeedAft),
        ["FO_CENTER_FUEL_LBS"] = new(CenterFuel, r => CenterFuelLbs(r(K.CenterFuelGallons), r(Bbs757Loadsheet.FuelDensityKey))),
        ["FO_CENTER_FUEL_FOR_PUMPS"] = new(CenterFuel, r =>
        {
            double lbs = CenterFuelLbs(r(K.CenterFuelGallons), r(Bbs757Loadsheet.FuelDensityKey));
            return double.IsNaN(lbs) ? double.NaN : B(CenterFuelWanted(lbs));
        }),

        // ---- anti-ice, windows, heaters ----
        ["FO_ENG_ANTI_ICE_OFF"] = Vars(v => !v.Any(On), K.LeftEngAntiIce, K.RightEngAntiIce),
        ["FO_ANTI_ICE_OFF"] = Vars(v => !v.Any(On), K.WingAntiIce, K.LeftEngAntiIce, K.RightEngAntiIce),
        ["FO_WINDOW_HEAT_ON"] = Vars(v => v.All(On), K.WindowHeat),
        ["FO_WINDOW_HEAT_OFF"] = Vars(v => !v.Any(On), K.WindowHeat),
        ["FO_HEATERS_OFF"] = Vars(v => v.All(x => Pos(x, K.HeaterOff)), K.Heaters),

        // ---- lights ----
        ["FO_LANDING_LIGHTS_ON"] = Vars(v => v.All(On), Landing),
        ["FO_LANDING_LIGHTS_OFF"] = Vars(v => !v.Any(On), Landing),
        ["FO_TURNOFF_LIGHTS_OFF"] = Vars(v => !v.Any(On), K.LeftTurnoff, K.RightTurnoff),
        ["FO_WXR_TERR_OFF"] = Vars(v => !v.Any(On), K.TerrainAndWeather),

        // ---- air ----
        ["FO_PACKS_AUTO"] = Vars(v => v.All(x => Pos(x, K.PackAuto)), K.LeftPackSelector, K.RightPackSelector),
        ["FO_PACKS_OFF"] = Vars(v => v.All(x => Pos(x, K.PackOff)), K.LeftPackSelector, K.RightPackSelector),
        ["FO_BLEEDS_ON"] = Vars(v => v.All(On), K.LeftEngBleed, K.RightEngBleed),

        // ---- MCP / main panel ----
        ["FO_FDS_ON"] = Vars(v => v.All(On), K.CaptainFd, K.FirstOfficerFd),
        ["FO_FDS_OFF"] = Vars(v => !v.Any(On), K.CaptainFd, K.FirstOfficerFd),
        // The autothrottle ARMED as BlueBird has it (not merely the switch: R40).
        ["FO_AT_ARMED"] = Vars(v => On(v[0]), K.AutothrottleArmedState),

        // ---- electrical / APU ----
        ["FO_ELEC_PANEL_SET"] = Vars(v => v.All(On), Bbs757FoChecklistDefinitions.ElecPanel),
        ["FO_EXT_POWER_ON"] = Vars(v => On(v[0]), K.ExtPwrOn),
        ["FO_EXT_POWER_OFF"] = Vars(v => !On(v[0]), K.ExtPwrOn),
        ["FO_APU_RUNNING"] = Vars(v => Pos(v[0], 2), K.ApuState),
        // "APU: OFF" is the APU switch at OFF, as on the 737s (APU_Selector < 0.5; owner, 2026-11-08), never the APU's
        // running state: BlueBird runs the APU on through a cool-down after OFF, and a line judged by the state un-ticked
        // a hand tick with "Unable to complete" while the APU was on its way off. Whether it has STOPPED is
        // B757_APU_STATE 0, read where that matters (Final Securing's wait, the battery).
        ["FO_APU_OFF"] = Vars(v => Pos(v[0], 0), K.ApuSelector),
        // The APU running with its generator control ON: the generator takes the busses by itself (the manual's
        // "APU GEN ON; RUN illuminated"), so external power may be deselected.
        ["FO_APU_ON_LINE"] = Vars(v => Pos(v[0], 2) && On(v[1]), K.ApuState, K.ApuGen),
        ["FO_APU_OR_EXT_POWER_ON"] = new(new[] { K.ApuState, K.ExtPwrOn }, r =>
        {
            double apu = r(K.ApuState), ext = r(K.ExtPwrOn);
            if (On(ext) || Pos(apu, 2)) return 1;
            return Known(apu, ext) ? 0 : double.NaN;
        }),

        // ---- overhead groups ----
        ["FO_YAW_DAMPERS_ON"] = Vars(v => v.All(On), K.LeftYawDamper, K.RightYawDamper),
        ["FO_START_SELECTORS_AUTO"] = Vars(v => v.All(x => Pos(x, K.StartAuto)), K.LeftStart, K.RightStart),
        ["FO_PREFLIGHT_LIGHTS_OFF"] = Vars(v => !v.Any(On), K.BeaconLight, K.StrobeLights, K.WingLights, K.LeftLanding, K.RightLanding,
            K.LeftTurnoff, K.RightTurnoff, K.TaxiLight),
        // After Takeoff / Before Landing: both runway turnoff lights and BlueBird's one taxi light together.
        ["FO_GROUND_LIGHTS_OFF"] = Vars(v => !v.Any(On), K.LeftTurnoff, K.RightTurnoff, K.TaxiLight),
        ["FO_GROUND_LIGHTS_ON"] = Vars(v => v.All(On), K.LeftTurnoff, K.RightTurnoff, K.TaxiLight),
        // Shutdown's powered parked configuration (E196): position lights only, so wing, logo and taxi OFF.
        ["FO_PARKED_LIGHTS_OFF"] = Vars(v => !v.Any(On), K.WingLights, K.LogoLights, K.TaxiLight),
        ["FO_TURNOFF_LIGHTS_ON"] = Vars(v => v.All(On), K.LeftTurnoff, K.RightTurnoff),
        ["FO_TRIM_AIR_RECIRC_ON"] = Vars(v => v.All(On), K.TrimAir, K.LeftRecirc, K.RightRecirc),
        ["FO_ISOLATION_OPEN"] = Vars(v => Pos(v[0], IsolationOpen), K.Isolation),
        ["FO_ISOLATION_CLOSED"] = Vars(v => Pos(v[0], IsolationClosed), K.Isolation),
        ["FO_ALL_BLEEDS_ON"] = Vars(v => v.All(On), K.LeftEngBleed, K.RightEngBleed, K.ApuBleed),
        // After Start's "Air conditioning panel: SET": packs AUTO, isolation closed, engine bleeds ON.
        ["FO_AIRCON_SET"] = Vars(v => Pos(v[0], K.PackAuto) && Pos(v[1], K.PackAuto) && Pos(v[2], IsolationClosed) && On(v[3]) && On(v[4]),
            K.LeftPackSelector, K.RightPackSelector, K.Isolation, K.LeftEngBleed, K.RightEngBleed),

        // ---- speedbrake (L:B757_FC_SPOILER_LEVER: 0 down, 10 armed, 100 up; Bbs757Readouts' bands) ----
        ["FO_SPEEDBRAKE_ARMED"] = Vars(v => SpeedbrakeIsArmed(v[0]), K.Speedbrake),
        ["FO_SPEEDBRAKE_DOWN"] = Vars(v => SpeedbrakeIsDown(v[0]), K.Speedbrake),
        // Extended past the ARMED band: an ARM is left alone (never snapped back over a deployed brake).
        ["FO_SPEEDBRAKE_EXTENDED"] = Vars(v => v[0] > 15, K.Speedbrake),

        // ---- flaps, gear ----
        ["FO_FLAPS_UP"] = Vars(v => Pos(v[0], 0), K.FlapsHandle),
        // A landing flap: 25 or 30 (handle index 5 or 6), the manual's "normal landing choices" (E181).
        ["FO_FLAPS_LANDING"] = Vars(v => Pos(v[0], 5) || Pos(v[0], 6), K.FlapsHandle),
        ["FO_GEAR_DOWN"] = new(Lever.Concat(K.GearLegs).ToArray(), r => Gear(r, down: true)),
        ["FO_GEAR_UP_OFF"] = new(Lever.Concat(K.GearLegs).ToArray(), r => Gear(r, down: false)),
    };

    /// <summary>The flap handle index a SimBrief takeoff flap names (1, 5, 15, 20), or -1.</summary>
    public static int TakeoffFlapIndex(int flaps) => flaps switch
    {
        1 => 1, 5 => 2, 15 => 3, 20 => 4,
        _ => -1,
    };

    internal static IReadOnlyCollection<string> SyntheticKeys { get; } =
        Synthetics.Keys.Concat(new[] { "FO_ENG1_N2", "FO_ENG2_N2", "FO_FLAPS_TAKEOFF" }).ToArray();

    internal static IReadOnlyCollection<string> SyntheticInputs { get; } =
        Synthetics.Values.SelectMany(s => s.Inputs).Append(K.FlapsHandle)
            .Where(k => !k.StartsWith("FO_", StringComparison.Ordinal)).Distinct().ToArray();

    /// <summary>
    /// Pure computation of one synthetic field (tests drive it with a dictionary).
    /// <c>FO_FLAPS_TAKEOFF</c>: the handle at a takeoff detent (1, 5, 15 or 20) and, with a SimBrief
    /// plan loaded, at the plan's own setting.
    /// </summary>
    internal static double Compute(string field, Func<string, double> read, int takeoffFlaps = -1)
    {
        if (field == "FO_FLAPS_TAKEOFF")
        {
            double h = read(K.FlapsHandle);
            if (double.IsNaN(h)) return double.NaN;
            int planned = TakeoffFlapIndex(takeoffFlaps);
            return planned >= 0 ? B(Pos(h, planned)) : B(h > 0.5 && h < 4.5 && Math.Abs(h - Math.Round(h)) < 0.25);
        }
        return Synthetics.TryGetValue(field, out var s) ? s.Compute(read) : double.NaN;
    }

    private double Raw(string key) => key switch
    {
        "FO_ENG1_N2" => Eng1N2,
        "FO_ENG2_N2" => Eng2N2,
        TitleVariantKey => TitleVariant(Sc?.CurrentAircraftTitle),
        _ => Sc?.GetCachedVariableValue(key) ?? double.NaN,
    };

    /// <summary>True on the ground, false airborne, null unread.</summary>
    public bool? OnGround
    {
        get
        {
            double v = GetValue("SIM_ON_GROUND");
            return double.IsNaN(v) ? null : v > 0.5;
        }
    }

    public double EngineN2(int engine) => engine switch { 1 => Eng1N2, 2 => Eng2N2, _ => double.NaN };

    /// <summary>True PW2000, false RB211, null unknown (no title variant and the L:var unread).</summary>
    public bool? IsPrattAndWhitney
    {
        get
        {
            double v = IsPw(Raw);
            return double.IsNaN(v) ? null : v > 0.5;
        }
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case "FO_ENG1_N2": value = Eng1N2; return true;
            case "FO_ENG2_N2": value = Eng2N2; return true;
            case "FO_FLAPS_TAKEOFF": value = Compute(field, Raw, GetTakeoffFlaps()); return true;
        }
        if (Synthetics.ContainsKey(field)) { value = Compute(field, Raw); return true; }
        value = double.NaN;
        return false;
    }
}
