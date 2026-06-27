// GsxTextProbe — locks GsxService's pure text rules (GsxService.TextRules.cs)
// with asserts: tooltip comma-splitting, currency/price normalization,
// charge/timer classification, timer-context keys, and the boarding-progress
// milestone gate. Also sweeps the real GSX receipts under
// %APPDATA%\Virtuali\GSX\Receipts when present (skipped silently if absent).
//
// Run: dotnet run --project tools/GsxTextProbe -p:Platform=x64
using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;

int passed = 0, failed = 0;

void Check(bool cond, string name)
{
    if (cond) { passed++; }
    else { failed++; Console.WriteLine($"FAIL  {name}"); }
}

void CheckEqual<T>(T actual, T expected, string name)
{
    if (Equals(actual, expected)) { passed++; }
    else { failed++; Console.WriteLine($"FAIL  {name}\n      expected: {expected}\n      actual:   {actual}"); }
}

// ── SplitTooltipParts ───────────────────────────────────────────────────────
{
    var parts = GsxService.SplitTooltipParts("Refueling in progress, ETA 5 minutes");
    CheckEqual(parts.Count, 2, "split: plain comma separates");

    parts = GsxService.SplitTooltipParts("Total $ 5,989.76");
    CheckEqual(parts.Count, 1, "split: thousands separator not split");

    parts = GsxService.SplitTooltipParts("[GSX] Boarding, 3 bags loaded, rear door closing");
    CheckEqual(parts.Count, 3, "split: multi-segment line");

    // A separator comma routinely FOLLOWS a digit: the status renderer joins
    // fragments and bullets with ", " ("... $ 5.50, Timer: ..."). Only
    // digit-on-BOTH-sides commas are thousands grouping.
    parts = GsxService.SplitTooltipParts("Passenger boarding 45/100, GPU connected");
    CheckEqual(parts.Count, 2, "split: digit-comma-space still separates");

    parts = GsxService.SplitTooltipParts("GPU $ 5.50, Timer: running 00:12:34");
    CheckEqual(parts.Count, 2, "split: amount-then-timer separates");

    parts = GsxService.SplitTooltipParts("Total $ 5,989.76, GPU connected");
    CheckEqual(parts.Count, 2, "split: thousands kept while trailing separator splits");
    if (parts.Count == 2)
        CheckEqual(parts[0], "Total $ 5,989.76", "split: thousands amount intact in first part");
}

// ── Price normalization (real receipt shapes) ───────────────────────────────
{
    string s = GsxService.NormalizeStatusStableText("Pushback service $ 769.99");
    Check(s.Contains("<price>"), "price: symbol-space-amount ($ 769.99)");

    s = GsxService.NormalizeStatusStableText("Total $ 5,989.76");
    CheckEqual(s, "Total <price>", "price: comma-grouped amount fully consumed");

    s = GsxService.NormalizeStatusStableText("GPU connection EUR 12.50");
    Check(s.Contains("<price>"), "price: uppercase EUR code (app-normalized euro symbol)");

    s = GsxService.NormalizeStatusStableText("Jetway operation $100.00/hr");
    Check(s.Contains("<price>"), "price: no-space symbol amount");
}

// ── Currency false positives must NOT eat real quantities ───────────────────
{
    // Weight in pounds is mass, not money — eating it makes two different
    // fuel states normalize identically and the change is never announced.
    string s1 = GsxService.NormalizeStatusStableText("Refueling 12,000 pounds");
    string s2 = GsxService.NormalizeStatusStableText("Refueling 13,000 pounds");
    Check(!s1.Contains("<price>"), "currency: 'pounds' (mass) not a price");
    Check(s1 != s2, "currency: fuel-in-pounds changes stay distinct");

    // Lowercase 'all' must not match the ALL (Albanian lek) ISO code.
    s1 = GsxService.NormalizeStatusStableText("boarded all 38 passengers");
    s2 = GsxService.NormalizeStatusStableText("boarded all 39 passengers");
    Check(!s1.Contains("<price>"), "currency: lowercase 'all 38' not a price");
    Check(s1 != s2, "currency: 'all N' changes stay distinct");

    // Embedded code fragments must not match ('overall 5' contains 'all 5').
    Check(!GsxService.NormalizeStatusStableText("overall 5 items").Contains("<price>"),
        "currency: word-boundary required ('overall 5')");
    Check(!GsxService.NormalizeStatusStableText("retry 3 times").Contains("<price>"),
        "currency: lowercase 'try 3' not a price (TRY code is case-sensitive)");

    // Real shapes must still work after tightening.
    Check(GsxService.NormalizeStatusStableText("Fee ALL 500").Contains("<price>"),
        "currency: uppercase ALL code still matches");
    Check(GsxService.NormalizeStatusStableText("Fee 25 euros").Contains("<price>"),
        "currency: spelled-out word still matches (any case)");
    Check(GsxService.NormalizeStatusStableText("Fee 1,250.75 USD").Contains("<price>"),
        "currency: grouped amount before code");
}

// ── Charge/timer classification ─────────────────────────────────────────────
{
    Check(GsxService.IsChargeStatusLine("GPU timer: running 00:12:34"), "charge: timer line is a charge line");
    Check(GsxService.IsChargeStatusLine("Subtotal $ 881.82"), "charge: dollar amount");
    Check(!GsxService.IsChargeStatusLine("Boarding completed"), "charge: plain status is not");
    Check(GsxService.IsTimerStatusLine("GPU timer: running 00:12:34"), "timer: detected");
    CheckEqual(GsxService.FormatGroundConnectionTimerServiceText("GPU timer: running 00:12:34"),
        "GPU service is running", "timer: service text formatting");
}

