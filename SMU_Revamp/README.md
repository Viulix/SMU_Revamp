# SMU Revamp

SMU Revamp is a modern, cross-platform C# desktop application built with [Avalonia UI](https://avaloniaui.net/) and the MVVM (Model-View-ViewModel) architectural pattern. It is designed to interface with and control Source Measure Units (SMUs) for advanced electrical characterization of semiconductor devices (such as Memristors, Transistors, etc.).

## 🚀 Features

- **Modular Measurement Plans:** Easily selectable and highly configurable measurement routines including:
  - Pulse Spot
  - Frequency Memory
  - Spike Timing
  - Memristor Sweep
  - And more...
- **Automated Wafer Scanning:** Step through target cells, sub-cells, and contacts automatically with live progress, ETA estimation, and per-contact failure tracking.
- **Advanced Sequence Editor:** Visually build and manage complex measurement sequences consisting of pulse, point, sweep, and measurement steps.
- **Dynamic Parameter Editor:** Configurable parameters for each measurement plan, featuring cross-parameter linking and multiplier logic.
- **Wafer & Sub-Cell Visualization:** Interactive 16x16 Wafermaps and 5x5 Sub-cell matrices to quickly locate and analyze specific contacts.
- **Real-Time Data Plotting:** Live Curve Plots with logarithmic axis support for immediate visual feedback during measurements.
- **Preset Management:** Save and load your hardware configuration and measurement parameters for repeatable experiments.
- **Hardware Simulation Mode:** Run every measurement — including complete wafer scans — against a built-in software simulator, no instruments required.
- **Structured Logging:** Daily log files with full instrument command/response traces and measurement session records.
- **Data Export:** Export measurement points natively to CSV format for further analysis.
- **Test Suite:** xUnit-based unit and integration tests covering parsers, the hardware simulator, and end-to-end measurement plans.

## 🏗️ Architecture

The application is structured around a clean **MVVM Architecture**:

- **Models:** Defines core structures such as `SequenceStep`, `MeasurementParameter`, `HardwareConfig`, and `ParameterLinkConfig`.
- **ViewModels:** 
  - `MainWindowViewModel` acts as the central hub and is broken down into partial classes (`.Measurements.cs`, `.Results.cs`, etc.) to maintain separation of concerns.
  - Handles the business logic, state management, and acts as the bridge between UI and Hardware.
- **Views:** Highly modular Avalonia XAML files. Complex layouts like `MeasurementsTabView` and `ResultTabView` have been refactored into smaller, reusable user controls (e.g., `SequenceEditorControl.axaml`, `ResultWafermapControl.axaml`).
- **Measurement Plans:** Encapsulates the specific SMU command logic for different types of tests. All plans inherit from `MeasurementPlanBase` to ensure a DRY and standardized approach to parameter retrieval and default initialization.

## 🛠️ Development & Building

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or whichever specific .NET version the project targets)
- An IDE such as Visual Studio 2022, Rider, or VS Code with C# Dev Kit.

### Building
Clone the repository and run:
```bash
dotnet build
```

### Running
```bash
dotnet run
```

### Testing
The solution includes an xUnit test suite (unit tests for the SMU response parsers and the hardware simulator, plus end-to-end integration tests that run complete measurement plans against the simulator):
```bash
dotnet test ../SMU_Revamp.Tests/SMU_Revamp.Tests.csproj
```
See [docs/Testing.md](docs/Testing.md) for details.

### Simulation Mode
Enable **Settings → Simulation → "Simulation mode"** to run all instrument connections in software. The built-in E5263 emulator generates physically plausible IV data and even reproduces compliance truncation — ideal for demos, UI development, and offline testing. See [docs/Simulation_Mode.md](docs/Simulation_Mode.md).

### Logging
Every session is logged to daily files in `%APPDATA%\SMU_Revamp\logs` (`smu_YYYYMMDD.log`), including full SMU command/response traces, measurement session banners, wafer-scan summaries, and warnings such as compliance truncation. See [docs/Logging.md](docs/Logging.md).

## 📖 Documentation
- For End Users: Guides live in the `docs/` folder.
  - [Developer Documentation Overview](docs/README.md)
  - [E5263 SMU Control](docs/E5263_SMU_Control.md)
  - [Prober Control](docs/Prober_Control.md)
  - [Switch Matrix Control](docs/SwitchMatrix_Control.md)
  - [Database Architecture](docs/Database.md)
  - [Application Configuration](docs/Configuration.md)
  - [Measurement Plans](docs/Measurement_Plans.md)
  - [Testing](docs/Testing.md)
  - [Simulation Mode](docs/Simulation_Mode.md)
  - [Logging](docs/Logging.md)
- For Developers: See the architectural breakdown and inline documentation within the source code.
