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
                await Task.Run(() => 
                {
                    var rm = new ResourceManager();
                    _session = rm.Open(_resourceString) as MessageBasedSession;
                    if (_session != null)
                    {
                        // Default NI VISA termination is \n (10).
                        _isConnected = true;
                    }
                });

                if (_isConnected)
                {
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
                throw new InvalidOperationException($"Failed to connect to prober. Check resource string and connection. Details: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Disconnects from the prober, closing the persistent GPIB session.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                await Task.Run(() => 
                {
                    _session?.Dispose();
                    _session = null;
                    _isConnected = false;
                });
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
        public async Task ConnectChuckAsync()
        {
            await SendProberAsync("MoveChuckContact");
            await Task.Delay(AlignContactDelayMs);
        }

        /// <inheritdoc />
        public async Task DisconnectChuckAsync()
        {
            await SendProberAsync("MoveChuckSeparation");
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

        /// <inheritdoc />
        public async Task GoToWaferContactAsync(string cell, int row, int col, int contactId)
        {
            if (cell.Length != 4)
                throw new ArgumentException("Cell must be a 4-digit string 'YYXX'.");
            
            if (!int.TryParse(cell.Substring(0, 2), out int cellY) || !int.TryParse(cell.Substring(2, 2), out int cellX))
                throw new ArgumentException("Cell must be a 4-digit string 'YYXX' containing numbers.");

            double grossx = (cellX - 4) * 5000;
            double grossy = (cellY - 1) * 5000;

            double subOffsetX = (col - 1) * 1000;
            double subOffsetY = (row - 1) * 1000;

            double contactOffsetX = 0;
            double contactOffsetY = 0;

            switch (contactId)
            {
                case 1: contactOffsetX = 0; contactOffsetY = 0; break;
                case 2: contactOffsetX = 290; contactOffsetY = 0; break;
                case 3: contactOffsetX = 580; contactOffsetY = 0; break;
                case 4: contactOffsetX = 0; contactOffsetY = 350; break;
                case 5: contactOffsetX = 290; contactOffsetY = 350; break;
                case 6: contactOffsetX = 580; contactOffsetY = 350; break;
                default: throw new ArgumentException("Contact ID must be between 1 and 6.");
            }

            double deltaX = grossx + subOffsetX + contactOffsetX;
            double deltaY = grossy + subOffsetY + contactOffsetY;

            await MoveProberAbsoluteAsync(-1 * deltaX, deltaY);
        }

        /// <inheritdoc />
        public async Task ScanWaferAsync(System.Collections.Generic.HashSet<string> targetCells, System.Collections.Generic.HashSet<(int row, int col)> targetSubCells, System.Collections.Generic.IEnumerable<int> targetContacts, int delayMs, Func<string, int, int, int, Task> onContactReached, CancellationToken ct = default)
        {

            for (int y = 1; y <= 16; y++)
            {
                for (int x = 1; x <= 16; x++)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    string cell = $"{y:D2}{x:D2}";
                    if (targetCells == null || !targetCells.Contains(cell))
                        continue;

                    for (int row = 1; row <= 5; row++)
                    {
                        for (int col = 1; col <= 5; col++)
                        {
                            if (targetSubCells == null || !targetSubCells.Contains((row, col)))
                                continue;

                            foreach (var contact in targetContacts)
                            {
                                ct.ThrowIfCancellationRequested();
                                await DisconnectChuckAsync();
                                if (delayMs > 0) await Task.Delay(delayMs, ct);
                                
                                await GoToWaferContactAsync(cell, row, col, contact);
                                if (delayMs > 0) await Task.Delay(delayMs, ct);
                                
                                await ConnectChuckAsync();
                                if (delayMs > 0) await Task.Delay(delayMs, ct);
                                
                                await onContactReached(cell, row, col, contact);
                            }
                        }
                    }
                }
            }
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
                
                var prevTermChar = _session.TerminationCharacter;
                var prevTermEnabled = _session.TerminationCharacterEnabled;

                // SUSS ProberBench commands must be terminated by a Carriage Return (\r, ASCII 13)
                _session.TerminationCharacter = 13;
                _session.TerminationCharacterEnabled = true;

                await Task.Run(() => _session.RawIO.Write(command + "\r"));

                if (postWriteDelayMs > 0)
                {
                    await Task.Delay(postWriteDelayMs);
                }

                response = await Task.Run(() => _session.RawIO.ReadString(readBufferChars)) ?? "No response received.";
                
                // Reset to default
                _session.TerminationCharacter = prevTermChar;
                _session.TerminationCharacterEnabled = prevTermEnabled;
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