// ── Timer context keys ──────────────────────────────────────────────────────
{
    // Same services, different durations/charges -> equal context.
    string a = "GPU connected\nCurrent charges:\nGPU timer: running 00:05:00, $ 2.50";
    string b = "GPU connected\nCurrent charges:\nGPU timer: running 00:09:30, $ 4.75";
    Check(GsxService.TimerStatusContextEquals(a, b), "timerctx: duration/charge changes are context-equal");

    // Different service set -> not equal.
    string c = "Stairs connected\nCurrent charges:\nStairs timer: running 00:01:00, $ 1.00";
    Check(!GsxService.TimerStatusContextEquals(a, c), "timerctx: different service is context-different");

    // Ordering lock: a line containing "timer:" must classify as timer
    // (-> "X service is running"), not be dropped as a charge line.
    string key = GsxService.BuildTimerStatusContextKey("GPU timer: running 00:05:00");
    Check(key.Contains("GPU service is running", StringComparison.OrdinalIgnoreCase),
        "timerctx: timer-before-charge classification order");
}

// ── Passenger parsing ───────────────────────────────────────────────────────
{
    Check(GsxService.TryParsePassengerCount("Passenger boarding 5/100 passengers", out int n) && n == 5,
        "pax: 'Passenger boarding 5/100 passengers' parses 5");
    Check(GsxService.TryParsePassengerCount("pax 47/180", out n) && n == 47,
        "pax: 'pax 47/180' parses 47");
    Check(GsxService.TryParsePassengerCount("Passenger deboarding 432/853 passengers", out n) && n == 432,
        "pax: deboarding shape parses");
    Check(GsxService.PaxOnlySegmentRegex.IsMatch("Passenger boarding 5/100 passengers"),
        "pax: PR status shape is strippable");
    Check(!GsxService.PaxOnlySegmentRegex.IsMatch("rear loader leaving while 5 boarded"),
        "pax: segment with extra info is NOT strippable");
}

// ── Boarding milestone gate ─────────────────────────────────────────────────
{
    // Simulate the per-service dict exactly as StripThrottledPaxSegments
    // drives it: announce when ShouldAnnounceBoardingProgress says so,
    // always record the latest milestone.
    List<int> Announced(IEnumerable<int> samples)
    {
        int? last = null;
        var spoken = new List<int>();
        foreach (int p in samples)
        {
            if (GsxService.ShouldAnnounceBoardingProgress(p, last))
                spoken.Add(p);
            last = GsxService.ComputeBoardingMilestone(p);
        }
        return spoken;
    }

    // Step-1 sampling: announce at 1 and each decade, no repeats in between.
    var spoken = Announced(Enumerable.Range(1, 35));
    CheckEqual(string.Join(",", spoken), "1,10,20,30", "boarding: step-1 announces 1 + decades");

    // Sampling that skips every multiple of 10 (fast boarding, ~1 s polling)
    // must STILL announce roughly once per decade — never go silent.
    spoken = Announced(new[] { 3, 8, 14, 19, 26, 33, 38, 45, 52, 58 });
    Check(spoken.Count >= 4, $"boarding: skipping sampler still announces (got {spoken.Count})");
    Check(spoken.Contains(14) && spoken.Contains(26), "boarding: first sample past a decade announces");

    // Repeated identical counts never re-announce.
    spoken = Announced(new[] { 50, 50, 50, 50 });
    CheckEqual(string.Join(",", spoken), "50", "boarding: repeats are silent");

    // Mid-boarding first sight (app started late) seeds silently, then the
    // next decade crossing announces.
    spoken = Announced(new[] { 47, 48, 53 });
    CheckEqual(string.Join(",", spoken), "53", "boarding: late start seeds silently, next bucket speaks");

    // Turnaround: counter drops back to low numbers -> announce restart.
    spoken = Announced(new[] { 150, 2 });
    Check(spoken.Contains(2), "boarding: turnaround restart announces");

    // The PR widened the parse patterns to \d{1,4}; the clamp must match so
    // 4-digit counts (A380 deboarding totals) are not silently truncated.
    Check(GsxService.TryParsePassengerCount("Passenger deboarding 1023/1200 passengers", out int big) && big == 1023,
        "boarding: 4-digit count parses unclamped");
}

// ── Real-data sweep: every charge amount in every real receipt ──────────────
{
    string receiptsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Virtuali", "GSX", "Receipts");
    if (Directory.Exists(receiptsRoot))
    {
        int files = 0, amounts = 0;
        foreach (string file in Directory.EnumerateFiles(receiptsRoot, "*.html", SearchOption.AllDirectories))
        {
            files++;
            string html = File.ReadAllText(file);
            // Strip tags/styles to text, then pick out bare money tokens the
            // way they reach the status path ("$ 1,234.56" style).
            string text = Regex.Replace(html,
                @"<style[\s\S]*?</style>|<script[\s\S]*?</script>|<[^>]+>", "\n");
            foreach (Match m in Regex.Matches(text, @"[$€£]\s?\d[\d,.]*\d|[$€£]\s?\d"))
            {
                amounts++;
                string line = "Service fee " + m.Value.Trim();
                Check(GsxService.ContainsMoneyAmount(line),
                    $"sweep: ContainsMoneyAmount('{line}') [{Path.GetFileName(file)}]");
                Check(GsxService.NormalizeStatusStableText(line).Contains("<price>"),
                    $"sweep: stable-normalizes to <price> ('{line}') [{Path.GetFileName(file)}]");
            }
        }
        Console.WriteLine($"sweep: {files} receipt files, {amounts} real amounts checked");
    }
    else
    {
        Console.WriteLine("sweep: no GSX receipts folder on this machine - skipped");
    }
}

Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
