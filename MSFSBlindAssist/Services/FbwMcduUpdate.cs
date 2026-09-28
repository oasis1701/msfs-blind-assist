using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Turns one FlyByWire MCDU "update" body — the <c>{left, right}</c> object the SimBridge
/// relay streams AND the object <c>coherent-a32nx-mcdu-agent.js</c> rebuilds from the
/// instrument's own fields — into the Captain screen as <see cref="MCDUDisplayData"/>.
/// One parser for both transports, so they can never decode a frame differently.
///
/// Captain ("left") is authoritative, matching FBW's own web remote. When MCDU 1 is
/// unpowered (AC ESS SHED bus) its side renders as blank lines while MCDU 2 may still
/// carry the screen, so a blank left falls back to right.
/// </summary>
public static class FbwMcduUpdate
{
    public const string CaptainSide = "left";
    public const string FirstOfficerSide = "right";

    /// <summary>
    /// Decodes the Captain screen, or the First Officer screen when the Captain side is
    /// blank. Returns null only when the body carries no Captain side at all.
    /// </summary>
    public static MCDUDisplayData? Parse(JObject content)
    {
        if (content[CaptainSide] is not JObject side) { return null; }
        var data = FbwMcduFormat.BuildDisplayData(side);
        if (IsBlankScreen(data) && content[FirstOfficerSide] is JObject rightSide)
        {
            var rightData = FbwMcduFormat.BuildDisplayData(rightSide);
            if (!IsBlankScreen(rightData)) { data = rightData; }
        }
        return data;
    }

    /// <summary>
    /// True when the display carries no readable text — title, scratchpad and all 14 raw
    /// line slots empty or whitespace. That is what an unpowered MCDU side looks like.
    /// </summary>
    public static bool IsBlankScreen(MCDUDisplayData d)
    {
        if (!string.IsNullOrWhiteSpace(d.Title)) { return false; }
        if (!string.IsNullOrWhiteSpace(d.Scratchpad)) { return false; }
        foreach (var line in d.RawLines)
        {
            if (!string.IsNullOrWhiteSpace(line)) { return false; }
        }
        return true;
    }
}
