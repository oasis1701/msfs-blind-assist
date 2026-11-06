namespace MSFSBlindAssist.Services;

/// <summary>
/// What a feature's decoder gives back for one section of a push: the value it decoded, or the reason it has none.
/// A decoder checks the wire shape of every field it uses and the presence of the content it needs. A section it
/// cannot decode stops that feature only: its window shows one status row, its keys say "not available" and its
/// announcer stays silent. The reason is what that status row says, and what the monitor's log gets once
/// (<see cref="FenixChannelMonitor.ReportFeatureProblem"/>).
/// </summary>
/// <remarks>
/// The reasons are worded to read after "not available, " in a window's status row and after the feature's name in
/// the log: "field 125.2 has the wrong shape", "no speed limits", "unknown code 'k' in the ground speed line". Build
/// a section with the factories, so a reason always has this wording. A section is ok only with a value as well as
/// no problem, so a decoder that returns <c>default</c> by mistake fails closed: for a reference type that section is
/// not ok, where it would otherwise read as ok with nothing in it. (A number or a flag has no null to check, and its
/// default is a value.)
/// </remarks>
internal readonly record struct FenixChannelSection<T>(T? Value, string? Problem)
{
    /// <summary>
    /// True for a section that decoded: it has a <see cref="Value"/> and no <see cref="Problem"/>. A default section of
    /// a reference type, which has neither, is not ok.
    /// </summary>
    public bool IsOk => Problem is null && Value is not null;

    /// <summary>A section that decoded.</summary>
    public static FenixChannelSection<T> Ok(T value) => new(value, null);

    /// <summary>
    /// A field of the section arrived in another wire shape than the contract gives it. <paramref name="path"/> is the
    /// field's path in the push, as the decoders record one: "125.2" for field 2 inside field 125.
    /// </summary>
    public static FenixChannelSection<T> WrongShape(string path) => new(default, $"field {path} has the wrong shape");

    /// <summary>The section lacks content the feature needs: <paramref name="what"/> is what is missing ("speed limits").</summary>
    public static FenixChannelSection<T> Missing(string what) => new(default, $"no {what}");

    /// <summary>
    /// A line of channel text has a format letter the app does not know ([FNX-24]), in the wording the trust gate uses
    /// ("unknown code 'k' in the ground speed line"). The caller names <paramref name="where"/> so that it reads after
    /// "the".
    /// </summary>
    public static FenixChannelSection<T> UnknownCode(char code, string where)
        => new(default, $"unknown code '{code}' in the {where}");
}

/// <summary>
/// What a window composer or an SD page composer gives back: the rows to show, top to bottom and left to right, or the
/// reason there are none, which the window shows as its one status row ("&lt;Display&gt;: not available,
/// &lt;reason&gt;").
/// </summary>
internal readonly record struct FenixChannelRows(IReadOnlyList<string>? Rows, string? Problem)
{
    /// <summary>The rows of a display that can be shown.</summary>
    public static FenixChannelRows Of(IReadOnlyList<string> rows) => new(rows, null);

    /// <summary>A display that cannot be shown: <paramref name="problem"/> is why, as the status row says it.</summary>
    public static FenixChannelRows NotAvailable(string problem) => new(null, problem);
}
