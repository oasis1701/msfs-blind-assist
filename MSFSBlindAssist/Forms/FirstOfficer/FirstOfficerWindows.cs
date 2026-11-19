using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using Fo = MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Forms.FirstOfficer;

/// <summary>
/// The one answer to "which aircraft has a First Officer window" (FO-24): one row per First
/// Officer aircraft, keyed on the EXACT aircraft code, so the Headwind A330 (whose definition
/// derives from FlyByWireA320Definition) gets its own profile without any type-test ordering.
/// <see cref="NameFor"/> gates Shift+C and the File menu item and names the item;
/// <see cref="Create"/> builds the window. A new First Officer aircraft adds one row here and
/// one in FirstOfficerWindowsTests.
/// </summary>
public static class FirstOfficerWindows
{
    private delegate IFirstOfficerWindow Builder<in TDef>(
        TDef aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings);

    // A row accepts only its own definition type as well as its code, and NameFor asks the same
    // question Create does, so the two never disagree: an aircraft is named (Shift+C opens, the
    // menu item shows) exactly when its window can be built.
    private sealed record Row(string Name, Func<IAircraftDefinition, bool> Accepts,
        Func<IAircraftDefinition, SimConnectManager, ScreenReaderAnnouncer, UserSettings, IFirstOfficerWindow> Build);

    private static Row Of<TDef>(string name, Builder<TDef> build) where TDef : IAircraftDefinition =>
        new(name, aircraft => aircraft is TDef, (aircraft, sc, a, s) => build((TDef)aircraft, sc, a, s));

    private static readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal)
    {
        ["PMDG_777"] = Of<PMDG777Definition>("PMDG 777 First Officer",
            (_, sc, a, s) => Window(new Fo.Pmdg777FoProfile(), sc, a, s)),
        ["PMDG_737"] = Of<PMDG737Definition>("PMDG 737 First Officer",
            (_, sc, a, s) => Window(new Fo.PMDG737.Pmdg737FoProfile(), sc, a, s)),
        ["FENIX_A320CEO"] = Of<FenixA320Definition>("Fenix A320 First Officer",
            (_, sc, a, s) => Window(new Fo.Fenix.FenixFoProfile(a), sc, a, s)),
        ["FBW_A380"] = Of<FlyByWireA380Definition>("FlyByWire A380 First Officer",
            (a380, sc, a, s) => Window(new Fo.FBWA380.FbwA380FoProfile(a380, a), sc, a, s)),
        ["A320"] = Of<FlyByWireA320Definition>("FlyByWire A32NX First Officer",
            (a320, sc, a, s) => Window(new Fo.FBWA320.FbwA320FoProfile(a320, a), sc, a, s)),
        ["HW_A330"] = Of<HeadwindA330Definition>("Headwind A330 First Officer",
            (a330, sc, a, s) => Window(new Fo.HWA330.HwA330FoProfile(a330, a), sc, a, s)),
        ["IFLY_737MAX8"] = Of<IFly737MAXDefinition>("iFly 737 MAX8 First Officer",
            (ifly, sc, a, s) => Window(new Fo.IFly737.IFly737FoProfile(ifly, a), sc, a, s)),
        ["TFDI_MD11"] = Of<TFDiMD11Definition>("TFDi MD-11 First Officer",
            (md11, sc, a, s) => Window(new Fo.MD11.Md11FoProfile(md11), sc, a, s)),
        ["BBS_757"] = Of<BBS757Definition>("BlueBird 757 First Officer",
            (b757, sc, a, s) => Window(new Fo.BBS757.Bbs757FoProfile(b757, a), sc, a, s)),
    };

    private static Row? RowFor(IAircraftDefinition? aircraft) =>
        aircraft != null && Rows.TryGetValue(aircraft.AircraftCode, out Row? row) && row.Accepts(aircraft) ? row : null;

    /// <summary>The spoken name of this aircraft's First Officer window (the File menu item's
    /// accessible name), or null when the aircraft has none or no aircraft is loaded.</summary>
    public static string? NameFor(IAircraftDefinition? aircraft) => RowFor(aircraft)?.Name;

    /// <summary>Builds this aircraft's First Officer window, or null exactly when
    /// <see cref="NameFor"/> is null.</summary>
    public static IFirstOfficerWindow? Create(
        IAircraftDefinition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings) =>
        RowFor(aircraft)?.Build(aircraft, simConnect, announcer, settings);

    private static IFirstOfficerWindow Window<TExec, TState>(
        Fo.IFoProfile<TExec, TState> profile, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings)
        where TExec : Fo.IFoActionExecutor
        where TState : Fo.IFoStateEvaluator
        => new FirstOfficerForm<TExec, TState>(profile, simConnect, announcer, settings, new SimBriefService());
}
