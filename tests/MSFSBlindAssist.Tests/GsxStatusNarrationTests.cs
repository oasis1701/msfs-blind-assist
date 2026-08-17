// GSX's per-vehicle ground-crew narration lives in a service row's `statusText`, and until
// now nothing spoke it.
//
// The pre-Remote-API transport scraped ONE tooltip string carrying every segment at once, and
// read the lot aloud -- its own regex comment names the shape: "rear loader leaving while 5
// boarded". The Remote API split that string in two: GSX's banner became the `message` slot
// (spoken by GsxService.AnnounceMessageIfChanged) and the per-vehicle detail became each
// row's `statusText`. Only the banner ever got an announcer, so since the migration the
// loaders, belts, stairs and trains have reached the TOOLTIP and nothing else.
//
// The bus is the exception that hid it: GSX publishes the bus phase TWICE, once inside
// statusText and once in a dedicated `detail.busPhase` that GsxServiceAnnouncer.BusPhrase
// speaks. So a pilot heard "Board bus approaching." and reasonably assumed the rest of the
// crew was simply quiet.
//
// Live capture from the reporting pilot, mid-boarding, read out of the tooltip:
//
//   Boarding service is being performed (rear stairs in position, front loader raising belt,
//   rear loader raising belt, front train on the way, rear train on the way, ETA 33 secs,
//   bus idle, pax 0/93)
//
// Two things in that line shape the rules below. "pax 0/93" is a QUANTITY the typed
// announcers already speak on their own milestone schedule, so it must never be read from
// here as well. And "ETA 33 secs" is a live COUNTDOWN -- announced naively it would tick once
// a second, which is the exact spam GsxPhraseGate was written for.

