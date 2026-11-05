using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

// D and Shift+D on the Fenix: the distance to the destination and to the top of descent, summed along the route of the
// display channel (Services/FenixChannelRouteDistances.cs) and spoken in the words and time format the other aircraft's
// readouts use (BaseAircraftDefinition's AnnounceDestFromSDK and AnnounceTODFromSDK). The time comes from SimConnect's
// ground speed, never the ND's ground speed text.
public partial class FenixA320Definition
{
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
