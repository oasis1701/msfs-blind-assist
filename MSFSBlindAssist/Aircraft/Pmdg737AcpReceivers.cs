namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 737's ACP VHF receiver buttons (Calls panel): each one's key, label, SDK event and
/// bit in its seat's <c>COMM_ReceiverSwitches</c> bitmask, plus the press bookkeeping that decides
/// what a bitmask update speaks.
///
/// Measured live 2026-10-14: the EVT_ACP_*_REC_VHFn CDA parameter is the TARGET state, not a
/// toggle, so a press sends the opposite of the receiver's current state. Captain and First Officer
/// VHF 2 receive each drive the stock COM RECEIVE:2 that SayIntentions reads.
/// </summary>
internal static class Pmdg737AcpReceivers
{
    public readonly record struct Receiver(string Key, string DisplayName, string EventName, string StateField, uint Bit);

    /// <summary>A press awaiting its read-back: the bit should reach <see cref="TurnOn"/>.</summary>
    public readonly record struct Press(Receiver Receiver, bool TurnOn, long AtMs);

    /// <summary>How long a press waits for its bit to move before it is reported as not changed.</summary>
    public const long ReadbackWindowMs = 3000;

    public static readonly IReadOnlyList<Receiver> All = Build();

    public static readonly IReadOnlyDictionary<string, Receiver> ByKey = All.ToDictionary(r => r.Key);

    private static List<Receiver> Build()
    {
        var list = new List<Receiver>();
        foreach (var (seatKey, seatName, evtSeat, field) in new[]
                 {
                     ("Capt", "Captain", "CAPT", "COMM_ReceiverSwitches_0"),
                     ("FO", "First Officer", "FO", "COMM_ReceiverSwitches_1"),
                 })
        {
            for (int vhf = 1; vhf <= 3; vhf++)
            {
                // ACP_SEL_RECV_VHF1/2/3 = 0x1/0x2/0x4 (NG3 SDK).
                list.Add(new Receiver($"ACP_{seatKey}_RecVHF{vhf}", $"{seatName} VHF {vhf} Receiver",
                    $"EVT_ACP_{evtSeat}_REC_VHF{vhf}", field, 1u << (vhf - 1)));
            }
        }
        return list;
    }

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

    /// <summary>
    /// Completes the presses on <paramref name="stateField"/> whose bit reached its target and
    /// returns their read-backs ("Captain VHF 2 Receiver on"), oldest first. <paramref name="inFlight"/>
    /// is true while another press on this field is still waiting, so the caller stays quiet rather
    /// than speak the seat's list for a change the pilot made.
    /// </summary>
    public static List<string> CompleteReadbacks(List<Press> pending, string stateField, uint bits, out bool inFlight)
    {
        var said = new List<string>();
        for (int i = 0; i < pending.Count; i++)
        {
            var p = pending[i];
            if (p.Receiver.StateField != stateField || ((bits & p.Receiver.Bit) != 0) != p.TurnOn) continue;
            said.Add($"{p.Receiver.DisplayName} {(p.TurnOn ? "on" : "off")}");
            pending.RemoveAt(i--);
        }
        inFlight = pending.Exists(p => p.Receiver.StateField == stateField);
        return said;
    }

    /// <summary>
    /// Removes <paramref name="press"/> if it is still waiting, returning true when it was: the
    /// receiver never moved (PMDG ignored the event, e.g. an unpowered ACP) and the pilot is told.
    /// </summary>
    public static bool ExpireIfUnconfirmed(List<Press> pending, Press press) => pending.Remove(press);
}
