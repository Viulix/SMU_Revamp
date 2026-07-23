# Switch Matrix Control

The switch matrix routes connections between the Source Measure Unit (SMU) channels and the physical pins on the wafer prober. The `SwitchMatrixService` abstracts this complexity behind the `ISwitchMatrixService` interface.

## Implementation Details

*   **Class:** `SwitchMatrixService` (Singleton, access via `SwitchMatrixService.Instance`)
*   **Default Resource String:** `GPIB0::23::INSTR`
*   **Protocol:** NI-VISA Message-Based Session (GPIB)

## Routing Logic and Cross-Point Formatting

A significant feature of this service is its parsing logic, which transforms logical endpoints into 5-digit cross-point formats required by an Agilent/Keysight E5250A mainframe:
*   **Card/Slot:** 1 digit (e.g., `1`).
*   **Input Port:** 2 digits (e.g., `01` to `10`).
*   **Output Port:** 2 digits (e.g., `01` to `12`).

For example, connecting input 1 to output 3 on card 1 is parsed as `@10103`.

## Command Examples

The switch matrix is controlled via standard SCPI `ROUTe` subsystem commands.

### Creating a Connection
Connecting a route involves defining the rule (FREE) and sequence (Break-Before-Make, BBM), followed by closing the specific channel.
```csharp
// Example internal calls to establish a route (e.g., channel "@10103")
await SendWriteCommandAsync(":ROUT:CONN:RULE ALL,FREE");
await SendWriteCommandAsync(":ROUT:CONN:SEQ ALL,BBM");
await SendWriteCommandAsync(":ROUT:CLOSE (@10103)");

// Ensure operation completes before returning
await SendReadCommandAsync("*OPC?", readBufferChars: 10);
```

### Breaking a Connection
Breaking a connection involves opening the specified channel.
```csharp
// Example internal call to break a route
await SendWriteCommandAsync(":ROUT:OPEN (@10103)");
```

### Clearing All Connections
Clearing everything at once is done by opening all channels on a specific card (or ALL).
```csharp
// Example internal call to clear all routes
await SendWriteCommandAsync(":ROUT:OPEN:CARD ALL");
```

### Querying Connections
To determine which connections are active, the system reads the status of the cards.
```csharp
// Queries card 1 for a list of closed channels
string response = await SendReadCommandAsync(":ROUT:CLOS:CARD? 1");
// The response is then parsed to build a human-readable list of active connections.
```
