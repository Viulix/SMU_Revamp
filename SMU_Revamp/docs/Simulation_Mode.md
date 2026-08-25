# Simulation Mode

Simulation mode replaces all physical instrument connections with in-process software emulation. It exists so measurements, wafer scans, UI work, and tests can run without access to the laboratory hardware.

## Enabling It

**Settings → Simulation → "Simulation mode (no hardware required)"**

The checkbox takes effect immediately for new connections. Devices that are currently connected should be disconnected/reconnected (or the app restarted) after toggling. The flag is persisted in `config.json` (`SimulationMode`), so the app also starts simulated if it was enabled during the last session.

When active, `E5263_SMU`, `ProberService`, and `SwitchMatrixService` skip VISA entirely:

| Service | Simulated behaviour |
|---|---|
| E5263 SMU | Full SCPI subset interpretation, generates measurement data |
| Prober | Acknowledges every command with `OK`; wafer scan movement/chuck control runs instantly |
| Switch Matrix | Accepts route commands; queries answer `1` (`*OPC?`) |

## The E5263 Emulator

The simulator (`Services/SmuSimulator.cs`) understands the command vocabulary used by the measurement plans:

- `*RST`, `FMT`, `TSC`, `AV`, `MM`, `CMM`, `RI`, `RV`, `TSR`, `DZ`, `CN`, `CL`
- **`DV`** (DC force), **`PV`** (pulse force) + `XE` trigger → single reading at the forced/pulse voltage
- **`WV` / `PWV`** staircase sweeps + `XE` → the requested number of points (mode 3 = up *and* down ramp)
- `TSQ` → empty flush block; `ERR? 1` → always `0` (no error); `*IDN?` reports an "E5263A SIMULATOR"

### Device Model

Readings follow a deterministic IV curve — a ~100 µS conductance with soft saturation:

```
I(V) = 1e-4 S · tanh(V / 1.2 V)
```

The data is noise-free and reproducible, which makes plots look realistic on a log scale while keeping tests exact. Responses use the same token shape as the real instrument (`N2I<value>`), so the regular parsers process them.

### Compliance Behaviour

Like the real E5263, a staircase sweep **stops early when compliance is reached**, returning only the measured prefix. This exercises the app's truncation handling end to end: voltage axes stay mapped to the true staircase values instead of being stretched.

## What Works / What Does Not

| Works in simulation | Requires real hardware |
|---|---|
| Measure Point, U-Sweep, Pulse Sweep, Pulse Spot | Eyeblink Conditioning (buffered instrument programs) |
| Memristor Sweep, PotDep, Modular Sequence | Timing-critical pulse experiments (no real µs pulses exist in simulation) |
| Complete automated wafer scans incl. prober movement | Actual device physics, obviously |
| Device Debug tools (test connection, query identity, force voltage) | |

Simulated runs are logged exactly like real ones — see [Logging.md](Logging.md); SMU traffic lines make it easy to verify what a plan sent.
