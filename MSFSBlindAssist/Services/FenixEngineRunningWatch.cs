namespace MSFSBlindAssist.Services;

/// <summary>
/// Turns the Fenix's own <c>running</c> flags into "Engine 1 running" / "Engine 1 stopped" lines, a
/// background change the pilot did not directly trigger (CORE-7). The first known value per engine is
/// a silent baseline, and an unknown value never changes what is remembered, so a data dropout does
/// not re-speak. One removable piece: the window speaks whatever this returns.
/// </summary>
public sealed class FenixEngineRunningWatch
{
    private readonly bool?[] _last = new bool?[2];

    /// <summary>The lines to speak for <paramref name="reading"/>, engine 1 first; empty when nothing changed.</summary>
    public IReadOnlyList<string> Observe(FenixEngineReading reading)
    {
        var lines = new List<string>();
        var now = new[] { reading.Engine1.Running, reading.Engine2.Running };
        for (int i = 0; i < 2; i++)
        {
            if (now[i] is not bool running) continue;
            if (_last[i] is bool previous && previous != running)
                lines.Add($"Engine {i + 1} {(running ? "running" : "stopped")}");
            _last[i] = running;
        }
        return lines;
    }
}
