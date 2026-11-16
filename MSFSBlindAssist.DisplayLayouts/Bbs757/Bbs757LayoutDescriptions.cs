using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Human-readable dumps of the 757 layouts for the module log (<c>bbs757-module.log</c>), as the PMDG
/// module logs its layouts. Pure.</summary>
public static class Bbs757LayoutDescriptions
{
    public static IReadOnlyList<string> Describe(Bbs757FmaLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"file analysis: {l.SourceFile}: FMA: manager {H(l.ManagerBase)} span {l.ManagerSpan}, pfd vptr {H(l.PfdVptr)}, " +
            $"pfd->afds {l.PfdToAfds}, pfd->tmc {l.PfdToTmc}",
            $"file analysis: {l.SourceFile}: FMA fields: pitch {l.Pitch}, pitchArmed {l.PitchArmed}, roll {l.Roll}, " +
            $"rollArmed {l.RollArmed}, atMode {l.AtMode}",
            $"file analysis: {l.SourceFile}: FMA gate fields: status {l.Status}, fdWord {l.FdWord}, fdCaptain {l.FdCaptain}, " +
            $"fdFirstOfficer {l.FdFirstOfficer}, afdsToMcp {l.AfdsToMcp}, mcpAltitude {l.McpAltitude}, " +
            $"mcpHeading {l.McpHeading}, mcpValueType {l.McpValueType}",
        };
        Words(lines, l.SourceFile, "A/T", l.AtWords);
        Words(lines, l.SourceFile, "pitch", l.PitchWords);
        Words(lines, l.SourceFile, "pitch armed", l.PitchArmedWords);
        Words(lines, l.SourceFile, "roll", l.RollWords);
        Words(lines, l.SourceFile, "roll armed", l.RollArmedWords);
        Words(lines, l.SourceFile, "status", l.StatusWords);
        return lines;
    }

    private static void Words(List<string> lines, string file, string column, Bbs757WordTable table)
    {
        string body = string.Join(", ", table.Words.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        lines.Add($"file analysis: {file}: FMA {column} words: {body}");
    }

    public static IReadOnlyList<string> DescribeLowerEicas(Bbs757LowerEicasLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string p = $"file analysis: {l.SourceFile}: lower EICAS";
        var lines = new List<string>
        {
            $"{p}: manager {H(l.ManagerBase)} span {l.ManagerSpan}, lowerVptr {H(l.LowerVptr)}",
            $"{p} power: set {l.PowerSet}, clear {l.PowerClear}, blankClear {l.BlankClear}",
            $"{p} page: page {l.Page}, enginePage {l.EnginePage}, statusPage {l.StatusPage}, engineOff {l.EngineOff}, nLatch {l.NLatch}",
            $"{p} engines: left {l.LeftEngine}, right {l.RightEngine}, engineToData {l.EngineToData}, engineType {l.EngineType}, " +
            $"engineInvalid {l.EngineInvalid}, engineInvalidValue {l.EngineInvalidValue}, hpSpoolRr {l.HpSpoolRr}, hpSpoolPw {l.HpSpoolPw}",
        };
        if (l.Engine is { } e)
        {
            lines.Add($"{p} engine page data: n2 {e.N2}, n3 {e.N3}, fuelFlow {e.FuelFlow}, oilPressure {e.OilPressure}, " +
                $"oilTemperature {e.OilTemperature}, oilQuantity {e.OilQuantity}, vibration {e.Vibration}");
            lines.Add($"{p} engine page fields: maxN2 {e.MaxN2Left}/{e.MaxN2Right}, maxN3 {e.MaxN3Left}/{e.MaxN3Right}, " +
                $"lowerToSettings {e.LowerToSettings}, poundsFlag {e.PoundsFlag}, startFlags {e.StartFlagA}/{e.StartFlagB}/{e.StartFlagC}, " +
                $"holdTimer {e.HoldTimer}, holdDelay {e.HoldDelay}, holdBytes {e.HoldByteA}/{e.HoldByteB}");
        }
        else
        {
            lines.Add($"{p}: engine page missing: {l.EngineMissing}");
        }
        if (l.Status is { } s)
        {
            lines.Add($"{p} status list: listAddress {H(s.ListAddress)}, begin {s.ListBegin}, end {s.ListEnd}, stride {s.ListStride}, " +
                $"listCap {s.ListCap}, text {s.RecordText}, length {s.RecordLength}, sequence {s.RecordSequence}");
            lines.Add($"{p} status hydraulics: lowerToHydraulics {s.LowerToHydraulics}, pressures {s.HydraulicPressures} stride {s.HydraulicPressureStride}, " +
                $"quantities {s.HydraulicQuantities} stride {s.HydraulicQuantityStride}, capacity {s.HydraulicCapacity}, quantity {s.HydraulicQuantity}");
            lines.Add($"{p} status apu: lowerToApu {s.LowerToApu}, egt {s.ApuEgt}, rpm {s.ApuRpm}, oilQuantity {s.ApuOilQuantity}");
            lines.Add($"{p} status oxygen: lowerToOxygen {s.LowerToOxygen}, shown {s.OxygenShown}, capacity {s.OxygenCapacity}, " +
                $"active {s.OxygenActive}, full {s.OxygenFull}, oxygenLevel {s.OxygenLevel}");
            lines.Add($"{p} status pointers: rudder {s.Rudder}, aileronLeft {s.AileronLeft}, elevator {s.Elevator}, aileronRight {s.AileronRight}");
            lines.Add($"{p} status brakes: lowerToComputer {s.LowerToComputer}, brakeTemperatures {s.BrakeTemperatures}, wheels {s.BrakeWheels}");
        }
        else
        {
            lines.Add($"{p}: status page missing: {l.StatusMissing}");
        }
        return lines;
    }

    public static IReadOnlyList<string> DescribeUpperEicas(Bbs757UpperEicasLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string D(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        string A(int[] v) => "[" + string.Join(", ", v.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";
        string f = l.SourceFile;
        var lines = new List<string>
        {
            $"file analysis: {f}: upper EICAS: manager {H(l.ManagerBase)} span {l.ManagerSpan}, upper EICAS vptr {H(l.UpperEicasVptr)}",
            $"file analysis: {f}: upper EICAS links: toComputer {l.ToComputer}, toEngineLeft {l.ToEngineLeft}, " +
            $"toEngineRight {l.ToEngineRight}, toLower {l.ToLower}, toPneumatic {l.ToPneumatic}, toFms {l.ToFms}, " +
            $"toFire {l.ToFire}, toReverserLeft {l.ToReverserLeft}, toReverserRight {l.ToReverserRight}, " +
            $"toTmc {l.ToTmc}, toTat {l.ToTat}",
            $"file analysis: {f}: upper EICAS power and limits: powered {l.Powered}, failed {l.Failed}, " +
            $"timedBlank {l.TimedBlank}, compact {l.Compact}, egtRed {l.EgtRed}, egtAmber {l.EgtAmber}, n1Red {l.N1Red}, " +
            $"n1Amber {l.N1Amber}, maxN1Left {l.MaxN1Left}, maxN1Right {l.MaxN1Right}, maxEgtLeft {l.MaxEgtLeft}, " +
            $"maxEgtRight {l.MaxEgtRight}, envelopeRows {l.EnvelopeRows}, envelopeRowStride {l.EnvelopeRowStride}, " +
            $"envelopeRowCount {l.EnvelopeRowCount}, envelopeRowFl {l.EnvelopeRowFl}, envelopeRowMin {l.EnvelopeRowMin}, " +
            $"envelopeRowMax {l.EnvelopeRowMax}",
            $"file analysis: {f}: upper EICAS messages: mainList {H(l.MainList)}, communicationList {H(l.CommunicationList)}, " +
            $"entryText {l.EntryText}, entryLength {l.EntryLength}, entryId {l.EntryId}, entryCancelled {l.EntryCancelled}, " +
            $"entryLevel {l.EntryLevel}, maxMessages {l.MaxMessages}, maxTextLength {l.MaxTextLength}, " +
            $"linesPerPage {l.LinesPerPage}, communicationLinesBase {l.CommunicationLinesBase}",
            $"file analysis: {f}: upper EICAS computer: computerPage {l.ComputerPage}, computerRecallTime {l.ComputerRecallTime}, " +
            $"computerBlank {l.ComputerBlank}, clock {H(l.Clock)}, recallShowSeconds {D(l.RecallShowSeconds)}, " +
            $"pageWord {l.PageWord}, recallWord {l.RecallWord}",
            $"file analysis: {f}: upper EICAS engines: engineBlock {l.EngineBlock}, blockEgt {l.BlockEgt}, blockN1 {l.BlockN1}, " +
            $"blockEpr {l.BlockEpr}, engineN1Alternate {l.EngineN1Alternate}, engineN1AlternateFlags {A(l.EngineN1AlternateFlags)}, " +
            $"engineValid {l.EngineValid}, engineInvalidValue {l.EngineInvalidValue}, engineType {l.EngineType}, " +
            $"engineTypePw {l.EngineTypePw}, engineCombustion {l.EngineCombustion}, pwBlankBelow {D(l.PwBlankBelow)}, " +
            $"blockN2Pw {l.BlockN2Pw}",
            $"file analysis: {f}: upper EICAS thrust reference: thrustMode {l.ThrustMode}, thrustNumber {l.ThrustNumber}, " +
            $"derate {l.Derate}, dtoFlags {A(l.DtoFlags)}, derateShown {l.DerateShown}, referenceEpr {l.ReferenceEpr}, " +
            $"manualEprLeft {l.ManualEprLeft}, manualEprRight {l.ManualEprRight}, tmcFailed {l.TmcFailed}, " +
            $"tmcChannelLeft {l.TmcChannelLeft}, tmcChannelRight {l.TmcChannelRight}, dtoWord {l.DtoWord}, " +
            $"manualWord {l.ManualWord}, numberedModes {A(l.NumberedModes)}, inhibitModes {A(l.InhibitModes)}",
            $"file analysis: {f}: upper EICAS others: tatValue {l.TatValue}, reverserState {l.ReverserState}, " +
            $"reverserMoving {l.ReverserMoving}, reverserDeployed {l.ReverserDeployed}, revWord {l.RevWord}, " +
            $"pneumaticComponents {l.PneumaticComponents}, componentStride {l.ComponentStride}, componentId {l.ComponentId}, " +
            $"componentPosition {l.ComponentPosition}, componentState {l.ComponentState}, taiLeftId {l.TaiLeftId}, " +
            $"taiRightId {l.TaiRightId}, taiMinOpen {D(l.TaiMinOpen)}, taiStates {A(l.TaiStates)}, taiWord {l.TaiWord}, " +
            $"maxComponents {l.MaxComponents}",
            $"file analysis: {f}: upper EICAS envelope gate: fmsOnGround {l.FmsOnGround}, fireSwitchLeft {l.FireSwitchLeft}, " +
            $"fireSwitchRight {l.FireSwitchRight}, lowerMode {l.LowerMode}, envelopeLowerMode {l.EnvelopeLowerMode}, " +
            $"envelopeTitle {l.EnvelopeTitle}, envelopeFl {l.EnvelopeFl}, envelopeKts {l.EnvelopeKts}",
        };
        Words(lines, f, "levels", l.Levels, "upper EICAS");
        Words(lines, f, "thrust", l.ThrustWords, "upper EICAS");
        Words(lines, f, "thrust number", l.ThrustNumbers, "upper EICAS");
        return lines;
    }

    public static IReadOnlyList<string> DescribeEhsi(Bbs757EhsiLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string D(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        string A(IEnumerable<int> v) => "[" + string.Join(", ", v.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";
        string f = l.SourceFile;
        string p = $"file analysis: {f}: EHSI";
        var lines = new List<string>
        {
            $"{p}: manager {H(l.ManagerBase)} span {l.ManagerSpan}, nd vptr {H(l.NdVptr)}",
            $"{p} gauge and links: powered {l.Powered}, failed {l.Failed}, blanked {l.Blanked}, ndToEfis {l.NdToEfis}, " +
            $"ndToSource {l.NdToSource}, ndToMcp {l.NdToMcp}, ndToRoute {l.NdToRoute}, sourceToIrs {l.SourceToIrs}, " +
            $"efisMode {l.EfisMode}, efisCentered {l.EfisCentered}",
            $"{p} nd values: range {l.Range}, rangeSelected {l.RangeSelected}, rangeChanging {l.RangeChanging}, " +
            $"valid {l.Valid}, aligning {l.Aligning}, number {l.Number}, trackUp {l.TrackUp}, trueReference {l.TrueReference}, " +
            $"width {l.Width}, height {l.Height}, longitude {l.Longitude}, latitude {l.Latitude}, " +
            $"centerLatitude {l.CenterLatitude}, centerLongitude {l.CenterLongitude}, rotation {l.Rotation}, " +
            $"screenX {l.ScreenX}, screenY {l.ScreenY}, dataSwitch {l.DataSwitch}, excessCount {l.ExcessCount}, " +
            $"excessLimit {l.ExcessLimit}, originAirport {l.OriginAirport}, destinationAirport {l.DestinationAirport}, " +
            $"airportLongitude {l.AirportLongitude}, airportLatitude {l.AirportLatitude}",
            $"{p} others: mcpHeading {l.McpHeading}, irsGroundSpeed {l.IrsGroundSpeed}, clock {H(l.Clock)}, " +
            $"radiusFactor {D(l.RadiusFactor)}, aircraftYFactor {D(l.AircraftYFactor)}, clipInset {D(l.ClipInset)}, " +
            $"markerMargin {D(l.MarkerMargin)}, minEtaGroundSpeed {D(l.MinEtaGroundSpeed)}",
            $"{p} route: routeLegs {l.RouteLegs}, routeFlownLegs {l.RouteFlownLegs}, legStride {l.LegStride}, " +
            $"maxLegs {l.MaxLegs}, routeActive {l.RouteActive}, routeModified {l.RouteModified}, " +
            $"routeModification {l.RouteModification}, routeId {l.RouteId}",
            $"{p} leg: legLabel {l.LegLabel}, legKind {l.LegKind}, discontinuityKind {l.DiscontinuityKind}, " +
            $"legPositions [{string.Join(", ", l.LegPositions.Select(x => $"{x.Kind}:{x.Latitude}/{x.Longitude}"))}], " +
            $"legLatitude {l.LegLatitude}, legLongitude {l.LegLongitude}, legTerminator {l.LegTerminator}, " +
            $"noSymbolTerminators {A(l.NoSymbolTerminators)}, manualTerminators {A(l.ManualTerminators)}, " +
            $"legConstraintKind {l.LegConstraintKind}, legConstraintFirst {l.LegConstraintFirst}, " +
            $"legConstraintSecond {l.LegConstraintSecond}, legEta {l.LegEta}",
            $"{p} path: pathRegistry {H(l.PathRegistry)}, pathRegistryFirst {l.PathRegistryFirst}, pathNodeKey {l.PathNodeKey}, " +
            $"pathNodeValue {l.PathNodeValue}, topOfClimbFlag {l.TopOfClimbFlag}, topOfClimbLatitude {l.TopOfClimbLatitude}, " +
            $"topOfClimbLongitude {l.TopOfClimbLongitude}, topOfDescentFlagA {l.TopOfDescentFlagA}, " +
            $"topOfDescentFlagB {l.TopOfDescentFlagB}, topOfDescentLatitude {l.TopOfDescentLatitude}, " +
            $"topOfDescentLongitude {l.TopOfDescentLongitude}, endOfDescentLeg {l.EndOfDescentLeg}",
            $"{p} words: hdg {l.HdgWord}, trk {l.TrkWord}, mag {l.MagWord}, true {l.TrueWord}, map {l.MapWord}, " +
            $"nm {l.NmWord}, etaSuffix {l.EtaSuffix}, topOfClimb {l.TopOfClimbWord}, topOfDescent {l.TopOfDescentWord}, " +
            $"endOfDescent {l.EndOfDescentWord}, excess {l.ExcessWord}",
        };
        Words(lines, f, "modes", l.Modes, "EHSI");
        Part(lines, p, "VOR and ILS", l.Approach, l.ApproachMissing, a =>
            $"vorCourse {a.VorCourse}, vorDeviation {a.VorDeviation}, vorDeviationValid {a.VorDeviationValid}, " +
            $"toFrom {a.ToFrom} (to {a.ToValue}, from {a.FromValue}), vorDme {a.VorDme}, vorDmeValid {a.VorDmeValid}, " +
            $"ilsCourse {a.IlsCourse}, localizerDeviation {a.LocalizerDeviation}, localizerBar {a.LocalizerBar}, " +
            $"ilsDots {a.IlsDots}, ilsTest {a.IlsTest}, ilsFrequency {a.IlsFrequency}, noIlsFrequency {D(a.NoIlsFrequency)}, " +
            $"locFlag {a.LocFlag}, glideslopeFlag {a.GlideslopeFlag}, displaySide {a.DisplaySide}, captainSide {a.CaptainSide}, " +
            $"ndToPfd {a.NdToPfd}, pfdToIls {a.PfdToIls}, glideslopeDeviation {a.GlideslopeDeviation}, " +
            $"glideslopeScale {a.GlideslopeScale}, glideslopePointer {a.GlideslopePointer}, glideslopeTest {a.GlideslopeTest}, " +
            $"ndToDme {a.NdToDme}, dmePowered {a.DmePowered}, dmeFailed {a.DmeFailed}, dmeStation {a.DmeStation}, " +
            $"dmeDistance {a.DmeDistance}, stationKind {a.StationKind}, ilsStationKind {a.IlsStationKind}, " +
            $"vorKinds {A(a.VorKinds)}, " +
            $"sourceToIlsReceiver {a.SourceToIlsReceiver}, receiverToSignal {a.ReceiverToSignal}, signalBlock {a.SignalBlock}, " +
            $"localizerReceived {a.LocalizerReceived}, rmiHandles {H(a.RmiLeftHandle)}/{H(a.RmiRightHandle)}, " +
            $"handleValue {a.HandleValue}, rmiVor {a.RmiVor}, rmiAdf {a.RmiAdf}, ndToVor {a.NdToVorLeft}/{a.NdToVorRight}, " +
            $"panelStation {a.PanelStation} ils {a.PanelIlsStation} manual {a.PanelManualWhole}/{a.PanelManualFraction}, " +
            $"station ident {a.StationIdent} length {a.StationIdentLength} frequency {a.StationFrequency} " +
            $"longitude {a.StationLongitude} latitude {a.StationLatitude}, " +
            $"ndToAdf {a.NdToAdf}, adfPositions {a.AdfPositions}, adf {a.AdfLeft}/{a.AdfRight} " +
            $"longitude {a.AdfLongitude} latitude {a.AdfLatitude}, " +
            $"words {Q(a.VorLeftWord)} {Q(a.VorRightWord)} {Q(a.IlsWord)} {Q(a.ToWord)} {Q(a.FromWord)} {Q(a.DmeWord)} " +
            $"{Q(a.NoFrequencyWord)} {Q(a.NoDmeWord)} {Q(a.LocWord)} {Q(a.GlideslopeWord)}, " +
            $"deviationFullScale {D(a.DeviationFullScale)}, dotsPerUnit {D(a.DotsPerUnit)}, glideslopePin {D(a.GlideslopePin)}");
        Part(lines, p, "TCAS", l.Tcas, l.TcasMissing, t =>
            $"ndToTcas {t.NdToTcas}, powered {t.TcasPowered}, failed {t.TcasFailed}, level {t.TcasLevel}, " +
            $"trafficAdvisory {t.TrafficAdvisory}, resolutionAdvisory {t.ResolutionAdvisory}, test {t.TcasTest}, " +
            $"trafficSwitch {t.TrafficSwitch}, messagesAllowed {t.MessagesAllowed}, trafficList {t.TrafficList}, " +
            $"trafficStride {t.TrafficStride}, maxTraffic {t.MaxTraffic}, entry kind {t.TrafficKind} trend {t.TrafficTrend} " +
            $"altitude {t.TrafficAltitude} longitude {t.TrafficLongitude} latitude {t.TrafficLatitude}, " +
            $"kinds {t.KindOther}/{t.KindProximate}/{t.KindAdvisory}/{t.KindResolution}, trends up {t.TrendUp} down {t.TrendDown}, " +
            $"levels taOnly {t.TaOnlyLevel} taRa {t.TaRaLevel}, tagDivisor {D(t.TagDivisor)}, " +
            $"words {Q(t.TcasWord)} {Q(t.FailWord)} {Q(t.OffWord)} {Q(t.TfcWord)} {Q(t.TaOnlyWord)} {Q(t.TestWord)} {Q(t.TrafficWord)}");
        Part(lines, p, "map options", l.MapOptions, l.MapOptionsMissing, o =>
            $"switches {o.NavAidSwitch}/{o.AirportSwitch}/{o.WaypointSwitch}, tuned {o.TunedLeft}/{o.TunedRight}, " +
            $"ndToSuper {o.NdToSuper}, superToNavdb {o.SuperToNavdb}, ndToFms {o.NdToFms}, navaids {o.Navaids} " +
            $"(ident {o.NavaidIdent} length {o.NavaidIdentLength}, frequency {o.NavaidFrequency}, kind {o.NavaidKind}, class {o.NavaidClass}, " +
            $"longitude {o.NavaidLongitude}, latitude {o.NavaidLatitude}), airports {o.Airports} stride {o.AirportStride} " +
            $"(name {o.AirportName} label {o.AirportLabelLength}, longitude {o.AirportRecordLongitude}, latitude {o.AirportRecordLatitude}), " +
            $"waypoints {o.Waypoints} (longitude {o.WaypointLongitude}, latitude {o.WaypointLatitude}, ident {o.WaypointIdent}), " +
            $"ndbs {o.Ndbs} (ident {o.NdbIdent}, longitude {o.NdbLongitude}, latitude {o.NdbLatitude}), " +
            $"fmsToVor {o.FmsToVorLeft}/{o.FmsToVorRight}, panel autotune {o.PanelAutotune} station {o.PanelStation} " +
            $"course {o.PanelCourse} manual {o.PanelManualWhole}/{o.PanelManualFraction}, drawnKinds {A(o.DrawnKinds)}, " +
            $"courseLineKinds {A(o.CourseLineKinds)}, kinds vor {o.KindVor} vorDme {o.KindVorDme} vortac {o.KindVortac} " +
            $"tacan {o.KindTacan} dme {o.KindDme}, " +
            $"highClass {o.HighClass} above {D(o.HighClassAbove)}, ndbsAbove {D(o.NdbsAbove)}, ndbDistance {D(o.NdbDistance)}, " +
            $"roundLimit {o.RoundLimit}, maxItems {o.MaxItems}");
        Part(lines, p, "TERR, wind and runways", l.MapExtras, l.MapExtrasMissing, x =>
            $"terrHandle {x.TerrHandle}, handleValue {x.HandleValue}, terrainPicture {x.TerrainPicture}, " +
            $"displaySide {x.DisplaySide}, captainSide {x.CaptainSide}, terrainStatic {H(x.TerrainStatic)}, " +
            $"terrainRecords {x.TerrainRecords} stride {x.TerrainSideStride} (highest {x.TerrainHighest}, lowest {x.TerrainLowest}), " +
            $"terrainDivisor {x.TerrainDivisor}, sourceToAdc {x.SourceToAdc}, adc altitude {x.AdcAltitude} " +
            $"verticalSpeed {x.AdcVerticalSpeed}, redAbove {D(x.RedAbove)}, amberAbove {D(x.AmberAbove)} " +
            $"({D(x.AmberAboveGearDown)} gear down {D(x.GearDown)}), lookAhead below {D(x.LookAheadBelow)} " +
            $"seconds {D(x.LookAheadSeconds)} floor {D(x.LookAheadFloor)}, windShown {H(x.WindShown)}, " +
            $"runways {x.DepartureRunway}/{x.ArrivalRunway} " +
            $"(first {x.RunwayFirstLongitude}/{x.RunwayFirstLatitude}, second {x.RunwaySecondLongitude}/{x.RunwaySecondLatitude}, " +
            $"name {x.RunwayName}), deceleration {x.DecelerationStart}/{x.DecelerationEnd} stride {x.DecelerationStride} " +
            $"(longitude {x.DecelerationLongitude}, latitude {x.DecelerationLatitude}) max {x.MaxDecelerationPoints}, " +
            $"legCapabilities first {x.LegCapabilityFirst} count {x.LegCapabilityCount} node next {x.CapabilityNodeNext} " +
            $"value {x.CapabilityNodeValue} max {x.MaxCapabilities}, capabilityLegKind {x.CapabilityLegKind}, " +
            $"capabilityChoices [{string.Join(", ", x.CapabilityChoices.Select(c => $"{c.Capability}:{c.Latitude}/{c.Longitude}{(c.UnlessZero ? " unless zero" : "")}"))}], " +
            $"words {Q(x.TerrWord)} {Q(x.NoWindWord)}");
        Part(lines, p, "FIX, descent and altitude guides", l.MapGuides, l.MapGuidesMissing, g =>
            $"ndToFms {g.NdToFms}, fix on {g.FmsFix1On}/{g.FmsFix2On}, fix {g.FmsFix1}/{g.FmsFix2}, " +
            $"entries {g.FmsFix1Entries}/{g.FmsFix2Entries}, fixEntryStride {g.FixEntryStride} (radial {g.FixEntryRadial}, " +
            $"distance {g.FixEntryDistance}, none {g.FixEntryNone}), fixEntries {g.FixEntries}, legName {g.LegName}, " +
            $"legNavaidName {g.LegNavaidName}, navaidLegKinds {A(g.NavaidLegKinds)}, " +
            $"radial start {D(g.RadialStart)} dashes {g.RadialDashes} step {D(g.RadialDashStep)} length {D(g.RadialDashLength)}, " +
            $"offpath on {g.OffpathOn} valid {g.OffpathValidA}/{g.OffpathValidB} latitude {g.OffpathLatitude} " +
            $"longitude {g.OffpathLongitude} radii {g.OffpathCleanRadius}/{g.OffpathSpeedbrakeRadius}, " +
            $"mcpAltitude {g.McpAltitude}, sourceToAdc {g.SourceToAdc}, adc indicatedAltitude {g.AdcIndicatedAltitude} " +
            $"verticalSpeed {g.AdcVerticalSpeed}, ndToAirGround {g.NdToAirGround}, airGroundOnGround {g.AirGroundOnGround}, " +
            $"irsTrueTrack {g.IrsTrueTrack}, arc minimums {D(g.ArcMinAltitudeDifference)} ft {D(g.ArcMinVerticalSpeed)} ft/min, " +
            $"pathPhase {g.PathPhase} descent {g.DescentPhase}, pathDeviation {g.PathDeviation}, deviationClamp {D(g.DeviationClamp)}");
        return lines;
    }

    public static IReadOnlyList<string> DescribeAdi(Bbs757AdiLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string D(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        string A(IEnumerable<int> v) => "[" + string.Join(", ", v.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";
        string p = $"file analysis: {l.SourceFile}: ADI";
        var lines = new List<string>
        {
            $"{p}: manager {H(l.ManagerBase)} span {l.ManagerSpan}, pfd vptr {H(l.PfdVptr)}, " +
            $"powered {l.Powered}, failed {l.Failed}, blanked {l.Blanked}, " +
            $"pfdToSource {l.PfdToSource}, sourceToIrs {l.SourceToIrs}, sourceToAdc {l.SourceToAdc}, sourceToRa {l.SourceToRa}, " +
            $"devicePowered {l.DevicePowered}, deviceFailed {l.DeviceFailed}, " +
            $"pfdToMcp {l.PfdToMcp}, pfdToAfds {l.PfdToAfds}, pfdToFms {l.PfdToFms}, ilsBlock {l.IlsBlock}, adcBlock {l.AdcBlock}",
        };
        Part(lines, p, "attitude", l.Attitude, l.AttitudeMissing, a =>
            $"irsAttitude {a.IrsAttitude}, irsMode {a.IrsMode}, irsModeNav {a.IrsModeNav}, irsState {a.IrsState}, " +
            $"irsStateValid {a.IrsStateValid}, irsStateAligning {a.IrsStateAligning}, attWord {Q(a.AttWord)}");
        Part(lines, p, "speed tape", l.Speed, l.SpeedMissing, s =>
            $"adcAirspeed {s.AdcAirspeed}, adcAltitude {s.AdcAltitude}, minAirspeed {D(s.MinAirspeed)}, mcpSpeed {s.McpSpeed}, " +
            $"afdsPitchMode {s.AfdsPitchMode}, vnavModes {A(s.VnavModes)}, afdsSpeedIntervene {s.AfdsSpeedIntervene}, " +
            $"fmsV1 {s.FmsV1}, fmsVr {s.FmsVr}, fmsV1Inop {s.FmsV1Inop}, afdsTimer {s.AfdsTimer}, timerStart {s.TimerStart}, " +
            $"timerStopped {s.TimerStopped}, clock {H(s.Clock)}, bugSeconds {D(s.BugSeconds)}, " +
            $"flapSpeedAfterSeconds {D(s.FlapSpeedAfterSeconds)}, afdsFlapSpeed {s.AfdsFlapSpeed}, noFlapSpeed {D(s.NoFlapSpeed)}, " +
            $"flapSpeedMaxAltitude {D(s.FlapSpeedMaxAltitude)}, flapSpeedNearVr {D(s.FlapSpeedNearVr)}, " +
            $"tapeHalfRange {D(s.TapeHalfRange)}, machShownFrom {D(s.MachShownFrom)}, machShownUntil {D(s.MachShownUntil)}, " +
            $"spdWord {Q(s.SpdWord)}, v1Word {Q(s.V1Word)}, inopWord {Q(s.InopWord)}, v1BugWord {Q(s.V1BugWord)}, " +
            $"vrBugWord {Q(s.VrBugWord)}, flapBugWord {Q(s.FlapBugWord)}, groundSpeedWord {Q(s.GroundSpeedWord)}, " +
            $"noMachWord {Q(s.NoMachWord)}");
        Part(lines, p, "localizer and glideslope", l.Ils, l.IlsMissing, i =>
            $"locPointer {i.LocPointer}, locDots {i.LocDots}, gsPointer {i.GsPointer}, locScale {i.LocScale}, gsScale {i.GsScale}, " +
            $"locValid {i.LocValid}, gsValid {i.GsValid}, expanded {i.Expanded}, locFlag {i.LocFlag}, gsFlag {i.GsFlag}, " +
            $"test {i.Test}, afdsRollMode {i.AfdsRollMode}, runwayModes {A(i.RunwayModes)}, runwayBelow {D(i.RunwayBelow)}, " +
            $"locWord {Q(i.LocWord)}, glideslopeWord {Q(i.GlideslopeWord)}");
        Part(lines, p, "radio altitude and decision height", l.RadioAltitude, l.RadioAltitudeMissing, r =>
            $"raAltitude {r.RaAltitude}, raOffset {D(r.RaOffset)}, raTest {r.RaTest}, raTestState {r.RaTestState}, " +
            $"raTestAltitude {r.RaTestAltitude}, raTestAlone {A(r.RaTestAlone)}, raTestAdded {r.RaTestAdded}, " +
            $"raMaxShown {D(r.RaMaxShown)}, raRoundFrom {D(r.RaRoundFrom)}, dhValue {r.DhValue}, dhShown {r.DhShown}, " +
            $"adcVerticalSpeed {r.AdcVerticalSpeed}, altDescent {D(r.AltDescent)}, altFrom {D(r.AltFrom)}, " +
            $"dhWord {Q(r.DhWord)}, raWord {Q(r.RaWord)}, altWord {Q(r.AltWord)}");
        Part(lines, p, "flight director", l.FlightDirector, l.FlightDirectorMissing, d =>
            $"fdCaptain {d.FdCaptain}, fdPitch {d.FdPitch}, fdRoll {d.FdRoll}");
        Part(lines, p, "TCAS", l.Tcas, l.TcasMissing, t =>
            $"tcasAbove {t.TcasAbove}, tcasBelow {t.TcasBelow}, tcasAbovePitch {t.TcasAbovePitch}, tcasBelowPitch {t.TcasBelowPitch}");
        Part(lines, p, "reference speeds", l.ReferenceSpeeds, l.ReferenceSpeedsMissing, v =>
            $"fmsVref30 {v.FmsVref30}, fmsVref25 {v.FmsVref25}");
        return lines;
    }

    /// <summary>One line for an optional part of the EHSI or the ADI: its fields, or why it is missing.</summary>
    private static void Part<T>(List<string> lines, string prefix, string name, T? part, string? missing, Func<T, string> fields)
        where T : class =>
        lines.Add(part is not null ? $"{prefix} {name}: {fields(part)}" : $"{prefix}: {name} missing: {missing}");

    /// <summary>A word in quotes, so its leading spaces show.</summary>
    private static string Q(string word) => "\"" + word + "\"";

    private static void Words(List<string> lines, string file, string column, Bbs757WordTable table, string display)
    {
        string body = string.Join(", ", table.Words.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        lines.Add($"file analysis: {file}: {display} {column} words: {body}");
    }

    public static IReadOnlyList<string> DescribeEfb(Bbs757EfbMemLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string classes = string.Join(", ", l.ClassVptrs.Select(c => $"{c.Kind}={H(c.Vptr)}"));
        return new List<string>
        {
            $"file analysis: {l.SourceFile}: EFB: manager {H(l.ManagerBase)} span {l.ManagerSpan}, efb vptr {H(l.EfbVptr)}, " +
            $"efb->bbos {l.EfbToBbos}, bbos->appMap {l.BbosToAppMap}",
            $"file analysis: {l.SourceFile}: EFB app fields: front {l.AppFront}, page {l.AppPage}, roots {l.AppRoots}, " +
            $"pageChildren {l.PageChildren}",
            $"file analysis: {l.SourceFile}: EFB control fields: name {l.CtrlName}, rect {l.CtrlRect}, visible {l.CtrlVisible}, " +
            $"children {l.CtrlChildren}, parent {l.CtrlParent}, labelText {l.CtrlLabelText}, buttonText {l.CtrlButtonText}, scrollboxOffset {l.ScrollboxOffset}, " +
            $"sliderKnob {l.SliderKnob}",
            $"file analysis: {l.SourceFile}: EFB phase 2 fields: textboxLabel {l.TextboxLabel}, multilineText {l.MultilineText}, " +
            $"pulldownFace {l.PulldownFace}, pulldownList {l.PulldownList}, pulldownEntries {l.PulldownEntries}, " +
            $"pulldownEntryText {l.PulldownEntryText}, pulldownOpen {l.PulldownOpen}, buttonSelectable {l.ButtonSelectable}, " +
            $"buttonUnselected {l.ButtonUnselected}, bbosModalOpen {l.BbosModalOpen}",
            $"file analysis: {l.SourceFile}: EFB notices: bbosNotices {l.BbosNotices}, noticeStride {l.NoticeStride}, " +
            $"noticeId {l.NoticeId}, noticeTitle {l.NoticeTitle}, noticeText {l.NoticeText}, noticeTime {l.NoticeTime}, " +
            $"noticeUnread {l.NoticeUnread}",
            $"file analysis: {l.SourceFile}: EFB notification panel: overlayPanelShown {l.OverlayPanelShown}, " +
            $"overlayListOpen {l.OverlayListOpen}, overlayPanelRect {l.OverlayPanelRect}, overlayPanelHeight {l.OverlayPanelHeight}, " +
            $"overlayFlightTimeText {l.OverlayFlightTimeText}",
            $"file analysis: {l.SourceFile}: EFB boarding box: overlayBoardingRect {l.OverlayBoardingRect}, bbosLoadState {l.BbosLoadState}, " +
            $"bbosLoadDeboarding {l.BbosLoadDeboarding}, bbosLoadFlown {l.BbosLoadFlown}, bbosLoadRemaining {l.BbosLoadRemaining}",
            $"file analysis: {l.SourceFile}: EFB keyboard: keyboardShown {l.KeyboardShown}, keyboardReady {l.KeyboardReady}, " +
            $"keyboardKeypad {l.KeyboardKeypad}, keyboardTarget {l.KeyboardTarget}, keypadRect {l.KeypadRect}, " +
            $"keyboardNumeric {l.KeyboardNumeric}, keyboardShift {l.KeyboardShift}, keyboardCaps {l.KeyboardCaps}, " +
            $"textboxCaret {l.TextboxCaret}, textboxMaxLength {l.TextboxMaxLength}, textboxKeypad {l.TextboxKeypad}, toggleOn {l.ToggleOn}",
            $"file analysis: {l.SourceFile}: EFB classes: {classes}",
        };
    }
}
