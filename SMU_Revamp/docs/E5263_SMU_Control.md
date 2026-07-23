# E5263 SMU Control

The E5263 Source Measure Unit (SMU) is controlled via the `E5263_SMU` Singleton service in the `SMU_Revamp.Services` namespace. This service establishes a persistent GPIB session using `NationalInstruments.Visa`.

## Implementation Details

*   **Class:** `E5263_SMU` (Singleton, access via `E5263_SMU.Instance`)
*   **Default Resource String:** `GPIB0::17::INSTR`
*   **Protocol:** NI-VISA Message-Based Session (GPIB)
*   **Timeout:** Default is 300,000 ms (5 minutes) to allow for long sweep measurements.

## Communication Methods

The service exposes the following main methods for instrument communication:
*   `ConnectAsync()`: Opens the persistent VISA session.
*   `DisconnectAsync()`: Closes and disposes of the VISA session.
*   `SendCommandAsync(string command)`: Writes a command to the SMU (appends `\n` automatically).
*   `ReadResponseAsync(int readBufferChars = 1024)`: Reads the instrument response buffer.
*   `QueryAsync(string command, ...)`: Helper method that sends a command and immediately reads the response.

## Command Examples

The SMU uses standard SCPI commands. Below are examples of how they are invoked within the codebase.

### Checking for Errors
To query the device's internal error register, the `CheckErrorAsync` method combines two commands: `ERR?` and `EMG?`.

```csharp
// Example command to query the oldest error code
string errCodeStr = await QueryAsync("ERR? 1", readBufferChars: 20);

// If the error code is not zero, fetch the error message text
string message = await QueryAsync($"EMG? {errorCode}", readBufferChars: 256);
```

### Performing Measurements (General)
Though specific measurement logic might be abstracted elsewhere, basic control commands typically look like:
```csharp
// Resetting the device
await SendCommandAsync("*RST");

// Example of querying the ID string
string idn = await QueryAsync("*IDN?");
```
