// Characterization tests for FenixChannelDcduKeys, the DCDU window's key map and press decisions on the Fenix display
// channel. Pure logic: every snapshot is built here with FenixChannelProtoWriter and the field numbers written out
// literally (186 for the DCDU, 12 to 15 for its four key labels), never read from FenixChannelFields and never from a
// capture, so a wrong number in the app's table fails a test.
//
// The contract: nine keys, each pressed through one registered button variable of the Fenix definition. The chords are the
// A32NX window's: Ctrl+1/Ctrl+2 and Alt+1/Alt+2 for the four soft keys, or F1/F2 and F7/F8 when the alternate soft keys
// are on (the other set then maps to nothing), always PageDown/PageUp for page plus and minus, Ctrl+PageDown/Ctrl+PageUp
// for message plus and minus (DOWN is forward) and Ctrl+P for print; a chord with any other modifier maps to nothing. A
// soft key is decided on the snapshot it is given: a DCDU the app cannot read gives "DCDU not available", a blank label
// gives "No action on that key.", otherwise the key presses. The page, message and print keys always press: they never
// depend on what the app reads.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduKeysTests
{
    // 14 spaces, as the empty DCDU's Info2 and Info3 were measured on 3.0.0.7613 (2026-11-03).
    private const string Spaces14 = "              ";

    private static readonly FenixDcduKey[] SoftKeys =
        [FenixDcduKey.LeftKey1, FenixDcduKey.LeftKey2, FenixDcduKey.RightKey1, FenixDcduKey.RightKey2];

    private static readonly FenixDcduKey[] OtherKeys =
    [
        FenixDcduKey.PageMinus, FenixDcduKey.PagePlus, FenixDcduKey.MessageMinus, FenixDcduKey.MessagePlus,
        FenixDcduKey.Print
    ];

    // Each key with the variable it presses and the name the definition gives that variable, written out literally.
    private static readonly (FenixDcduKey Key, string Variable, string Name)[] Presses =
    [
        (FenixDcduKey.LeftKey1, "S_DCDU1_LSK1L", "LSK 1 Left"),
        (FenixDcduKey.LeftKey2, "S_DCDU1_LSK2L", "LSK 2 Left"),
        (FenixDcduKey.RightKey1, "S_DCDU1_LSK1R", "LSK 1 Right"),
        (FenixDcduKey.RightKey2, "S_DCDU1_LSK2R", "LSK 2 Right"),
        (FenixDcduKey.PagePlus, "S_DCDU1_PGDN", "Page Plus"),
        (FenixDcduKey.PageMinus, "S_DCDU1_PGUP", "Page Minus"),
        (FenixDcduKey.MessagePlus, "S_DCDU1_MSGUP", "Message Plus"),
        (FenixDcduKey.MessageMinus, "S_DCDU1_MSGDWN", "Message Minus"),
        (FenixDcduKey.Print, "S_DCDU1_PRINT", "Print")
    ];

    // A DCDU part with the print frame set, as the empty DCDU was measured, and the lines given: field number and text.
    private static FenixChannelProtoWriter Content(params (int Field, string Text)[] lines)
    {
        var content = new FenixChannelProtoWriter().Bool(1, true);
        foreach ((int field, string text) in lines)
        {
            content.String(field, text);
        }

        return content;
    }

    // The RemoteData message of a push whose field 186 is the given part.
    private static FenixChannelPush Remote(FenixChannelProtoWriter dcdu)
        => FenixChannelPush.From(new FenixChannelProtoWriter().Message(186, dcdu).ToArray())!;

    private static FenixChannelSnapshot Snapshot(bool trusted, string reason, FenixChannelPush? push)
        => new(new FenixDisplayVerdict(trusted, reason), null, push, null, 0, 1);

    // A trusted snapshot of a DCDU with the key labels given: field number (12 to 15) and text.
    private static FenixChannelSnapshot Trusted(params (int Field, string Text)[] lines)
        => Snapshot(true, "trusted", Remote(Content(lines)));

    // The label field of each soft key, by the position of the key.
    private static int FieldOf(FenixDcduKey key) => key switch
    {
        FenixDcduKey.LeftKey1 => 12,
        FenixDcduKey.LeftKey2 => 13,
        FenixDcduKey.RightKey1 => 14,
        FenixDcduKey.RightKey2 => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    // ---- The chords --------------------------------------------------------------------------

    [Fact]
    public void Each_chord_maps_to_its_key()
    {
        (Keys Chord, FenixDcduKey Key)[] standardSoftKeys =
        [
            (Keys.Control | Keys.D1, FenixDcduKey.LeftKey1), (Keys.Control | Keys.D2, FenixDcduKey.LeftKey2),
            (Keys.Alt | Keys.D1, FenixDcduKey.RightKey1), (Keys.Alt | Keys.D2, FenixDcduKey.RightKey2)
        ];
        (Keys Chord, FenixDcduKey Key)[] alternateSoftKeys =
        [
            (Keys.F1, FenixDcduKey.LeftKey1), (Keys.F2, FenixDcduKey.LeftKey2),
            (Keys.F7, FenixDcduKey.RightKey1), (Keys.F8, FenixDcduKey.RightKey2)
        ];
        // DOWN is forward: page down is page plus, and with Ctrl message plus.
        (Keys Chord, FenixDcduKey Key)[] always =
        [
            (Keys.PageDown, FenixDcduKey.PagePlus), (Keys.PageUp, FenixDcduKey.PageMinus),
            (Keys.Control | Keys.PageDown, FenixDcduKey.MessagePlus),
            (Keys.Control | Keys.PageUp, FenixDcduKey.MessageMinus),
            (Keys.Control | Keys.P, FenixDcduKey.Print)
        ];

        foreach (bool alternate in new[] { false, true })
        {
            foreach ((Keys chord, FenixDcduKey key) in alternate ? alternateSoftKeys : standardSoftKeys)
            {
                Assert.Equal(key, FenixChannelDcduKeys.Map(chord, alternate));
            }

            // The other set of soft keys maps to nothing.
            foreach ((Keys chord, _) in alternate ? standardSoftKeys : alternateSoftKeys)
            {
                Assert.Null(FenixChannelDcduKeys.Map(chord, alternate));
            }

            foreach ((Keys chord, FenixDcduKey key) in always)
            {
                Assert.Equal(key, FenixChannelDcduKeys.Map(chord, alternate));
            }

            // Keys the window does not use, a modifier that is not the chord's, and a plain 1.
            foreach (Keys other in new[]
                     {
                         Keys.F5, Keys.Escape, Keys.Shift | Keys.PageDown, Keys.D1, Keys.D2, Keys.Control | Keys.Shift | Keys.D1,
                         Keys.Alt | Keys.PageDown, Keys.Control | Keys.Alt | Keys.PageUp, Keys.P, Keys.Shift | Keys.F1,
                         Keys.Control | Keys.F7, Keys.Alt | Keys.F8, Keys.None, Keys.Control, Keys.F3, Keys.F6, Keys.Home
                     })
            {
                Assert.Null(FenixChannelDcduKeys.Map(other, alternate));
            }
        }
    }

    // ---- The variables -----------------------------------------------------------------------

    [Fact]
    public void Each_key_presses_its_own_variable()
    {
        Dictionary<string, SimVarDefinition> variables = new FenixA320Definition().GetVariables();

        foreach ((FenixDcduKey key, string variable, string name) in Presses)
        {
            Assert.Equal(variable, FenixChannelDcduKeys.VariableOf(key));

            // The variable is a press button of the definition, the one the key's name says.
            Assert.True(variables.TryGetValue(variable, out SimVarDefinition? definition), variable);
            Assert.True(definition!.RenderAsButton, variable);
            Assert.Equal(name, definition.DisplayName);
        }

        // No two keys press one variable.
        Assert.Equal(Presses.Length, Presses.Select(press => FenixChannelDcduKeys.VariableOf(press.Key)).Distinct().Count());
        Assert.Equal(Enum.GetValues<FenixDcduKey>().Length, Presses.Length);
    }

    [Fact]
    public void The_soft_keys_and_the_navigation_keys_are_told_apart()
    {
        foreach (FenixDcduKey key in SoftKeys)
        {
            Assert.True(FenixChannelDcduKeys.IsSoftKey(key), key.ToString());
            Assert.False(FenixChannelDcduKeys.IsNavigation(key), key.ToString());
        }

        // Page and message keys are navigation; print is neither.
        foreach (FenixDcduKey key in new[]
                 {
                     FenixDcduKey.PageMinus, FenixDcduKey.PagePlus, FenixDcduKey.MessageMinus, FenixDcduKey.MessagePlus
                 })
        {
            Assert.False(FenixChannelDcduKeys.IsSoftKey(key), key.ToString());
            Assert.True(FenixChannelDcduKeys.IsNavigation(key), key.ToString());
        }

        Assert.False(FenixChannelDcduKeys.IsSoftKey(FenixDcduKey.Print));
        Assert.False(FenixChannelDcduKeys.IsNavigation(FenixDcduKey.Print));
    }

    [Fact]
    public void The_two_messages_are_the_ones_the_window_speaks()
    {
        Assert.Equal("No action on that key.", FenixChannelDcduKeys.NoActionMessage);
        Assert.Equal("DCDU not available", FenixChannelDcduKeys.NotAvailableMessage);
    }

    // ---- A soft key --------------------------------------------------------------------------

    [Fact]
    public void A_blank_soft_key_sends_nothing()
    {
        // Lsk2R (15) blank, Lsk1R (14) labeled: the second presses, the first is refused.
        FenixChannelSnapshot snapshot = Trusted((14, "cWILCO*"), (15, ""));

        Assert.Equal(new FenixDcduDecision("S_DCDU1_LSK1R", null), FenixChannelDcduKeys.Decide(FenixDcduKey.RightKey1, snapshot));
        Assert.Equal(new FenixDcduDecision(null, "No action on that key."),
            FenixChannelDcduKeys.Decide(FenixDcduKey.RightKey2, snapshot));

        // A label the push left out reads as "", and one of only spaces speaks nothing: both are blank.
        foreach (FenixChannelSnapshot blank in new[]
                 {
                     Trusted(), Trusted((12, ""), (13, ""), (14, ""), (15, "")), Trusted((12, Spaces14), (13, " "), (14, "   "), (15, Spaces14)),
                     // Only a color letter, or a space after one, has nothing to say.
                     Trusted((12, "c"), (13, "c "), (14, "g"), (15, "wc"))
                 })
        {
            foreach (FenixDcduKey key in SoftKeys)
            {
                Assert.Equal(new FenixDcduDecision(null, "No action on that key."), FenixChannelDcduKeys.Decide(key, blank));
            }
        }
    }

    [Fact]
    public void Each_soft_key_reads_the_label_of_its_own_position()
    {
        // One label at a time: only the key whose field has text presses, and it presses its own variable.
        foreach (FenixDcduKey labeled in SoftKeys)
        {
            FenixChannelSnapshot snapshot = Trusted((FieldOf(labeled), "c>ROGER"));

            foreach (FenixDcduKey key in SoftKeys)
            {
                FenixDcduDecision decision = FenixChannelDcduKeys.Decide(key, snapshot);

                if (key == labeled)
                {
                    Assert.Equal(new FenixDcduDecision(FenixChannelDcduKeys.VariableOf(key), null), decision);
                }
                else
                {
                    Assert.Equal(new FenixDcduDecision(null, "No action on that key."), decision);
                }
            }
        }
    }

    [Fact]
    public void A_soft_key_on_an_untrusted_dcdu_sends_nothing()
    {
        FenixDcduDecision notAvailable = new(null, "DCDU not available");
        (int, string)[] labels = [(12, "cROGER"), (13, "cSTBY"), (14, "cWILCO*"), (15, "cREJECT*")];

        FenixChannelSnapshot[] cannotBeRead =
        [
            // The display is off, with a message that would read fine, and with none.
            Snapshot(false, "display off", Remote(Content(labels))),
            Snapshot(false, "display off", null),
            // Trusted, with a field 186 that is not a message (a varint, a fixed64), a label in another shape, and none at all.
            Snapshot(true, "trusted", FenixChannelPush.From(new FenixChannelProtoWriter().Varint(186, 1).ToArray())!),
            Snapshot(true, "trusted", FenixChannelPush.From(new FenixChannelProtoWriter().Double(186, 1.5).ToArray())!),
            Snapshot(true, "trusted", Remote(Content((12, "cROGER"), (13, "cSTBY"), (15, "cREJECT*")).Varint(14, 1))),
            Snapshot(true, "trusted", FenixChannelPush.From(new FenixChannelProtoWriter().Double(197, 1000).ToArray())!),
            Snapshot(true, "trusted", null),
            // Trusted, with lowercase free text: a letter the app does not know, so nothing is sent for any key.
            Snapshot(true, "trusted", Remote(Content((4, "climb"), (12, "cROGER"), (13, "cSTBY"), (14, "cWILCO*"), (15, "cREJECT*"))))
        ];

        foreach (FenixChannelSnapshot snapshot in cannotBeRead)
        {
            foreach (FenixDcduKey key in SoftKeys)
            {
                Assert.Equal(notAvailable, FenixChannelDcduKeys.Decide(key, snapshot));
            }
        }
    }

    [Fact]
    public void A_soft_key_is_judged_on_the_snapshot_it_is_given()
    {
        // A message arriving after the last refresh relabels the slot: the key is judged on the snapshot taken at the
        // press, whatever an earlier press saw. Labeled, then blank, then labeled again.
        FenixChannelSnapshot labeled = Trusted((12, "cROGER"), (14, "cWILCO*"));
        FenixChannelSnapshot blank = Trusted((12, ""), (14, Spaces14));

        Assert.Equal(new FenixDcduDecision("S_DCDU1_LSK1L", null), FenixChannelDcduKeys.Decide(FenixDcduKey.LeftKey1, labeled));
        Assert.Equal(new FenixDcduDecision(null, "No action on that key."), FenixChannelDcduKeys.Decide(FenixDcduKey.LeftKey1, blank));
        Assert.Equal(new FenixDcduDecision("S_DCDU1_LSK1L", null), FenixChannelDcduKeys.Decide(FenixDcduKey.LeftKey1, labeled));

        Assert.Equal(new FenixDcduDecision("S_DCDU1_LSK1R", null), FenixChannelDcduKeys.Decide(FenixDcduKey.RightKey1, labeled));
        Assert.Equal(new FenixDcduDecision(null, "No action on that key."), FenixChannelDcduKeys.Decide(FenixDcduKey.RightKey1, blank));
    }

    // ---- The page, message and print keys ----------------------------------------------------

    [Fact]
    public void Page_message_and_print_keys_always_press()
    {
        FenixChannelSnapshot[] snapshots =
        [
            // A DCDU that reads, with labels and with none.
            Trusted((12, "cROGER"), (14, "cWILCO*")),
            Trusted(),
            // One the app cannot read, whatever the reason: they never depend on what the app reads.
            Snapshot(false, "display off", Remote(Content((12, "cROGER")))),
            Snapshot(false, "display off", null),
            Snapshot(true, "trusted", null),
            Snapshot(true, "trusted", FenixChannelPush.From(new FenixChannelProtoWriter().Varint(186, 1).ToArray())!),
            Snapshot(true, "trusted", Remote(Content((4, "climb"))))
        ];

        foreach (FenixChannelSnapshot snapshot in snapshots)
        {
            foreach (FenixDcduKey key in OtherKeys)
            {
                Assert.Equal(new FenixDcduDecision(FenixChannelDcduKeys.VariableOf(key), null),
                    FenixChannelDcduKeys.Decide(key, snapshot));
            }
        }

        // And their variables are the ones the definition names.
        foreach ((FenixDcduKey key, string variable, _) in Presses.Where(press => OtherKeys.Contains(press.Key)))
        {
            Assert.Equal(variable, FenixChannelDcduKeys.Decide(key, snapshots[0]).VariableKey);
        }
    }
}
