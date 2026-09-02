# Testing

The project ships with an xUnit test suite that runs entirely without hardware.

## Running the Tests

From the repository root:

```bash
dotnet test SMU_Revamp.Tests/SMU_Revamp.Tests.csproj
```

Or from the solution file in `SMU_Revamp/`:

```bash
dotnet test ../SMU_Revamp.Tests/SMU_Revamp.Tests.csproj
```

The suite completes in well under a second; no VISA drivers or instruments are required because every integration test runs against the [software simulator](Simulation_Mode.md).

## Project Layout

```
SMU_Revamp/            <- application project (Avalonia UI)
SMU_Revamp.Tests/      <- xUnit test project (sibling, referenced by the .slnx)
```

The test project references the app project directly. Parser methods under test are declared `internal`, and the app project grants test access via:

```xml
<InternalsVisibleTo Include="SMU_Revamp.Tests" />
```

## Test Suites

| Suite | What it covers |
|---|---|
| `SweepParserTests` | Staircase sweep response parsing: full sweeps, compliance truncation in both ramp directions, single vs. double staircase, current inversion for separate read channels, garbage input |
| `PointParserTests` | Point/pulse measurement parsers (`MeasurePoint`, `PulseSpot`) and the PotDep single-reading parser |
| `ModularSequenceTests` | Sequence step sweep parsing, steps JSON round-trip (preset persistence), per-step plot series partitioning |
| `MeasurementPlanLoaderTests` | All plans load, names are unique, every parameter has a value, plot defaults are sane |
| `SmuSimulatorTests` | Simulator command interpretation: staircase generation, compliance truncation behaviour, error queries, TSQ flushes, output compatibility with plan parsers |
| `SmuSimulationIntegrationTests` | End-to-end: complete plans (`Measure Point`, `U-Sweep`, `Memristor Sweep`, `Modular Sequence`, `PotDep`) run against the simulated connection and their result data is verified |
| `LogServiceTests` | Log file creation/format, session markers, message truncation, console tee capture |

## Conventions and Gotchas

- **Singleton isolation:** `E5263_SMU.Instance` is shared state. All tests touching it live in a single class (`SmuSimulationIntegrationTests`) which enables simulation in its constructor (`SetSimulationMode(true)`) and disables it in `Dispose`, so tests never leak hardware mode into each other.
- **Raw data format:** Instrument responses are simulated as comma-separated tokens like `N2I1.2345678901E-005`; the parsers identify current tokens by `'I'` at index 2.
- **Compliance truncation:** The simulator stops a staircase at the compliance limit exactly like the real instrument. Tests assert that truncated responses map to *true* voltages rather than being stretched across the sweep range.
- **Isolated configuration:** Plan constructors call `LoadDefaults()`, which reads `ConfigurationService`. A `[ModuleInitializer]` in the test project (`TestEnvironment.cs`) sets `SMU_REVAMP_CONFIG_DIR` to a throwaway temp directory before any test runs, so every test sees factory-default settings and the real `%APPDATA%\SMU_Revamp\config.json` is never read or written. The same environment variable can be used for portable app deployments.

## Adding Tests

Place new test classes in `SMU_Revamp.Tests/`. Follow the existing pattern: build raw instrument strings with invariant culture formatting, call the `internal` parser methods directly, and prefer exact expected arrays over loose ranges so axis-mapping regressions cannot slip through.
