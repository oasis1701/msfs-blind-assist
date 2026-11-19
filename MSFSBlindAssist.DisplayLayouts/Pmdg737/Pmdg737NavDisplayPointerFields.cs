namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The VOR/ADF bearing pointers PMDG draws in every mode but PLN: one per receiver
/// (<see cref="Count"/>), chosen by the side's VOR/ADF switch (the bottom corners' switch). At
/// <see cref="VorPosition"/> the VOR pointer, turned by the VOR's bearing from the nose (glass data
/// <see cref="VorBearing"/>, per receiver), drawn while the VOR is tuned (the systems object's <see cref="VorPower"/>
/// byte set and its state <see cref="TunedState"/>, PMDG's tuned flag) and the glass data's <see cref="VorValid"/>
/// is set; at <see cref="AdfPosition"/> the ADF pointer (<see cref="AdfBearing"/>), drawn while the systems object's
/// <see cref="AdfPower"/> byte is set and the ADF receiver's int at <see cref="AdfSignal"/> is not 0. Turned on the
/// screen by the heading minus PMDG's up direction. POS's lines and
/// positions are <see cref="Pos"/>.
/// </summary>
public sealed record NdPointerFields(
    int Count,
    int VorPosition,
    int AdfPosition,
    GlassField VorPower,
    int TunedState,
    GlassField VorValid,
    GlassField VorBearing,
    GlassField AdfPower,
    int AdfSignal,
    GlassField AdfBearing,
    NdPosFields Pos);

/// <summary>
/// POS in MAP (the VOR and ADF lines and the IRS and GPS positions), drawn while the panel's POS button is on. A
/// line from the aircraft toward each tuned VOR (the pointers' tuned flag, whatever the switch; only while the
/// readout is valid), labelled in the full format with <see cref="RadialFormat"/> of the radial (the receiver's
/// simulator interface <see cref="StationRadial"/>, plus <see cref="StationCorrection"/> minus the station's variation
/// while its position is valid; no correction when <see cref="StationCorrection"/> is null, as the MSFS 2020 file
/// prints the radial; 0 printed as <see cref="RadialZeroAs"/>), otherwise with <see cref="VorLabels"/>;
/// a line toward each ADF while the pointers' ADF flag is set (the ADF bearing), labelled <see cref="AdfLabels"/>;
/// the side's IRS position (IRU + <see cref="IruPosition"/>) and the GPS position (the IRS object's pointer at
/// <see cref="GpsArray"/> + side × <see cref="GpsStride"/> + <see cref="GpsPosition"/>), each while valid.
/// </summary>
public sealed record NdPosFields(
    string RadialFormat,
    int RadialZeroAs,
    int StationRadial,
    int? StationCorrection,
    NdLabelPair VorLabels,
    NdLabelPair AdfLabels,
    int IruPosition,
    int GpsArray,
    int GpsStride,
    int GpsPosition);
