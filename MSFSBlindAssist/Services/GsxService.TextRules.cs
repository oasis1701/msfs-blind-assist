// GsxService.TextRules — the pure, dependency-free text-processing rules of
// the GSX accessibility integration: tooltip segmentation, stable-text
// normalization (price/duration bucketing), charge/timer line classification,
// and the boarding-progress milestone gate.
//
// This file is deliberately self-contained (Regex/LINQ/Globalization only —
// no SimConnect, no WinForms) so tools/GsxTextProbe can compile it directly
// via a linked <Compile> and lock the rules with asserts, including a sweep
// over the real GSX receipts on the developer's machine. Members are
// `internal` (not private) for exactly that reason.
using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

public sealed partial class GsxService
{
    internal const int BoardingPassengerAnnouncementInterval = 10;
    // ISO-4217 codes are matched CASE-SENSITIVELY ((?-i:...) overrides the
    // call sites' IgnoreCase): the status renderer itself emits uppercase
    // ("EUR ", "GBP ") and receipts print uppercase codes, while several
    // codes are common lowercase English words ("all", "try", "top") that
    // would otherwise eat real quantities ("boarded all 38 passengers").
    internal const string CurrencyCodePattern =
        @"(?-i:\b(?:AED|AFN|ALL|AMD|ANG|AOA|ARS|AUD|AWG|AZN|BAM|BBD|BDT|BGN|BHD|BIF|BMD|BND|BOB|BOV|BRL|BSD|BTN|BWP|BYN|BZD|CAD|CDF|CHE|CHF|CHW|CLF|CLP|CNY|COP|COU|CRC|CUC|CUP|CVE|CZK|DJF|DKK|DOP|DZD|EGP|ERN|ETB|EUR|FJD|FKP|GBP|GEL|GHS|GIP|GMD|GNF|GTQ|GYD|HKD|HNL|HTG|HUF|IDR|ILS|INR|IQD|IRR|ISK|JMD|JOD|JPY|KES|KGS|KHR|KMF|KPW|KRW|KWD|KYD|KZT|LAK|LBP|LKR|LRD|LSL|LYD|MAD|MDL|MGA|MKD|MMK|MNT|MOP|MRU|MUR|MVR|MWK|MXN|MXV|MYR|MZN|NAD|NGN|NIO|NOK|NPR|NZD|OMR|PAB|PEN|PGK|PHP|PKR|PLN|PYG|QAR|RON|RSD|RUB|RWF|SAR|SBD|SCR|SDG|SEK|SGD|SHP|SLE|SLL|SOS|SRD|SSP|STN|SVC|SYP|SZL|THB|TJS|TMT|TND|TOP|TRY|TTD|TWD|TZS|UAH|UGX|USD|USN|UYI|UYU|UYW|UZS|VED|VES|VND|VUV|WST|XAF|XAG|XAU|XBA|XBB|XBC|XBD|XCD|XCG|XDR|XOF|XPD|XPF|XPT|XSU|XTS|XUA|XXX|YER|ZAR|ZMW|ZWG)\b)";
    // Spelled-out currency names stay case-insensitive ("25 euros"), but are
    // word-bounded, and POUND/POUNDS are deliberately ABSENT: "pounds" is a
    // mass unit in fuel/W&B text, and eating "12,000 pounds" as a price
    // makes two different fuel states normalize identically — the change is
    // then silently never announced. GBP money arrives as "GBP " (the
    // renderer maps the pound symbol) so nothing is lost.
    internal const string CurrencyWordPattern =
        @"\b(?:ARIARY|BAHT|BALBOA|BIRR|BOLIVAR|BOLIVARES|BOLIVIANO|BOLIVIANOS|CEDI|CEDIS|COLON|COLONES|CORDOBA|CORDOBAS|DALASI|DENAR|DENARS|DINAR|DINARS|DIRHAM|DIRHAMS|DOLLAR|DOLLARS|DONG|DRAM|ESCUDO|ESCUDOS|EURO|EUROS|FLORIN|FORINT|FRANC|FRANCS|GOURDE|GOURDES|GUARANI|HRYVNIA|KINA|KIP|KORUNA|KORUNY|KRONA|KRONER|KRONOR|KWACHA|KWANZA|KYAT|LARI|LEI|LEK|LEMPIRA|LEMPIRAS|LEONE|LEV|LEVA|LILANGENI|LIRA|LIRE|LOTI|MANAT|METICAL|METICALS|NAIRA|NAKFA|OUGUIYA|PAANGA|PATACA|PATACAS|PESO|PESOS|PULA|QUETZAL|RAND|REAL|REALS|RIEL|RINGGIT|RIYAL|RIYALS|RMB|ROUBLE|ROUBLES|RUBLE|RUBLES|RUFIYAA|RUPEE|RUPEES|RUPIAH|SHEKEL|SHEKELS|SHILLING|SHILLINGS|SOL|SOM|SOMONI|TAKA|TALA|TENGE|TUGRIK|VATU|WON|YEN|YUAN|ZLOTY|ZLOTYS)\b";
    internal const string CurrencySymbolPattern = @"[$€£¥₩₹₽₺₪₫₴¢₦₱฿₡₲₵₭₮₾₼]";
    internal const string CurrencyTokenPattern =
        @"(?:" + CurrencyCodePattern + @"|" + CurrencyWordPattern + @"|" + CurrencySymbolPattern + @")";

