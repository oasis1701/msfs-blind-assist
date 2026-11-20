namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Which COWS DA40 airframe is loaded. The two share a cockpit layout and the stock
/// Working Title G1000, but differ in powerplant and therefore in a large part of the
/// panel set: the NG is an Austro AE300 turbodiesel with FADEC/ECU and Main+Auxiliary
/// tanks, the XLS a Lycoming IO-360 with prop and mixture levers and Left/Right tanks.
///
/// Detection is by aircraft TITLE ("DA40-NG …" / "DA40-XLS …"). It CANNOT be done by
/// probing for an L-var: MobiFlight returns 0 for an undefined L-var rather than an
/// error, so NG-only names such as ECU_VOTER:1 read 0 on the XLS and look present
/// (verified live 2026-08-28).
/// </summary>
public enum DA40Variant
{
    /// <summary>DA40-NG — Austro AE300 turbodiesel, FADEC, single power lever.</summary>
    NG,

    /// <summary>DA40-XLS — Lycoming IO-360, throttle/prop/mixture, magnetos.</summary>
    XLS
}
