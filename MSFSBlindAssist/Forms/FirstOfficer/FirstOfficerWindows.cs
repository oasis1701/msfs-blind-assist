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
    private delegate IFirstOfficerWindow? Builder(
        IAircraftDefinition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings);

    private sealed record Row(string Name, Builder Build);

    private static readonly Dictionary<string, Row> Rows = new(StringComparer.Ordinal)
    {
        ["PMDG_777"] = new("PMDG 777 First Officer",
            (_, sc, a, s) => Window(new Fo.Pmdg777FoProfile(), sc, a, s)),
        ["PMDG_737"] = new("PMDG 737 First Officer",
            (_, sc, a, s) => Window(new Fo.PMDG737.Pmdg737FoProfile(), sc, a, s)),
        ["FENIX_A320CEO"] = new("Fenix A320 First Officer",
            (_, sc, a, s) => Window(new Fo.Fenix.FenixFoProfile(a), sc, a, s)),
        ["FBW_A380"] = new("FlyByWire A380 First Officer",
            (def, sc, a, s) => def is FlyByWireA380Definition a380
                ? Window(new Fo.FBWA380.FbwA380FoProfile(a380, a), sc, a, s) : null),
        ["A320"] = new("FlyByWire A32NX First Officer",
            (def, sc, a, s) => def is FlyByWireA320Definition a320
                ? Window(new Fo.FBWA320.FbwA320FoProfile(a320, a), sc, a, s) : null),
        ["HW_A330"] = new("Headwind A330 First Officer",
            (def, sc, a, s) => def is HeadwindA330Definition a330
                ? Window(new Fo.HWA330.HwA330FoProfile(a330, a), sc, a, s) : null),
        ["IFLY_737MAX8"] = new("iFly 737 MAX8 First Officer",
            (def, sc, a, s) => def is IFly737MAXDefinition ifly
                ? Window(new Fo.IFly737.IFly737FoProfile(ifly, a), sc, a, s) : null),
        ["TFDI_MD11"] = new("TFDi MD-11 First Officer",
            (def, sc, a, s) => def is TFDiMD11Definition md11
                ? Window(new Fo.MD11.Md11FoProfile(md11), sc, a, s) : null),
    };

    /// <summary>The spoken name of this aircraft's First Officer window (the File menu item's
    /// accessible name), or null when the aircraft has none or no aircraft is loaded.</summary>
    public static string? NameFor(IAircraftDefinition? aircraft) =>
        aircraft != null && Rows.TryGetValue(aircraft.AircraftCode, out Row? row) ? row.Name : null;

    /// <summary>Builds this aircraft's First Officer window, or null when it has none.</summary>
    public static IFirstOfficerWindow? Create(
        IAircraftDefinition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings) =>
        Rows.TryGetValue(aircraft.AircraftCode, out Row? row) ? row.Build(aircraft, simConnect, announcer, settings) : null;

    private static IFirstOfficerWindow Window<TExec, TState>(
        Fo.IFoProfile<TExec, TState> profile, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings)
        where TExec : Fo.IFoActionExecutor
        where TState : Fo.IFoStateEvaluator
        => new FirstOfficerForm<TExec, TState>(profile, simConnect, announcer, settings, new SimBriefService());
}