using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxStatusNarrationTests
{
    // The live capture, as GSX publishes it: one line per element.
    private const string Boarding =
        "rear stairs in position\nfront loader raising belt\nrear loader raising belt\n" +
        "front train on the way\nrear train on the way, ETA 33 secs\nbus idle\npax 0/93";

    [Fact]
    public void Every_vehicle_line_of_the_live_capture_is_narration()
    {
        var lines = GsxStatusNarration.VehicleLines(Boarding);

        Assert.Equal(new[]
        {
            "rear stairs in position",
            "front loader raising belt",
            "rear loader raising belt",
            "front train on the way",
            "rear train on the way",
            // The ETA is its own clause once commas split, which is what lets the countdown
            // be held back without holding back the train's phase alongside it.
            "ETA 33 secs",
        }, lines);
    }

    [Theory]
    [InlineData("bus idle")]
    [InlineData("bus in position")]
    [InlineData("Bus approaching")]
    public void The_bus_line_is_left_to_the_dedicated_bus_phase(string busLine)
    {
        // GSX publishes the bus TWICE -- inside statusText and in detail.busPhase, which
        // GsxServiceAnnouncer.BusPhrase already speaks as "Board bus approaching." The
        // captured row proves the overlap: statusText "bus in position" alongside
        // busPhase "in position". Reading both would double every bus callout.
        Assert.DoesNotContain(busLine,
            GsxStatusNarration.VehicleLines($"front loader raising belt\n{busLine}"));
    }

    [Theory]
    [InlineData("pax 0/93")]
    [InlineData("pax 181/186")]
    [InlineData("bags 100%")]
    [InlineData("Bags 83%")]
    public void A_quantity_line_is_never_narration(string quantity)
    {
        // The typed pax/bags announcers own these on their own milestone schedule. Reading
        // them here too would say the same number twice from two places.
        Assert.DoesNotContain(quantity, GsxStatusNarration.VehicleLines($"front loader raising belt\n{quantity}"));
    }

    [Fact]
    public void Blank_and_whitespace_lines_are_dropped()
    {
        Assert.Equal(new[] { "front train on the way" }, GsxStatusNarration.VehicleLines("\n  \nfront train on the way\n\n"));
        Assert.Empty(GsxStatusNarration.VehicleLines(""));
        Assert.Empty(GsxStatusNarration.VehicleLines("   "));
    }

    [Fact]
    public void Line_endings_are_normalised_like_ComposeDetail_does()
    {
        Assert.Equal(new[] { "rear stairs in position", "front loader approaching" },
                     GsxStatusNarration.VehicleLines("rear stairs in position\r\nfront loader approaching"));
    }

    [Fact]
    public void The_first_reading_of_a_service_narrates_every_vehicle()
    {
        var fresh = GsxStatusNarration.NewSince(GsxStatusNarration.VehicleLines(Boarding), Array.Empty<string>());

        Assert.Equal(6, fresh.Count);
        Assert.Contains("front loader raising belt", fresh);
        Assert.DoesNotContain("pax 0/93", fresh);
    }

    [Fact]
    public void An_unchanged_reading_says_nothing()
    {
        var lines = GsxStatusNarration.VehicleLines(Boarding);

        Assert.Empty(GsxStatusNarration.NewSince(lines, lines));
    }

    [Fact]
    public void Only_the_vehicle_that_moved_is_narrated()
    {
        var before = GsxStatusNarration.VehicleLines(Boarding);
        var after = GsxStatusNarration.VehicleLines(
            Boarding.Replace("front loader raising belt", "front loader lowering belt"));

        Assert.Equal(new[] { "front loader lowering belt" }, GsxStatusNarration.NewSince(after, before));
    }

    [Fact]
    public void A_ticking_ETA_countdown_is_not_narrated_again()
    {
        // "rear train on the way, ETA 33 secs" -> "... ETA 32 secs" differs only in a
        // standalone digit run, which GsxPhraseGate classifies as a tick rather than news.
        var before = GsxStatusNarration.VehicleLines(Boarding);
        var after = GsxStatusNarration.VehicleLines(Boarding.Replace("ETA 33 secs", "ETA 32 secs"));

        Assert.Empty(GsxStatusNarration.NewSince(after, before));
    }

    [Fact]
    public void A_countdown_on_its_own_line_is_also_a_tick()
    {
        // The same capture may publish the ETA as its own line; the rule must not depend on
        // which, since both reach us as one line among several.
        Assert.Empty(GsxStatusNarration.NewSince(
            new[] { "front train on the way", "ETA 12 secs" },
            new[] { "front train on the way", "ETA 15 secs" }));
    }

    [Fact]
    public void A_vehicle_that_finishes_and_disappears_is_not_narrated()
    {
        // Silence is the right answer for a line GSX simply stopped publishing: the state
        // change that matters is announced when the vehicle NEXT does something.
        Assert.Empty(GsxStatusNarration.NewSince(
            new[] { "rear stairs in position" },
            new[] { "rear stairs in position", "front loader raising belt" }));
    }

    [Fact]
    public void Simultaneous_changes_are_narrated_in_GSXs_own_order()
    {
        var fresh = GsxStatusNarration.NewSince(
            new[] { "front loader raising belt", "rear loader raising belt" },
            new[] { "front loader approaching", "rear loader approaching" });

        Assert.Equal(new[] { "front loader raising belt", "rear loader raising belt" }, fresh);
    }

    // ── GSX publishes the vehicle block as ONE comma-separated line ──────────
    // Measured from a live turnaround AFTER the first version shipped: 31 narration
    // utterances carried 83 clauses, and 55 of them (66 %) were clauses already spoken.
    //   "Board rear stairs approaching, front loader on the way, rear loader on the way."
    //   "Board rear stairs approaching, front loader approaching, rear loader on the way."
    // Only the front loader moved, but the whole block re-read, because splitting on
    // newlines alone left the block as a single line. The captured fixtures had misled us:
    // their newline-separated "bus in position" / "pax 181/186" made per-line look like
    // per-vehicle. A clause is the unit, and clauses are comma-separated.

    [Fact]
    public void A_comma_separated_block_is_split_per_clause()
    {
        Assert.Equal(
            new[] { "rear stairs approaching", "front loader on the way", "rear loader on the way" },
            GsxStatusNarration.VehicleLines("rear stairs approaching, front loader on the way, rear loader on the way"));
    }

    [Fact]
    public void Only_the_clause_that_moved_is_narrated_from_a_comma_block()
    {
        var before = GsxStatusNarration.VehicleLines("rear stairs approaching, front loader on the way, rear loader on the way");
        var after = GsxStatusNarration.VehicleLines("rear stairs approaching, front loader approaching, rear loader on the way");

        Assert.Equal(new[] { "front loader approaching" }, GsxStatusNarration.NewSince(after, before));
    }

    [Fact]
    public void A_quantity_or_bus_clause_inside_a_comma_block_is_still_dropped()
    {
        Assert.Equal(
            new[] { "rear stairs in position", "front loader raising belt" },
            GsxStatusNarration.VehicleLines("rear stairs in position, front loader raising belt, bus idle, pax 0/93"));
    }

    [Fact]
    public void A_thousands_separator_does_not_split_a_clause()
    {
        // "fuel 4,801 lb" is one clause. Splitting inside the number would make "fuel 4" and
        // "801 lb" two clauses, and the second would re-announce on every tick.
        Assert.Equal(new[] { "pumping", "fuel 4,801 lb" },
                     GsxStatusNarration.VehicleLines("pumping, fuel 4,801 lb"));
    }

    [Fact]
    public void A_metered_refuel_clause_settles_after_one_reading()
    {
        // The live line: "pumping, fuel 4801/12682 lb, aircraft 7313 lb, Bill $2863". Each
        // clause differs from its predecessor only in digits, so only "pumping" survives the
        // first reading and the numbers never speak again.
        var before = GsxStatusNarration.VehicleLines("pumping, fuel 4801/12682 lb, aircraft 7313 lb, Bill $2863");
        var after = GsxStatusNarration.VehicleLines("pumping, fuel 4900/12682 lb, aircraft 7412 lb, Bill $2901");

        Assert.Empty(GsxStatusNarration.NewSince(after, before));
    }

    [Fact]
    public void The_real_captured_boarding_never_says_a_clause_twice()
    {
        // The eight status blocks GSX actually published during one live boarding, in order,
        // recovered from gsx.log. Under the newline-only split these produced 83 spoken
        // clauses of which 55 (66 %) repeated something already said.
        string[] blocks =
        {
            "rear stairs approaching, front loader on the way, rear loader on the way",
            "rear stairs approaching, front loader approaching, rear loader on the way",
            "rear stairs extending stairs, front loader raising belt, rear loader on the way, front train on the way",
            "rear stairs raising staircase, front loader raising belt, rear loader on the way, front train on the way",
            "rear stairs repositioning, front loader raising belt, rear loader on the way, front train on the way",
            "rear stairs moving staircase to door, front loader raising belt, rear loader on the way, front train on the way",
            "waiting for FuelTruck to clear the work area, rear stairs in position, front loader raising belt, rear loader on the way, front train on the way",
            "rear stairs in position, front loader waiting for train, rear loader on the way, front train on the way",
        };

        var spokenEver = new List<string>();
        IReadOnlyList<string> last = Array.Empty<string>();
        foreach (string block in blocks)
        {
            var current = GsxStatusNarration.VehicleLines(block);
            spokenEver.AddRange(GsxStatusNarration.NewSince(current, last));
            last = current;
        }

        // Every clause the crew actually reached is still announced ...
        Assert.Contains("front loader raising belt", spokenEver);
        Assert.Contains("rear stairs moving staircase to door", spokenEver);
        Assert.Contains("waiting for FuelTruck to clear the work area", spokenEver);
        Assert.Contains("front loader waiting for train", spokenEver);

        // ... and none of them twice. "rear loader on the way" spans all eight blocks and is
        // spoken once; "rear stairs in position" recurs in blocks 7 and 8 and is spoken once.
        Assert.Equal(spokenEver.Count, spokenEver.Distinct().Count());
        Assert.Equal(13, spokenEver.Count);
    }
}
