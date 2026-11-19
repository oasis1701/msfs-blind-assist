using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The Captain's EHSI as a reader of <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>: its layouts from the shared
/// module session's builds, a <see cref="Bbs757EhsiReader"/> per located build on its own lease, which takes the
/// aircraft's position where BlueBird's top line does (S10, <see cref="SimInputs"/>; nothing is compared with a simulator
/// value, B757-30), the screen's own power, its
/// words (<see cref="Bbs757EhsiText"/>) on one Ctrl+M row, which also carries the active waypoint's line and the monitor's
/// status lines, and the Ctrl+W line. Every sampler it makes asks <see cref="FullWanted"/> at each reading whether to read
/// the route too. Called on the UI thread only.
/// </summary>
public sealed class Bbs757EhsiDisplay : IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>
{
    public const string Name = "EHSI";
    public const string LogChannel = "bbs757-ehsi";
    public const int TickMs = 500;

    /// <summary>The Ctrl+M row "EHSI messages": the messages, the flags, the active waypoint and the status lines.</summary>
    public const string MuteKey = "MSFSBA_BBS757_EHSI_MESSAGES";

    /// <summary>Whether a reading should hold the route, the modification and the map's points (the window is shown);
    /// asked by every sampler this display made at each of its readings, so setting it later takes effect at once.
    /// False until the definition sets it.</summary>
    public Func<bool> FullWanted { get; set; } = () => false;

    /// <summary>The simulator variables the sampler takes, where BlueBird's draw does (S10): the aircraft's position, which
    /// the top line's distance reads fresh on each draw (the map keeps the nd's own copy); and plan 28's three, which the
    /// map extras' draw reads through simulator handles with no value in memory: the wind's speed and direction (the
    /// units BlueBird registers them in) and the gear position (in BlueBird's "Enum", which TERR's amber threshold
    /// compares with the layout's gear-down value). Inputs, never compared with memory (B757-30).</summary>
    public static IReadOnlyList<Bbs757SimInput> SimInputs { get; } = Array.AsReadOnly(new[]
    {
        new Bbs757SimInput(Bbs757EhsiReader.LatitudeKey, Bbs757EhsiReader.LatitudeKey, SimVarType.SimVar, "degrees"),
        new Bbs757SimInput(Bbs757EhsiReader.LongitudeKey, Bbs757EhsiReader.LongitudeKey, SimVarType.SimVar, "degrees"),
        new Bbs757SimInput(Bbs757EhsiReader.WindSpeedKey, Bbs757EhsiReader.WindSpeedKey, SimVarType.SimVar, "knots"),
        new Bbs757SimInput(Bbs757EhsiReader.WindDirectionKey, Bbs757EhsiReader.WindDirectionKey, SimVarType.SimVar, "degrees"),
        new Bbs757SimInput(Bbs757EhsiReader.GearKey, Bbs757EhsiReader.GearKey, SimVarType.SimVar, "enum"),
    });

    IReadOnlyList<Bbs757SimInput> IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>.SimInputs => SimInputs;

    string IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>.TickMs => TickMs;

    /// <summary>The distinct EHSI layouts of the analyzed builds, in file order; a build without one is skipped.</summary>
    public IReadOnlyList<Bbs757EhsiLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757EhsiLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757EhsiLayout>(Bbs757Module.Ehsi) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, holding its own lease of
    /// the shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>,
    /// which the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757EhsiReading>? SamplerFor(LocatedModule module, Bbs757EhsiLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757EhsiLayout>(Bbs757Module.Ehsi))) return null;
        return new Bbs757EhsiReader(module.Memory, module.AcquireLease(), layout) { FullWanted = () => FullWanted() };
    }

    /// <summary>The screen's own power (powered, not failed, not timed blank): never null, so a dark screen is never
    /// spoken and what it shows as it lights is a silent baseline (S4).</summary>
    public bool? Lit(Bbs757EhsiReading reading) => reading.Powered;

    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix) =>
        Bbs757EhsiText.Changes(heard, now, withPrefix);

    public IReadOnlyList<string> Rows(Bbs757EhsiReading reading) => Bbs757EhsiText.Rows(reading);

    /// <summary>Ctrl+W: the active waypoint and the top line from <paramref name="latest"/> (the monitor's latest reading,
    /// light or full), or "EHSI waypoint unavailable: <paramref name="unavailableReason"/>" when there is none or it is
    /// corrupt ("no reading yet" when no reason is given); "EHSI waypoint unavailable: display dark" while not powered.</summary>
    public string WaypointLine(Bbs757EhsiReading? latest, string? unavailableReason) =>
        Bbs757EhsiText.WaypointLine(latest, unavailableReason);
}
