using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One engine as the E/WD draws it, from a <see cref="SingleEngineState"/>. A value may be NaN when the display does not
/// draw it, and a composer must not speak it as a number.
/// </summary>
/// <param name="N1">The N1 (2); 0 when absent.</param>
/// <param name="N2">The N2 (3); 0 when absent.</param>
/// <param name="FuelFlow">The fuel flow (Ff, 4), in the weight unit of <see cref="FenixEwdData.IsKg"/> per hour; 0 when absent.</param>
/// <param name="Epr">The EPR (5); 0 when absent.</param>
/// <param name="Egt">The EGT (6); 0 when absent.</param>
internal readonly record struct FenixEwdEngine(double N1, double N2, double FuelFlow, double Epr, double Egt);

/// <summary>
/// What the E/WD draws of its own, read from the RemoteData message of a push: the two engines, the thrust limit, the
/// weight unit and the flap and slat text. The memo and warning columns are not here.
/// </summary>
/// <param name="Left">The left engine (129.1).</param>
/// <param name="Right">The right engine (129.2).</param>
/// <param name="ThrustLimit">
/// The thrust limit lines (129.8), channel text as sent and in the order sent. An element the push sent without its
/// string is "" and keeps its place.
/// </param>
/// <param name="IsKg">The weight unit is kilograms (154); false, pounds, when the push left it out.</param>
/// <param name="FlapText">The flap and slat text (152), channel text as sent; "" when absent.</param>
internal sealed record FenixEwdData(
    FenixEwdEngine Left, FenixEwdEngine Right, IReadOnlyList<string> ThrustLimit, bool IsKg, string FlapText);

/// <summary>Reads the E/WD's own fields out of the RemoteData message of a push.</summary>
internal static class FenixChannelEwdData
{
    /// <summary>
    /// Reads the E/WD's engines, thrust limit, flap text and weight unit from the RemoteData message
    /// <paramref name="push"/>. Every field is checked for its wire shape, and a string for valid UTF-8; the first one in
    /// another shape, in the order of a path (the engine message, the left engine and its numbers, the right engine and
    /// its numbers, the thrust limit, the flap text, the weight unit), gives a section that names it by its path
    /// ("129.1.4"). A shape problem is reported before missing content, since a damaged part is what hides the content.
    /// The engine message and both of its engines must be there; one that is not is missing "engine data". Fields not
    /// listed here are skipped whatever their shape.
    /// </summary>
    /// <remarks>
    /// The gate does not scan the thrust limit or the flap text for a format letter the app does not know, so this
    /// does ([FNX-24]): the first such letter, in the thrust limit and then the flap text, makes the section an unknown
    /// code that names it. The texts are returned as sent, with their codes.
    /// </remarks>
    public static FenixChannelSection<FenixEwdData> Decode(FenixChannelPush push)
    {
        // The path of the first field found in another wire shape; null while every field read had its shape.
        string? wrong = null;

        FenixChannelPush? engines = PartOf(push, Remote.EngineState, "129");
        FenixChannelPush? leftPart = PartOf(engines, EngineControlState.Left, "129.1");
        FenixEwdEngine left = EngineOf(leftPart, "129.1");
        FenixChannelPush? rightPart = PartOf(engines, EngineControlState.Right, "129.2");
        FenixEwdEngine right = EngineOf(rightPart, "129.2");

        // Repeated: every element, in the order sent, each a message whose field 1 is the string.
        List<string> thrustLimit = [];
        if (engines is not null)
        {
            foreach (FenixChannelField element in engines.All(EngineControlState.EngLimit))
            {
                FenixChannelPush? wrapped = element.AsMessage();
                if (wrapped is null)
                {
                    Wrong("129.8");
                    continue;
                }

                thrustLimit.Add(TextOf(wrapped, NullWrapped.Value, "129.8.1"));
            }
        }

        string flapText = TextOf(push, Remote.FlapEcamState, "152");
        bool isKg = FlagOf(push, Remote.IsKg, "154");

        if (wrong is not null) return FenixChannelSection<FenixEwdData>.WrongShape(wrong);
        if (leftPart is null || rightPart is null) return FenixChannelSection<FenixEwdData>.Missing("engine data");

        foreach (string line in thrustLimit)
        {
            if (UnknownIn(line) is char limitLetter)
            {
                return FenixChannelSection<FenixEwdData>.UnknownCode(limitLetter, "thrust limit");
            }
        }

        if (UnknownIn(flapText) is char flapLetter)
        {
            return FenixChannelSection<FenixEwdData>.UnknownCode(flapLetter, "flap text");
        }

        return FenixChannelSection<FenixEwdData>.Ok(new FenixEwdData(left, right, thrustLimit, isKg, flapText));

        // ---- The reads. Each takes the part it reads from, which is null when the message did not send it or sent it
        // in another shape, and then answers the value a field left out has. A field in another shape records its path,
        // the first only, and reads as left out.

        FenixEwdEngine EngineOf(FenixChannelPush? engine, string path) => new(
            NumberOf(engine, SingleEngineState.N1, $"{path}.2"),
            NumberOf(engine, SingleEngineState.N2, $"{path}.3"),
            NumberOf(engine, SingleEngineState.Ff, $"{path}.4"),
            NumberOf(engine, SingleEngineState.Epr, $"{path}.5"),
            NumberOf(engine, SingleEngineState.Egt, $"{path}.6"));

        // A singular nested part.
        FenixChannelPush? PartOf(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return null;

            FenixChannelPush? message = field.AsMessage();
            if (message is null) Wrong(path);
            return message;
        }

        double NumberOf(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return 0;

            if (field.Type != FenixWireType.Fixed64)
            {
                Wrong(path);
                return 0;
            }

            return field.AsDouble();
        }

        bool FlagOf(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return false;

            if (field.Type != FenixWireType.Varint)
            {
                Wrong(path);
                return false;
            }

            return field.AsBool();
        }

        string TextOf(FenixChannelPush? parent, int number, string path)
        {
            if (parent is null || !parent.TryGet(number, out FenixChannelField field)) return "";

            string? text = field.AsString();
            if (text is null) Wrong(path);
            return text ?? "";
        }

        void Wrong(string path) => wrong ??= path;
    }

    // The first format letter in the text that the app does not know, or null when it knows them all.
    private static char? UnknownIn(string text)
    {
        FenixTextLine line = FenixChannelText.Parse(text);
        return line.UnknownCodes.Count > 0 ? line.UnknownCodes[0] : null;
    }
}
