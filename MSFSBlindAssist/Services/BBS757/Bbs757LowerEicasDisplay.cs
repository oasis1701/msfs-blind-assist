using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757's lower EICAS as a reader of <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>: its layouts from the shared
/// module session's builds, a <see cref="Bbs757LowerEicasReader"/> per located build on its own lease, which reads memory
/// alone (no simulator input), the display's power as its lit state, and BlueBird's words
/// (<see cref="Bbs757LowerEicasText"/>). Every line it says, page changes and status lines alike, is on the one
/// "EICAS status messages" row. Called on the UI thread only.
/// </summary>
public sealed class Bbs757LowerEicasDisplay : IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>
{
    public const string Name = "Lower EICAS";
    public const string MuteKey = "MSFSBA_BBS757_STATUS_MESSAGES";
    public const string LogChannel = "bbs757-lower-eicas";
    public const int TickMs = 250;

    /// <summary>The Ctrl+M row's label.</summary>
    public const string MonitorRowLabel = "EICAS status messages";

    string IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>.TickMs => TickMs;

    /// <summary>The distinct lower EICAS layouts of the analyzed builds, in file order; a build without one is skipped.</summary>
    public IReadOnlyList<Bbs757LowerEicasLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757LowerEicasLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757LowerEicasLayout>(Bbs757Module.LowerEicas) is { } layout && !layouts.Contains(layout))
                layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, on its own lease of the
    /// shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>, which
    /// the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757LowerEicasReading>? SamplerFor(LocatedModule module, Bbs757LowerEicasLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757LowerEicasLayout>(Bbs757Module.LowerEicas))) return null;
        return new Bbs757LowerEicasReader(module.Memory, module.AcquireLease(), layout);
    }

    /// <summary>The display is lit while it draws.</summary>
    public bool? Lit(Bbs757LowerEicasReading reading) => reading.Powered;

    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757LowerEicasReading heard, Bbs757LowerEicasReading now, bool withPrefix) =>
        Bbs757LowerEicasText.Changes(heard, now, withPrefix).Select(text => new Bbs757SpokenLine(MuteKey, text)).ToArray();

    public IReadOnlyList<string> Rows(Bbs757LowerEicasReading reading) => Bbs757LowerEicasText.Rows(reading);
}
