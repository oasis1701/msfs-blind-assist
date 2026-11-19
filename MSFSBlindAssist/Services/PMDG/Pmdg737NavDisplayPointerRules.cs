namespace MSFSBlindAssist.Services.PMDG;

/// <summary>What the bearing pointers' fields decide about a receiver. They take the sample types, which stay in
/// the app, so they are extension methods here rather than members of <see cref="NdPointerFields"/>.</summary>
public static class Pmdg737NavDisplayPointerRules
{
    /// <summary>Whether PMDG draws receiver <paramref name="r"/>'s VOR pointer.</summary>
    public static bool VorDrawn(this NdPointerFields p, NdReceiver r, bool vorValid) => r.Switch == p.VorPosition && r.Powered && r.VorState == p.TunedState && vorValid;

    /// <summary>Whether PMDG draws receiver <paramref name="r"/>'s ADF pointer.</summary>
    public static bool AdfDrawn(this NdPointerFields p, NdReceiver r, bool adfPowered) => r.Switch == p.AdfPosition && adfPowered && r.AdfValid != 0;

    /// <summary>Whether receiver <paramref name="r"/>'s VOR (<paramref name="t"/>) is tuned (PMDG's tuned flag),
    /// whatever the switch.</summary>
    public static bool VorTuned(this NdPointerFields p, NdReceiver r, NdTunedStation t) => r.Powered && t.State == p.TunedState;

    /// <summary>Whether PMDG's ADF flag is set for receiver <paramref name="r"/>, whatever the switch.</summary>
    public static bool AdfReceiving(this NdPointerFields p, NdReceiver r, bool adfPowered) => adfPowered && r.AdfValid != 0;
}
