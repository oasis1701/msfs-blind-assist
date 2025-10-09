using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Xml;
using FBWBA.Database.Models;

namespace FBWBA.Database
{
    public class DatabaseBuilder
    {
        private readonly AirportDatabase _database;

        public event EventHandler<string> ProgressUpdate;

        public DatabaseBuilder(AirportDatabase database)
        {
            _database = database;
        }

        public void BuildDatabase(string makeRwysFolder)
        {
            if (!Directory.Exists(makeRwysFolder))
                throw new DirectoryNotFoundException($"MakeRwys folder not found: {makeRwysFolder}");

            string runwaysXmlPath = Path.Combine(makeRwysFolder, "runways.xml");
            string gatesCsvPath = Path.Combine(makeRwysFolder, "g5.csv");

            if (!File.Exists(runwaysXmlPath))
                throw new FileNotFoundException($"runways.xml not found in {makeRwysFolder}");

            if (!File.Exists(gatesCsvPath))
                throw new FileNotFoundException($"g5.csv not found in {makeRwysFolder}");

            OnProgressUpdate("Initializing database...");
            _database.InitializeDatabase();

            OnProgressUpdate("Clearing existing data...");
            _database.ClearDatabase();

            // Use optimized connection for all operations
            using (var connection = _database.CreateOptimizedConnection())
            {
                OnProgressUpdate("Processing airports and runways from runways.xml...");
                ProcessRunwaysXml(runwaysXmlPath, connection);

                OnProgressUpdate("Processing gates and parking spots from g5.csv...");
                ProcessGatesCsv(gatesCsvPath, connection);
            }

            OnProgressUpdate("Database build completed successfully.");
        }

        private void ProcessRunwaysXml(string xmlPath, SQLiteConnection connection)
        {
            var doc = new XmlDocument();
            using (var sanitizedStream = SanitizeXmlFile(xmlPath))
            {
                var settings = new XmlReaderSettings
                {
                    CheckCharacters = false
                };
                using (var reader = XmlReader.Create(sanitizedStream, settings))
                {
                    doc.Load(reader);
                }
            }

            var airportNodes = doc.SelectNodes("//ICAO");
            int totalAirports = airportNodes.Count;
            int processedAirports = 0;
            int batchSize = 1000; // Process in batches of 1000 airports

            OnProgressUpdate($"Found {totalAirports} airports to process...");

            for (int i = 0; i < totalAirports; i += batchSize)
            {
                using (var transaction = _database.BeginTransaction(connection))
                {
                    try
                    {
                        int batchEnd = Math.Min(i + batchSize, totalAirports);

                        for (int j = i; j < batchEnd; j++)
                        {
                            XmlNode airportNode = airportNodes[j];

                            try
                            {
                                var icao = airportNode.Attributes["id"]?.Value;
                                if (string.IsNullOrEmpty(icao)) continue;

                                // Parse airport data
                                var airport = new Airport
                                {
                                    ICAO = icao,
                                    Name = GetNodeText(airportNode, "ICAOName"),
                                    City = GetNodeText(airportNode, "City"),
                                    Country = GetNodeText(airportNode, "Country"),
                                    Latitude = ParseDouble(GetNodeText(airportNode, "Latitude")),
                                    Longitude = ParseDouble(GetNodeText(airportNode, "Longitude")),
                                    Altitude = ParseDouble(GetNodeText(airportNode, "Altitude")),
                                    MagVar = ParseDouble(GetNodeText(airportNode, "MagVar"))
                                };

                                _database.InsertAirport(airport, connection, transaction);

                                // Process runways for this airport
                                var runwayNodes = airportNode.SelectNodes("Runway");
                                foreach (XmlNode runwayNode in runwayNodes)
                                {
                                    try
                                    {
                                        var runway = new Runway
                                        {
                                            AirportICAO = icao,
                                            RunwayID = runwayNode.Attributes["id"]?.Value ?? "Unknown",
                                            Length = ParseDouble(GetNodeText(runwayNode, "Len")),
                                            Heading = ParseDouble(GetNodeText(runwayNode, "Hdg")),
                                            HeadingMag = ParseDouble(GetNodeText(runwayNode, "Hdg")), // Using Hdg as HeadingMag since it's magnetic in XML
                                            StartLat = ParseDouble(GetNodeText(runwayNode, "Lat")),
                                            StartLon = ParseDouble(GetNodeText(runwayNode, "Lon")),
                                            EndLat = 0, // Will need to calculate if needed
                                            EndLon = 0, // Will need to calculate if needed
                                            Width = 0, // Not available in XML
                                            Surface = GetSurfaceType(GetNodeText(runwayNode, "Def")),
                                            ILSFreq = ParseDouble(GetNodeText(runwayNode, "ILSFreq")),
                                            ILSHeading = ParseDouble(GetNodeText(runwayNode, "ILSHdg")),
                                            ThresholdOffset = ParseDouble(GetNodeText(runwayNode, "ThresholdOffset"))
                                        };

                                        _database.InsertRunway(runway, connection, transaction);
                                    }
                                    catch (Exception ex)
                                    {
                                        OnProgressUpdate($"Error processing runway for {icao}: {ex.Message}");
                                    }
                                }

                                processedAirports++;
                            }
                            catch (Exception ex)
                            {
                                OnProgressUpdate($"Error processing airport {airportNode.Attributes?["id"]?.Value}: {ex.Message}");
                            }
                        }

                        transaction.Commit();
                        OnProgressUpdate($"Processed {processedAirports}/{totalAirports} airports...");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        OnProgressUpdate($"Error in batch starting at airport {i}: {ex.Message}");
                        throw;
                    }
                }
            }

            OnProgressUpdate($"Processed {processedAirports} airports with runways.");
        }

