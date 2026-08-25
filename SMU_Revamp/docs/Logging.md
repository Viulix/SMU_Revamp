# Logging

The application logs structured, timestamped entries to daily rotating files. Logging is always on and is designed to answer "what exactly happened during that measurement?" after the fact — including every command sent to the SMU.

## Log Location

```
%APPDATA%\SMU_Revamp\logs\smu_YYYYMMDD.log
```

(e.g. `C:\Users\<User>\AppData\Roaming\SMU_Revamp\logs\smu_20260825.log`)

A new file is started automatically each day; entries are appended.

## Line Format

```
2026-08-25 14:03:22.187 [INFO] SMU >> WV 1,3,0,0,1,21,0.01
2026-08-25 14:03:22.205 [DEBUG] SMU << N2I9.05... (+214 chars)
2026-08-25 14:03:45.900 [WARNING] Measurement canceled by stop request.
```

Levels: `DEBUG` (instrument traffic), `INFO` (lifecycle events), `WARNING` (cancellations, partial failures), `ERROR` (exceptions with stack/message).

## What Gets Logged

### Instrument traffic
Every SMU command (`SMU >> ...`) and response (`SMU << ...`) is logged in both real and simulated connections. Long data blocks (sweep responses) are truncated to keep files manageable. Prober and switch matrix services log their connection lifecycle and any failed commands.

### Measurement sessions
Each single measurement writes a distinct session banner:

```
===========================================================
=== Measurement 'Memristor Sweep' | Profile: test | Device: D01
===========================================================
```

followed by the full command trace, and a closing entry such as `Measurement finished (72 result points).` Wafer scans log one banner per scan plus a per-contact banner for every measurement, and a final summary that includes the count of failed contacts.

### Failures and cancellations
Exceptions from measurements, wafer scans, configuration loading, and hardware connections are recorded with their messages. Stop requests are logged as warnings so incomplete data sets can be identified later.

### Console capture
All existing `Console.WriteLine` diagnostics — notably the compliance-truncation warnings emitted by the sweep plans — are mirrored into the log via a console tee installed at startup. This captures plan output without requiring changes inside the measurement plans themselves.

## API

Services and view models use the `LogService` singleton:

```csharp
LogService.Instance.Info("...");
LogService.Instance.Warning("...");
LogService.Instance.Error("context", exception);
LogService.Instance.Debug("...");
LogService.Instance.Session("Measurement 'X'");   // visual separator
LogService.Truncate(longString);                  // for payloads
```

Logging never throws and never takes the application down; internal write failures are swallowed with a debug trace only.
