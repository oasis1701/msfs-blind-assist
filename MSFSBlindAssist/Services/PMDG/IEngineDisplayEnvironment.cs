namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The three Ctrl+M rows of the engine display announcements; the status lines follow the alerts row.</summary>
public enum EngineDisplayMuteRow { Alerts, Colors, ThrustMode }

/// <summary>Layouts derived from the installed files; <see cref="Failure"/> is one of <see cref="FmaReasons"/> when <see cref="Layouts"/> is empty.</summary>
public sealed record EngineDisplayAnalysis(IReadOnlyList<Pmdg737EngineDisplayLayout> Layouts, string? Failure)
{
    /// <summary>The module session loaded a layout file (it has a build): empty <see cref="Layouts"/> then means only
    /// this reader's own layout failed in it, which the reader says itself; false, no file loaded, which the four
    /// readers say as one line (WASM-29).</summary>
    public bool LayoutFileLoaded { get; init; }
}

/// <summary>A non-empty <see cref="Candidates"/> means found; <see cref="FmaReasons.NoAccess"/> is final; an empty list with a null failure means not found yet.</summary>
public sealed record EngineDisplayLocateResult(IReadOnlyList<IEngineDisplaySampler> Candidates, string? Failure);

/// <summary>Everything the engine display monitor needs from the outside world, so it can run against fakes.</summary>
public interface IEngineDisplayEnvironment
{
    long NowMs { get; }

    /// <summary>Null when SimConnect is disconnected or the PMDG data manager is missing or not ready; read fresh on every call.</summary>
    EngineDisplaySdkSnapshot? ReadSdk();

    bool IsMuted(EngineDisplayMuteRow row);

    /// <summary>The module session's one "display files could not be loaded" line, shared by the four readers (WASM-29).</summary>
    Pmdg737DisplayFilesNotice FilesNotice { get; }

    void Speak(string text);
    void Log(string line);
    Task<EngineDisplayAnalysis> AnalyseAsync();
    Task<EngineDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737EngineDisplayLayout> layouts);
}
