namespace MSFSBlindAssist.Services;

/// <summary>How a <see cref="FenixCabinWrite"/> is sent: the data service's typed write for each.</summary>
public enum FenixCabinWriteKind { Int, Bool, Float, String }

/// <summary>A cabin temperature zone: the FAP's <c>fwd</c> and <c>aft</c> areas.</summary>
public enum FenixCabinZone { Forward, Aft }

/// <summary>The three cargo holds the EFB's ground service opens: forward, aft and bulk.</summary>
public enum FenixCargoDoor { Forward, Aft, Bulk }

/// <summary>The FAP's cabin lighting levels (its BRT, DIM 1, DIM 2 buttons and the main On/Off toggle's off).</summary>
public enum FenixCabinLightLevel { Bright, Dim1, Dim2, Off }

/// <summary>
/// One data-name write: the kind says which of the data service's typed writes sends it and <paramref name="Value"/>
/// is the matching .NET type (<c>int</c> for Int, <c>bool</c> for Bool, <c>double</c> for Float,
/// <c>string</c> for String).
/// </summary>
public sealed record FenixCabinWrite(FenixCabinWriteKind Kind, string Name, object Value);

/// <summary>
/// Every data-name write the Cabin window makes, built the way the Fenix's own FAP and EFB build them
/// (Fenix 3.0.0.7426 bundles: the FAP's cabin ready and cabin lights writes, temperature store, water
/// preselect and Set-Up store, and the EFB's door enum, door control names and toggle writes). Pure
/// functions: nothing here reads the sim or touches the network except <see cref="SendAsync"/>, which
/// sends one write through the <see cref="FenixDataService"/>.
/// <para>
/// FAP limits, in words. The temperature store steps a zone's offset by half a degree between minus 2.5
/// and plus 2.5; the temperature page greys the warmer button when the offset is at the top or the
/// zone's target (the cockpit selection plus the offset) is 30 or more, and the cooler button when the
/// offset is at the bottom or the target is 18 or less. The water store steps the preselect by 25 from
/// 25 to 100, counting an unknown preselect as 100. The FAP Set-Up store shows the loudspeaker volume as
/// the data value times 100, rounded to the nearest 5 (JavaScript rounding: halves go up) and held
/// between 0 and 100, with a default of 100 when it has no value yet; its increase effect writes that
/// shown value plus 5 (at most 100) and its decrease effect the shown value minus 5 (at least 0), each
/// as a Float of the percent divided by 100 to the FAP speaker-volume name. The EFB writes a door's
/// ground-service control as the Int enum Armed 0, Disarmed 1, Open 2: its toggle writes Open or
/// Disarmed, never Armed. The cargo doors are Bool writes.
/// </para>
/// </summary>
public static class FenixCabinWrites
{
    /// <summary>The temperature offset step, in degrees.</summary>
    public const double OffsetStep = 0.5;
    /// <summary>The lowest temperature offset, in degrees.</summary>
    public const double MinOffset = -2.5;
    /// <summary>The highest temperature offset, in degrees.</summary>
    public const double MaxOffset = 2.5;
    /// <summary>The lowest zone target the FAP lets the offset reach, in degrees Celsius.</summary>
    public const double MinTemperature = 18;
    /// <summary>The highest zone target the FAP lets the offset reach, in degrees Celsius.</summary>
    public const double MaxTemperature = 30;

    /// <summary>The water preselect step, in percent (named for its unit because <see cref="PreselectStep"/>
    /// is the step method).</summary>
    public const double PreselectStepPercent = 25;
    /// <summary>The lowest water preselect, in percent.</summary>
    public const double MinPreselect = 25;
    /// <summary>The highest water preselect, in percent.</summary>
    public const double MaxPreselect = 100;

    /// <summary>The loudspeaker volume step, in percent.</summary>
    public const int SpeakerVolumeStepPercent = 5;
    /// <summary>The lowest loudspeaker volume, in percent.</summary>
    public const int MinSpeakerVolumePercent = 0;
    /// <summary>The highest loudspeaker volume, in percent (also what an unknown volume counts as).</summary>
    public const int MaxSpeakerVolumePercent = 100;

    /// <summary>The EFB's Disarmed value of a door control (Armed, 0, is never written).</summary>
    public const int DoorDisarmed = 1;
    /// <summary>The EFB's Open value of a door control.</summary>
    public const int DoorOpen = 2;

    // ---- Cabin lights ---------------------------------------------------------------------------

