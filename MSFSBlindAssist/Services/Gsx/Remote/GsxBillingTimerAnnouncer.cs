using System.Globalization;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Decides what to SAY about GSX's persistent-connection timers — the metered
/// jetway / GPU / stairs connections <c>billing.timers[]</c> reports as RUNNING
/// (see <see cref="GsxBilling"/>) — from successive billing readings. Pure and
/// stateful, like <see cref="GsxServiceAnnouncer"/>: it holds the previous reading
/// per timer and emits only what changed.
///
/// <para>
/// Three phrases: a timer that STARTS running ("Jetway operations timer
/// running."), a running timer REMINDED once per <see cref="ReminderInterval"/>
/// with its elapsed time and accrued amount ("… still running, 1 hour 6 minutes,
/// amount 116.97."), and a timer that STOPS ("… timer stopped, 45 minutes, amount
/// 75.50."). The 15-minute cadence is the pre-Remote-API transport's
/// <c>GroundConnectionTimerAnnouncementInterval</c>: those reminders were dropped in
/// the migration, and a pilot's first notice of an hour-old metered jetway became
/// the invoice.
/// </para>
///
/// <para>
/// BASELINE-FIRST: the first Update after construction or Reset() is silent — a
/// timer already running when this session joins is recorded, and first spoken at
/// its next reminder mark. Reminders fire on the next billing patch at or after the
/// interval, never on their own clock — GSX republishes <c>billing</c> as the hours
/// accrue, so a reminder lands within a few minutes of its mark; if GSX ever stopped
/// republishing a running timer, this would fall silent rather than invent one.
/// A timer that VANISHES from the list is forgotten silently — GSX drops a settled
/// connection from <c>timers</c>, and the service row's own state transition
/// (announced by <see cref="GsxServiceAnnouncer"/>) already says the jetway went.
/// </para>
/// </summary>
public sealed class GsxBillingTimerAnnouncer
{
    internal static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(15);

    private readonly Dictionary<string, Memo> _known = new(StringComparer.Ordinal);
    private bool _baselined;

    private readonly record struct Memo(bool Running, DateTime LastSpokenUtc);

    public void Reset()
    {
        _known.Clear();
        _baselined = false;
    }

    /// <param name="billingPublished">
    /// Whether GSX has published a <c>billing</c> key at all. A snapshot taken while
    /// Couatl is still booting carries none (observed live: the key arrived as a
    /// <c>/billing</c> patch within the same second), and baselining on that ABSENCE
    /// would make the first patch announce an already-running jetway as freshly
    /// started. False = record nothing, baseline nothing; the first published reading
    /// is the baseline.
    /// </param>
    public IReadOnlyList<string> Update(GsxBilling billing, DateTime nowUtc, bool billingPublished = true)
    {
        var said = new List<string>();
        if (!billingPublished) return said;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Occurrence counter per key WITHIN this reading: two live timers can share
        // a subService (a dual-jetway stand, if GSX bills each jetway separately —
        // unverified, the fixture has one), and one Memo for both would flip
        // "running"/"stopped" on every patch once their states diverge. Suffixing
        // the second occurrence keeps them apart for as long as GSX's order holds.
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var t in billing.Timers)
        {
            string baseKey = KeyOf(t);
            if (baseKey.Length == 0) continue;
            int n = occurrences.TryGetValue(baseKey, out int c) ? c + 1 : 1;
            occurrences[baseKey] = n;
            string key = n == 1 ? baseKey : baseKey + "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            seen.Add(key);

            if (!_known.TryGetValue(key, out var memo))
            {
                // First sight of this timer. Before the baseline it is recorded
                // silently; after it, a timer that appears already running has
                // just started (GSX adds a timer when the connection is made).
                _known[key] = new Memo(t.Running, nowUtc);
                if (_baselined && t.Running)
                    said.Add(StartedPhrase(t));
                continue;
            }

            if (!_baselined)
            {
                _known[key] = new Memo(t.Running, nowUtc);
                continue;
            }

            if (t.Running && !memo.Running)
            {
                said.Add(StartedPhrase(t));
                _known[key] = new Memo(true, nowUtc);
            }
            else if (!t.Running && memo.Running)
            {
                said.Add(StoppedPhrase(t));
                _known[key] = new Memo(false, nowUtc);
            }
            else if (t.Running && nowUtc - memo.LastSpokenUtc >= ReminderInterval)
            {
                said.Add(RunningPhrase(t));
                _known[key] = new Memo(true, nowUtc);
            }
        }

        // Forget what GSX no longer publishes (silently — see the class summary).
        foreach (string gone in _known.Keys.Where(k => !seen.Contains(k)).ToList())
            _known.Remove(gone);

        _baselined = true;
        return said;
    }

    private static string KeyOf(GsxBillingTimer t) =>
        !string.IsNullOrWhiteSpace(t.SubService) ? t.SubService.Trim()
        : !string.IsNullOrWhiteSpace(t.Friendly) ? t.Friendly.Trim()
        : string.Empty;

    private static string NameOf(GsxBillingTimer t) =>
        !string.IsNullOrWhiteSpace(t.Friendly) ? t.Friendly.Trim()
        : !string.IsNullOrWhiteSpace(t.SubService) ? t.SubService.Trim()
        : "Ground connection";

    private static string StartedPhrase(GsxBillingTimer t) => $"{NameOf(t)} timer running.";

    private static string RunningPhrase(GsxBillingTimer t) =>
        $"{NameOf(t)} still running, {DescribeDuration(t.Hours)}{AmountSuffix(t.Amount)}.";

    private static string StoppedPhrase(GsxBillingTimer t) =>
        $"{NameOf(t)} timer stopped, {DescribeDuration(t.Hours)}{AmountSuffix(t.Amount)}.";

    /// <summary>
    /// ", amount 116.97" — or nothing for a zero/negative amount, which is a free
    /// connection, not a charge worth reading. No currency: GSX publishes a bare
    /// number, and the invoice line ("Total 1761.42.") already speaks it that way.
    /// </summary>
    private static string AmountSuffix(double amount) =>
        amount > 0 ? ", amount " + amount.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// GSX's fractional <c>hours</c> as spoken time: "6 minutes", "1 hour",
    /// "1 hour 6 minutes", "2 hours 30 minutes"; anything under a minute reads
    /// "under a minute" rather than "0 minutes".
    /// </summary>
    internal static string DescribeDuration(double hours)
    {
        if (double.IsNaN(hours) || hours <= 0) return "under a minute";
        // Floor, not round: a connection 40 s old is "under a minute", not "1 minute".
        // The epsilon absorbs binary-fraction noise (0.24 h * 60 = 14.399999…).
        int totalMinutes = (int)Math.Floor(hours * 60.0 + 1e-6);
        if (totalMinutes < 1) return "under a minute";

        int h = totalMinutes / 60;
        int m = totalMinutes % 60;
        string hoursPart = h == 0 ? string.Empty : h == 1 ? "1 hour" : $"{h} hours";
        string minutesPart = m == 0 ? string.Empty : m == 1 ? "1 minute" : $"{m} minutes";

        if (hoursPart.Length == 0) return minutesPart;
        if (minutesPart.Length == 0) return hoursPart;
        return hoursPart + " " + minutesPart;
    }
}