    // Splits a tooltip line on commas — but never inside a digit-grouped
    // amount ("$ 5,989.76"). A comma is a separator unless digits flank
    // BOTH sides: the status renderer joins fragments and bullet items with
    // ", ", so separators routinely FOLLOW a digit ("... $ 5.50, Timer:
    // ..."), while digit,digit only occurs as thousands grouping.
    private static readonly Regex TooltipPartSeparatorRegex = new(
        @"(?<!\d),|,(?!\d)",
        RegexOptions.Compiled);

    internal static List<string> SplitTooltipParts(string text)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return parts;

        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            foreach (string segment in TooltipPartSeparatorRegex.Split(line))
            {
                string trimmed = segment.Trim();
                if (trimmed.Length > 0)
                    parts.Add(trimmed);
            }
        }
        return parts;
    }

    internal static string StripSegmentsMatching(string text, Regex regex)
    {
        var parts = SplitTooltipParts(text);
        if (parts.Count == 0)
            return text;

        var kept = parts.Where(p => !regex.IsMatch(p)).ToList();
        if (kept.Count == 0)
            return string.Empty;
        if (kept.Count == parts.Count)
            return text;
        return string.Join(", ", kept);
    }

    // Pax-count-only segments (the things we silence when boarding's
    // milestone hasn't advanced). Whole-segment match so any segment that
    // carries additional info ("rear loader leaving while 5 boarded") is
    // left alone — only standalone "pax 5/100" / "5 passengers" / "5 pax
    // boarded" type lines, plus GSX's "Passenger boarding 5/100 passengers"
    // status shape, are stripped.
    internal static readonly Regex PaxOnlySegmentRegex = new(
        @"^\s*(?:\[gsx\]\s+)?(?:(?:passenger\s+(?:de)?boarding)\s+)?(?:pax\s+\d{1,4}(?:\s*/\s*\d{1,4})?|\d{1,4}(?:\s*/\s*\d{1,4})?\s+(?:passengers?|pax)(?:\s+boarded)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool TryParsePassengerCount(string text, out int passengers)
    {
        passengers = 0;

        // GSX writes the boarding count several ways. The "N/M" forms must
        // be tried first — the bare "\b\d{1,3}\s*passengers\b" pattern
        // would otherwise greedily capture M (the total) out of "50/550
        // passengers", poisoning the milestone throttle by recording
        // milestone 100 (M/10) and silencing every subsequent milestone
        // below that for the rest of the session.
        var match = Regex.Match(
            text,
            @"\bpassenger\s+(?:de)?boarding\s+(?<count>\d{1,4})\s*/\s*\d+\s*(?:passengers|pax)\b",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\bpassenger\s+(?:de)?boarding\s+(?<count>\d{1,4})\s*(?:passengers|pax)\b",
                RegexOptions.IgnoreCase);
        }
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\b(?<count>\d{1,4})\s*/\s*\d+\s*(?:passengers|pax)\b",
                RegexOptions.IgnoreCase);
        }
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\bpax\s+(?<count>\d{1,4})\s*/\s*\d+\b",
                RegexOptions.IgnoreCase);
        }
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\b(?<count>\d{1,4})\s*(?:passengers|pax)\b",
                RegexOptions.IgnoreCase);
        }
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\bpax\s+(?<count>\d{1,4})\b",
                RegexOptions.IgnoreCase);
        }
        if (!match.Success)
            return false;

        // Clamp ceiling matches the \d{1,4} patterns above — 4-digit counts
        // (A380 deboarding totals) must not silently truncate to 999.
        passengers = Math.Clamp(int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture), 0, 9999);
        return true;
    }

    // Boarding-progress milestones — chosen so the user hears:
    //   * pax 0 (service started, nobody on yet)
    //   * pax 1 (boarding has actually begun)
    //   * every multiple of BoardingPassengerAnnouncementInterval (10, 20, …)
    // with no upper cap, so 110 / 120 / 130 / … keep announcing instead of
    // collapsing into a single milestone-100 ceiling like Tower's original
    // formula did.
    internal static int ComputeBoardingMilestone(int passengers)
    {
        if (passengers <= 0) return 0;
        if (passengers < BoardingPassengerAnnouncementInterval) return 1;
        return (passengers / BoardingPassengerAnnouncementInterval) + 1;
    }

    internal static bool IsBoardingAnnouncementBoundary(int passengers) =>
        passengers <= 1 || passengers % BoardingPassengerAnnouncementInterval == 0;

    // Decides whether a parsed boarding/deboarding count should be announced.
    // First sight of a count for a service announces only on a clean boundary
    // (0/1 start marker or an exact multiple of the interval) — late app
    // starts and dict resets stay quiet mid-decade. Every later sample
    // announces on any milestone-BUCKET change, NOT only on exact multiples:
    // the tooltip is polled (~1 s) while GSX increments the counter, so
    // samples routinely skip the round numbers (48 -> 53). Requiring
    // passengers % 10 == 0 on every announce silenced entire boardings at
    // fast boarding rates.
    internal static bool ShouldAnnounceBoardingProgress(int passengers, int? lastMilestone) =>
        lastMilestone is null
            ? IsBoardingAnnouncementBoundary(passengers)
            : ComputeBoardingMilestone(passengers) != lastMilestone.Value;

    internal static bool IsTimerStatusText(string text) =>
        text.Contains("timer:", StringComparison.OrdinalIgnoreCase);

    internal static bool TimerStatusContextEquals(string currentText, string previousText)
    {
        if (string.IsNullOrWhiteSpace(previousText))
            return false;

        string currentContext = BuildTimerStatusContextKey(currentText);
        string previousContext = BuildTimerStatusContextKey(previousText);
        return currentContext.Length > 0
            && string.Equals(currentContext, previousContext, StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildTimerStatusContextKey(string text)
    {
        var parts = SplitTooltipParts(text);
        if (parts.Count == 0)
            return string.Empty;

        var stableParts = parts
            .Select(BuildTimerStatusContextPart)
            .Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join("|", stableParts);
    }

    internal static string BuildTimerStatusContextPart(string part)
    {
        if (part.Equals("Current charges:", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (IsTimerStatusLine(part))
            return NormalizeStatusStableText(FormatGroundConnectionTimerServiceText(part));

        if (IsChargeStatusLine(part))
            return string.Empty;

        return NormalizeStatusStableText(part);
    }

    internal static bool IsChargeStatusLine(string text)
    {
        string normalized = text.ToLowerInvariant();
        return normalized.Contains("timer:")
            || normalized.Contains("invoice:")
            || normalized.Contains("eur ")
            || normalized.Contains("â‚¬")
            || normalized.Contains("$")
            || normalized.Contains("£")
            || ContainsMoneyAmount(text);
    }

    internal static bool ContainsMoneyAmount(string text) =>
        Regex.IsMatch(
            text,
            $@"{CurrencyTokenPattern}\s*\d|\d+(?:[.,]\d+)*\s*{CurrencyTokenPattern}",
            RegexOptions.IgnoreCase);

    internal static bool IsTimerStatusLine(string text) =>
        text.Contains("timer:", StringComparison.OrdinalIgnoreCase);

    internal static string FormatGroundConnectionTimerServiceText(string timerLine)
    {
        string serviceName = Regex.Replace(timerLine, @"\s+timer\s*:.*$", string.Empty,
            RegexOptions.IgnoreCase);
        serviceName = NormalizeWhitespace(serviceName);
        if (string.IsNullOrWhiteSpace(serviceName))
            return string.Empty;

        return $"{serviceName} service is running";
    }

    internal static string NormalizeStatusStableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string stable = text;
        stable = Regex.Replace(stable, @"\b\d{1,2}:\d{2}(?::\d{2})?\b", "<time>");
        // Bucket durations into 5-minute groups so an ETA that counts down
        // second-by-second doesn't re-announce on every tick. A re-announce
        // fires only when the value crosses a 5-minute boundary (which
        // approximates the user-requested "tell me again if the ETA changes
        // by ~5 minutes" behaviour).
        stable = DurationTokenRegex.Replace(stable, BucketDurationToken);
        stable = Regex.Replace(stable, $@"{CurrencyTokenPattern}\s*\d+(?:[.,]\d+)*|\d+(?:[.,]\d+)*\s*{CurrencyTokenPattern}", "<price>",
            RegexOptions.IgnoreCase);
        stable = Regex.Replace(stable, @"\(~?\s*<price>\)", "(<price>)",
            RegexOptions.IgnoreCase);
        return NormalizeWhitespace(stable);
    }

    internal static readonly Regex DurationTokenRegex = new(
        @"\b(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>seconds?|secs?|minutes?|mins?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static string BucketDurationToken(Match match)
    {
        if (!double.TryParse(
                match.Groups["num"].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double n))
        {
            return "<duration>";
        }

        string unit = match.Groups["unit"].Value.ToLowerInvariant();
        double totalSeconds = unit.StartsWith("m") ? n * 60.0 : n;
        int bucket = (int)Math.Floor(totalSeconds / 300.0);
        return $"<duration-{bucket}>";
    }

    internal static string NormalizeWhitespace(string value) =>
        Regex.Replace(value.ReplaceLineEndings(" "), @"\s+", " ").Trim();
}
