using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's three altimeter STD toggles (review round 2, A20): which knob each row pushes, what
/// confirms it, where the rows sit, and the read-back's words. STD itself is unreadable (it lives on
/// the WASM PFD), so a press is confirmed by the altimeter setting it changes. The press routing in
/// SetControl needs a live bus and is verified in the sim.
/// </summary>
public class Md11StdTogglesTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    private static Md11Control Control(string nodeId)
        => Map.Controls.Single(c => string.Equals(c.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

    // ---- the push-target table ---------------------------------------------------------

    [Theory]
    [InlineData(Md11StdToggles.CaptainKey, "MD11_LECP_BAROSET_CAP", "MD11_CAP_ALTIMETER", false)]
    [InlineData(Md11StdToggles.FirstOfficerKey, "MD11_RECP_BAROSET_CAP", "MD11_FO_ALTIMETER", true)]
    [InlineData("MD11_MIP_ISFD_STD_BT", "MD11_MIP_ISFD_BARO_KB", "MD11_STBY_ALTIMETER", true)]
    public void EachToggle_PushesItsAltimeterKnob_AndIsReadBackUnlessAnAnnouncerSpeaksIt(
        string key, string knob, string altimeter, bool readsBack)
    {
        Assert.True(Md11StdToggles.TryGet(key, out var t));
        Assert.Equal(knob, t.Knob);
        Assert.Equal(altimeter, t.AltimeterVar);
        Assert.Equal(readsBack, t.ReadsBack);   // the captain's change is spoken by Md11AltimeterAnnouncer
    }

    [Theory]
    [InlineData("MD11_LECP_BAROSET_CAP")]   // the knob's own row stays a read-only status field
    [InlineData("MD11_MIP_ISFD_BARO_KB")]
    [InlineData("MD11_CAP_ALTIMETER")]
    public void NothingElse_IsAToggle(string key)
    {
        Assert.False(Md11StdToggles.TryGet(key, out _));
    }

    [Fact]
    public void TheCaptainsKnob_IsMd11FcpsBaroKnob()
    {
        Assert.Equal(Md11Fcp.BaroKnob, Md11StdToggles.Captain.Knob);
    }

    /// <summary>
    /// Each knob is TFDi's push knob with a press pair (the push is the STD toggle) and reads the
    /// altimeter its row confirms — pinned against the embedded map, because a wrong pairing does
    /// not throw: it pushes one side and reads back another.
    /// </summary>
    [Theory]
    [InlineData("MD11_LECP_BAROSET_CAP", 86057, 86058, "MD11_CAP_ALTIMETER")]
    [InlineData("MD11_RECP_BAROSET_CAP", 86127, 86128, "MD11_FO_ALTIMETER")]
    [InlineData("MD11_MIP_ISFD_BARO_KB", 94991, 95002, "MD11_STBY_ALTIMETER")]
    public void EachKnob_IsAPushKnobWithAPressPair_ReadingItsAltimeter(string knob, int down, int up, string altimeter)
    {
        var c = Control(knob);

        Assert.Equal(Md11Kinds.KnobPush, c.Kind);
        Assert.Equal(down, c.Event("LEFT_BUTTON_DOWN"));
        Assert.Equal(up, c.Event("LEFT_BUTTON_UP"));
        Assert.Equal(altimeter, c.StateVar);
        Assert.Contains(Md11StdToggles.Targets, t => t.Knob == knob && t.AltimeterVar == altimeter);
    }

    /// <summary>TFDi's standby STD button is the animation its baro knob's push drives — no events of its own, which is why its row is redirected.</summary>
    [Fact]
    public void TheStandbyDisplaysStdButton_HasNoEventsOfItsOwn()
    {
        var c = Control(Md11StdToggles.StandbyKey);

        Assert.Equal(Md11Kinds.Button, c.Kind);
        Assert.Empty(c.Events);
    }

    // ---- the read-back sentence --------------------------------------------------------

    /// <summary>
    /// Each side is spelled exactly as Ctrl+B spells it (<see cref="Md11Fcp.Altimeters"/>) — "First
    /// Officer", capital O — so an STD read-back brailles like "First Officer altimeter not set …".
    /// ReadBackSentence speaks whatever side it is handed, so the targets' own spelling is pinned here.
    /// </summary>
    [Fact]
    public void EachSide_IsSpelledAsCtrlBSpellsIt()
    {
        Assert.Equal(Md11Fcp.Altimeters.Select(a => a.Side), Md11StdToggles.Targets.Select(t => t.Side));
    }

    [Theory]
    [InlineData("First Officer", 29.92, "First Officer altimeter standard")]
    [InlineData("Standby", 1013.0, "Standby altimeter standard")]
    [InlineData("Standby", 30.12, "Standby altimeter: 1020, 30.12")]
    [InlineData("First Officer", 995.0, "First Officer altimeter: 995, 29.38")]
    public void ReadBackSentence_IsTheBKeysWords_PrefixedWithTheSide(string side, double reading, string expected)
    {
        Assert.Equal(expected, Md11StdToggles.ReadBackSentence(side, reading));
    }

    /// <summary>Word for word the B key's sentence (Md11AltimeterAnnouncer.Sentence), with the side in front.</summary>
    [Theory]
    [InlineData(29.92)]
    [InlineData(30.12)]
    [InlineData(1013.0)]
    [InlineData(1020.0)]
    public void ReadBackSentence_MatchesTheBKey(double reading)
    {
        var b = Md11AltimeterAnnouncer.Sentence(reading);   // "Altimeter standard" / "Altimeter: 1020, 30.12"

        Assert.Equal("Standby " + char.ToLowerInvariant(b[0]) + b[1..], Md11StdToggles.ReadBackSentence("Standby", reading));
    }

    [Fact]
    public void ReadBackSentence_IsNull_WithoutADeliveredReading()
    {
        Assert.Null(Md11StdToggles.ReadBackSentence("Standby", null));   // nothing delivered
        Assert.Null(Md11StdToggles.ReadBackSentence("Standby", 0));      // a missing export reads a flat 0
    }

    // ---- the rows -----------------------------------------------------------------------

    [Theory]
    [InlineData(Md11StdToggles.CaptainKey, "Captain Altimeter STD")]
    [InlineData(Md11StdToggles.FirstOfficerKey, "First Officer Altimeter STD")]
    public void TheTwoMsfsbaRows_AreWriteOnlyButtons(string key, string label)
    {
        var d = new TFDiMD11Definition().GetVariables()[key];

        Assert.Equal(label, d.DisplayName);
        Assert.True(d.RenderAsButton);
        Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);   // claimed by SetControl: never read, never written generically
        Assert.False(d.IsAnnounced);
    }

    [Theory]
    [InlineData("EFIS Captain", "MD11_LECP_BAROSET_CAP", Md11StdToggles.CaptainKey)]
    [InlineData("EFIS First Officer", "MD11_RECP_BAROSET_CAP", Md11StdToggles.FirstOfficerKey)]
    public void EachRow_SitsRightAfterItsAltimeterSettingRow(string panel, string settingRow, string key)
    {
        var controls = new TFDiMD11Definition().GetPanelControls();
        var keys = controls[panel];
        int at = keys.IndexOf(settingRow);

        Assert.True(at >= 0, $"{settingRow} missing from {panel}");
        Assert.Equal(key, keys[at + 1]);
        Assert.Equal(1, controls.Values.SelectMany(k => k).Count(k => k == key));
    }

    [Fact]
    public void TheStandbyRow_StaysWhereTheLayoutPutsIt()
    {
        Assert.Contains(Md11StdToggles.StandbyKey, new TFDiMD11Definition().GetPanelControls()["Standby Instruments"]);
    }

    /// <summary>The fleet rule (VarNameCollisionTests.Panel_rows_do_not_share_a_spoken_name), stated for the three panels these rows join.</summary>
    [Theory]
    [InlineData("EFIS Captain")]
    [InlineData("EFIS First Officer")]
    [InlineData("Standby Instruments")]
    public void NoTwoRowsOnThesePanels_ShareASpokenName(string panel)
    {
        var def = new TFDiMD11Definition();
        var vars = def.GetVariables();
        var names = def.GetPanelControls()[panel].Where(vars.ContainsKey).Select(k => vars[k].DisplayName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>Invariant: an export-backed knob row stays read-only — the STD rows are separate rows, not a loosened gate.</summary>
    [Theory]
    [InlineData("MD11_LECP_BAROSET_CAP")]
    [InlineData("MD11_RECP_BAROSET_CAP")]
    [InlineData("MD11_MIP_ISFD_BARO_KB")]
    public void TheKnobsOwnRows_StayReadOnly(string knob)
    {
        Assert.True(new TFDiMD11Definition().GetVariables()[knob].RenderAsReadOnlyStatus);
    }
}
