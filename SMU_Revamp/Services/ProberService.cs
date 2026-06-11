using System;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Singleton prober/stager service that mirrors the legacy VB module behavior.
    /// Uses the same resource string, commands, timeouts, and delays from the original code.
    /// Features persistent GPIB session management, error resilience, and comprehensive logging.
    /// </summary>
    public sealed class ProberService : IProberService
    {
        private string _resourceString = "GPIB0::22::INSTR";
        private bool _isConnected;
        private MessageBasedSession? _session; // NI VISA session for the prober

        // Constants based on the original source code
        private const int AlignContactDelayMs = 100;
        private const int MoveXYTimeoutMs = 20000;
        private const int MoveZTimeoutMs = 15000;
        private const int AbsoluteMoveTimeoutMs = 15000;
        private const int GenericCommandTimeoutMs = 25000;
        private const int ReadBufferChars = 90;
        private const int AbsoluteReadDelayMs = 10;
        private const int ExceptionPauseMs = 30000;

        private static readonly Lazy<ProberService> _instance = new(() => new ProberService());

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static ProberService Instance => _instance.Value;

        private bool _quietMode;
        /// <inheritdoc />
        public bool QuietMode
        {
            get => _quietMode;
            set
            {
                if (_quietMode != value)
                {
                    _quietMode = value;
                    if (_isConnected && _session != null)
                    {
                        _ = SendProberAsync(_quietMode ? "EnableMotorQuiet 1" : "EnableMotorQuiet 0");
                    }
                }
            }
        }

        /// <inheritdoc />
        public string ResourceString
        {
            get => _resourceString;
            set => _resourceString = value ?? "GPIB0::22::INSTR";
        }

        private ProberService()
        {
        }

        /// <summary>
        /// Connects to the prober, opening a persistent GPIB session.
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                if (_isConnected && _session != null)
                    return;
                var rm = new ResourceManager();
                _session = rm.Open(_resourceString) as MessageBasedSession;
                if (_session != null)
                {
                    // SUSS ProberBench responses are terminated by a Carriage Return (\r, ASCII 13)
                    _session.TerminationCharacter = 13;
                    _session.TerminationCharacterEnabled = true;
                    _isConnected = true;

                    // Apply quiet mode setting to the motor if enabled (non-fatal if unsupported)
                    if (QuietMode)
                    {
                        try
                        {
                            await SendProberAsync("EnableMotorQuiet 1");
                        }
                        catch (Exception quietEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ProberService] Failed to enable motor quiet mode: {quietEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Error during connect: {ex.Message}");
                throw new InvalidOperationException("Failed to connect to prober. Check resource string and connection.", ex);
            }
        }

        /// <summary>
        /// Disconnects from the prober, closing the persistent GPIB session.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _session?.Dispose();
                _session = null;
                _isConnected = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Error during disconnect: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task ProberAlignAsync()
        {
            await SendProberAsync("MoveChuckSeparation");
            await Task.Delay(AlignContactDelayMs);
        }

        /// <inheritdoc />
        public async Task ProberContactAsync()
        {
            await SendProberAsync("MoveChuckContact");
            await Task.Delay(AlignContactDelayMs);
        }

        /// <inheritdoc />
        public Task<string> MoveProberAsync(double x, double y)
        {
            return SendProberAsync(System.FormattableString.Invariant($"MoveChuck {x} {y} R"), MoveXYTimeoutMs);
        }

        /// <inheritdoc />
        public Task<string> MoveProberZAsync(double z)
        {
            return SendProberAsync(System.FormattableString.Invariant($"MoveChuckZ {z} R"), MoveZTimeoutMs);
        }

        /// <inheritdoc />
        public Task<string> MoveProberAbsoluteAsync(double x, double y)
        {
            return SendProberAsync(System.FormattableString.Invariant($"MoveChuck {x} {y} H"), AbsoluteMoveTimeoutMs, AbsoluteReadDelayMs);
        }

        /// <inheritdoc />
        public Task<string> MoveProberAbsAsync(double x, double y)
        {
            return SendProberAsync(System.FormattableString.Invariant($"MoveChuck {x} {y} Z"), AbsoluteMoveTimeoutMs, AbsoluteReadDelayMs);
        }


        /// <inheritdoc />
        public Task ProberSetHomeAsync()
        {
            return SendProberAsync("SetChuckHome");
        }

        /// <inheritdoc />
        public Task ProberGoHomeAsync()
        {
            return SendProberAsync("MoveChuck 0 0 H");
        }

        /// <inheritdoc />
        /// This method implements the contact sequence logic based on the original code's behavior.
        /// The origin of the specific values is unclear but relates to the legacy prober's contact sequencing.
        public int NextContact(string cellPosition, int contactNumber, bool combSputtering, bool hugeDeltaA, bool hugeDeltaB)
        {
            if (combSputtering)
            {
                var secondChar = cellPosition.Length > 1 ? char.ToLowerInvariant(cellPosition[1]) : '\0';
                if (secondChar == 'i' || secondChar == 'j') // TODO: Verify if this is the correct logic for determining the position type
                {
                    if (contactNumber < 7)
                    {
                        _ = MoveProberAsync(-1 * 300, 0);
                        contactNumber += 1;
                    }
                    else if (contactNumber == 7)
                    {
                        contactNumber = 8;
                    }
                }
                else
                {
                    if (contactNumber == 1)
                    {
                        _ = MoveProberAsync(-1 * 270, 0);
                        contactNumber = 2;
                    }
                    else if (contactNumber == 2)
                    {
                        _ = MoveProberAsync(-1 * 275, 0);
                        contactNumber = 3;
                    }
                    else if (contactNumber == 3)
                    {
                        _ = MoveProberAsync(-1 * 140, 130);
                        contactNumber = 4;
                    }
                    else if (contactNumber == 4)
                    {
                        _ = MoveProberAsync(-1 * 180, -130);
                        contactNumber = 5;
                    }
                    else if (contactNumber == 5)
                    {
                        _ = MoveProberAsync(-1 * 210, 130);
                        contactNumber = 6;
                    }
                    else if (contactNumber == 6)
                    {
                        _ = MoveProberAsync(-1 * 110, -130);
                        contactNumber = 7;
                    }
                    else if (contactNumber == 7)
                    {
                        contactNumber = 8;
                    }
                }
            }

            if (hugeDeltaA || hugeDeltaB)
            {
                if (contactNumber == 1)
                {
                    _ = MoveProberAsync(-1 * 290, 0);
                    contactNumber = 2;
                }
                else if (contactNumber == 2)
                {
                    _ = MoveProberAsync(-1 * 290, 0);
                    contactNumber = 3;
                }
                else if (contactNumber == 3)
                {
                    _ = MoveProberAsync(-1 * 290, 0);
                    contactNumber = 4;
                }
                else if (contactNumber == 4)
                {
                    _ = MoveProberAsync(-1 * 290, 0);
                    contactNumber = 5;
                }
                else if (contactNumber == 5)
                {
                    _ = MoveProberAsync(-1 * 290, 0);
                    contactNumber = 6;
                }
                else if (contactNumber == 6)
                {
                    contactNumber = 7;
                }
            }

            return contactNumber;
        }

        /// <inheritdoc />
        public string[] AbsToRel(int absZeile)
        {
            int subZeile;
            int zeile;

            if (absZeile % 5 == 0)
            {
                zeile = absZeile / 5;
                subZeile = 5;
            }
            else
            {
                zeile = (absZeile - (absZeile % 5)) / 5 + 1;
                subZeile = absZeile % 5;
            }

            string paddedZeile = zeile < 10 ? "0" + zeile : zeile.ToString();
            return [paddedZeile, subZeile.ToString()];
        }

        /// <inheritdoc />
        public int RelToAbs(int pos, int subpos)
        {
            return (pos - 1) * 5 + subpos;
        }

        private async Task<string> SendProberAsync(string command, int timeoutMs = GenericCommandTimeoutMs, int postWriteDelayMs = 100, int readBufferChars = ReadBufferChars)
        {
            string response;

            try
            {
                if (!_isConnected)
                {
                    throw new InvalidOperationException("Not connected to prober. Call ConnectAsync first.");
                }
                if (_session == null)
                {
                    throw new InvalidOperationException("GPIB session is not initialized. Call ConnectAsync first.");
                }
                _session.TimeoutMilliseconds = timeoutMs;
                
                // SUSS ProberBench commands must be terminated by a Carriage Return (\r, ASCII 13)
                _session.RawIO.Write(command + "\r");

                if (postWriteDelayMs > 0)
                {
                    await Task.Delay(postWriteDelayMs);
                }

                response = _session.RawIO.ReadString(readBufferChars) ?? "No response received.";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProberService] Exception in SendProberAsync for command '{command}': {ex.Message}");
                // Allow a brief cooling down period, fully async without blocking the thread
                await Task.Delay(1000);
                throw;
            }

            return response;
        }
    }
}
