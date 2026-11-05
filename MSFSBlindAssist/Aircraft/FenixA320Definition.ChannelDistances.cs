using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

// D and Shift+D on the Fenix: the distance to the destination and to the top of descent, summed along the route of the
// display channel (Services/FenixChannelRouteDistances.cs) and spoken in the words and time format the other aircraft's
// readouts use (BaseAircraftDefinition's AnnounceDestFromSDK and AnnounceTODFromSDK). The time comes from SimConnect's
// ground speed, never the ND's ground speed text. The answer is a method of its own, and the seam of
// FenixA320Definition.Hooks.cs only hands it the key, so that the tests can call it.
public partial class FenixA320Definition
{
    // The name the keys' problems are reported under in the channel's log.
    private const string DistancesFeature = "D and Shift+D";

    // The distance keys' seam (declared in FenixA320Definition.Hooks.cs): D and Shift+D. Nothing else answers these keys
    // on the Fenix, so the key is always answered.
    partial void ChannelDistancesKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        handled = true;
        SpeakChannelDistance(action == Hotkeys.HotkeyAction.ReadDistanceToTOD, ctx);
    }

    /// <summary>
    /// D's answer, or Shift+D's with <paramref name="topOfDescent"/>, spoken at once: the distance with its time, "Past
    /// top of descent", or "&lt;name&gt; not available" when there is no monitor, the ND is not trusted, the route cannot
    /// be read or the sum cannot follow it. The verdict and the route come from one snapshot of the ND ([FNX-22]); a route
    /// that does not decode is reported once in the channel's log.
    /// </summary>
    internal void SpeakChannelDistance(bool topOfDescent, ChannelKeyContext ctx)
    {
        string name = topOfDescent ? TopOfDescentName : DestinationName;
        if (ctx.Monitor is not { } monitor)
        {
            SpeakNotAvailable(ctx, name);
            return;
        }

        Services.FenixChannelSnapshot snapshot = monitor.Snapshot(Services.FenixChannelDisplay.Nd);
        Services.FenixDistanceAnswer answer = Services.FenixChannelRouteDistances.Answer(
            snapshot, topOfDescent, problem => monitor.ReportFeatureProblem(DistancesFeature, problem));

        switch (answer.Kind)
        {
            case Services.FenixDistanceKind.Past:
                ctx.Announcer.AnnounceImmediate(PastTopOfDescent);
                break;

            case Services.FenixDistanceKind.Distance:
                Log.Debug("Fenix", DistanceLogLine(topOfDescent, answer.Nm, snapshot.Push!));
                SpeakDistance(topOfDescent, answer.Nm, ctx.SimConnect.IsConnected,
                    withGroundSpeed => ctx.SimConnect.RequestAircraftPositionAsync(
                        position => withGroundSpeed(position.GroundSpeedKnots)),
                    ctx.Announcer.AnnounceImmediate);
                break;

            default:
                SpeakNotAvailable(ctx, name);
                break;
        }
    }

    // The debug.log line of a spoken distance, for the in-sim check: the key, the distance to a tenth, the TO's ident and
    // the number of route elements, read again from the same snapshot's message.
    private static string DistanceLogLine(bool topOfDescent, double nm, Services.FenixChannelPush push)
    {
        Services.FenixRoute? route = Services.FenixChannelRoute.Decode(push).Value;
        return string.Create(CultureInfo.InvariantCulture,
            $"{DistancesFeature}: {(topOfDescent ? "Shift+D" : "D")} {nm:F1} NM, TO {route?.ActiveLeg?.Ident ?? "none"}, {route?.Legs.Count ?? 0} legs");
    }

    /// <summary>What Shift+D says once the descent has begun.</summary>
    internal const string PastTopOfDescent = "Past top of descent";

    /// <summary>D's name for <see cref="SpeakNotAvailable"/>, which adds " not available".</summary>
    internal const string DestinationName = "Distance to destination";

    /// <summary>Shift+D's name for <see cref="SpeakNotAvailable"/>, which adds " not available".</summary>
    internal const string TopOfDescentName = "Top of descent";

    /// <summary>
    /// The sentence for a distance: the whole miles in the invariant culture, "miles to destination" or "miles to top of
    /// descent", then the time at <paramref name="groundSpeedKnots"/> (": HH:MM:SS"), which is left out under 30 knots.
    /// </summary>
    internal static string DistanceSentence(bool topOfDescent, double nm, double groundSpeedKnots)
        => nm.ToString("F0", CultureInfo.InvariantCulture)
            + (topOfDescent ? " miles to top of descent" : " miles to destination")
            + FormatEtaFromDistance(nm, groundSpeedKnots);

    /// <summary>
    /// Speaks a distance through <paramref name="speak"/>. With SimConnect connected it asks for the ground speed
    /// (<paramref name="requestGroundSpeed"/>) and speaks the sentence with its time when the speed comes. Disconnected,
    /// it speaks the sentence at once without a time: the position request does nothing then, and the pilot still gets
    /// the distance.
    /// </summary>
    internal static void SpeakDistance(bool topOfDescent, double nm, bool simConnected,
        Action<Action<double>> requestGroundSpeed, Action<string> speak)
    {
        if (simConnected)
            requestGroundSpeed(groundSpeed => speak(DistanceSentence(topOfDescent, nm, groundSpeed)));
        else
            speak(DistanceSentence(topOfDescent, nm, 0));
    }
}
