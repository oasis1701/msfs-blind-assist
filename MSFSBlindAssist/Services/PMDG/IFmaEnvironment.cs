namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The PMDG SDK's own view of the cross-check fields, read fresh from the data manager.</summary>
public readonly record struct FmaSdkSnapshot(bool FdCaptain, bool FdFirstOfficer, int McpAltitude, int McpHeading);

/// <summary>One read of the FMA from module memory, with the fields the SDK cross-check compares.</summary>
public readonly record struct FmaSample(FmaState Fma, bool Fd1, bool Fd2, int McpAlt, int McpHdg, bool PointersAgree);

/// <summary>Reads FMA samples through one layout from one located module memory.</summary>
public interface IFmaSampler : IDisposable
{
    Pmdg737FmaLayout Layout { get; }

    /// <summary>False when memory reads fail or the object is no longer inside the module.</summary>
    bool TryRead(out FmaSample sample);
}

/// <summary>Layouts derived from the installed files; <see cref="Failure"/> is one of <see cref="FmaReasons"/> when <see cref="Layouts"/> is empty.</summary>
public sealed record FmaAnalysis(IReadOnlyList<Pmdg737FmaLayout> Layouts, string? Failure)
{
    /// <summary>The module session loaded a layout file (it has a build): empty <see cref="Layouts"/> then means only
    /// this reader's own layout failed in it, which the reader says itself; false, no file loaded, which the four
    /// readers say as one line (WASM-29).</summary>
    public bool LayoutFileLoaded { get; init; }
}

/// <summary>A non-empty <see cref="Candidates"/> means found; <see cref="FmaReasons.NoAccess"/> is final; an empty list with a null failure means not found yet.</summary>
public sealed record FmaLocateResult(IReadOnlyList<IFmaSampler> Candidates, string? Failure);

/// <summary>Everything the FMA monitor needs from the outside world, so it can run against fakes.</summary>
public interface IFmaEnvironment
{
    long NowMs { get; }

    /// <summary>Null when the PMDG data manager is missing or not ready; read fresh on every call.</summary>
    FmaSdkSnapshot? ReadSdk();

    bool IsMuted { get; }

    /// <summary>The module session's one "display files could not be loaded" line, shared by the four readers (WASM-29).</summary>
    Pmdg737DisplayFilesNotice FilesNotice { get; }

    void Speak(string text);
    void Log(string line);
    Task<FmaAnalysis> AnalyseAsync();
    Task<FmaLocateResult> LocateAsync(IReadOnlyList<Pmdg737FmaLayout> layouts);
}
