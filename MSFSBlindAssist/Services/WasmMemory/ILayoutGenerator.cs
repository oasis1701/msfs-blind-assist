namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// The layout generator as the store sees it: an optional program beside the app that writes layout files, turning
/// a .wasm file into its layout file
/// (<c>LayoutGenerator &lt;wasm file&gt; &lt;layouts root&gt;</c>). Neither member throws for a generator that is
/// missing, crashes or hangs: that is a null version or a failed run.
/// </summary>
public interface ILayoutGenerator
{
    /// <summary>The generator's version, or null when it crashed, timed out or did not answer a whole number.</summary>
    int? AskVersion();

    /// <summary>Runs the generator on <paramref name="wasmFile"/>, writing under <paramref name="layoutsRoot"/>, and
    /// waits for it to end.</summary>
    GeneratorRun Run(string wasmFile, string layoutsRoot);
}

/// <summary>One run of the generator.</summary>
/// <param name="ExitCode">0 when a layout file was written; null when the generator timed out (and was killed) or
/// could not start.</param>
/// <param name="TimedOut">True when the run was killed at its timeout.</param>
/// <param name="Output">What it printed, standard output's lines then standard error's, for the log.</param>
public sealed record GeneratorRun(int? ExitCode, bool TimedOut, IReadOnlyList<string> Output);
