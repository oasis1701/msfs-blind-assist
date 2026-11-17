using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The spoken reasons a 757 display reader is unavailable or stopped (the 757's own list; the PMDG
/// reader has its own). "BlueBird file not recognised" is the one a missing layout always gives.</summary>
public static class Bbs757DisplayReasons
{
    public const string FileNotRecognised = "BlueBird file not recognised";
    public const string FilesNotFound = "BlueBird 757 files not found";
    public const string MemoryNotFound = "aircraft memory not found";
    public const string NoAccess = "no access to the simulator";
    public const string UnexpectedValues = "unexpected values";
    public const string InternalError = "internal error, see the log";

    /// <summary>The 757's reason for a failure the shared module session gave. The session words its reasons for the PMDG
    /// 737 (<see cref="FmaReasons"/>) whatever the module, so a 757 reader passing one on as it is would tell the pilot
    /// "PMDG file not recognised" (found 2026-11-07). A reason worded alike in both lists passes through.</summary>
    public static string FromSession(string failure) => failure switch
    {
        FmaReasons.FileNotRecognised => FileNotRecognised,
        FmaReasons.FilesNotFound => FilesNotFound,
        _ => failure,
    };
}

/// <summary>
/// Where one installed BBS752.wasm keeps the ADI flight mode annunciator, all from that file's layout file,
/// never hardcoded (BBS757-D2). The 757 reaches its objects through the gauge manager: <see cref="ManagerBase"/>
/// is the manager's static address (the constant the gauge-callback accessor returns); the Captain's primary
/// flight display is the first pointer in the manager whose object carries <see cref="PfdVptr"/>; the autoflight
/// and thrust objects hang off it at <see cref="PfdToAfds"/> and <see cref="PfdToTmc"/>. Field offsets are into
/// those objects. Equality compares <see cref="Anchor"/> and the word tables by content and ignores
/// <see cref="SourceFile"/>, so the same build installed twice gives one layout.
/// </summary>
public sealed record Bbs757FmaLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint PfdVptr,
    int PfdToAfds,
    int PfdToTmc,
    int Pitch,
    int PitchArmed,
    int Roll,
    int RollArmed,
    int AtMode,
    Bbs757WordTable AtWords,
    Bbs757WordTable PitchWords,
    Bbs757WordTable PitchArmedWords,
    Bbs757WordTable RollWords,
    Bbs757WordTable RollArmedWords,
    int Status,
    Bbs757WordTable StatusWords,
    string FdWord,
    int FdCaptain,
    int FdFirstOfficer,
    int AfdsToMcp,
    int McpAltitude,
    int McpHeading,
    Bbs757McpValueType McpValueType)
{
    public bool Equals(Bbs757FmaLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase
        && ManagerSpan == other.ManagerSpan
        && PfdVptr == other.PfdVptr
        && PfdToAfds == other.PfdToAfds
        && PfdToTmc == other.PfdToTmc
        && Pitch == other.Pitch
        && PitchArmed == other.PitchArmed
        && Roll == other.Roll
        && RollArmed == other.RollArmed
        && AtMode == other.AtMode
        && AtWords.Equals(other.AtWords)
        && PitchWords.Equals(other.PitchWords)
        && PitchArmedWords.Equals(other.PitchArmedWords)
        && RollWords.Equals(other.RollWords)
        && RollArmedWords.Equals(other.RollArmedWords)
        && Status == other.Status
        && StatusWords.Equals(other.StatusWords)
        && string.Equals(FdWord, other.FdWord, StringComparison.Ordinal)
        && FdCaptain == other.FdCaptain
        && FdFirstOfficer == other.FdFirstOfficer
        && AfdsToMcp == other.AfdsToMcp
        && McpAltitude == other.McpAltitude
        && McpHeading == other.McpHeading
        && McpValueType == other.McpValueType;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase);
        h.Add(ManagerSpan);
        h.Add(PfdVptr);
        h.Add(PfdToAfds);
        h.Add(PfdToTmc);
        h.Add(Pitch);
        h.Add(PitchArmed);
        h.Add(Roll);
        h.Add(RollArmed);
        h.Add(AtMode);
        h.Add(AtWords);
        h.Add(PitchWords);
        h.Add(PitchArmedWords);
        h.Add(RollWords);
        h.Add(RollArmedWords);
        h.Add(Status);
        h.Add(StatusWords);
        h.Add(FdWord, StringComparer.Ordinal);
        h.Add(FdCaptain);
        h.Add(FdFirstOfficer);
        h.Add(AfdsToMcp);
        h.Add(McpAltitude);
        h.Add(McpHeading);
        h.Add(McpValueType);
        return h.ToHashCode();
    }
}

/// <summary>How the MCP object stores its altitude and heading: a 32-bit integer or a 64-bit float. The layout names it
/// because it is a property of the BlueBird build, never guessed by the app.</summary>
public enum Bbs757McpValueType { I32, F64 }

/// <summary>The FMA's result in a layout file: a layout, or the detailed reason (for the log) there is none.</summary>
public sealed record Bbs757FmaDerivation(Bbs757FmaLayout? Layout, string? Failure);
