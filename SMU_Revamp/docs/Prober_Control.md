# Prober (Wafer Stager) Control

The wafer prober is controlled by the `ProberService` which implements the `IProberService` interface. This service handles movement, alignment, and complex wafer scanning logic by issuing legacy SUSS ProberBench commands over GPIB.

## Implementation Details

*   **Class:** `ProberService` (Singleton, access via `ProberService.Instance`)
*   **Default Resource String:** `GPIB0::22::INSTR`
*   **Protocol:** NI-VISA Message-Based Session (GPIB)
*   **Termination Character:** Commands must be terminated with a Carriage Return (`\r`, ASCII 13).

## Wafer Scanning Logic

The `ProberService` is unique because it provides high-level geometric operations:
*   `GoToWaferContactAsync`: Computes absolute X and Y coordinates based on a hierarchical layout (Cell > Sub-Cell > Contact) and performs the physical move.
*   `ScanWaferAsync`: A sophisticated routine that steps through requested targets on the wafer. It handles safety by automatically moving the chuck to separation before traveling, and moving it to contact before taking a measurement.

## Command Examples

The prober uses SUSS ProberBench commands rather than standard SCPI. The `ProberService.SendProberAsync` method handles adding the `\r` termination character to all commands automatically.

### Chuck Alignment
Aligning the chuck into contact or separation mode.
```csharp
// Move Chuck to Separation mode
await SendProberAsync("MoveChuckSeparation");

// Move Chuck to Contact mode
await SendProberAsync("MoveChuckContact");
```

### Motor Modes
Setting motor behavior.
```csharp
// Enable or disable quiet mode on the prober motors
await SendProberAsync("EnableMotorQuiet 1"); // Enable
await SendProberAsync("EnableMotorQuiet 0"); // Disable
```

### Movement Commands
Moving the chuck uses specific suffixes like `R` for relative, `H` for absolute (Home-based), or `Z` for absolute (Z-based).
```csharp
// Relative move in X and Y
await SendProberAsync($"MoveChuck {x} {y} R");

// Relative move in Z
await SendProberAsync($"MoveChuckZ {z} R");

// Absolute move relative to Home
await SendProberAsync($"MoveChuck {x} {y} H");

// Set the current position as Home
await SendProberAsync("SetChuckHome");
```
