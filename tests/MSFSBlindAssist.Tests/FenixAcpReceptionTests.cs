// Pins the Fenix A320's Audio Control Panel (ACP) against the Fenix model
// (Panel_ACP_{Original,Modern}_Behavior.xml, FNX32X_Interact_Knob_PushButton_ACP_Template):
//
// - all 15 reception latches sit on the panel, each naming its own light as UiEchoVariable, so
//   MainForm's UI-set echo gate swallows the light's change after a panel pick (CORE-7). The
//   light names are irregular (VOR 1/2 light as NAV_1/NAV_2, MLS as MLS_1), so they are listed
//   here from the model, never derived by pattern;
// - each latch and its light are labelled from one constant ([VAR-1]);
// - every ACP1 transmit key reads its light (HF 2 was the one missing);
// - the volume, transmit and receive groups list their channels in the model's order.
//
// The fleet-wide theory pins what MarkUiSet relies on for any aircraft: the partner is an
// announced light that reads back the same values as the switch, since its echo entry goes
// through the value-matched generic gate.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixAcpReceptionTests
{
    private const string AcpPanel = "Audio Control Panel (ACP)";

    private static readonly FenixA320Definition Fenix = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Fenix.GetVariables();
    private static readonly List<string> Panel = Fenix.GetPanelControls()[AcpPanel];

    // The model's channel order, and each reception latch's light, from the ACP1 template.
    private static readonly (string Channel, string Light)[] Channels =
    {
        ("VHF_1", "I_ASP_VHF_1_REC"),
        ("VHF_2", "I_ASP_VHF_2_REC"),
        ("VHF_3", "I_ASP_VHF_3_REC"),
        ("HF_1", "I_ASP_HF_1_REC"),
        ("HF_2", "I_ASP_HF_2_REC"),
        ("INT", "I_ASP_INT_REC"),
        ("CAB", "I_ASP_CAB_REC"),
        ("VOR_1", "I_ASP_NAV_1_REC"),
        ("VOR_2", "I_ASP_NAV_2_REC"),
        ("MARKER", "I_ASP_MARKER_REC"),
        ("ILS", "I_ASP_ILS_REC"),
        ("MLS", "I_ASP_MLS_1_REC"),
        ("ADF_1", "I_ASP_ADF_1_REC"),
        ("ADF_2", "I_ASP_ADF_2_REC"),
        ("PA", "I_ASP_PA_REC"),
    };

    public static IEnumerable<object[]> ReceptionChannels()
        => Channels.Select(c => new object[] { c.Channel, c.Light });

    [Theory, MemberData(nameof(ReceptionChannels))]
    public void Each_reception_latch_is_on_the_panel_and_names_its_light(string channel, string light)
    {
        string latch = $"S_ASP_{channel}_REC_LATCH";
        Assert.Contains(latch, Panel);
        Assert.Equal(light, Vars[latch].UiEchoVariable);
    }

    [Theory, MemberData(nameof(ReceptionChannels))]
    public void Each_reception_light_is_labelled_from_its_latch(string channel, string light)
        => Assert.Equal(Vars[$"S_ASP_{channel}_REC_LATCH"].DisplayName + " Light", Vars[light].DisplayName);

    [Fact]
    public void Every_acp1_transmit_key_reads_its_light()
    {
        var keys = Panel.Where(k => k.StartsWith("S_ASP_") && k.EndsWith("_SEND")).ToList();
        Assert.Equal(8, keys.Count);
        foreach (var key in keys)
        {
            string? light = Vars[key].StateVariable;
            Assert.Equal(key.Replace("S_ASP_", "I_ASP_"), light);
            Assert.True(Vars.ContainsKey(light!), $"{key} reads {light}, which is not registered");
        }
    }

    [Fact]
    public void Every_group_lists_its_channels_in_the_model_order()
    {
        var order = Channels.Select(c => c.Channel).ToList();
        List<string> ChannelsOf(string prefix, string suffix) => Panel
            .Where(k => k.StartsWith(prefix) && k.EndsWith(suffix))
            .Select(k => k[prefix.Length..^suffix.Length])
            .ToList();

        Assert.Equal(order, ChannelsOf("A_ASP_", "_VOLUME"));
        Assert.Equal(order, ChannelsOf("S_ASP_", "_REC_LATCH"));
        var transmit = ChannelsOf("S_ASP_", "_SEND");
        Assert.Equal(order.Where(transmit.Contains), transmit);
    }

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_ui_echo_partner_is_an_announced_light_with_the_same_values(IAircraftDefinition aircraft)
    {
        var vars = aircraft.GetVariables();
        foreach (var (key, def) in vars.Where(kv => !string.IsNullOrEmpty(kv.Value.UiEchoVariable)))
        {
            string id = $"{aircraft.AircraftCode}/{key} -> {def.UiEchoVariable}";
            Assert.True(vars.TryGetValue(def.UiEchoVariable!, out var light), $"{id}: partner not registered");
            Assert.True(light!.UpdateFrequency == UpdateFrequency.Continuous && light.IsAnnounced,
                $"{id}: partner is not a Continuous announced var, so there is no echo to swallow");
            Assert.True(def.ValueDescriptions.Keys.OrderBy(v => v).SequenceEqual(light.ValueDescriptions.Keys.OrderBy(v => v)),
                $"{id}: partner reads back different values, which the value-matched gate would miss");
        }
    }
}
