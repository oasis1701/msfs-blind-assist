using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using FBWBA.Database.Models;

namespace FBWBA.Database
{
    public class AirportDatabase
    {
        private readonly string _connectionString;
        private readonly string _databasePath;

        public AirportDatabase(string databasePath = null)
        {
            _databasePath = databasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "airports.db");
            _connectionString = $"Data Source={_databasePath};Version=3;";
        }

        public bool DatabaseExists => File.Exists(_databasePath);

        public void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                // Create Airports table
                var createAirportsTable = @"
                    CREATE TABLE IF NOT EXISTS Airports (
                        ICAO TEXT PRIMARY KEY,
                        Name TEXT,
                        City TEXT,
                        Country TEXT,
                        Latitude REAL,
                        Longitude REAL,
                        Altitude REAL,
                        MagVar REAL
                    )";

                // Create Runways table
                var createRunwaysTable = @"
                    CREATE TABLE IF NOT EXISTS Runways (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AirportICAO TEXT,
                        RunwayID TEXT,
                        Heading REAL,
                        HeadingMag REAL,
                        StartLat REAL,
                        StartLon REAL,
                        EndLat REAL,
                        EndLon REAL,
                        Length REAL,
                        Width REAL,
                        Surface INTEGER,
                        ILSFreq REAL,
                        ILSHeading REAL,
                        ThresholdOffset REAL,
                        FOREIGN KEY (AirportICAO) REFERENCES Airports(ICAO)
                    )";

                // Create ParkingSpots table
                var createParkingSpotsTable = @"
                    CREATE TABLE IF NOT EXISTS ParkingSpots (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        AirportICAO TEXT,
                        Name TEXT,
                        Number INTEGER,
                        Type INTEGER,
                        Latitude REAL,
                        Longitude REAL,
                        Heading REAL,
                        Radius REAL,
                        FOREIGN KEY (AirportICAO) REFERENCES Airports(ICAO)
                    )";

                // Create indexes for performance
                var createIndexes = @"
                    CREATE INDEX IF NOT EXISTS idx_runway_icao ON Runways(AirportICAO);
                    CREATE INDEX IF NOT EXISTS idx_parking_icao ON ParkingSpots(AirportICAO);
                ";

                using (var command = new SQLiteCommand(createAirportsTable, connection))
                    command.ExecuteNonQuery();

                using (var command = new SQLiteCommand(createRunwaysTable, connection))
                    command.ExecuteNonQuery();

                using (var command = new SQLiteCommand(createParkingSpotsTable, connection))
                    command.ExecuteNonQuery();

