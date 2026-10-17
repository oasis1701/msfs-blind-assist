namespace MSFSBlindAssist.FirstOfficer.MD11;

/// <summary>
/// Everything the MD-11 First Officer executor needs from the outside world: the definition's
/// single CEVENT bus, reads, and time. Production is <see cref="Md11DefinitionFoTransport"/>;
/// the tests drive the executor through a fake MD-11 that implements TFDi's decoded semantics.
/// </summary>
public interface IMd11FoTransport
{
    /// <summary>A write would reach the aircraft (bus present, calculator path can land it).</summary>
    bool Ready { get; }
    /// <summary>CEVENTs queued on the bus and not yet written.</summary>
    int PendingWrites { get; }
    /// <summary>Monotonic milliseconds.</summary>
    long NowMs { get; }
    /// <summary>The annunciators have power (lamps are meaningful).</summary>
    bool IsPowered { get; }

    bool Fire(int eventId);
    bool Press(int downId, int upId);
    Task<bool> HoldAsync(int downId, int upId, int holdMs);
    bool WriteExternal(string var, double value);

    /// <summary>Completes on the NEXT delivery of <paramref name="key"/>, or null on timeout/unreadable.</summary>
    Task<double?> ReadFreshAsync(string key, int timeoutMs);
    /// <summary>The cache (a streamed var's cache is fresh); null when never delivered.</summary>
    double? ReadCached(string key);

    /// <summary>Keep the aircraft's own lamp speech for this control quiet (the FO narration says it).</summary>
    void NoteActuation(string nodeId);
    /// <summary>Silence all lamp speech for a while (the annunciator light test).</summary>
    void MuteLampSpeech(int ms);

    Task DelayAsync(int ms);
}
