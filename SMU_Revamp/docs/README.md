# SMU_Revamp Developer Documentation

Welcome to the Developer Documentation for the `SMU_Revamp` project. This guide is intended to help developers understand the system architecture, hardware integration, and workflows.

## Documentation Structure

The documentation is split into multiple files for easier reading:

1. **[E5263 SMU Control](E5263_SMU_Control.md)**
   Details the integration, session management, and SCPI commands used to control the E5263 Source Measure Unit.

2. **[Prober (Wafer Stager) Control](Prober_Control.md)**
   Explains the `ProberService`, including Chuck alignment, movement abstraction, wafer scanning logic, and legacy SUSS ProberBench commands.

3. **[Switch Matrix Control](SwitchMatrix_Control.md)**
   Describes how the `SwitchMatrixService` routes connections between the SMU channels and Prober contacts using SCPI-based crosspoint switching.

4. **[Database Architecture](Database.md)**
   Details the MySQL schema, automated migrations, and how measurement data is persisted using the `DatabaseService`.

5. **[Application Configuration](Configuration.md)**
   Explains how settings are loaded and saved to the `config.json` file in AppData using the `ConfigurationService`.

6. **[Measurement Plans](Measurement_Plans.md)**
    A guide on how to add a new measurement plan by extending `MeasurementPlanBase` using the reflection-based `MeasurementPlanLoader`.

7. **[Testing](Testing.md)**
    How to run and extend the xUnit test suite: parser unit tests, simulator tests, and end-to-end plan runs without hardware.

8. **[Simulation Mode](Simulation_Mode.md)**
    Running SMU, prober, and switch matrix in software: capabilities of the E5263 emulator, device model, and limitations.

9. **[Logging](Logging.md)**
    Daily log files, line format, what is recorded (instrument traffic, session banners, failures), and how to use `LogService`.

10. **[Yield Dashboard Specification](Yield_Dashboard_Spec.md)**
    Draft specification for the planned yield-statistics dashboard: scope, data foundation, and open questions.

---

## High-Level Architecture Overview

The `SMU_Revamp` software is a C# Desktop application built with the **Avalonia UI** framework utilizing the **MVVM (Model-View-ViewModel)** pattern. 

### Core Components:
*   **Models:** Contain data structures representing measurement plans, sequence steps, curves, and application configuration.
*   **ViewModels:** Act as the binding layer between the UI and backend logic. E.g., `MainWindowViewModel` manages the state of wafer scans and measurements.
*   **Views (`.axaml`):** The Avalonia markup files defining the user interface.
*   **Services:** Implementations for hardware control, database persistence (SQLite), and logic execution. Services are generally implemented as Singletons and abstract the hardware protocols behind specific Interfaces.
*   **Interfaces:** Define contracts for hardware abstractions, enabling easier testing and dependency injection (e.g., `IProberService`, `ISwitchMatrixService`).

### Hardware Integration Layer
All hardware communication relies on the `NationalInstruments.Visa` library to interact over GPIB/USB/Ethernet. Each physical device is wrapped in a dedicated Service class that maintains a persistent VISA session.
