namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI's TCAS messages and traffic, an optional part of <see cref="Bbs757EhsiLayout"/> (plan 28), all from the
/// layout file. The TCAS computer is at <see cref="NdToTcas"/> in the nd; <see cref="TcasPowered"/> to
/// <see cref="TcasTest"/> are offsets in it, <see cref="TrafficSwitch"/> (TFC) and <see cref="MessagesAllowed"/> in the
/// nd. The traffic is a vector at <see cref="TrafficList"/> of <see cref="TrafficStride"/>-byte entries, at most
/// <see cref="MaxTraffic"/> in the walk, each with its kind, trend, relative altitude (feet) and position; the kind and
/// trend values and the level values are BlueBird's, the tag is the relative altitude over <see cref="TagDivisor"/>.
/// The words are the screen's own (<see cref="TestWord"/> trimmed of its leading spaces). Records compare by content.
/// </summary>
public sealed record Bbs757EhsiTcas(
    int NdToTcas,
    int TcasPowered,
    int TcasFailed,
    int TcasLevel,
    int TrafficAdvisory,
    int ResolutionAdvisory,
    int TcasTest,
    int TrafficSwitch,
    int MessagesAllowed,
    int TrafficList,
    int TrafficStride,
    int MaxTraffic,
    int TrafficKind,
    int TrafficTrend,
    int TrafficAltitude,
    int TrafficLongitude,
    int TrafficLatitude,
    int KindOther,
    int KindProximate,
    int KindAdvisory,
    int KindResolution,
    int TrendUp,
    int TrendDown,
    int TaOnlyLevel,
    int TaRaLevel,
    double TagDivisor,
    string TcasWord,
    string FailWord,
    string OffWord,
    string TfcWord,
    string TaOnlyWord,
    string TestWord,
    string TrafficWord);
