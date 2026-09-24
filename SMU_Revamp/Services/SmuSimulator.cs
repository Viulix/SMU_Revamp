using System;
using System.Collections.Generic;
using System.Globalization;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Software simulation of the E5263 SMU command set used by the app.
    ///
    /// Interprets the SCPI subset the measurement plans send and produces
    /// deterministic, physically plausible response data so measurements,
    /// wafer scans, and device debug tools run without real hardware.
    ///
    /// Supported behaviour:
    /// - Point/pulse measurements (DV/PV + XE): one reading at the forced voltage.
    /// - Staircase sweeps (WV/PWV + XE): the requested number of points along a
    ///   mildly nonlinear IV curve; mode 3 generates up then down. Compliance is
    ///   honoured by truncating the sweep exactly like the real instrument.
    /// - Error queries (ERR?) report no error; *IDN? identifies the simulator.
    /// </summary>
    internal sealed class SmuSimulator
    {
        private readonly object _gate = new();
        private readonly Queue<string> _responses = new();

        // Device model: ~100 µS conductance with soft saturation, so log-scale
        // plots look like a real two-terminal device at small voltages.
        private const double ConductanceS = 1.0e-4;
        private const double SoftSaturationV = 1.2;

        private readonly HashSet<string> _channelsOn = new(StringComparer.Ordinal);
        private double _forcedVoltage;
        private double _compliance = 0.01;
        private bool _pulseArmed;
        private double _pulseVoltage;

        private bool _sweepArmed;
        private int _sweepMode = 1;
        private double _sweepStart;
        private double _sweepStop;
        private int _sweepPoints;

        /// <summary>Clears all state as if the instrument was power-cycled.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _responses.Clear();
                _channelsOn.Clear();
                _forcedVoltage = 0;
                _compliance = 0.01;
                _pulseArmed = false;
                _pulseVoltage = 0;
                ClearArmedSweep();
            }
        }

        /// <summary>Processes one command written to the instrument.</summary>
        public void Execute(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            lock (_gate)
            {
                var trimmed = command.Trim();
                if (trimmed.Contains('?'))
                {
                    HandleQuery(trimmed);
                    return;
                }

                var parts = trimmed.Split(new[] { ' ', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;

                switch (parts[0].ToUpperInvariant())
                {
                    case "*RST":
                        Reset();
                        break;

                    case "CN":
                        for (int i = 1; i < parts.Length; i++) _channelsOn.Add(parts[i]);
                        break;

                    case "CL":
                        for (int i = 1; i < parts.Length; i++) _channelsOn.Remove(parts[i]);
                        _forcedVoltage = 0;
                        _pulseArmed = false;
                        ClearArmedSweep();
                        break;

                    case "DZ":
                        // Zero the output; keep channel links armed like the instrument.
                        _forcedVoltage = 0;
                        _pulseArmed = false;
                        ClearArmedSweep();
                        break;

                    case "DV":
                        // DV <ch>,<range>,<voltage>,<compliance>
                        if (parts.Length >= 4)
                        {
                            _forcedVoltage = ParseDoubleOrDefault(parts[3], 0);
                            _compliance = parts.Length >= 5 ? ParseDoubleOrDefault(parts[4], _compliance) : _compliance;
                        }
                        break;

                    case "PV":
                        // PV <ch>,<range>,<baseVoltage>,<pulseVoltage>,<compliance>
                        if (parts.Length >= 5)
                        {
                            _pulseVoltage = ParseDoubleOrDefault(parts[4], 0);
                            _compliance = parts.Length >= 6 ? ParseDoubleOrDefault(parts[5], _compliance) : _compliance;
                            _pulseArmed = true;
                        }
                        break;

                    case "WV":
                        if (parts.Length >= 7)
                        {
                            _sweepMode = (int)ParseDoubleOrDefault(parts[2], 1);
                            _sweepStart = ParseDoubleOrDefault(parts[4], 0);
                            _sweepStop = ParseDoubleOrDefault(parts[5], 0);
                            _sweepPoints = (int)ParseDoubleOrDefault(parts[6], 0);
                            _compliance = parts.Length >= 8 ? ParseDoubleOrDefault(parts[7], _compliance) : _compliance;
                            _sweepArmed = true;
                        }
                        break;

                    case "PWV":
                        if (parts.Length >= 8)
                        {
                            _sweepMode = (int)ParseDoubleOrDefault(parts[2], 1);
                            _sweepStart = ParseDoubleOrDefault(parts[5], 0);
                            _sweepStop = ParseDoubleOrDefault(parts[6], 0);
                            _sweepPoints = (int)ParseDoubleOrDefault(parts[7], 0);
                            _compliance = parts.Length >= 9 ? ParseDoubleOrDefault(parts[8], _compliance) : _compliance;
                            _sweepArmed = true;
                        }
                        break;

                    case "XE":
                        TriggerMeasurement();
                        break;

                    case "TSQ":
                        _responses.Enqueue(string.Empty);
                        break;

                    // FMT/TSC/AV/MM/CMM/RI/RV/PT/TSR and unknown commands
                    // (e.g. buffered program syntax) are accepted and ignored.
                }
            }
        }

        /// <summary>Reads the next pending response block, or an empty string.</summary>
        public string Read()
        {
            lock (_gate)
            {
                return _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            }
        }

        private void TriggerMeasurement()
        {
            if (_sweepArmed)
            {
                _responses.Enqueue(BuildSweepResponse());
                _sweepArmed = false;
            }
            else if (_pulseArmed)
            {
                _responses.Enqueue(FormatToken(_pulseVoltage));
                _pulseArmed = false;
            }
            else
            {
                _responses.Enqueue(FormatToken(_forcedVoltage));
            }
        }

        private string BuildSweepResponse()
        {
            int n = Math.Max(_sweepPoints, 1);
            int total = _sweepMode == 3 ? n * 2 : n;
            double span = _sweepStop - _sweepStart;

            var tokens = new List<string>(total);
            for (int i = 0; i < total; i++)
            {
                double v;
                if (i < n)
                {
                    v = n > 1 ? _sweepStart + i * span / (n - 1) : _sweepStart;
                }
                else
                {
                    v = n > 1 ? _sweepStop - (i - n) * span / (n - 1) : _sweepStop;
                }

                double current = CurrentAt(v);
                // Compliance behaves like on the real instrument: the staircase
                // stops early, returning only the points measured so far.
                if (Math.Abs(current) > _compliance && tokens.Count > 0)
                {
                    break;
                }
                tokens.Add(FormatToken(v));
            }

            return string.Join(",", tokens);
        }

        private static double CurrentAt(double voltage)
        {
            return ConductanceS * Math.Tanh(voltage / SoftSaturationV);
        }

        private static string FormatToken(double voltage)
        {
            // Same shape as real E5263 data blocks: "<prefix>N I<value>" where the
            // app's parsers look for 'I' at index 2 of each token.
            double current = CurrentAt(voltage);
            return $"N2I{current.ToString("E10", CultureInfo.InvariantCulture)}";
        }

        private void HandleQuery(string query)
        {
            var upper = query.ToUpperInvariant();
            if (upper.StartsWith("ERR?"))
            {
                _responses.Enqueue("0");
            }
            else if (upper.StartsWith("*IDN?"))
            {
                _responses.Enqueue("Agilent Technologies,E5263A SIMULATOR,0,A.01");
            }
            else
            {
                // EMG? and other queries succeed silently.
                _responses.Enqueue("OK");
            }
        }

        private void ClearArmedSweep()
        {
            _sweepArmed = false;
            _sweepPoints = 0;
        }

        private static double ParseDoubleOrDefault(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }
    }
}
