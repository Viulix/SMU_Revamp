# SMU Revamp

SMU Revamp is a modern, cross-platform C# desktop application built with [Avalonia UI](https://avaloniaui.net/) and the MVVM (Model-View-ViewModel) architectural pattern. It is designed to interface with and control Source Measure Units (SMUs) for advanced electrical characterization of semiconductor devices (such as Memristors, Transistors, etc.).

## 🚀 Features

- **Modular Measurement Plans:** Easily selectable and highly configurable measurement routines including:
  - Pulse Spot
  - Frequency Memory
  - Spike Timing
  - Memristor Sweep
  - And more...
- **Advanced Sequence Editor:** Visually build and manage complex measurement sequences consisting of pulse, point, sweep, and measurement steps.
- **Dynamic Parameter Editor:** Configurable parameters for each measurement plan, featuring cross-parameter linking and multiplier logic.
- **Wafer & Sub-Cell Visualization:** Interactive 16x16 Wafermaps and 5x5 Sub-cell matrices to quickly locate and analyze specific contacts.
- **Real-Time Data Plotting:** Live Curve Plots with logarithmic axis support for immediate visual feedback during measurements.
- **Preset Management:** Save and load your hardware configuration and measurement parameters for repeatable experiments.
- **Data Export:** Export measurement points natively to CSV format for further analysis.

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

## 📖 Documentation
Detailed documentation is currently being written. 
- For End Users: A comprehensive **User Guide** is available in the `docs/` folder (coming soon).
- For Developers: See the architectural breakdown and inline documentation within the source code.
