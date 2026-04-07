namespace MSFSBlindAssist.Models;

/// <summary>
/// Full SimBrief OFP (Operational Flight Plan) data parsed from the API response.
/// </summary>
public class SimBriefOFP
{
    // ── General ───────────────────────────────────────────────────────────────
    public string AirlineIcao       { get; set; } = "";
    public string FlightNumber      { get; set; } = "";
    public string Callsign          { get; set; } = "";
    public string Route             { get; set; } = "";
    public string RouteDistance     { get; set; } = "";
    public string AirTime           { get; set; } = "";
    public string CostIndex         { get; set; } = "";
    public string InitialAltitude   { get; set; } = "";
    public string CruiseMach        { get; set; } = "";
    public string CruiseTas         { get; set; } = "";
    public string ClimbProfile      { get; set; } = "";
    public string CruiseProfile     { get; set; } = "";
    public string DescentProfile    { get; set; } = "";
    public string Passengers        { get; set; } = "";
    public string AvgWindComp       { get; set; } = "";
    public string AvgIsaDev         { get; set; } = "";
    public string Units             { get; set; } = "lbs";

    // ── Aircraft ──────────────────────────────────────────────────────────────
    public string AircraftName      { get; set; } = "";
    public string AircraftIcao      { get; set; } = "";
    public string AircraftReg       { get; set; } = "";

    // ── Origin ────────────────────────────────────────────────────────────────
    public string OriginIcao        { get; set; } = "";
    public string OriginName        { get; set; } = "";
    public string OriginElevation   { get; set; } = "";
    public string OriginRunway      { get; set; } = "";
    public string OriginSid         { get; set; } = "";
    public string OriginSidTrans    { get; set; } = "";
    public string OriginMetar       { get; set; } = "";
    public string OriginTaf         { get; set; } = "";
    public string OriginWindDir     { get; set; } = "";
    public string OriginWindSpd     { get; set; } = "";
    public string OriginTransAlt    { get; set; } = "";
    public string OriginTransLevel  { get; set; } = "";

    // ── Destination ───────────────────────────────────────────────────────────
    public string DestIcao          { get; set; } = "";
    public string DestName          { get; set; } = "";
    public string DestElevation     { get; set; } = "";
    public string DestRunway        { get; set; } = "";
    public string DestStar          { get; set; } = "";
    public string DestStarTrans     { get; set; } = "";
    public string DestApproach      { get; set; } = "";
    public string DestApproachTrans { get; set; } = "";
    public string DestIlsFreq       { get; set; } = "";
    public string DestMetar         { get; set; } = "";
    public string DestTaf           { get; set; } = "";
    public string DestTransAlt      { get; set; } = "";
    public string DestTransLevel    { get; set; } = "";

    // ── Alternate ─────────────────────────────────────────────────────────────
    public string AltnIcao          { get; set; } = "";
    public string AltnName          { get; set; } = "";
    public string AltnMetar         { get; set; } = "";
    public string AltnTaf           { get; set; } = "";

    // ── Fuel ──────────────────────────────────────────────────────────────────
    public string FuelBlockRamp      { get; set; } = "";
    public string FuelTrip           { get; set; } = "";
    public string FuelReserve        { get; set; } = "";
    public string FuelAlternate      { get; set; } = "";
    public string FuelContingency    { get; set; } = "";
    public string FuelExtra          { get; set; } = "";
    public string FuelMinTakeoff     { get; set; } = "";
    public string FuelTaxi           { get; set; } = "";
    public string FuelPlannedLanding { get; set; } = "";

    // ── Weights ───────────────────────────────────────────────────────────────
    public string WeightOew          { get; set; } = "";
    public string WeightPayload      { get; set; } = "";
    public string WeightPaxWeight    { get; set; } = "";
    public string WeightCargo        { get; set; } = "";
    public string WeightZfw          { get; set; } = "";
    public string WeightTow          { get; set; } = "";
    public string WeightLw           { get; set; } = "";
    public string WeightMaxZfw       { get; set; } = "";
    public string WeightMaxTow       { get; set; } = "";
    public string WeightMaxLw        { get; set; } = "";

    // ── Performance – Takeoff ─────────────────────────────────────────────────
    public string TakeoffV1          { get; set; } = "";
    public string TakeoffVr          { get; set; } = "";
    public string TakeoffV2          { get; set; } = "";
    public string TakeoffFlaps       { get; set; } = "";
    public string TakeoffTrim        { get; set; } = "";
    public string TakeoffHw          { get; set; } = "";
    public string TakeoffXw          { get; set; } = "";
    public string PerfLimitFactor    { get; set; } = "";

    // ── Performance – En Route ────────────────────────────────────────────────
    public string ClimbIas           { get; set; } = "";
    public string ClimbMach          { get; set; } = "";
    public string DescentIas         { get; set; } = "";
    public string DescentMach        { get; set; } = "";

    // ── Performance – Landing ─────────────────────────────────────────────────
    public string LandingFlaps       { get; set; } = "";
    public string LandingVapp        { get; set; } = "";
    public string LandingDistDry     { get; set; } = "";
    public string LandingDistWet     { get; set; } = "";
    public string LandingBrakeSetting{ get; set; } = "";
    public string LandingHw          { get; set; } = "";
    public string LandingXw          { get; set; } = "";

    // ── Step Climbs ───────────────────────────────────────────────────────────
    public string StepClimbString { get; set; } = "";

    // ── TLR (Takeoff and Landing Report) raw text ─────────────────────────────
    public string TlrText         { get; set; } = "";

    // ── Nav Log ───────────────────────────────────────────────────────────────
    public List<SimBriefNavFix> NavLog { get; set; } = new();

    // ── Diagnostics ───────────────────────────────────────────────────────────
    /// <summary>Space-separated XML element names in the first navlog fix node (for diagnosing missing fields).</summary>
    public string NavLogFieldNames { get; set; } = "";
}

/// <summary>
/// A single fix (waypoint) from the SimBrief navigation log.
/// </summary>
public class SimBriefNavFix
{
    public string Ident      { get; set; } = "";
    public string ViaAirway  { get; set; } = "";
    public string Type       { get; set; } = "";
    public string AltitudeFt { get; set; } = "";
    public string DistLeg    { get; set; } = "";
    public string DistCum    { get; set; } = "";
    public string WindDir    { get; set; } = "";
    public string WindSpd    { get; set; } = "";
    public string WindComp   { get; set; } = "";
    public string Efob       { get; set; } = "";
    public string TimeLeg    { get; set; } = "";
    public string TimeTotal  { get; set; } = "";
    public string VorName    { get; set; } = "";
    public string Frequency  { get; set; } = "";
    public bool   IsSidStar  { get; set; }
    public string Course     { get; set; } = "";
    public string Ias        { get; set; } = "";
    public string Mach       { get; set; } = "";
    public string Oat        { get; set; } = "";
    public string IsaDev     { get; set; } = "";
    public string Mora       { get; set; } = "";
    public string IcaoFir         { get; set; } = "";
    public string FuelPlanOnboard { get; set; } = "";
    public string FuelTotalUsed   { get; set; } = "";
    public string FuelLeg         { get; set; } = "";
}
