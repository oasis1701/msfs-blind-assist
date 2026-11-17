namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The status lines and rows of every 757 display reader, worded as the PMDG 737's, with the reader's name.</summary>
public static class Bbs757DisplaySpeech
{
    public static string Unavailable(string name, string reason) => $"{name} announcements unavailable: {reason}";

    public static string Stopped(string name, string reason) => $"{name} announcements stopped: {reason}";

    public static string Resumed(string name) => $"{name} announcements resumed";

    public static string Starting(string name) => $"{name} reader starting";

    public static string Waiting(string name) => $"{name} reader waiting for the simulator";

    /// <summary>The window's one row while the reader runs and its display is dark: never spoken (S4).</summary>
    public static string Unpowered(string name) => $"{name} display not powered";
}