    /// <summary>The value the Fenix reads and writes for a lighting level: <c>Bright</c>, <c>Dim1</c>, <c>Dim2</c>, <c>Off</c>.</summary>
    public static string LightWire(FenixCabinLightLevel level) => level switch
    {
        FenixCabinLightLevel.Bright => "Bright",
        FenixCabinLightLevel.Dim1 => "Dim1",
        FenixCabinLightLevel.Dim2 => "Dim2",
        FenixCabinLightLevel.Off => "Off",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a cabin light level.")
    };

    /// <summary>A lighting level as it is spoken: <c>Bright</c>, <c>Dim 1</c>, <c>Dim 2</c>, <c>Off</c>.</summary>
    public static string LightName(FenixCabinLightLevel level) => level switch
    {
        FenixCabinLightLevel.Bright => "Bright",
        FenixCabinLightLevel.Dim1 => "Dim 1",
        FenixCabinLightLevel.Dim2 => "Dim 2",
        FenixCabinLightLevel.Off => "Off",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a cabin light level.")
    };

    /// <summary>The level behind a value the Fenix read back, or null for anything but the four exact (ordinal) wire values.</summary>
    public static FenixCabinLightLevel? ParseLight(string? wire) => wire switch
    {
        "Bright" => FenixCabinLightLevel.Bright,
        "Dim1" => FenixCabinLightLevel.Dim1,
        "Dim2" => FenixCabinLightLevel.Dim2,
        "Off" => FenixCabinLightLevel.Off,
        _ => null
    };

    /// <summary>The FAP's cabin-ready button: a Bool write.</summary>
    public static FenixCabinWrite CabinReady(bool ready)
        => new(FenixCabinWriteKind.Bool, FenixCabinNames.CabinReady, ready);

    /// <summary>The FAP's cabin-lights buttons: a String write of the level's wire value.</summary>
    public static FenixCabinWrite CabinLights(FenixCabinLightLevel level)
        => new(FenixCabinWriteKind.String, FenixCabinNames.CabinLights, LightWire(level));

    // ---- Temperature ----------------------------------------------------------------------------

    /// <summary>
    /// One half-degree step of a zone's offset, as the FAP's temperature page allows it: the new offset as
    /// a Float write, or null when the FAP would grey the button (the offset is at its limit, or the zone's
    /// target, <paramref name="selected"/> plus <paramref name="offset"/>, is at 30 for warmer or 18 for
    /// cooler). A value that is not a finite number steps nothing.
    /// </summary>
    /// <param name="selected">The cockpit-selected temperature of the zone.</param>
    /// <param name="offset">The zone's current offset.</param>
    public static FenixCabinWrite? TemperatureStep(FenixCabinZone zone, double selected, double offset, bool warmer)
    {
        string name = OffsetName(zone);
        if (!double.IsFinite(selected) || !double.IsFinite(offset)) return null;

        double target = selected + offset;
        bool refused = warmer
            ? offset >= MaxOffset || target >= MaxTemperature
            : offset <= MinOffset || target <= MinTemperature;
        if (refused) return null;

        return new FenixCabinWrite(FenixCabinWriteKind.Float, name, warmer ? offset + OffsetStep : offset - OffsetStep);
    }

    /// <summary>The FAP's Reset button: both zone offsets back to zero, forward then aft.</summary>
    public static IReadOnlyList<FenixCabinWrite> ResetTemperatures() => new[]
    {
        new FenixCabinWrite(FenixCabinWriteKind.Float, FenixCabinNames.TempFwdOffset, 0.0),
        new FenixCabinWrite(FenixCabinWriteKind.Float, FenixCabinNames.TempAftOffset, 0.0)
    };

    private static string OffsetName(FenixCabinZone zone) => zone switch
    {
        FenixCabinZone.Forward => FenixCabinNames.TempFwdOffset,
        FenixCabinZone.Aft => FenixCabinNames.TempAftOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Not a cabin temperature zone.")
    };

    // ---- Water ----------------------------------------------------------------------------------

    /// <summary>
    /// One 25 percent step of the water preselect, as the FAP's water page takes it: the new preselect as a
    /// Float write, kept between 25 and 100, with an unknown current value counting as 100. Null when the
    /// step would leave the preselect where it is.
    /// </summary>
    public static FenixCabinWrite? PreselectStep(double? current, bool up)
    {
        double? known = Finite(current);
        double basis = known ?? MaxPreselect;
        double next = up
            ? Math.Min(basis + PreselectStepPercent, MaxPreselect)
            : Math.Max(basis - PreselectStepPercent, MinPreselect);
        if (next == known) return null;

        return new FenixCabinWrite(FenixCabinWriteKind.Float, FenixCabinNames.WaterPreselect, next);
    }

