using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757's upper EICAS as a reader of <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>: its layouts from the
/// shared module session's builds, a <see cref="Bbs757UpperEicasReader"/> per located build on its own lease, which takes
/// BlueBird's manual thrust reference flag where the draw does (S10, its one simulator input, never a check), the
/// screen's own power, and its words (<see cref="Bbs757UpperEicasSpeech"/>) on three Ctrl+M rows: the messages (which
/// also carry the monitor's status lines), the thrust reference and the engine gauge colors. Called on the UI thread only.
/// </summary>
public sealed class Bbs757UpperEicasDisplay : IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>
{
    public const string Name = "EICAS";
    public const string LogChannel = "bbs757-upper-eicas";
    public const int TickMs = 250;

    /// <summary>The Ctrl+M row of the messages' lines and the monitor's status lines.</summary>
    public const string MessagesKey = "MSFSBA_BBS757_EICAS_MESSAGES";
    public const string MessagesLabel = "EICAS messages";

    /// <summary>The Ctrl+M row of the thrust reference's line.</summary>
    public const string ThrustKey = "MSFSBA_BBS757_THRUST_REFERENCE";
    public const string ThrustLabel = "Thrust reference";

    /// <summary>The Ctrl+M row of the N1 and EGT colors' line, spoken because a sighted pilot sees a readout turn
    /// amber or red.</summary>
    public const string ColorsKey = "MSFSBA_BBS757_ENGINE_COLORS";
    public const string ColorsLabel = "Engine gauge colors";

    /// <summary>The row of the monitor's status lines ("unavailable", "stopped", "resumed"): the messages'.</summary>
    public const string MuteKey = MessagesKey;

    /// <summary>The one simulator variable the sampler takes, where BlueBird's draw does (S10): the manual thrust reference
    /// flag, BlueBird's L:var.</summary>
    public static IReadOnlyList<Bbs757SimInput> SimInputs { get; } = Array.AsReadOnly(new[]
    {
        Bbs757SimInput.LVar(Bbs757UpperEicasReader.ManualKey),
    });

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.TickMs => TickMs;

    IReadOnlyList<Bbs757SimInput> IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>.SimInputs => SimInputs;

    /// <summary>The distinct upper EICAS layouts of the analyzed builds, in file order; a build without one is skipped
    /// (another reader's layout found it).</summary>
    public IReadOnlyList<Bbs757UpperEicasLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757UpperEicasLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757UpperEicasLayout>(Bbs757Module.UpperEicas) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, holding its own lease of
    /// the shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>,
    /// which the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757UpperEicasReading>? SamplerFor(LocatedModule module, Bbs757UpperEicasLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757UpperEicasLayout>(Bbs757Module.UpperEicas))) return null;
        return new Bbs757UpperEicasReader(module.Memory, module.AcquireLease(), layout);
    }

    /// <summary>The screen's own power, as the reading decoded it: never null, so a dark screen is never spoken and its
    /// power-up messages are a silent baseline (S4).</summary>
    public bool? Lit(Bbs757UpperEicasReading reading) => reading.Powered;

    /// <summary>Each line on its own row; <paramref name="withPrefix"/> changes nothing: "EICAS warning: …" names the
    /// display, and "Thrust reference: …" has no prefix.</summary>
    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757UpperEicasReading heard, Bbs757UpperEicasReading now, bool withPrefix) =>
        Bbs757UpperEicasSpeech.Changes(heard, now);

    public IReadOnlyList<string> Rows(Bbs757UpperEicasReading reading) => Bbs757UpperEicasSpeech.Rows(reading);
}
