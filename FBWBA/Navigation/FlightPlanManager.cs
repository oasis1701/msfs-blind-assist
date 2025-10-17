using System;
using System.Collections.Generic;
using System.Linq;
using FBWBA.Database;
using FBWBA.Database.Models;
using FBWBA.Services;
using FBWBA.SimConnect;

namespace FBWBA.Navigation
{
    /// <summary>
    /// Manages flight plan loading, updating, and position calculations
    /// </summary>
    public class FlightPlanManager
    {
        private readonly SimBriefService _simbriefService;
        private readonly NavigationDatabaseProvider _navigationDatabase;
        private readonly IAirportDataProvider _airportDatabase;

        public FlightPlan CurrentFlightPlan { get; private set; }

        public event EventHandler<string> StatusChanged;
        public event EventHandler<FlightPlan> FlightPlanUpdated;

        public FlightPlanManager(string navigationDatabasePath, IAirportDataProvider airportDatabase)
        {
            _navigationDatabase = new NavigationDatabaseProvider(navigationDatabasePath);
            _simbriefService = new SimBriefService(_navigationDatabase);
            _airportDatabase = airportDatabase;
            CurrentFlightPlan = new FlightPlan();
        }

        /// <summary>
        /// Loads flight plan from SimBrief
        /// </summary>
        public void LoadFromSimBrief(string username)
        {
            try
            {
                StatusChanged?.Invoke(this, "Fetching flight plan from SimBrief...");

                CurrentFlightPlan = _simbriefService.FetchFlightPlan(username);

                // Calculate leg distances for all waypoints after loading
                CurrentFlightPlan.CalculateLegDistances();

                StatusChanged?.Invoke(this, $"Loaded flight plan: {CurrentFlightPlan.GetSummary()}");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading from SimBrief: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads departure airport and runway (Section A)
        /// </summary>
        public void LoadDeparture(string icao, string runwayId)
        {
            try
            {
                var airport = _airportDatabase.GetAirport(icao);
                if (airport == null)
                    throw new Exception($"Airport {icao} not found");

                var waypoints = new List<WaypointFix>();

                // Add airport waypoint
                waypoints.Add(new WaypointFix
                {
                    Ident = icao,
                    Name = airport.Name,
                    Type = "Airport",
                    Latitude = airport.Latitude,
                    Longitude = airport.Longitude,
                    Altitude = (int)airport.Altitude,
                    InboundAirway = "ORIGIN",
                    Section = FlightPlanSection.DepartureAirport
                });

                // Add runway waypoint if specified
                if (!string.IsNullOrEmpty(runwayId))
                {
                    waypoints.Add(new WaypointFix
                    {
                        Ident = $"RW{runwayId}",
                        Name = $"{icao} Runway {runwayId}",
                        Type = "Runway",
                        Latitude = airport.Latitude,
                        Longitude = airport.Longitude,
                        Altitude = (int)airport.Altitude,
                        InboundAirway = "DEPART",
                        Section = FlightPlanSection.DepartureAirport
                    });
                }

                CurrentFlightPlan.DepartureICAO = icao;
                CurrentFlightPlan.DepartureRunway = runwayId;
                CurrentFlightPlan.UpdateSection(FlightPlanSection.DepartureAirport, waypoints);

                StatusChanged?.Invoke(this, $"Loaded departure: {icao} Runway {runwayId}");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading departure: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads arrival airport and runway (Section F)
        /// </summary>
        public void LoadArrival(string icao, string runwayId)
        {
            try
            {
                var airport = _airportDatabase.GetAirport(icao);
                if (airport == null)
                    throw new Exception($"Airport {icao} not found");

                var waypoints = new List<WaypointFix>();

                // Add runway waypoint if specified
                if (!string.IsNullOrEmpty(runwayId))
                {
                    waypoints.Add(new WaypointFix
                    {
                        Ident = $"RW{runwayId}",
                        Name = $"{icao} Runway {runwayId}",
                        Type = "Runway",
                        Latitude = airport.Latitude,
                        Longitude = airport.Longitude,
                        Altitude = (int)airport.Altitude,
                        InboundAirway = "ARRIVAL",
                        Section = FlightPlanSection.ArrivalAirport
                    });
                }

                // Add airport waypoint
                waypoints.Add(new WaypointFix
                {
                    Ident = icao,
                    Name = airport.Name,
                    Type = "Airport",
                    Latitude = airport.Latitude,
                    Longitude = airport.Longitude,
                    Altitude = (int)airport.Altitude,
                    InboundAirway = "DESTINATION",
                    Section = FlightPlanSection.ArrivalAirport
                });

                CurrentFlightPlan.ArrivalICAO = icao;
                CurrentFlightPlan.ArrivalRunway = runwayId;
                CurrentFlightPlan.UpdateSection(FlightPlanSection.ArrivalAirport, waypoints);

                StatusChanged?.Invoke(this, $"Loaded arrival: {icao} Runway {runwayId}");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading arrival: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads SID procedure (Section B)
        /// </summary>
        public void LoadSID(int sidId, int? transitionId, string sidName)
        {
            try
            {
                var waypoints = new List<WaypointFix>();

                // Load SID waypoints
                var sidWaypoints = _navigationDatabase.GetSIDWaypoints(sidId);
                waypoints.AddRange(sidWaypoints);

                // Load transition waypoints if specified
                if (transitionId.HasValue)
                {
                    var transitionWaypoints = _navigationDatabase.GetTransitionWaypoints(transitionId.Value);
                    foreach (var wpt in transitionWaypoints)
                    {
                        wpt.Section = FlightPlanSection.SID;
                        wpt.InboundAirway = "SID";
                    }
                    waypoints.AddRange(transitionWaypoints);
                }

                CurrentFlightPlan.SIDName = sidName;
                CurrentFlightPlan.UpdateSection(FlightPlanSection.SID, waypoints);

                StatusChanged?.Invoke(this, $"Loaded SID: {sidName} ({waypoints.Count} waypoints)");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading SID: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads STAR procedure (Section D)
        /// </summary>
        public void LoadSTAR(int starId, int? transitionId, string starName)
        {
            try
            {
                var waypoints = new List<WaypointFix>();

                // Load transition waypoints if specified (transitions come first for STARs)
                if (transitionId.HasValue)
                {
                    var transitionWaypoints = _navigationDatabase.GetTransitionWaypoints(transitionId.Value);
                    foreach (var wpt in transitionWaypoints)
                    {
                        wpt.Section = FlightPlanSection.STAR;
                        wpt.InboundAirway = "STAR";
                    }
                    waypoints.AddRange(transitionWaypoints);
                }

                // Load STAR waypoints
                var starWaypoints = _navigationDatabase.GetSTARWaypoints(starId);
                waypoints.AddRange(starWaypoints);

                CurrentFlightPlan.STARName = starName;
                CurrentFlightPlan.UpdateSection(FlightPlanSection.STAR, waypoints);

                StatusChanged?.Invoke(this, $"Loaded STAR: {starName} ({waypoints.Count} waypoints)");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading STAR: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads approach procedure (Section E)
        /// </summary>
        public void LoadApproach(int approachId, int? transitionId, string approachName)
        {
            try
            {
                var waypoints = new List<WaypointFix>();

                // Load transition waypoints if specified
                if (transitionId.HasValue)
                {
                    var transitionWaypoints = _navigationDatabase.GetTransitionWaypoints(transitionId.Value);
                    waypoints.AddRange(transitionWaypoints);
                }

                // Load approach waypoints
                var approachWaypoints = _navigationDatabase.GetApproachWaypoints(approachId);
                waypoints.AddRange(approachWaypoints);

                // Set section and airway info
                for (int i = 0; i < waypoints.Count; i++)
                {
                    waypoints[i].Section = FlightPlanSection.Approach;
                    if (string.IsNullOrEmpty(waypoints[i].InboundAirway))
                    {
                        waypoints[i].InboundAirway = i == 0 ? "APPROACH" : "PROC";
                    }
                }

                CurrentFlightPlan.ApproachName = approachName;
                CurrentFlightPlan.UpdateSection(FlightPlanSection.Approach, waypoints);

                StatusChanged?.Invoke(this, $"Loaded approach: {approachName} ({waypoints.Count} waypoints)");
                FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Error loading approach: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates distance and bearing for all waypoints based on aircraft position
        /// </summary>
        public void UpdateAircraftPosition(double latitude, double longitude, double magneticVariation)
        {
            if (CurrentFlightPlan == null || CurrentFlightPlan.IsEmpty())
                return;

            var allWaypoints = CurrentFlightPlan.GetAllWaypoints();

            foreach (var waypoint in allWaypoints)
            {
                // Calculate distance in nautical miles
                waypoint.DistanceFromAircraft = NavigationCalculator.CalculateDistance(
                    latitude, longitude, waypoint.Latitude, waypoint.Longitude);

                // Calculate magnetic bearing
                waypoint.BearingFromAircraft = NavigationCalculator.CalculateMagneticBearing(
                    latitude, longitude, waypoint.Latitude, waypoint.Longitude, magneticVariation);
            }

            FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
        }

        /// <summary>
        /// Clears the current flight plan
        /// </summary>
        public void ClearFlightPlan()
        {
            CurrentFlightPlan = new FlightPlan();
            StatusChanged?.Invoke(this, "Flight plan cleared");
            FlightPlanUpdated?.Invoke(this, CurrentFlightPlan);
        }

        /// <summary>
        /// Gets all unique SID procedures for an airport
        /// </summary>
        public List<(string name, string fixIdent)> GetSIDs(string icao)
        {
            return _navigationDatabase.GetSIDs(icao);
        }

        /// <summary>
        /// Gets all unique STAR procedures for an airport
        /// </summary>
        public List<(string name, string fixIdent)> GetSTARs(string icao)
        {
            return _navigationDatabase.GetSTARs(icao);
        }

        /// <summary>
        /// Gets all runways that have SID departures for an airport
        /// </summary>
        public List<string> GetRunwaysForSIDs(string icao)
        {
            return _navigationDatabase.GetRunwaysForSIDs(icao);
        }

        /// <summary>
        /// Gets all SID procedures available for a specific runway
        /// </summary>
        public List<(string sidName, string fixIdent, int approachId)> GetSIDsForRunway(string icao, string runwayName)
        {
            return _navigationDatabase.GetSIDsForRunway(icao, runwayName);
        }

        /// <summary>
        /// Gets all runways that have STAR arrivals for an airport
        /// </summary>
        public List<string> GetRunwaysForSTARs(string icao)
        {
            return _navigationDatabase.GetRunwaysForSTARs(icao);
        }

        /// <summary>
        /// Gets all STAR procedures available for a specific runway
        /// </summary>
        public List<(string starName, string fixIdent, int approachId)> GetSTARsForRunway(string icao, string runwayName)
        {
            return _navigationDatabase.GetSTARsForRunway(icao, runwayName);
        }

        /// <summary>
        /// Gets all approaches for an airport
        /// </summary>
        public List<(string name, string suffix, int id)> GetApproaches(string icao)
        {
            return _navigationDatabase.GetApproaches(icao);
        }

        /// <summary>
        /// Gets transitions for an approach
        /// </summary>
        public List<(string name, int id)> GetTransitions(int approachId)
        {
            return _navigationDatabase.GetTransitions(approachId);
        }

        /// <summary>
        /// Gets runways for an airport
        /// </summary>
        public List<Runway> GetRunways(string icao)
        {
            return _airportDatabase.GetRunways(icao);
        }

        /// <summary>
        /// Gets airport information
        /// </summary>
        public Airport GetAirport(string icao)
        {
            return _airportDatabase.GetAirport(icao);
        }
    }
}
