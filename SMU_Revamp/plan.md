# Rewrite Plan for Nano Measurement App

## Goal
Rewrite the VB.NET / WinForms project as a **C# / Avalonia** application with:
- clear separation of concerns
- pluggable measurement types
- hardware abstraction for easier testing
- easier maintenance and future measurement additions

---

## Current Roles in the Legacy App

### 1) Prober / Stager
Moves the probe needles or chuck to the correct physical contact.

### 2) Switch Matrix
Routes the selected physical contacts to the correct SMU terminals.

### 3) SMU / Measurement Instrument
Applies voltage/current and reads back measurements.

### 4) UI / Orchestration
The current `Form1.vb` contains most of the logic and coordinates the full flow.

---

## Key Legacy Files

### `Form1.vb`
Main application form and central coordination point.
Contains:
- UI event handlers
- measurement flow
- device setup
- DB/file persistence hooks
- run sequencing logic

### `switchmatrix.vb`
Contains switch matrix communication:
- `read_connection()`
- `create_connection(x, y)`

This file is the clearest reference for how the switch matrix is used.

---

## Agreed High-Level Architecture

### Presentation Layer
- `MainWindow` / Avalonia UI
- `MainWindowViewModel`

Responsibilities:
- show run configuration
- start/stop measurements
- display progress/logs/results
- no direct hardware logic

---

## Core Services

### 1) `MeasurementOrchestrator`
Coordinates the full workflow:
1. move prober
2. configure switch matrix
3. select measurement strategy
4. execute measurement
5. store result

---

### 2) `IProberService`
Responsible for prober/stage movement.

Typical responsibilities:
- move to contact
- move relative / absolute
- home / align
- contact / retract actions if available

---

### 3) `ISwitchMatrixService`
Responsible for routing the measurement hardware to the selected contacts.

Based on `switchmatrix.vb`, this service should support:

- `ReadConnection()`
- `CreateConnection(string x, string y, bool overrideFlag = false)`

Notes:
- it should wrap the VISA/GPIB matrix session
- it should hide command strings from the UI
- it should be a separate service from the prober and instrument

---

### 4) `IInstrumentService`
Abstracts measurement devices such as:
- E5263A / E5270 style SMU
- HP4156A
- future devices

Responsibilities:
- open/close session
- send commands
- trigger measurement
- read result
- configure source/mode/compliance/channel

Recommended sub-implementations:
- `E5270Service`
- `HP4156AService`
- later: `B2912AService`

---

### 5) `IDatabaseService`
Stores:
- metadata
- measurement results
- run information
- optional device states

---

### 6) `IFileStorageService`
Stores:
- raw measurement files
- exports
- configuration snapshots
- logs

---

### 7) `IConfigurationService`
Loads/saves:
- GPIB addresses
- COM ports
- default compliance values
- run settings
- user preferences

---

## Domain Models

### `MeasurementPlan`
Typed replacement for the old `adv_table` row.

Contains:
- measurement type id
- voltage/current ranges
- compliance
- number of points
- delay / duration
- cycle count
- channel selection
- comments
- switch matrix path identifiers
- extra flags

---

### `BatchPlan`
Higher-level run container, replacing the old “bob” sequencing logic.

Contains:
- multiple measurement plans
- run order
- wafer/cell/contact mapping
- file selection or plan source

---

### `SwitchRoute`
Represents one matrix routing pair:
- `PathA`
- `PathB`

---

### `ContactTarget`
Represents a physical target:
- row / column / cell
- needle / pad position
- optional probe coordinates

---

### `MeasurementResult`
Contains:
- x-values
- y-values
- timestamp
- metadata
- measurement type
- status / errors

---

### `DeviceConfig`
Contains:
- GPIB addresses
- COM port
- device type
- default mode
- communication timeouts

---

## Measurement Strategy Pattern

Use a strategy per measurement type.

### Interface
- `IMeasurementStrategy`
  - `MeasurementTypeId`
  - `ValidateParameters(...)`
  - `Execute(...)`

### Example Strategies
- `USweepStrategy` for U-Sweep
- `ISweepStrategy` for I-Sweep
- `UConstStrategy` for constant voltage hold
- `PulsedStrategy`
- `RetentionStrategy`
- `STDPStrategy`
- `LcrStrategy`
- future custom strategies

### Purpose
This makes it easy to:
- add new measurement types
- modify one measurement without touching others
- unit test each measurement separately

---

## Switch Matrix Details

### Legacy behavior from `switchmatrix.vb`
`create_connection(x, y)`:
- opens GPIB matrix at `GPIB0::23::INSTR`
- resets matrix
- sets routing rules
- closes the selected paths
- waits briefly for completion
- logs the channel string

### Rewrite behavior
`SwitchMatrixService` should:
- encapsulate VISA session handling
- expose `ReadConnection()`
- expose `CreateConnection(...)`
- provide logging callback or logger injection
- keep hardware-specific command strings out of UI and measurement code

---

## Instrument Access and VISA Abstraction

Do **not** expose raw VISA objects to the application layer.

Recommended abstraction:
- `IGpibSession`
- implementation wrapper for Keysight VISA COM objects

Then:
- `E5270Service` and `HP4156AService` depend on `IGpibSession`
- the rest of the app depends on `IInstrumentService`

This makes the code portable and testable.

---

## Suggested Execution Flow

1. UI selects a `MeasurementPlan`
2. `MeasurementOrchestrator` starts a run
3. `IProberService` moves to contact
4. `ISwitchMatrixService.CreateConnection(...)` routes the contact
5. `IInstrumentService` configures the measurement device
6. The selected `IMeasurementStrategy` executes the measurement
7. Results are stored in file and/or database

---

## Mapping From Legacy Concepts

### Legacy `Form1`
Split into:
- UI state
- orchestration
- hardware services
- persistence

### Legacy `adv_table`
Replace with:
- `List<MeasurementPlan>`

### Legacy `bob`
Replace with:
- `BatchPlan`

### Legacy `create_connection(...)`
Replace with:
- `ISwitchMatrixService.CreateConnection(...)`

### Legacy `Module1.Main`
Replace with:
- `MeasurementOrchestrator + IMeasurementStrategy`

---

## Recommended First Implementation Order

1. Create domain models
2. Create `IGpibSession`
3. Create `ISwitchMatrixService`
4. Create `IInstrumentService`
5. Implement `E5270Service`
6. Implement `HP4156AService`
7. Implement `IProberService`
8. Implement `IMeasurementStrategy` and `USweepStrategy`
9. Implement `MeasurementOrchestrator`
10. Connect Avalonia UI to the orchestrator

---

## Minimum Initial Class List

### Services
- `MeasurementOrchestrator`
- `IProberService`
- `ISwitchMatrixService`
- `IInstrumentService`
- `IDatabaseService`
- `IFileStorageService`
- `IConfigurationService`

### Strategies
- `USweepStrategy`
- `ISweepStrategy`
- `UConstStrategy`
- `PulsedStrategy`

### Models
- `MeasurementPlan`
- `BatchPlan`
- `SwitchRoute`
- `ContactTarget`
- `MeasurementResult`
- `DeviceConfig`

---

## Main Design Goal
Keep all hardware control and measurement logic out of the UI.

The UI should only:
- select plans
- start/stop runs
- show progress
- display results

Hardware logic should live in services and strategies.