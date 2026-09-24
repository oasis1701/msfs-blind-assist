using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// Which control MainForm builds for a panel row — the part of that decision that is pure, so it is
/// pinned without a form (PanelRowRulesTests). MainForm.PanelBuilder asks it twice: the numeric
/// read-out declines every row it claims, and the status box takes every row it claims.
/// </summary>
public static class PanelRowRules
{
    /// <summary>
    /// True when MainForm builds <paramref name="def"/> as the read-only status TextBox, whose text is
    /// the definition's composed state (<c>TryDescribeControlState</c>) or the value's description.
    ///
    /// A read-only row whose state the definition COMPOSES — <see cref="SimVarDefinition.StateVariables"/>
    /// set, which only the TFDi MD-11 does — is one whatever its description count: its words come
    /// from TryDescribeControlState, and only the status box asks for them when it is built. Left to
    /// the count, the MD-11's Elevator Feel knob (a composite with ONE outer word, "Auto") fell past
    /// every branch to the plain Button at the end of the row chain, whose click writes 1 straight into
    /// the row's var — its MANUAL latch — without SetControl or the CEVENT bus; with no description it
    /// would be the numeric read-out, which shows the raw value and, the row being OnRequest, keeps
    /// showing it on a re-open (an unchanged re-read raises no update to relabel it). The read-only
    /// signal is <see cref="SimVarDefinition.RenderAsReadOnlyStatus"/>, what the MD-11 sets on a
    /// composite row; composing alone is not enough, since an MD-11 button composes its label too.
    ///
    /// Every row WITHOUT StateVariables keeps the rule it always had: more than one description, and
    /// read-only or <see cref="SimVarDefinition.OnlyAnnounceValueDescriptionMatches"/>.
    /// </summary>
    public static bool IsReadOnlyStatusRow(SimVarDefinition def)
    {
        if (def.StateVariables != null && def.RenderAsReadOnlyStatus) return true;
        return def.ValueDescriptions != null && def.ValueDescriptions.Count > 1
            && (def.RenderAsReadOnlyStatus || def.OnlyAnnounceValueDescriptionMatches);
    }
}
