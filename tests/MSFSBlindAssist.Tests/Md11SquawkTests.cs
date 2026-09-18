using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 transponder's typed squawk entry: what is accepted, how the read-back is decoded
/// and worded, and that the panel carries the field and the read-back row instead of the
/// eight digit keys (which stay registered so the entry can press them).
/// </summary>
public class Md11SquawkTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    [Theory]
    [InlineData(1200, "1200")]
    [InlineData(421, "0421")]     // the box's "0421" arrives as 421 and is padded back
    [InlineData(7777, "7777")]
    [InlineData(7, "0007")]
    public void AValidCode_IsPaddedToFourOctalDigits(double typed, string expected)
    {
        Assert.True(Md11Squawk.TryParse(typed, out var code, out var error));
        Assert.Equal(expected, code);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(1280)]      // an 8
    [InlineData(9)]         // a 9
    [InlineData(12345)]     // five digits
    [InlineData(12.5)]      // not whole
    [InlineData(-1)]
    public void AnInvalidCode_IsRefusedWithGuidance(double typed)
    {
        Assert.False(Md11Squawk.TryParse(typed, out var code, out var error));
        Assert.Equal("", code);
        Assert.NotEqual("", error);
    }

    [Fact]
    public void AnEmptyBox_IsRefused_NotSentAsZeroZeroZeroZero()
    {
        // MainForm passes 0 for an empty or unparseable box; 0000 must never go out by accident.
        Assert.False(Md11Squawk.TryParse(0, out _, out var error));
        Assert.Equal(Md11Squawk.EmptyMessage, error);
    }

    [Theory]
    [InlineData(0x5473, "5473")]   // read live on 2026-09-06
    [InlineData(0x1200, "1200")]
    [InlineData(0x0421, "0421")]
    public void TheStockCode_DecodesFromBco16(int word, string expected)
    {
        Assert.Equal(expected, Md11Squawk.Decode(word));
        Assert.True(Def.TryGetDisplayOverride(Md11Squawk.CodeKey, word, out var shown));
        Assert.Equal(expected, shown);
    }

    [Fact]
    public void TheConfirmation_SaysWhatTheTransponderActuallyReads()
    {
        Assert.Equal("Squawk 1200.", Md11Squawk.Confirmation("1200", "1200"));
        Assert.Equal("Squawk entry did not take, the transponder reads 5473.", Md11Squawk.Confirmation("1200", "5473"));
        Assert.Equal("Squawk 1200 entered, the transponder did not report back.", Md11Squawk.Confirmation("1200", null));
    }

    [Fact]
    public void EachDigit_MapsToItsKeypadButton_WhichStaysARegisteredControl()
    {
        Assert.Equal("MD11_PED_XPNDR_5_BT", Md11Squawk.DigitButton('5'));
        Assert.Equal(8, Md11Squawk.DigitButtons.Length);
        foreach (var button in Md11Squawk.DigitButtons)
            Assert.True(Vars.ContainsKey(button), $"{button} must stay registered: the entry presses it");
    }

    [Fact]
    public void TheTransponderPanel_HasTheFieldAndTheReadBack_NotTheDigitKeys()
    {
        var panel = Def.GetPanelControls()["Transponder"];
        Assert.Contains(Md11Squawk.SetKey, panel);
        Assert.DoesNotContain(Md11Squawk.CodeKey, panel);   // the read-back is a Status Display row now, first in the list
        Assert.Equal(Md11Squawk.CodeKey, Def.GetPanelDisplayVariables()["Transponder"][0]);
        Assert.Contains("MD11_PED_XPNDR_FAIL_LT", Def.GetPanelDisplayVariables()["Transponder"]);
        Assert.Equal(panel.IndexOf("MD11_PED_XPNDR_ABV_BLW_SW") + 1, panel.IndexOf(Md11Squawk.SetKey));   // right after the selectors
        Assert.DoesNotContain(panel, k => Md11Squawk.DigitButtons.Contains(k));
        Assert.Contains("MD11_PED_XPNDR_IDENT_BT", panel);
        Assert.Contains("MD11_PED_XPNDR_CLR_BT", panel);
    }

    [Fact]
    public void TheField_IsATextEntry_AndTheReadBack_IsAReadOnlyStatusRow()
    {
        var set = Vars[Md11Squawk.SetKey];
        Assert.Contains("_SET", Md11Squawk.SetKey);       // MainForm's text box + Set button convention
        Assert.Equal("Squawk", set.DisplayName);
        Assert.False(set.PreventTextInput);

        var code = Vars[Md11Squawk.CodeKey];
        Assert.Equal("TRANSPONDER CODE:1", code.Name);
        Assert.Equal("BCO16", code.Units);
        Assert.Equal(SimVarType.SimVar, code.Type);
        Assert.Equal(UpdateFrequency.Continuous, code.UpdateFrequency);   // rides the batch: it announces on change
        Assert.True(code.IsAnnounced);
        Assert.False(code.ExcludeFromMonitorManager);                     // "Squawk code" is the Ctrl+M row that mutes it
        Assert.True(code.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void TheDigitKeys_AreSupersededNotUnplaced()
    {
        // The safety net appends every unlisted control; the keypad is the one deliberate exception.
        var placement = Md11PanelLayout.Place(Md11ControlMap.Load());
        Assert.Empty(placement.Unplaced);
        Assert.All(Md11Squawk.DigitButtons, b => Assert.Contains(b, Md11PanelLayout.SupersededByEntryField));
    }

    /// <summary>
    /// The code is announced on CHANGE whichever way it was set — a hardware transponder, the
    /// sim's own keys, an ATC assignment — the way the A380 and the PMDGs do it. Baseline-first,
    /// and an unchanged redelivery (the panel's per-second force-read) is silent.
    /// </summary>
    [Fact]
    public void TheCode_IsAnnouncedOnChange_BaselineFirst()
    {
        var a = new Md11SquawkAnnouncer();
        Assert.Null(a.OnUpdate(0x1200));            // connecting mid-flight is not a change
        Assert.Null(a.OnUpdate(0x1200));            // redelivered unchanged
        Assert.Equal("Squawk 5473", a.OnUpdate(0x5473));
        Assert.Null(a.OnUpdate(0x5473));
        a.Reset();                                  // the disconnect wipe
        Assert.Null(a.OnUpdate(0x7000));            // the reconnect's first delivery is a baseline again
        Assert.Equal("Squawk 1200", a.OnUpdate(0x1200));
    }

    /// <summary>
    /// A typed entry presses four digits and speaks its own confirmation, so while it is in
    /// progress the transponder's intermediate codes are tracked but never spoken, and the final
    /// code is not spoken a second time after the confirmation. A change AFTER the entry speaks.
    /// </summary>
    [Fact]
    public void ATypedEntry_IsNeitherNarratedDigitByDigit_NorRepeatedAfterItsConfirmation()
    {
        var a = new Md11SquawkAnnouncer();
        a.OnUpdate(0x1200);
        a.BeginEntry();
        Assert.Null(a.OnUpdate(0x5000));
        Assert.Null(a.OnUpdate(0x5400));
        Assert.Null(a.OnUpdate(0x5473));
        a.EndEntry();                               // the confirmation has spoken "Squawk 5473."
        Assert.Null(a.OnUpdate(0x5473));            // the same code again is not news
        Assert.Equal("Squawk 1200", a.OnUpdate(0x1200));
    }

    /// <summary>
    /// Entries are counted: a second Set pressed inside the first entry's window (two to four
    /// seconds — the digits, the commit wait and the read-back) must not have the first entry's
    /// end unmute the second one mid-flight (its keypad presses would then be spoken as the
    /// transponder's setting). A reconnect's Reset re-baselines the code but leaves a running
    /// entry's silence to the entry itself; an EndEntry with nothing running must not drive the
    /// counter below zero, or the next entry would start unmuted.
    /// </summary>
    [Fact]
    public void OverlappingEntries_StaySilentUntilTheLastOneEnds()
    {
        var a = new Md11SquawkAnnouncer();
        a.OnUpdate(0x1200);
        a.BeginEntry();
        a.BeginEntry();
        a.EndEntry();                               // the first entry's finally
        Assert.True(a.EntryInProgress);
        Assert.Null(a.OnUpdate(0x5400));            // the second entry's intermediate code
        a.Reset();                                  // a disconnect mid-entry
        Assert.True(a.EntryInProgress);
        Assert.Null(a.OnUpdate(0x5473));
        a.EndEntry();
        Assert.False(a.EntryInProgress);
        a.EndEntry();                               // nothing running: must not underflow…
        Assert.False(a.EntryInProgress);
        a.BeginEntry();
        Assert.True(a.EntryInProgress);             // …or this entry would start unmuted
        a.EndEntry();
        Assert.Null(a.OnUpdate(0x5473));            // the baseline after the reset, not a change
        Assert.Equal("Squawk 1200", a.OnUpdate(0x1200));
    }

    /// <summary>A flight load re-delivers only what changed, so an empty baseline is seeded from the cache and a present one is left alone.</summary>
    [Fact]
    public void SeedIfEmpty_SeedsOnlyWhenThereIsNoBaseline()
    {
        var a = new Md11SquawkAnnouncer();
        Assert.True(a.SeedIfEmpty(0x1200));
        Assert.False(a.SeedIfEmpty(0x7000));                 // already seeded: untouched
        Assert.Null(a.OnUpdate(0x1200));                     // unchanged
        Assert.Equal("Squawk 7000", a.OnUpdate(0x7000));     // the first real change speaks
    }
}
