using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One reading of a 757 display from module memory. <see cref="Corrupt"/> is a torn or out-of-range reading:
/// never shown, spoken or a baseline.</summary>
public interface IBbs757Reading
{
    bool Corrupt { get; }
}

/// <summary>Reads one display object from one located module, on its own lease of the shared simulator handle (disposing
/// it releases the lease). False when a read falls outside the module or the object cannot be found: the memory moved
/// or went away.</summary>
public interface IBbs757DisplaySampler<TReading> : IDisposable
{
    bool TryRead(out TReading reading);

    /// <summary>The read the monitor makes, with this tick's values of the reader's
    /// <see cref="IBbs757DisplayReader{TLayout, TReading}.SimInputs"/>: where BlueBird's draw takes a simulator variable,
    /// the reader takes the same one (S10). A sampler that takes none (the FMA's, the lower EICAS's) keeps the default,
    /// which reads memory alone.</summary>
    bool TryRead(Bbs757SimValues sim, out TReading reading) => TryRead(out reading);
}

/// <summary>The simulator's own values of a reader's
/// <see cref="IBbs757DisplayReader{TLayout, TReading}.SimInputs"/>, each under its <see cref="Bbs757SimInput.Key"/>,
/// read fresh each tick.</summary>
public readonly record struct Bbs757SimValues(IReadOnlyDictionary<string, double> Values)
{
    /// <summary>The key's value, or null when it was not read.</summary>
    public double? this[string key] => Values is not null && Values.TryGetValue(key, out double v) ? v : null;
}

/// <summary>
/// One simulator variable a reader's sampler takes where BlueBird's draw takes it (S10): an input to the reading, never a
/// check of it (B757-30). <see cref="Key"/> is the name the sampler looks its value up by in <see cref="Bbs757SimValues"/>;
/// <see cref="Name"/>, <see cref="Type"/> and <see cref="Units"/> are the variable as SimConnect reads it. BlueBird's own
/// variables are L:vars, with no units (<see cref="LVar"/>); a stock simulator variable (<c>PLANE LATITUDE</c>,
/// <c>TURB ENG N2:1</c>) is a <see cref="SimVarType.SimVar"/> with its units, never an L:var of that name ([VAR-2]).
/// </summary>
public readonly record struct Bbs757SimInput(string Key, string Name, SimVarType Type, string? Units)
{
    /// <summary>BlueBird's L:var <paramref name="name"/>, looked up by its own name.</summary>
    public static Bbs757SimInput LVar(string name) => new(name, name, SimVarType.LVar, null);

    /// <summary>Whether <paramref name="other"/> is the same variable: the same name and type, and for a stock variable
    /// the same units in any case (an L:var always reads as a number).</summary>
    public bool SameVariable(Bbs757SimInput other) =>
        string.Equals(Name, other.Name, StringComparison.Ordinal) && Type == other.Type
        && (Type == SimVarType.LVar || string.Equals(Units, other.Units, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One line a reader speaks by itself, with the Ctrl+M row that mutes it: a reader with several rows (the upper
/// EICAS's messages and thrust reference) is muted row by row. The monitor's status lines ("unavailable", "stopped",
/// "resumed") belong to the reader's <see cref="IBbs757DisplayReader{TLayout, TReading}.MuteKey"/>.</summary>
public readonly record struct Bbs757SpokenLine(string MuteKey, string Text);

/// <summary>
/// What one 757 display reader adds to <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>, which owns the states,
/// timings, the settle and every status line: its layouts and samplers, the simulator inputs its sampler takes, its power
/// state, and its words. Nothing here judges a reading by a simulator value: the monitor trusts any reading that is not
/// <see cref="IBbs757Reading.Corrupt"/> (B757-30). Every member is called on the UI thread and must not throw for any
/// reading a sampler returns.
/// </summary>
public interface IBbs757DisplayReader<TLayout, TReading>
    where TLayout : class
    where TReading : IBbs757Reading
{
    /// <summary>The display's name in the status lines: "<c>Name</c> announcements unavailable: …".</summary>
    string Name { get; }

    /// <summary>The Ctrl+M row of the monitor's status lines.</summary>
    string MuteKey { get; }

    /// <summary>The named log the monitor writes this reader's lines to.</summary>
    string LogChannel { get; }

    /// <summary>The monitor's timer interval, in milliseconds.</summary>
    int TickMs { get; }

    /// <summary>The simulator variables the sampler takes where BlueBird's draw takes them (S10), each under its
    /// <see cref="Bbs757SimInput.Key"/>: inputs to the reading, never compared with it. None by default (the FMA, the
    /// lower EICAS).</summary>
    IReadOnlyList<Bbs757SimInput> SimInputs => Array.Empty<Bbs757SimInput>();

    /// <summary>This reader's distinct layouts in the analysed builds, in file order (empty: none).</summary>
    IReadOnlyList<TLayout> LayoutsIn(ModuleAnalysis analysis);

    /// <summary>A sampler for <paramref name="layout"/> in <paramref name="module"/>, on its own lease; null when this
    /// module's build has another layout.</summary>
    IBbs757DisplaySampler<TReading>? SamplerFor(LocatedModule module, TLayout layout);

    /// <summary>Whether the display is powered, as the reading decoded it; null for a display with no power state (the
    /// FMA). A reader returns null always or never: a <c>Lit</c> that turned null for a tick would reset the power-up
    /// quiet.</summary>
    bool? Lit(TReading reading);

    /// <summary>The lines that tell what changed from <paramref name="heard"/> to <paramref name="now"/> (empty: nothing to
    /// say); <paramref name="withPrefix"/> false for the parts that follow "resumed".</summary>
    IReadOnlyList<Bbs757SpokenLine> Changes(TReading heard, TReading now, bool withPrefix);

    /// <summary>The display window's rows for a reading.</summary>
    IReadOnlyList<string> Rows(TReading reading);
}

/// <summary>
/// The app's side of a 757 display monitor, so it runs against fakes: the clock, the readers' simulator inputs, the Ctrl+M
/// mutes, speech, the named logs and the shared module session's analysis and memory search. The monitor calls every
/// member on the UI thread except <see cref="Log"/>, which an abandoned job's clean-up may call from a pool thread.
/// </summary>
public interface IBbs757DisplayEnvironment
{
    long NowMs { get; }

    /// <summary>The values of the reader inputs whose <see cref="Bbs757SimInput.Key"/>s are <paramref name="keys"/>, each
    /// under its key, or null while the simulator is disconnected, no flight runs or another aircraft is loaded (the
    /// monitor then waits, silently): with no keys at all, too.</summary>
    Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys);

    bool IsMuted(string muteKey);

    /// <summary>The module session's one "display files could not be loaded" line, shared by every 757 reader (S14).</summary>
    Bbs757DisplayFilesNotice FilesNotice { get; }

    /// <summary>The non-interrupting announce (S5).</summary>
    void Speak(string text);

    void Log(string channel, string line);

    Task<ModuleAnalysis> AnalyseAsync();

    Task<ModuleLocation> LocateAsync();
}
