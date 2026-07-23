# Application Configuration

The application's configuration is managed by the `ConfigurationService` singleton, which handles loading, parsing, and saving the user's settings.

## Storage Location

Settings are stored in a standard JSON format. The service attempts to save the `config.json` file in the user's `AppData` directory:
*   **Path:** `%APPDATA%\SMU_Revamp\config.json` (e.g., `C:\Users\<User>\AppData\Roaming\SMU_Revamp\config.json`)
*   **Fallback:** If the `AppData` directory is inaccessible, the service falls back to the system's temporary directory (`%TEMP%\SMU_Revamp_config.json`).

## The `AppConfig` Model

The `AppConfig` class acts as the data structure for all settings. It includes:
*   **Database Settings:** `DbAddress`, `DbUser`, `DbPassword`, `DbName`.
*   **Hardware Settings:** Default compliance limits (`SweepCompliance`) or channels (`SweepChannel`).
*   **Measurement Presets:** A global list of `MeasurementPreset` objects that store saved configurations for different measurement plans.

## Loading and Saving

*   **Deserialization:** The service uses `System.Text.Json` to read the configuration. If the file doesn't exist or is corrupted, it silently falls back to a default `AppConfig` instance.
*   **Migrations:** The `LoadAsync` method includes automatic data migration logic. For instance, if legacy `PlanPresets` exist but the new global `Presets` list is empty, it automatically translates and migrates the old dictionary format into the new flat list format, then saves it back to disk.
*   **Saving:** The `SaveAsync(AppConfig config)` method serializes the object with `WriteIndented = true` to ensure the resulting JSON is human-readable.
