namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 737's ACP VHF receiver buttons (Calls panel): each one's key, label, SDK event and
/// bit in its seat's <c>COMM_ReceiverSwitches</c> bitmask, the bitmask's spoken labels, and the
/// press bookkeeping that decides what a bitmask update speaks.
///
/// Measured live 2026-10-14: the EVT_ACP_*_REC_VHFn CDA parameter is the TARGET state, not a
/// toggle, so a press sends the opposite of the receiver's current state. Captain and First Officer
/// VHF 2 receive each drive the stock COM RECEIVE:2 that SayIntentions reads.
///
/// Each button is labelled with its receiver's state: PMDGNG3DataManager publishes <see cref="IsOn"/>
/// as a derived field under the button's own key, which the button names as its StateVariable.
/// </summary>
internal static class Pmdg737AcpReceivers
{
    public readonly record struct Receiver(string Key, string DisplayName, string EventName, string StateField, int Seat, uint Bit);

    /// <summary>
    /// A press awaiting its read-back: the bit should reach <see cref="TurnOn"/>. A class, so each press
    /// is its own: a timer expires only the press it was started for, never a later one that looks alike.
    /// </summary>
    public sealed class Press(Receiver receiver, bool turnOn)
    {
        public Receiver Receiver { get; } = receiver;
        public bool TurnOn { get; } = turnOn;
    }

    /// <summary>How long a press waits for its bit to move before it is settled from the snapshot.</summary>
    public const int ReadbackWindowMs = 3000;

    /// <summary>The NG3 SDK's ACP_SEL_RECV_* bits (SDK lines 610-623), in bit order, with the label each is spoken by.</summary>
    public static readonly IReadOnlyList<(uint Bit, string Label)> BitLabels =
    [
        (0x0001, "VHF1"), (0x0002, "VHF2"), (0x0004, "VHF3"), (0x0008, "HF1"), (0x0010, "HF2"),
        (0x0020, "FLT"), (0x0040, "SVC"), (0x0080, "PA"), (0x0100, "NAV1"), (0x0200, "NAV2"),
        (0x0400, "ADF1"), (0x0800, "ADF2"), (0x1000, "MKR"), (0x2000, "SPKR"),
    ];

    public static readonly IReadOnlyList<Receiver> All = Build();

    public static readonly IReadOnlyDictionary<string, Receiver> ByKey = All.ToDictionary(r => r.Key);

    private static List<Receiver> Build()
    {
        var list = new List<Receiver>();
        foreach (var (seat, seatKey, seatName, evtSeat) in new[]
                 {
                     (0, "Capt", "Captain", "CAPT"),
                     (1, "FO", "First Officer", "FO"),
                 })
        {
            for (int vhf = 1; vhf <= 3; vhf++)
            {
                // VHF1/2/3 are the first three ACP_SEL_RECV bits, spoken as the mic selector and the
                // seat's list speak them ("VHF2").
                var (bit, label) = BitLabels[vhf - 1];
                list.Add(new Receiver($"ACP_{seatKey}_RecVHF{vhf}", $"{seatName} {label} Receiver",
                    $"EVT_ACP_{evtSeat}_REC_VHF{vhf}", $"COMM_ReceiverSwitches_{seat}", seat, bit));
            }
        }
        return list;
    }

    /// <summary>The receivers a <c>COMM_ReceiverSwitches</c> bitmask selects ("VHF1, VHF2"), or "none".</summary>
    public static string Describe(uint bits)
    {
        var labels = new List<string>();
        foreach (var (bit, label) in BitLabels)
            if ((bits & bit) != 0) labels.Add(label);
        return labels.Count == 0 ? "none" : string.Join(", ", labels);
    }

    /// <summary>Whether <paramref name="receiver"/> is on in a snapshot's <c>COMM_ReceiverSwitches</c> array.</summary>
    public static bool IsOn(Receiver receiver, uint[]? switches) =>
        switches != null && receiver.Seat < switches.Length && (switches[receiver.Seat] & receiver.Bit) != 0;

    /// <summary>
    /// Whether a press of <paramref name="receiver"/> should turn it on: the opposite of its newest
    /// unconfirmed press, else of the cached bitmask. The cache lags a press, so without the
    /// pending check a quick second press would send the same target again and be lost.
    /// </summary>
    public static bool TargetFor(IReadOnlyList<Press> pending, Receiver receiver, uint cachedBits)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
            if (pending[i].Receiver.Key == receiver.Key) return !pending[i].TurnOn;
        return (cachedBits & receiver.Bit) == 0;
    }

    /// <summary>Records a press, replacing any older press of the same receiver it supersedes.</summary>
    public static void AddPress(List<Press> pending, Press press)
    {
        pending.RemoveAll(p => p.Receiver.Key == press.Receiver.Key);
        pending.Add(press);
    }

    /// <summary>"Captain VHF2 Receiver on" / "off": what a press did.</summary>
    public static string Readback(Press press) => $"{press.Receiver.DisplayName} {(press.TurnOn ? "on" : "off")}";

    private static bool AtTarget(Press press, uint bits) => ((bits & press.Receiver.Bit) != 0) == press.TurnOn;

    /// <summary>
    /// Completes the presses on <paramref name="stateField"/> whose bit reached its target and
    /// returns their read-backs, oldest first. <paramref name="otherChange"/> is true when the update
    /// also holds a change no press on this field explains (a bit outside the pressed receivers moved
    /// since <paramref name="previousBits"/>), so the caller still speaks the seat's list for it, even
    /// beside a read-back. With no baseline (null) a change is assumed only when no press is involved.
    /// </summary>
    public static List<string> CompleteReadbacks(List<Press> pending, string stateField, uint? previousBits, uint bits, out bool otherChange)
    {
        var said = new List<string>();
        uint pressed = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            var p = pending[i];
            if (p.Receiver.StateField != stateField) continue;
            pressed |= p.Receiver.Bit;
            if (!AtTarget(p, bits)) continue;
            said.Add(Readback(p));
            pending.RemoveAt(i--);
        }
        otherChange = previousBits is uint previous ? ((previous ^ bits) & ~pressed) != 0 : pressed == 0;
        return said;
    }

    /// <summary>
    /// Removes <paramref name="press"/> if it is still waiting, returning true when it was: no update
    /// completed it within the read-back window, and <see cref="ExpiryReadback"/> says what it did.
    /// </summary>
    public static bool ExpireIfUnconfirmed(List<Press> pending, Press press) => pending.Remove(press);

    /// <summary>
    /// What an expired press speaks, given its seat's bits now: the read-back when the receiver is at
    /// the press's target after all (a quick double press can return it there between two 1 Hz
    /// snapshots, so no update ever fired), else "&lt;receiver&gt; did not change" (PMDG ignored the
    /// event, e.g. an unpowered ACP).
    /// </summary>
    public static string ExpiryReadback(Press press, uint bits) =>
        AtTarget(press, bits) ? Readback(press) : $"{press.Receiver.DisplayName} did not change";
}
