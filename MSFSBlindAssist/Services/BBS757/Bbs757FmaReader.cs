using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// One read of the 757 FMA: the words the Captain's ADI draws (null = blank, <see cref="Bbs757WordTable.Unrecognised"/>
/// for a value with no word), the raw field values behind them (for change detection, "unrecognised" and the range
/// check), the two F/D switch bytes (the Captain's decides what the strip draws), and the MCP's selected altitude (feet)
/// and heading (degrees), read with the rest and range-checked, never compared with the simulator (B757-30).
/// <see cref="Corrupt"/> is a torn or out-of-range reading, never spoken, shown or a baseline.
/// </summary>
public readonly record struct Bbs757FmaReading(
    string? AutoThrottle,
    string? Pitch,
    string? PitchArmed,
    string? Roll,
    string? RollArmed,
    string? Status,
    int AtValue,
    int PitchValue,
    int PitchArmedValue,
    int RollValue,
    int RollArmedValue,
    int StatusValue,
    bool FdCaptain,
    bool FdFirstOfficer,
    int McpAltitude,
    int McpHeading,
    bool Corrupt) : IBbs757Reading;

/// <summary>The FMA's fields as read from memory, before the layout's words and BlueBird's draw rules are applied
/// (<see cref="Bbs757FmaReader.Decode"/>). The MCP values are as stored, whatever <see cref="Bbs757McpValueType"/>.</summary>
public readonly record struct Bbs757FmaRaw(
    int At,
    int Pitch,
    int PitchArmed,
    int Roll,
    int RollArmed,
    int Status,
    bool FdCaptain,
    bool FdFirstOfficer,
    double McpAltitude,
    double McpHeading);

/// <summary>
/// Reads FMA samples from the 757 module's memory through a derived layout (BBS757-D2, WASM-2: no hardcoded offset):
/// the FMA reader's <see cref="IBbs757DisplaySampler{TReading}"/>. The Captain PFD is the first pointer in the gauge
/// manager (<see cref="Bbs757FmaLayout.ManagerBase"/>, within <see cref="Bbs757FmaLayout.ManagerSpan"/>) whose object's
/// first word is the pfd class vptr; its autoflight and thrust objects hang off it, and the MCP object off the
/// autoflight one (<see cref="Bbs757FmaLayout.AfdsToMcp"/>, the pointer the autoflight's own code reads the MCP
/// through). Owns its lease of the simulator handle. Every read is bounds-checked against committed module memory
/// (WASM-4), re-walked each call; the regions are asked about once per reading (WASM-15).
/// </summary>
public sealed class Bbs757FmaReader : IBbs757DisplaySampler<Bbs757FmaReading>
{
    /// <summary>The MCP's selected altitude range, in feet: a 757's altitude window never leaves 0 to 50,000 ft.</summary>
    internal const double MaxMcpAltitude = 50_000;

    /// <summary>The MCP's selected heading range, in degrees: 0 to 360 (0 and 360 are both north).</summary>
    internal const double MaxMcpHeading = 360;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Bbs757FmaLayout Layout { get; }

