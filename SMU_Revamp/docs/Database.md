# Database Architecture

The `SMU_Revamp` application uses a MySQL database to persist measurement data, parameters, and resulting data points. The communication is handled by the `DatabaseService` singleton.

## Implementation Details
*   **Class:** `DatabaseService`
*   **Driver:** `MySqlConnector`
*   **Connection:** The connection details (Address, User, Password, DB Name) are loaded from the global `AppConfig` via the `ConfigurationService`.

## Schema Management

The `DatabaseService` includes an automatic schema migration feature. Before saving data, it calls `EnsureSchemaUpdatedAsync()` which:
1.  Connects to the MySQL server and creates the database if it does not exist.
2.  Creates the `Measurements`, `MeasurementParameters`, and `MeasurementPoints` tables if they do not exist.
3.  Automatically alters tables to add any missing columns (e.g., adding `ProfileName` or `FolderName` if migrating from an older schema version).
4.  Creates indexes (e.g., on the `Timestamp` column) to improve query performance.

## Database Tables

1.  **Measurements:** The root table holding metadata for a specific run.
    *   Fields: `Id`, `ProfileName`, `PlanName`, `SampleName`, `Timestamp`, `SourceFilename`, `FolderName`, `Notes`.
2.  **MeasurementParameters:** Stores the configuration parameters used during the measurement (key-value pairs).
    *   Fields: `Id`, `MeasurementId` (Foreign Key), `Name`, `Value`.
3.  **MeasurementPoints:** Stores the actual recorded data points (X, Y).
    *   Fields: `Id`, `MeasurementId` (Foreign Key), `X`, `Y`.

## Saving Data

The `SaveMeasurementAsync` method wraps the insertion of the measurement, its parameters, and its data points into a single transaction. To optimize performance, data points are batch-inserted in chunks of 1000 records.