        private void ProcessGatesCsv(string csvPath, SQLiteConnection connection)
        {
            var lines = File.ReadAllLines(csvPath);
            int totalLines = lines.Length;
            int processedGates = 0;
            int batchSize = 5000; // Process in larger batches for parking spots

            OnProgressUpdate($"Found {totalLines} parking spot lines to process...");

            // Get all airport ICAOs once for fast lookup
            var validAirports = _database.GetAllAirportICAOs(connection);
            OnProgressUpdate($"Loaded {validAirports.Count} valid airports for validation...");

            for (int i = 0; i < totalLines; i += batchSize)
            {
                using (var transaction = _database.BeginTransaction(connection))
                {
                    try
                    {
                        int batchEnd = Math.Min(i + batchSize, totalLines);

                        for (int j = i; j < batchEnd; j++)
                        {
                            var line = lines[j];

                            try
                            {
                                var parts = line.Split(',');
                                if (parts.Length < 8) continue;

                                var parkingSpot = new ParkingSpot
                                {
                                    AirportICAO = parts[0].Trim(),
                                    Name = parts[1].Trim(),
                                    Number = ParseInt(parts[2].Trim()),
                                    Latitude = ParseDouble(parts[3].Trim()),
                                    Longitude = ParseDouble(parts[4].Trim()),
                                    Radius = ParseDouble(parts[5].Trim()),
                                    Heading = ParseDouble(parts[6].Trim()),
                                    Type = ParseInt(parts[7].Trim())
                                };

                                // Only insert if we have a valid airport (using cached lookup)
                                if (!string.IsNullOrEmpty(parkingSpot.AirportICAO) &&
                                    validAirports.Contains(parkingSpot.AirportICAO))
                                {
                                    _database.InsertParkingSpot(parkingSpot, connection, transaction);
                                    processedGates++;
                                }
                            }
                            catch (Exception ex)
                            {
                                OnProgressUpdate($"Error processing gate line: {line} - {ex.Message}");
                            }
                        }

                        transaction.Commit();
                        OnProgressUpdate($"Processed {processedGates} gates/parking spots ({batchEnd}/{totalLines} lines)...");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        OnProgressUpdate($"Error in parking spot batch starting at line {i}: {ex.Message}");
                        throw;
                    }
                }
            }

            OnProgressUpdate($"Processed {processedGates} gates and parking spots.");
        }

        private string GetNodeText(XmlNode parentNode, string nodeName)
        {
            var node = parentNode.SelectSingleNode(nodeName);
            return node?.InnerText ?? string.Empty;
        }

        private double ParseDouble(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0.0;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return result;

            return 0.0;
        }

        private int ParseInt(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            if (int.TryParse(value, out int result))
                return result;

            return 0;
        }

        private int GetSurfaceType(string surfaceText)
        {
            if (string.IsNullOrEmpty(surfaceText)) return 3; // Unknown

            switch (surfaceText.ToLower())
            {
                case "concrete": return 0;
                case "grass": return 1;
                case "water": return 2;
                case "asphalt": return 4;
                case "clay": return 7;
                case "snow": return 8;
                case "ice": return 9;
                case "dirt": return 12;
                case "coral": return 13;
                case "gravel": return 14;
                case "oil-treated": return 15;
                case "mats": return 16;
                case "bituminous": return 17;
                case "brick": return 18;
                case "macadam": return 19;
                case "planks": return 20;
                case "sand": return 21;
                case "shale": return 22;
                case "tarmac": return 23;
                default: return 3; // Unknown
            }
        }

        private MemoryStream SanitizeXmlFile(string xmlPath)
        {
            // Read the entire file as bytes
            byte[] fileBytes = File.ReadAllBytes(xmlPath);

            // Create a new array to hold sanitized bytes
            var sanitized = new List<byte>(fileBytes.Length);

            // Remove invalid XML characters
            // Valid: 0x09 (tab), 0x0A (newline), 0x0D (carriage return), 0x20-0xFF
            // Invalid: 0x00-0x08, 0x0B-0x0C, 0x0E-0x1F
            foreach (byte b in fileBytes)
            {
                if (b == 0x09 || b == 0x0A || b == 0x0D || b >= 0x20)
                {
                    sanitized.Add(b);
                }
                // Skip invalid characters (they're simply not added to the sanitized list)
            }

            // Return as a MemoryStream for XML reading
            return new MemoryStream(sanitized.ToArray());
        }

        private void OnProgressUpdate(string message)
        {
            ProgressUpdate?.Invoke(this, message);
        }

        public DatabaseStats GetDatabaseStats()
        {
            return new DatabaseStats
            {
                AirportCount = _database.GetAirportCount(),
                RunwayCount = _database.GetRunwayCount(),
                ParkingSpotCount = _database.GetParkingSpotCount()
            };
        }
    }

    public class DatabaseStats
    {
        public int AirportCount { get; set; }
        public int RunwayCount { get; set; }
        public int ParkingSpotCount { get; set; }

        public override string ToString()
        {
            return $"Airports: {AirportCount}, Runways: {RunwayCount}, Parking Spots: {ParkingSpotCount}";
        }
    }
}