    public Bbs757FmaReader(ModuleMemory memory, IDisposable ownedSource, Bbs757FmaLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    /// <summary>The Captain PFD object: the first manager pointer whose target carries the pfd vptr. False when
    /// none in the manager's span does (the module is not where the anchor said, or not ready).</summary>
    public bool TryFindPfd(out uint pfd)
    {
        pfd = 0;
        for (uint off = 0; off + 4 <= Layout.ManagerSpan; off += 4)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, 4)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.PfdVptr)
            {
                pfd = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>False when the PFD cannot be found, an object pointer is null or a read falls outside the module (the
    /// memory moved or went away); otherwise a reading, <see cref="Bbs757FmaReading.Corrupt"/> when a value is out of
    /// range.</summary>
    public bool TryRead(out Bbs757FmaReading reading)
    {
        reading = default;
        using var _ = _memory.RememberRegions();
        if (!TryFindPfd(out uint pfd)) return false;
        if (!Int(pfd, Layout.PfdToAfds, out int afdsPtr) || !Int(pfd, Layout.PfdToTmc, out int tmcPtr)) return false;
        uint afds = unchecked((uint)afdsPtr), tmc = unchecked((uint)tmcPtr);
        if (afds == 0 || tmc == 0) return false;

        if (!Int(tmc, Layout.AtMode, out int at) || !Int(afds, Layout.Pitch, out int pitch)
            || !Int(afds, Layout.PitchArmed, out int pitchArmed) || !Int(afds, Layout.Roll, out int roll)
            || !Int(afds, Layout.RollArmed, out int rollArmed) || !Int(afds, Layout.Status, out int status)
            || !Byte(afds, Layout.FdCaptain, out byte fdCaptain) || !Byte(afds, Layout.FdFirstOfficer, out byte fdFirstOfficer))
            return false;

        if (!Int(afds, Layout.AfdsToMcp, out int mcpPtr)) return false;
        uint mcp = unchecked((uint)mcpPtr);
        if (mcp == 0 || !Mcp(mcp, Layout.McpAltitude, out double altitude) || !Mcp(mcp, Layout.McpHeading, out double heading))
            return false;

        // BlueBird's F/D getter returns the byte "& 1".
        reading = Decode(Layout, new Bbs757FmaRaw(at, pitch, pitchArmed, roll, rollArmed, status,
            (fdCaptain & 1) != 0, (fdFirstOfficer & 1) != 0, altitude, heading));
        return true;
    }

    /// <summary>
    /// The reading the Captain's ADI draws from <paramref name="raw"/>, by BlueBird's own draw rules (S10):
    /// <list type="bullet">
    /// <item>AFDS (R4): the status value's word when <see cref="Bbs757FmaLayout.StatusWords"/> has it ("CMD"), else
    /// <see cref="Bbs757FmaLayout.FdWord"/> ("FD") while the Captain's F/D is on, else blank; a status value with no word is
    /// never "unrecognised".</item>
    /// <item>The strip (R5): pitch with pitch armed and roll with roll armed only while the Captain's F/D is on or the
    /// status has its word; otherwise all four read blank, whatever the raw fields hold. A/T always.</item>
    /// </list>
    /// Corrupt when a raw mode value is negative or far beyond any word table, drawn or not, or an MCP value is not a
    /// number or out of range (0 to <see cref="MaxMcpAltitude"/> ft, 0 to <see cref="MaxMcpHeading"/> degrees). The MCP
    /// values are whole numbers, rounded.
    /// </summary>
    public static Bbs757FmaReading Decode(Bbs757FmaLayout layout, Bbs757FmaRaw raw)
    {
        bool hasStatusWord = layout.StatusWords.Words.TryGetValue(raw.Status, out string? statusWord);
        string? status = hasStatusWord ? statusWord : raw.FdCaptain ? layout.FdWord : null;
        bool stripDrawn = raw.FdCaptain || hasStatusWord;

        bool altitudeOk = InRange(raw.McpAltitude, MaxMcpAltitude), headingOk = InRange(raw.McpHeading, MaxMcpHeading);
        bool corrupt = IsCorrupt(raw.At) || IsCorrupt(raw.Pitch) || IsCorrupt(raw.PitchArmed) || IsCorrupt(raw.Roll)
            || IsCorrupt(raw.RollArmed) || IsCorrupt(raw.Status) || !altitudeOk || !headingOk;

        return new Bbs757FmaReading(
            AutoThrottle: layout.AtWords.Lookup(raw.At),
            Pitch: stripDrawn ? layout.PitchWords.Lookup(raw.Pitch) : null,
            PitchArmed: stripDrawn ? layout.PitchArmedWords.Lookup(raw.PitchArmed) : null,
            Roll: stripDrawn ? layout.RollWords.Lookup(raw.Roll) : null,
            RollArmed: stripDrawn ? layout.RollArmedWords.Lookup(raw.RollArmed) : null,
            Status: status,
            AtValue: raw.At,
            PitchValue: raw.Pitch,
            PitchArmedValue: raw.PitchArmed,
            RollValue: raw.Roll,
            RollArmedValue: raw.RollArmed,
            StatusValue: raw.Status,
            FdCaptain: raw.FdCaptain,
            FdFirstOfficer: raw.FdFirstOfficer,
            McpAltitude: altitudeOk ? (int)Math.Round(raw.McpAltitude) : 0,
            McpHeading: headingOk ? (int)Math.Round(raw.McpHeading) : 0,
            Corrupt: corrupt);
    }

    /// <summary>A raw field value that cannot be a mode index: negative, or far beyond any word table.</summary>
    private static bool IsCorrupt(int v) => v < 0 || v > 255;

    /// <summary>A finite value from 0 to <paramref name="max"/>, both included.</summary>
    private static bool InRange(double v, double max) => double.IsFinite(v) && v >= 0 && v <= max;

    private bool Int(uint baseAddr, int offset, out int v) => _memory.TryReadInt32(baseAddr + (uint)offset, out v);

    private bool Byte(uint baseAddr, int offset, out byte v) => _memory.TryReadByte(baseAddr + (uint)offset, out v);

    /// <summary>One MCP value, read as the layout says this build stores it.</summary>
    private bool Mcp(uint mcp, int offset, out double v)
    {
        uint at = mcp + (uint)offset;
        if (Layout.McpValueType == Bbs757McpValueType.F64) return _memory.TryReadDouble(at, out v);
        bool ok = _memory.TryReadInt32(at, out int i);
        v = i;
        return ok;
    }

    public void Dispose() => _ownedSource.Dispose();
}
