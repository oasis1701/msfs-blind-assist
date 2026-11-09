using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// The A300 First Officer's reads, from the SimConnect cache: a row's position by its row key, a light through the
/// pure <see cref="A300LampBoard.IsLit"/> the status boxes use (its bus's light power included), and the synthetic
/// fields below. Unread is NaN, never off ([FO-12]'s contract). Every field goes through <see cref="Read"/>, which
/// tests replace.
/// </summary>
public sealed class A300FoStateEvaluator : LVarStateEvaluator
{
    public const double EngineStoppedN2 = 10;
    public const double EngineSpooledDownN2 = 20;
    public const double EngineRunningN2 = 50;

    /// <summary>Below this APU speed (percent) the APU has run down: the Securing flow's wait before the batteries.</summary>
    public const double ApuStoppedPercent = 10;

    /// <summary>Reads a key from the cache; tests replace it.</summary>
    internal Func<string, double?> Read { get; set; }

    public A300FoStateEvaluator() => Read = key => Sc?.GetCachedVariableValue(key);

    public static readonly IReadOnlyList<string> SyntheticKeys = new[]
    {
        A300FoKeys.Eng1N2Field, A300FoKeys.Eng2N2Field, A300FoKeys.EnginesStopped, A300FoKeys.EnginesSpooledDown,
        A300FoKeys.GearUp, A300FoKeys.GearDown, A300FoKeys.IrsAllAligned, A300FoKeys.FlapsTakeoff,
        A300FoKeys.TransponderOn, A300FoKeys.ApuStartingOrAvailable, A300FoKeys.RudderTrimZero,
        A300FoKeys.BrakesBelow100, A300FoKeys.ApuOrExtPower,
    }.Concat(A300FoKeys.AllAt.Keys).ToArray();

    private static readonly string[] PollFields = A300FoVariables.Keys.Concat(new[]
    {
        A300FoKeys.FuelTotal, A300FoKeys.BaroCaptain, A300FoKeys.BaroFirstOfficer, A300FoKeys.LandingElevation,
        A300FoKeys.PitchTrim, A300FoKeys.FlexTemperature, A300FoKeys.Squawk, A300FoKeys.Minimums, A300FoKeys.DeltaP,
        A300FoKeys.ApuSpeed,
    }).ToArray();

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    /// <summary>A field as a nullable value, for the live values: null where <see cref="IFoStateEvaluator.GetValue"/> is NaN.</summary>
    public double? ReadValue(string key)
    {
        double v = GetValue(key);
        return double.IsNaN(v) ? null : v;
    }

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        value = Value(field) ?? double.NaN;
        return true;   // every field, plain ones included, goes through Read
    }

    private double? Value(string field)
    {
        switch (field)
        {
            case A300FoKeys.Eng1N2Field: return Eng1N2;
            case A300FoKeys.Eng2N2Field: return Eng2N2;
            case A300FoKeys.EnginesStopped:
                // Both N2 read (unknown until the window's first feed) and both fuel levers read off.
                return N2Read && Read(A300FoKeys.FuelLever1) is double l1 && Read(A300FoKeys.FuelLever2) is double l2
                    ? Flag(l1 < 0.5 && l2 < 0.5 && Eng1N2 < EngineStoppedN2 && Eng2N2 < EngineStoppedN2)
                    : null;
            case A300FoKeys.EnginesSpooledDown:
                return N2Read ? Flag(Eng1N2 < EngineSpooledDownN2 && Eng2N2 < EngineSpooledDownN2) : null;
            case A300FoKeys.GearDown:
                return Flag(A300FoGearConfirmation.Down(Read(A300FoKeys.GearLever), Lit(A300FoKeys.GearDownLights)));
            case A300FoKeys.GearUp:
                return Flag(A300FoGearConfirmation.Up(Read(A300FoKeys.GearLever),
                    Lit(A300FoKeys.GearDownLights), Lit(A300FoKeys.GearUnlockedLights)));
            case A300FoKeys.IrsAllAligned:
            {
                var aligned = A300FoKeys.IrsAligned.Select(Read).ToList();
                return aligned.Any(a => a is null) ? null : Flag(aligned.All(a => a >= 0.5));
            }
            case A300FoKeys.FlapsTakeoff:
                return Read(A300FoKeys.Flaps) is double flaps ? Flag(Math.Round(flaps) is >= 1 and <= 3) : null;
            case A300FoKeys.TransponderOn:
                return Read(A300FoKeys.Transponder) is double mode ? Flag(mode >= 0.5) : null;
            case A300FoKeys.ApuStartingOrAvailable:
            {
                bool? starting = LampLit(A300FoKeys.ApuStartOnLight), available = LampLit(A300FoKeys.ApuAvailLight);
                return starting == true || available == true ? 1 : starting is null || available is null ? null : 0;
            }
            case A300FoKeys.RudderTrimZero:
                return Read(A300FoVariables.RudderTrimKey) is double trim ? Flag(Math.Abs(trim) < 0.05) : null;
            case A300FoKeys.ApuOrExtPower:
            {
                // Either source on is enough with the other unread; both read off is no source.
                bool? apu = LampLit(A300FoKeys.ApuAvailLight);
                bool? ext = Read(A300FoKeys.ExtPower) is double e ? e >= 0.5 : null;
                return apu == true || ext == true ? 1 : apu is null || ext is null ? null : 0;
            }
            case A300FoKeys.BrakesBelow100:
                // iniBuilds' own test: (L:LEFT_BRAKES_TEMP) 100 < (L:RIGHT_BRAKES_TEMP) 100 < and
                return Read(A300FoVariables.BrakeTempLeftKey) is double left && Read(A300FoVariables.BrakeTempRightKey) is double right
                    ? Flag(left < 100 && right < 100)
                    : null;
        }
        if (A300FoKeys.AllAt.TryGetValue(field, out var rows))
        {
            var values = rows.Select(r => Read(r.Key)).ToList();
            return values.Any(v => v is null) ? null
                : Flag(rows.Zip(values).All(p => Math.Abs(p.Second!.Value - p.First.Target) < 0.5));
        }
        if (A300LampBoard.ById.ContainsKey(field))
            return LampLit(field) is bool lit ? Flag(lit) : null;
        return Read(field);
    }

    private bool N2Read => !double.IsNaN(Eng1N2) && !double.IsNaN(Eng2N2);

    private bool? LampLit(string id) => A300LampBoard.IsLit(A300LampBoard.ById[id], Read);

    private IReadOnlyList<bool?> Lit(IEnumerable<string> ids) => ids.Select(LampLit).ToList();

    private static double? Flag(bool? on) => on is bool b ? (b ? 1 : 0) : null;
}
