namespace MSFSBlindAssist.Services;

// The ELEC page (7) of the Fenix SD: the boxes of the electrical system as the SD draws them, top to bottom and left to
// right. Written as the ENG page is (FenixChannelSdPage.Eng.cs): one reader, every field read before any row is built, and
// the reader's problem returned as the page's one row. Every member added here is named for the ELEC page.
internal static partial class FenixChannelSdPages
{
    /// <summary>
    /// The ELEC page's rows, in the SD's order, every number whole: the batteries (<c>BAT 1: 27 V, 0 A</c>, with
    /// <c>, charging</c> while its charging flag is set) and the buses (<c>DC BAT: powered</c>, or <c>not powered</c>), the
    /// transformer-rectifiers (<c>TR 1</c>, <c>ESS TR</c>, <c>TR 2</c>: volts and amps), the generators (<c>GEN 1: 25 %,
    /// 115 V, 400 HZ</c>, or <c>GEN 1: OFF</c> while the generator's selector is clear), the APU generator (the same three
    /// values while the APU master is on, or <c>APU GEN: OFF</c> while its pushbutton is off, else <c>APU GEN</c> alone,
    /// the legend the SD draws), the external power
    /// (<c>EXT PWR: 112 V, 400 HZ</c>, only while the buses say it is connected), and each IDG's temperature (<c>IDG 1: 99
    /// °C</c>, then <c>, LO PR</c> while its generator's oil pressure is low and <c>, DISC</c> while it is disconnected),
    /// with <c>GALLEY SHED</c> between the two while the flags say it is shed. The emergency generator and the static
    /// inverter have no field, so no row. No row speaks a color: they are values and flags the app builds.
    /// </summary>
    /// <remarks>
    /// <para>Every box the rows read must be in the push: one left out is "no field 17" (or 16, 18 to 22, 24 to 26), and a
    /// field in another wire shape is that field's wrong shape, both as one row with no values, never a bus not powered or
    /// a battery at 0 V. The page returns that problem and never reports it: the window reports it once, under the page's
    /// name. A box present but empty is its defaults: the Fenix sends an empty flags message while every flag is false.</para>
    /// <para>A box a row does not show is not read, so that a value the SD does not draw can neither stop the page nor be
    /// reported: the external power (23) only while 17.8 says it is connected, the APU's pushbutton (24.14) only while its
    /// master (24.12) is on and its figures only while that pushbutton is not off, a generator's figures only while its
    /// selector is set. The APU message itself is read on every page, since its legend is always drawn.</para>
    /// <para>Which flag means what is inferred from the contract's names and the plan's text; Task 1 measures what the SD
    /// draws with each, and a difference changes the rows and their tests together: 16.1 the galley shed, 16.2 and 16.3 the
    /// batteries charging, 16.4 and 16.5 the generators disconnected, 16.6 and 16.7 their low oil pressure, 17.8 external
    /// power connected, 18.4 and 19.4 the generator selectors, 24.12 the APU master. 24.14, the APU GEN pushbutton off, was
    /// measured in the sim (2026-11-03, the APU running with the pushbutton off: the SD shows the generator OFF, the push
    /// still carries 0 %, 115 V, 399 HZ).</para>
    /// </remarks>
    internal static FenixChannelRows ElecRows(FenixChannelSdPageContext ctx)
    {
        FenixChannelSdReader remote = FenixChannelSdReader.Of(ctx.Push, ctx.ReportProblem);

        FenixChannelSdReader flagsMessage = remote.Message(FenixChannelFields.Remote.ElecFlags);
        ElecFlagsReading flags = new(
            flagsMessage.Bool(FenixChannelFields.ElecFlags.GalleyShed),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Bat1Charging),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Bat2Charging),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Gen1Disconnected),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Gen2Disconnected),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Gen1LowOilPressure),
            flagsMessage.Bool(FenixChannelFields.ElecFlags.Gen2LowOilPressure));

        FenixChannelSdReader buses = remote.Message(FenixChannelFields.Remote.BusStatus);
        bool dc1 = buses.Bool(FenixChannelFields.BusStatus.DcBus1);
        bool dc2 = buses.Bool(FenixChannelFields.BusStatus.DcBus2);
        bool dcEssential = buses.Bool(FenixChannelFields.BusStatus.DcEssential);
        bool ac1 = buses.Bool(FenixChannelFields.BusStatus.AcBus1);
        bool ac2 = buses.Bool(FenixChannelFields.BusStatus.AcBus2);
        bool dcBat = buses.Bool(FenixChannelFields.BusStatus.DcBat);
        bool acEssential = buses.Bool(FenixChannelFields.BusStatus.AcEssential);
        bool externalPower = buses.Bool(FenixChannelFields.BusStatus.ExternalPowerConnected);

        ElecGenerator gen1 = ReadElecGenerator(remote.Message(FenixChannelFields.Remote.Gen1));
        ElecGenerator gen2 = ReadElecGenerator(remote.Message(FenixChannelFields.Remote.Gen2));
        ElecVoltAmpsReading tr1 = ReadElecVoltAmps(remote.Message(FenixChannelFields.Remote.Tr1));
        ElecVoltAmpsReading tr2 = ReadElecVoltAmps(remote.Message(FenixChannelFields.Remote.Tr2));
        ElecVoltAmpsReading essTr = ReadElecVoltAmps(remote.Message(FenixChannelFields.Remote.EssTr));

        // The external power is read only while the buses say it is connected, which the buses just told.
        double extVolts = 0;
        double extFrequency = 0;
        if (externalPower)
        {
            FenixChannelSdReader ext = remote.Message(FenixChannelFields.Remote.ExtPwr);
            extVolts = ext.Double(FenixChannelFields.ExtPwrData.Volts);
            extFrequency = ext.Double(FenixChannelFields.ExtPwrData.Frequency);
        }

        // The APU message is read on every page, its legend being always drawn; the pushbutton's off only while its master
        // is on, and its figures only while that pushbutton is not off.
        FenixChannelSdReader apu = remote.Message(FenixChannelFields.Remote.ApuData);
        bool apuMaster = apu.Bool(FenixChannelFields.ApuData.MasterPower);
        bool apuOff = false;
        double apuLoad = 0;
        double apuVolts = 0;
        double apuFrequency = 0;
        if (apuMaster)
        {
            apuOff = apu.Bool(FenixChannelFields.ApuData.Off);
            if (!apuOff)
            {
                apuLoad = apu.Double(FenixChannelFields.ApuData.Load);
                apuVolts = apu.Double(FenixChannelFields.ApuData.Volts);
                apuFrequency = apu.Double(FenixChannelFields.ApuData.Frequency);
            }
        }

        ElecVoltAmpsReading bat1 = ReadElecVoltAmps(remote.Message(FenixChannelFields.Remote.Bat1));
        ElecVoltAmpsReading bat2 = ReadElecVoltAmps(remote.Message(FenixChannelFields.Remote.Bat2));

        // Every read after a problem gives its default, which must never be spoken.
        if (remote.Problem is { } problem) return FenixChannelRows.NotAvailable(problem);

        var rows = new List<string>
        {
            ElecBattery("BAT 1", bat1, flags.Bat1Charging),
            ElecBus("DC BAT", dcBat),
            ElecBattery("BAT 2", bat2, flags.Bat2Charging),
            ElecBus("DC 1", dc1),
            ElecBus("DC ESS", dcEssential),
            ElecBus("DC 2", dc2),
            ElecBus("AC 1", ac1),
            ElecBus("AC ESS", acEssential),
            ElecBus("AC 2", ac2),
            ElecVoltAmpsRow("TR 1", tr1),
            ElecVoltAmpsRow("ESS TR", essTr),
            ElecVoltAmpsRow("TR 2", tr2),
            ElecGeneratorRow("GEN 1", gen1),
            ElecApuRow(apuMaster, apuOff, apuLoad, apuVolts, apuFrequency)
        };

        if (externalPower)
        {
            rows.Add($"EXT PWR: {FenixChannelSd.Whole(extVolts)} V, {FenixChannelSd.Whole(extFrequency)} HZ");
        }

        rows.Add(ElecGeneratorRow("GEN 2", gen2));
        rows.Add(ElecIdgRow("IDG 1", gen1, flags.Gen1LowOilPressure, flags.Gen1Disconnected));

        if (flags.GalleyShed)
        {
            rows.Add("GALLEY SHED");
        }

        rows.Add(ElecIdgRow("IDG 2", gen2, flags.Gen2LowOilPressure, flags.Gen2Disconnected));
        return FenixChannelRows.Of(rows);
    }

    /// <summary>The ELEC page (7).</summary>
    static partial void ComposeElec(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows)
        => rows = ElecRows(ctx);

    // The flags of the ELEC page (16), each as the contract names it.
    private readonly record struct ElecFlagsReading(
        bool GalleyShed, bool Bat1Charging, bool Bat2Charging, bool Gen1Disconnected, bool Gen2Disconnected,
        bool Gen1LowOilPressure, bool Gen2LowOilPressure);

    // What the page reads of one engine generator: its IDG temperature always, and its figures only while it is on.
    private readonly record struct ElecGenerator(
        bool On, double Load, double Voltage, double Frequency, double Temperature);

    // The volts and the amps of a transformer-rectifier or a battery.
    private readonly record struct ElecVoltAmpsReading(double Volts, double Amps);

    private static ElecGenerator ReadElecGenerator(FenixChannelSdReader generator)
    {
        bool on = generator.Bool(FenixChannelFields.GeneratorData.Selector);
        double temperature = generator.Double(FenixChannelFields.GeneratorData.Temperature);

        double load = 0;
        double voltage = 0;
        double frequency = 0;
        if (on)
        {
            load = generator.Double(FenixChannelFields.GeneratorData.Load);
            voltage = generator.Double(FenixChannelFields.GeneratorData.Voltage);
            frequency = generator.Double(FenixChannelFields.GeneratorData.Frequency);
        }

        return new ElecGenerator(on, load, voltage, frequency, temperature);
    }

    private static ElecVoltAmpsReading ReadElecVoltAmps(FenixChannelSdReader box)
        => new(box.Double(FenixChannelFields.VoltAmps.Volts), box.Double(FenixChannelFields.VoltAmps.Amps));

    // A bus: "DC BAT: powered" or "DC BAT: not powered".
    private static string ElecBus(string label, bool powered) => $"{label}: {(powered ? "powered" : "not powered")}";

    // "TR 1: 28 V, 12 A".
    private static string ElecVoltAmpsRow(string label, ElecVoltAmpsReading box)
        => $"{label}: {FenixChannelSd.Whole(box.Volts)} V, {FenixChannelSd.Whole(box.Amps)} A";

    // A battery: its volts and amps, then ", charging" while its flag says so.
    private static string ElecBattery(string label, ElecVoltAmpsReading battery, bool charging)
        => ElecVoltAmpsRow(label, battery) + (charging ? ", charging" : "");

    // "GEN 1: 25 %, 115 V, 400 HZ" while the generator is on, else "GEN 1: OFF".
    private static string ElecGeneratorRow(string label, ElecGenerator generator)
        => generator.On
            ? ElecValues(label, generator.Load, generator.Voltage, generator.Frequency)
            : $"{label}: OFF";

    // The APU generator: its three figures while the APU master is on, "APU GEN: OFF" while the master is on and the
    // pushbutton is off (as "GEN 1: OFF"), else the bare legend the SD draws.
    private static string ElecApuRow(bool master, bool off, double load, double volts, double frequency)
        => !master ? "APU GEN" : off ? "APU GEN: OFF" : ElecValues("APU GEN", load, volts, frequency);

    // A generator's three figures, the load in percent: "APU GEN: 13 %, 116 V, 399 HZ".
    private static string ElecValues(string label, double load, double volts, double frequency)
        => $"{label}: {FenixChannelSd.Whole(load)} %, {FenixChannelSd.Whole(volts)} V, {FenixChannelSd.Whole(frequency)} HZ";

    // An IDG: its temperature, then ", LO PR" while its generator's oil pressure is low and ", DISC" while the generator
    // is disconnected.
    private static string ElecIdgRow(string label, ElecGenerator generator, bool lowOilPressure, bool disconnected)
        => $"{label}: {FenixChannelSd.Whole(generator.Temperature)} °C"
           + (lowOilPressure ? ", LO PR" : "")
           + (disconnected ? ", DISC" : "");
}