    // ---- Doors ----------------------------------------------------------------------------------

    /// <summary>
    /// The EFB's door toggle: an Int write of Disarmed when the door's control is Open now, else Open (also
    /// when it is unknown). It never writes Armed, which the EFB does not either.
    /// </summary>
    /// <param name="doorKey">One of <see cref="FenixCabinNames.DoorKeys"/>.</param>
    /// <param name="controlValue">What <c>doors.control.&lt;key&gt;</c> reads now, or null when unknown.</param>
    public static FenixCabinWrite DoorToggle(string doorKey, double? controlValue)
        => new(FenixCabinWriteKind.Int, FenixCabinNames.DoorControl(doorKey),
            controlValue == DoorOpen ? DoorDisarmed : DoorOpen);

    /// <summary>The EFB's cargo door toggle: a Bool write, true to open.</summary>
    public static FenixCabinWrite CargoDoor(FenixCargoDoor door, bool open)
    {
        string name = door switch
        {
            FenixCargoDoor.Forward => FenixCabinNames.CargoForward,
            FenixCargoDoor.Aft => FenixCabinNames.CargoAft,
            FenixCargoDoor.Bulk => FenixCabinNames.CargoBulk,
            _ => throw new ArgumentOutOfRangeException(nameof(door), door, "Not a cargo door.")
        };
        return new FenixCabinWrite(FenixCabinWriteKind.Bool, name, open);
    }

    // ---- FAP loudspeaker volume -----------------------------------------------------------------

    /// <summary>
    /// One 5 percent step of the FAP loudspeaker volume, as the FAP Set-Up page takes it: from the volume the
    /// page shows (the data value in percent, rounded to the nearest 5 like JavaScript's <c>Math.round</c>,
    /// held between 0 and 100; unknown counts as 100), up or down by 5 inside 0 to 100. A Float write of
    /// the new percent divided by 100 (a fraction from 0 to 1), or null when the step would leave the
    /// shown volume where it is.
    /// </summary>
    /// <param name="current">What the speaker-volume name reads now, a fraction, or null when unknown.</param>
    public static FenixCabinWrite? SpeakerVolumeStep(double? current, bool up)
    {
        double? known = Finite(current);
        int shown = known is { } fraction ? ShownSpeakerPercent(fraction) : MaxSpeakerVolumePercent;
        int next = up
            ? Math.Min(shown + SpeakerVolumeStepPercent, MaxSpeakerVolumePercent)
            : Math.Max(shown - SpeakerVolumeStepPercent, MinSpeakerVolumePercent);
        if (next == shown) return null;

        return new FenixCabinWrite(FenixCabinWriteKind.Float, FenixCabinNames.FapSpeakerVolume, next / 100.0);
    }

    // The FAP Set-Up page's own display value: 5 * round(100 * v / 5), kept in 0 to 100. JavaScript's
    // Math.round is floor(x + 0.5), so a half goes up (also for a negative number).
    internal static int ShownSpeakerPercent(double fraction)
    {
        double rounded = SpeakerVolumeStepPercent * Math.Floor(100 * fraction / SpeakerVolumeStepPercent + 0.5);
        return (int)Math.Min(MaxSpeakerVolumePercent, Math.Max(MinSpeakerVolumePercent, rounded));
    }

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    // ---- Sending --------------------------------------------------------------------------------

    /// <summary>
    /// Sends one write through the data service's typed write for its kind and says whether the Fenix accepted it.
    /// False when the data service is down or refuses it, and for a write whose value is not the type its
    /// kind needs (nothing is sent then); nothing is thrown. A Float is sent with an invariant decimal point.
    /// </summary>
    public static Task<bool> SendAsync(this FenixDataService data, FenixCabinWrite write, CancellationToken ct = default)
    {
        if (data == null || write == null) return Task.FromResult(false);

        return write switch
        {
            { Kind: FenixCabinWriteKind.Int, Value: int i } => data.WriteIntAsync(write.Name, i, ct),
            { Kind: FenixCabinWriteKind.Bool, Value: bool b } => data.WriteBoolAsync(write.Name, b, ct),
            { Kind: FenixCabinWriteKind.Float, Value: double d } => data.WriteFloatAsync(write.Name, d, ct),
            { Kind: FenixCabinWriteKind.String, Value: string s } => data.WriteStringAsync(write.Name, s, ct),
            _ => Task.FromResult(false)
        };
    }
}