                using (var command = new SQLiteCommand(createIndexes, connection))
                    command.ExecuteNonQuery();
            }
        }

        public void ClearDatabase()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand("DELETE FROM ParkingSpots", connection))
                    command.ExecuteNonQuery();

                using (var command = new SQLiteCommand("DELETE FROM Runways", connection))
                    command.ExecuteNonQuery();

                using (var command = new SQLiteCommand("DELETE FROM Airports", connection))
                    command.ExecuteNonQuery();
            }
        }

        public SQLiteConnection CreateOptimizedConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            // Apply performance optimizations
            using (var command = new SQLiteCommand("PRAGMA journal_mode=WAL", connection))
                command.ExecuteNonQuery();

            using (var command = new SQLiteCommand("PRAGMA synchronous=NORMAL", connection))
                command.ExecuteNonQuery();

            using (var command = new SQLiteCommand("PRAGMA cache_size=10000", connection))
                command.ExecuteNonQuery();

            using (var command = new SQLiteCommand("PRAGMA temp_store=MEMORY", connection))
                command.ExecuteNonQuery();

            return connection;
        }

        public SQLiteTransaction BeginTransaction(SQLiteConnection connection)
        {
            return connection.BeginTransaction();
        }

        public void InsertAirport(Airport airport, SQLiteConnection connection = null, SQLiteTransaction transaction = null)
        {
            bool shouldDisposeConnection = false;
            if (connection == null)
            {
                connection = new SQLiteConnection(_connectionString);
                connection.Open();
                shouldDisposeConnection = true;
            }

            try
            {
                var sql = @"INSERT OR REPLACE INTO Airports
                           (ICAO, Name, City, Country, Latitude, Longitude, Altitude, MagVar)
                           VALUES (@ICAO, @Name, @City, @Country, @Latitude, @Longitude, @Altitude, @MagVar)";

                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@ICAO", airport.ICAO);
                    command.Parameters.AddWithValue("@Name", airport.Name);
                    command.Parameters.AddWithValue("@City", airport.City);
                    command.Parameters.AddWithValue("@Country", airport.Country);
                    command.Parameters.AddWithValue("@Latitude", airport.Latitude);
                    command.Parameters.AddWithValue("@Longitude", airport.Longitude);
                    command.Parameters.AddWithValue("@Altitude", airport.Altitude);
                    command.Parameters.AddWithValue("@MagVar", airport.MagVar);

                    command.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldDisposeConnection)
                {
                    connection?.Dispose();
                }
            }
        }

        public void InsertRunway(Runway runway, SQLiteConnection connection = null, SQLiteTransaction transaction = null)
        {
            bool shouldDisposeConnection = false;
            if (connection == null)
            {
                connection = new SQLiteConnection(_connectionString);
                connection.Open();
                shouldDisposeConnection = true;
            }

            try
            {
                var sql = @"INSERT INTO Runways
                           (AirportICAO, RunwayID, Heading, HeadingMag, StartLat, StartLon, EndLat, EndLon,
                            Length, Width, Surface, ILSFreq, ILSHeading, ThresholdOffset)
                           VALUES (@AirportICAO, @RunwayID, @Heading, @HeadingMag, @StartLat, @StartLon, @EndLat, @EndLon,
                                   @Length, @Width, @Surface, @ILSFreq, @ILSHeading, @ThresholdOffset)";

                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@AirportICAO", runway.AirportICAO);
                    command.Parameters.AddWithValue("@RunwayID", runway.RunwayID);
                    command.Parameters.AddWithValue("@Heading", runway.Heading);
                    command.Parameters.AddWithValue("@HeadingMag", runway.HeadingMag);
                    command.Parameters.AddWithValue("@StartLat", runway.StartLat);
                    command.Parameters.AddWithValue("@StartLon", runway.StartLon);
                    command.Parameters.AddWithValue("@EndLat", runway.EndLat);
                    command.Parameters.AddWithValue("@EndLon", runway.EndLon);
                    command.Parameters.AddWithValue("@Length", runway.Length);
                    command.Parameters.AddWithValue("@Width", runway.Width);
                    command.Parameters.AddWithValue("@Surface", runway.Surface);
                    command.Parameters.AddWithValue("@ILSFreq", runway.ILSFreq);
                    command.Parameters.AddWithValue("@ILSHeading", runway.ILSHeading);
                    command.Parameters.AddWithValue("@ThresholdOffset", runway.ThresholdOffset);

                    command.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldDisposeConnection)
                {
                    connection?.Dispose();
                }
            }
        }

        public void InsertParkingSpot(ParkingSpot parkingSpot, SQLiteConnection connection = null, SQLiteTransaction transaction = null)
        {
            bool shouldDisposeConnection = false;
            if (connection == null)
            {
                connection = new SQLiteConnection(_connectionString);
                connection.Open();
                shouldDisposeConnection = true;
            }

            try
            {
                var sql = @"INSERT INTO ParkingSpots
                           (AirportICAO, Name, Number, Type, Latitude, Longitude, Heading, Radius)
                           VALUES (@AirportICAO, @Name, @Number, @Type, @Latitude, @Longitude, @Heading, @Radius)";

                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@AirportICAO", parkingSpot.AirportICAO);
                    command.Parameters.AddWithValue("@Name", parkingSpot.Name);
                    command.Parameters.AddWithValue("@Number", parkingSpot.Number);
                    command.Parameters.AddWithValue("@Type", parkingSpot.Type);
                    command.Parameters.AddWithValue("@Latitude", parkingSpot.Latitude);
                    command.Parameters.AddWithValue("@Longitude", parkingSpot.Longitude);
                    command.Parameters.AddWithValue("@Heading", parkingSpot.Heading);
                    command.Parameters.AddWithValue("@Radius", parkingSpot.Radius);

                    command.ExecuteNonQuery();
                }
            }
            finally
            {
                if (shouldDisposeConnection)
                {
                    connection?.Dispose();
                }
            }
        }

        public Airport GetAirport(string icao)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = "SELECT * FROM Airports WHERE UPPER(ICAO) = UPPER(@ICAO)";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ICAO", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Airport
                            {
                                ICAO = reader["ICAO"].ToString(),
                                Name = reader["Name"].ToString(),
                                City = reader["City"].ToString(),
                                Country = reader["Country"].ToString(),
                                Latitude = Convert.ToDouble(reader["Latitude"]),
                                Longitude = Convert.ToDouble(reader["Longitude"]),
                                Altitude = Convert.ToDouble(reader["Altitude"]),
                                MagVar = Convert.ToDouble(reader["MagVar"])
                            };
                        }
                    }
                }
            }

            return null;
        }

        public List<Runway> GetRunways(string icao)
        {
            var runways = new List<Runway>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = @"SELECT r.*, a.Altitude as AirportAltitude FROM Runways r
                           JOIN Airports a ON r.AirportICAO = a.ICAO
                           WHERE UPPER(r.AirportICAO) = UPPER(@ICAO)
                           ORDER BY r.RunwayID";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ICAO", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            runways.Add(new Runway
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                AirportICAO = reader["AirportICAO"].ToString(),
                                RunwayID = reader["RunwayID"].ToString(),
                                Heading = Convert.ToDouble(reader["Heading"]),
                                HeadingMag = Convert.ToDouble(reader["HeadingMag"]),
                                StartLat = Convert.ToDouble(reader["StartLat"]),
                                StartLon = Convert.ToDouble(reader["StartLon"]),
                                EndLat = Convert.ToDouble(reader["EndLat"]),
                                EndLon = Convert.ToDouble(reader["EndLon"]),
                                Length = Convert.ToDouble(reader["Length"]),
                                Width = Convert.ToDouble(reader["Width"]),
                                Surface = Convert.ToInt32(reader["Surface"]),
                                ILSFreq = Convert.ToDouble(reader["ILSFreq"]),
                                ILSHeading = Convert.ToDouble(reader["ILSHeading"]),
                                ThresholdOffset = Convert.ToDouble(reader["ThresholdOffset"])
                            });
                        }
                    }
                }
            }

            return runways;
        }

        public List<ParkingSpot> GetParkingSpots(string icao)
        {
            var parkingSpots = new List<ParkingSpot>();

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = @"SELECT * FROM ParkingSpots
                           WHERE UPPER(AirportICAO) = UPPER(@ICAO)
                           ORDER BY Name, Number";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ICAO", icao);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parkingSpots.Add(new ParkingSpot
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                AirportICAO = reader["AirportICAO"].ToString(),
                                Name = reader["Name"].ToString(),
                                Number = Convert.ToInt32(reader["Number"]),
                                Type = Convert.ToInt32(reader["Type"]),
                                Latitude = Convert.ToDouble(reader["Latitude"]),
                                Longitude = Convert.ToDouble(reader["Longitude"]),
                                Heading = Convert.ToDouble(reader["Heading"]),
                                Radius = Convert.ToDouble(reader["Radius"])
                            });
                        }
                    }
                }
            }

            return parkingSpots;
        }

        public bool AirportExists(string icao)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM Airports WHERE UPPER(ICAO) = UPPER(@ICAO)";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ICAO", icao);
                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            }
        }

        public HashSet<string> GetAllAirportICAOs(SQLiteConnection connection = null)
        {
            bool shouldDisposeConnection = false;
            if (connection == null)
            {
                connection = new SQLiteConnection(_connectionString);
                connection.Open();
                shouldDisposeConnection = true;
            }

            try
            {
                var icaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sql = "SELECT ICAO FROM Airports";

                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        icaos.Add(reader["ICAO"].ToString());
                    }
                }

                return icaos;
            }
            finally
            {
                if (shouldDisposeConnection)
                {
                    connection?.Dispose();
                }
            }
        }

        public int GetAirportCount()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM Airports";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public int GetRunwayCount()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM Runways";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public int GetParkingSpotCount()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                var sql = "SELECT COUNT(*) FROM ParkingSpots";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
    }
}