using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>The pure parts of controller button bindings: press detection, the hat directions,
/// the saved action name, and the command list the pilot picks from. Reading a real controller
/// (WinMM) is sim-side hardware and is checked in the in-sim test plan, not here.</summary>
public class ControllerBindingTests
{
    [Fact]
    public void Every_hotkey_action_has_a_name_in_the_command_list()
    {
        var missing = Enum.GetValues<HotkeyAction>().Where(a => !ControllerActionCatalog.HasName(a)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Command_names_are_unique_so_the_pilot_can_tell_them_apart()
    {
        var names = ControllerActionCatalog.All().Select(kv => kv.Value).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Only_the_press_edge_fires_and_a_held_button_fires_once()
    {
        Assert.Equal(new[] { 1, 5 }, ControllerButtonEdges.NewlyPressed(0, 0, 0b1_0001, 0));
        Assert.Empty(ControllerButtonEdges.NewlyPressed(0b1_0001, 0, 0b1_0001, 0));   // held
        Assert.Empty(ControllerButtonEdges.NewlyPressed(0b1_0001, 0, 0, 0));          // released
        Assert.Equal(new[] { 2 }, ControllerButtonEdges.NewlyPressed(0b01, 0, 0b11, 0));
    }

    [Fact]
    public void Button_32_is_reported_as_32()
    {
        Assert.Equal(new[] { 32 }, ControllerButtonEdges.NewlyPressed(0, 0, 0x8000_0000u, 0));
    }

    [Theory]
    [InlineData(0u, ControllerBinding.HatUp)]
    [InlineData(9000u, ControllerBinding.HatRight)]
    [InlineData(18000u, ControllerBinding.HatDown)]
    [InlineData(27000u, ControllerBinding.HatLeft)]
    [InlineData(35000u, ControllerBinding.HatUp)]
    [InlineData(4500u, 0)]      // diagonal: neither neighbour
    [InlineData(31500u, 0)]
    [InlineData(0xFFFFu, 0)]    // centred
    public void Hat_readings_map_to_the_four_directions_only(uint pov, int expected)
    {
        Assert.Equal(expected, ControllerButtonEdges.HatDirection(pov));
    }

    [Fact]
    public void A_hat_direction_fires_when_it_starts_not_while_it_is_held()
    {
        Assert.Equal(new[] { ControllerBinding.HatUp }, ControllerButtonEdges.NewlyPressed(0, 0, 0, ControllerBinding.HatUp));
        Assert.Empty(ControllerButtonEdges.NewlyPressed(0, ControllerBinding.HatUp, 0, ControllerBinding.HatUp));
        Assert.Equal(new[] { ControllerBinding.HatLeft },
            ControllerButtonEdges.NewlyPressed(0, ControllerBinding.HatUp, 0, ControllerBinding.HatLeft));
    }

    [Theory]
    [InlineData("TaxiStatus", true)]
    [InlineData("5", false)]                          // a number would follow enum reordering
    [InlineData("ReadHeading, ReadSpeed", false)]     // flags syntax
    [InlineData("taxistatus", false)]
    [InlineData("RetiredAction", false)]
    [InlineData("", false)]
    public void Only_an_exact_action_name_is_accepted(string saved, bool accepted)
    {
        var binding = new ControllerBinding { Action = saved };
        Assert.Equal(accepted, binding.TryGetAction(out _));
    }

    [Fact]
    public void A_binding_matches_its_own_device_and_button_only()
    {
        var binding = new ControllerBinding { VendorId = 0x044F, ProductId = 0x0405, Button = 5 };
        Assert.True(binding.Matches(0x044F, 0x0405, 5));
        Assert.False(binding.Matches(0x044F, 0x0405, 6));
        Assert.False(binding.Matches(0x044F, 0x0406, 5));
    }

    [Theory]
    [InlineData(5, "button 5")]
    [InlineData(ControllerBinding.HatUp, "hat up")]
    [InlineData(ControllerBinding.HatLeft, "hat left")]
    public void Buttons_are_named_for_the_pilot(int button, string label)
    {
        Assert.Equal(label, ControllerBinding.ButtonLabel(button));
    }

    [Fact]
    public void Bindings_survive_a_settings_round_trip()
    {
        var settings = new UserSettings();
        settings.ControllerBindings.Add(new ControllerBinding
        {
            VendorId = 0x044F, ProductId = 0x0405, DeviceName = "TCA Yoke Boeing",
            Button = ControllerBinding.HatDown, Action = nameof(HotkeyAction.ReadGroundSpeed),
        });

        var clone = settings.Clone();

        var binding = Assert.Single(clone.ControllerBindings);
        Assert.Equal(0x044F, binding.VendorId);
        Assert.Equal(0x0405, binding.ProductId);
        Assert.Equal("TCA Yoke Boeing", binding.DeviceName);
        Assert.Equal(ControllerBinding.HatDown, binding.Button);
        Assert.True(binding.TryGetAction(out var action));
        Assert.Equal(HotkeyAction.ReadGroundSpeed, action);
    }
}
