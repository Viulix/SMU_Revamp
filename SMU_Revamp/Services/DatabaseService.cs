using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using MySqlConnector;
using SMU_Revamp.Interfaces;
using SMU_Revamp.Models;

namespace SMU_Revamp.Services
{
    public class DatabaseService
    {
        private static readonly Lazy<DatabaseService> _instance = new(() => new DatabaseService());
        public static DatabaseService Instance => _instance.Value;

        private bool _schemaUpdated = false;

        private DatabaseService() { }

        private async Task EnsureSchemaUpdatedAsync()
        {
            if (_schemaUpdated) return;
            try
            {
                var config = ConfigurationService.Instance.GetConfig();
                // TestConnectionAsync also updates the schema if tables/columns are missing
                await TestConnectionAsync(config.DbAddress, config.DbUser, config.DbPassword, config.DbName);
                _schemaUpdated = true;
            }
            catch { /* Ignore */ }
        }

        private string GetConnectionString(string address, string user, string password, string dbName, bool includeDb = true)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = address,
                UserID = user,
                Password = password,
                SslMode = MySqlSslMode.Preferred
            };
            if (includeDb)
            {
                builder.Database = dbName;
            }
            return builder.ConnectionString;
        }

        private string GetCurrentConnectionString()
        {
            var config = ConfigurationService.Instance.GetConfig();
            return GetConnectionString(config.DbAddress, config.DbUser, config.DbPassword, config.DbName);
        }

        public async Task<bool> TestConnectionAsync(string address, string user, string password, string dbName)
        {
            try
            {
                // First connect without DB to create it if it doesn't exist
                using (var connection = new MySqlConnection(GetConnectionString(address, user, password, "", false)))
                {
                    await connection.OpenAsync();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE DATABASE IF NOT EXISTS `{dbName}`;";
                    await command.ExecuteNonQueryAsync();
                }

                // Now connect to the specific DB and create tables
                using (var connection = new MySqlConnection(GetConnectionString(address, user, password, dbName)))
                {
                    await connection.OpenAsync();
                    
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Measurements (
                            Id INT AUTO_INCREMENT PRIMARY KEY,
                            ProfileName VARCHAR(255) NULL,
                            PlanName VARCHAR(255),
                            SampleName VARCHAR(255),
                            Timestamp DATETIME,
                            SourceFilename VARCHAR(500) NULL,
                            Notes TEXT NULL
                        );

                        CREATE TABLE IF NOT EXISTS MeasurementParameters (
                            Id INT AUTO_INCREMENT PRIMARY KEY,
                            MeasurementId INT,
                            Name VARCHAR(255),
                            Value VARCHAR(255),
                            FOREIGN KEY (MeasurementId) REFERENCES Measurements(Id) ON DELETE CASCADE
                        );

                        CREATE TABLE IF NOT EXISTS MeasurementPoints (
                            Id INT AUTO_INCREMENT PRIMARY KEY,
                            MeasurementId INT,
                            X DOUBLE,
                            Y DOUBLE,
                            FOREIGN KEY (MeasurementId) REFERENCES Measurements(Id) ON DELETE CASCADE
                        );
                    ";
                    await cmd.ExecuteNonQueryAsync();

                    // Try to add ProfileName column if it's missing (for older DB versions)
                    try
                    {
                        using var alterCmd = connection.CreateCommand();
                        alterCmd.CommandText = "ALTER TABLE Measurements ADD COLUMN ProfileName VARCHAR(255) NULL;";
                        await alterCmd.ExecuteNonQueryAsync();
                    }
                    catch { /* Ignore if it already exists */ }

                    // Fill missing ProfileNames with a default value
                    try
                    {
                        using var updateCmd = connection.CreateCommand();
                        updateCmd.CommandText = "UPDATE Measurements SET ProfileName = 'Default' WHERE ProfileName IS NULL OR ProfileName = '';";
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                    catch { /* Ignore */ }

                    // Try to add FolderName column if it's missing
                    try
                    {
                        using var alterCmd2 = connection.CreateCommand();
                        alterCmd2.CommandText = "ALTER TABLE Measurements ADD COLUMN FolderName VARCHAR(500) NULL;";
                        await alterCmd2.ExecuteNonQueryAsync();
                    }
                    catch { /* Ignore if it already exists */ }
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsMeasurementUploadedAsync(string sourceFilename)
        {
            if (string.IsNullOrEmpty(sourceFilename)) return false;

            try
            {
                using var connection = new MySqlConnection(GetCurrentConnectionString());
                await connection.OpenAsync();
                
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM Measurements WHERE SourceFilename = @filename";
                cmd.Parameters.AddWithValue("@filename", sourceFilename);
                
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking uploaded measurement: {ex.Message}");
                return false;
            }
        }

        public async Task<int> SaveMeasurementAsync(IMeasurementPlan plan, string profileName, string sampleName, DateTime timestamp, string folderName, string? sourceFilename = null)
        {
            await EnsureSchemaUpdatedAsync();
            using var connection = new MySqlConnection(GetCurrentConnectionString());
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Insert Measurement
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Measurements (ProfileName, PlanName, SampleName, Timestamp, FolderName, SourceFilename)
                    VALUES (@profileName, @planName, @sampleName, @timestamp, @folderName, @sourceFilename);
                    SELECT LAST_INSERT_ID();";
                cmd.Parameters.AddWithValue("@profileName", profileName);
                cmd.Parameters.AddWithValue("@planName", plan.Name);
                cmd.Parameters.AddWithValue("@sampleName", sampleName);
                cmd.Parameters.AddWithValue("@timestamp", timestamp);
                cmd.Parameters.AddWithValue("@folderName", folderName);
                cmd.Parameters.AddWithValue("@sourceFilename", sourceFilename);

                int measurementId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                // Insert Parameters
                if (plan.Parameters != null && plan.Parameters.Count > 0)
                {
                    using var paramCmd = connection.CreateCommand();
                    paramCmd.Transaction = transaction;
                    paramCmd.CommandText = "INSERT INTO MeasurementParameters (MeasurementId, Name, Value) VALUES (@mId, @name, @val)";
                    paramCmd.Parameters.Add("@mId", MySqlDbType.Int32);
                    paramCmd.Parameters.Add("@name", MySqlDbType.VarChar);
                    paramCmd.Parameters.Add("@val", MySqlDbType.VarChar);

                    paramCmd.Prepare();

                    foreach (var param in plan.Parameters)
                    {
                        paramCmd.Parameters["@mId"].Value = measurementId;
                        paramCmd.Parameters["@name"].Value = param.Name;
                        paramCmd.Parameters["@val"].Value = param.GetValueAsString();
                        await paramCmd.ExecuteNonQueryAsync();
                    }
                }

                // Insert Points
                if (plan.ResultPoints != null && plan.ResultPoints.Count > 0)
                {
                    // Batch insert using a single query string for better performance
                    using var pointCmd = connection.CreateCommand();
                    pointCmd.Transaction = transaction;
                    
                    var values = new List<string>();
                    for (int i = 0; i < plan.ResultPoints.Count; i++)
                    {
                        var point = plan.ResultPoints[i];
                        values.Add($"({measurementId}, {point.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {point.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                        
                        // Execute in batches of 1000 to avoid query size limits
                        if (values.Count >= 1000 || i == plan.ResultPoints.Count - 1)
                        {
                            pointCmd.CommandText = $"INSERT INTO MeasurementPoints (MeasurementId, X, Y) VALUES {string.Join(",", values)}";
                            await pointCmd.ExecuteNonQueryAsync();
                            values.Clear();
                        }
                    }
                }

                await transaction.CommitAsync();
                return measurementId;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        
        public class MeasurementSummary
        {
            public int Id { get; set; }
            public string ProfileName { get; set; } = string.Empty;
            public string PlanName { get; set; } = string.Empty;
            public string SampleName { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public string FolderName { get; set; } = string.Empty;
            public string SourceFilename { get; set; } = string.Empty;
        }

        public async Task<List<MeasurementSummary>> GetRecentMeasurementsAsync(int limit = 100)
        {
            await EnsureSchemaUpdatedAsync();
            var list = new List<MeasurementSummary>();
            using var connection = new MySqlConnection(GetCurrentConnectionString());
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id, ProfileName, PlanName, SampleName, Timestamp, FolderName, SourceFilename FROM Measurements ORDER BY Timestamp DESC LIMIT @limit";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MeasurementSummary
                {
                    Id = reader.GetInt32(0),
                    ProfileName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    PlanName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    SampleName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Timestamp = reader.GetDateTime(4),
                    FolderName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    SourceFilename = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }
            return list;
        }

        public async Task<(Dictionary<string, string> Parameters, List<CurvePoint> Points)> LoadMeasurementDataAsync(int measurementId)
        {
            var parameters = new Dictionary<string, string>();
            var points = new List<CurvePoint>();

            using var connection = new MySqlConnection(GetCurrentConnectionString());
            await connection.OpenAsync();

            // Load parameters
            using (var pCmd = connection.CreateCommand())
            {
                pCmd.CommandText = "SELECT Name, Value FROM MeasurementParameters WHERE MeasurementId = @id";
                pCmd.Parameters.AddWithValue("@id", measurementId);
                using var reader = await pCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    parameters[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                }
            }

            // Load points
            using (var dCmd = connection.CreateCommand())
            {
                dCmd.CommandText = "SELECT X, Y FROM MeasurementPoints WHERE MeasurementId = @id ORDER BY Id ASC";
                dCmd.Parameters.AddWithValue("@id", measurementId);
                using var reader = await dCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    points.Add(new CurvePoint(reader.GetDouble(0), reader.GetDouble(1)));
                }
            }

            return (parameters, points);
        }
    }
}
