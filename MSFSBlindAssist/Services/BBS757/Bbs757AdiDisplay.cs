using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The Captain's ADI, besides its flight mode annunciator, as a reader of
/// <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/> (plan 27): its layouts from the shared module session's builds, a
/// <see cref="Bbs757AdiReader"/> per located build on its own lease, which takes the four simulator variables BlueBird's
/// draw takes (S10, <see cref="SimInputs"/>; nothing is compared with a simulator value, B757-30), the display's own
/// power as its lit state, and its words (<see cref="Bbs757AdiText"/>). Every line it says, the five flags and the
/// monitor's status lines alike, is on the one "ADI messages" row. The FMA stays its own reader: this one never reads it.
/// Called on the UI thread only.
/// </summary>
public sealed class Bbs757AdiDisplay : IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>
{
    public const string Name = "ADI";
    public const string LogChannel = Bbs757AdiReader.LogChannel;
    public const int TickMs = 500;

    /// <summary>The Ctrl+M row "ADI messages": the flags and the status lines.</summary>
    public const string MuteKey = "MSFSBA_BBS757_ADI_MESSAGES";

    /// <summary>The Ctrl+M row's label.</summary>
    public const string MonitorRowLabel = "ADI messages";

    /// <summary>The simulator variables the sampler takes, where BlueBird's draw does (S10): the pfd reads its bank and
    /// pitch (radians), ground speed (knots) and Mach number through simulator handles that hold no value in memory.
    /// Stock variables in the units BlueBird registers them in, never L:vars of those names ([VAR-2]); inputs, never
    /// compared with memory (B757-30).</summary>
    public static IReadOnlyList<Bbs757SimInput> SimInputs { get; } = Array.AsReadOnly(new[]
    {
        new Bbs757SimInput(Bbs757AdiReader.BankKey, Bbs757AdiReader.BankKey, SimVarType.SimVar, "radians"),
        new Bbs757SimInput(Bbs757AdiReader.PitchKey, Bbs757AdiReader.PitchKey, SimVarType.SimVar, "radians"),
        new Bbs757SimInput(Bbs757AdiReader.GroundSpeedKey, Bbs757AdiReader.GroundSpeedKey, SimVarType.SimVar, "knots"),
        new Bbs757SimInput(Bbs757AdiReader.MachKey, Bbs757AdiReader.MachKey, SimVarType.SimVar, "mach"),
    });

    IReadOnlyList<Bbs757SimInput> IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>.SimInputs => SimInputs;

    string IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>.TickMs => TickMs;

    /// <summary>The distinct ADI layouts of the analyzed builds, in file order; a build without one is skipped.</summary>
    public IReadOnlyList<Bbs757AdiLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757AdiLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757AdiLayout>(Bbs757Module.Adi) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, on its own lease of the
    /// shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>, which
    /// the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757AdiReading>? SamplerFor(LocatedModule module, Bbs757AdiLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757AdiLayout>(Bbs757Module.Adi))) return null;
        return new Bbs757AdiReader(module.Memory, module.AcquireLease(), layout);
    }

    /// <summary>The display's own power (powered, not failed, not timed blank): never null, so a dark display is never
    /// spoken and what it shows as it lights is a silent baseline (S4).</summary>
    public bool? Lit(Bbs757AdiReading reading) => reading.Powered;

    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757AdiReading heard, Bbs757AdiReading now, bool withPrefix) =>
        Bbs757AdiText.Changes(heard, now, withPrefix).Select(text => new Bbs757SpokenLine(MuteKey, text)).ToArray();

    public IReadOnlyList<string> Rows(Bbs757AdiReading reading) => Bbs757AdiText.Rows(reading);
}
