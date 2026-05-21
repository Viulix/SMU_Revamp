using System;
using System.Threading;
using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Singleton prober/stager service that mirrors the legacy VB module behavior.
    /// Uses the same resource string, commands, timeouts, and delays from the original code.
    /// </summary>
    public sealed class ProberService : IProberService
    {
        private const string ProberResourceString = "GPIB0::22::INSTR";

        private const int AlignContactDelayMs = 100;
        private const int MoveXYTimeoutMs = 20000;
        private const int MoveZTimeoutMs = 15000;
        private const int AbsoluteMoveTimeoutMs = 15000;
        private const int GenericCommandTimeoutMs = 25000;
        private const int ReadBufferChars = 90;
        private const int QuietEnableDelayMs = 200;
        private const int QuietPostDelayMs = 300;
        private const int MoveReadDelayMs = 100;
        private const int AbsoluteReadDelayMs = 10;
        private const int PositionReadDelayMs = 30;
        private const int ExceptionPauseMs = 30000;

        private static readonly Lazy<ProberService> _instance = new(() => new ProberService());

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static ProberService Instance => _instance.Value;

        /// <inheritdoc />
        public bool QuietMode { get; set; }

        private ProberService()
        {
        }

        /// <inheritdoc />
        public Task ProberAlignAsync()
        {
            return SendProberAsync("MoveChuckSeparation").ContinueWith(_ => Thread.Sleep(AlignContactDelayMs));
        }

        /// <inheritdoc />
        public Task ProberContactAsync()
        {
            return SendProberAsync("MoveChuckContact").ContinueWith(_ => Thread.Sleep(AlignContactDelayMs));
        }

        /// <inheritdoc />
        public Task<string> MoveProberAsync(double x, double y)
        {
            return ExecuteMoveAsync($"MoveChuck {x} {y} R", MoveXYTimeoutMs, MoveReadDelayMs, includePositionRead: false);
        }

        /// <inheritdoc />
        public Task<string> MoveProberZAsync(double z)
        {
            return ExecuteMoveAsync($"MoveChuckZ {z} R", MoveZTimeoutMs, MoveReadDelayMs, includePositionRead: false);
        }

        /// <inheritdoc />
        public Task<string> MoveProberAbsoluteAsync(double x, double y)
        {
            return ExecuteMoveAsync($"MoveChuck {x} {y} H", AbsoluteMoveTimeoutMs, AbsoluteReadDelayMs, includePositionRead: true);
        }

        /// <inheritdoc />
        public Task<string> MoveProberAbsAsync(double x, double y)
        {
            return ExecuteMoveAsync($"MoveChuck {x} {y} Z", AbsoluteMoveTimeoutMs, AbsoluteReadDelayMs, includePositionRead: false);
        }

        /// <inheritdoc />
        public Task<string> SendProberAsync(string command)
        {
            return ExecuteCommandAsync(command, GenericCommandTimeoutMs);
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
        public int NextContact(string cellPosition, int contactNumber, bool combSputtering, bool hugeDeltaA, bool hugeDeltaB)
        {
            if (combSputtering)
            {
                var secondChar = cellPosition.Length > 1 ? char.ToLowerInvariant(cellPosition[1]) : '\0';
                if (secondChar == 'i' || secondChar == 'j')
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
            return new[] { paddedZeile, subZeile.ToString() };
        }

        /// <inheritdoc />
        public int RelToAbs(int pos, int subpos)
        {
            return (pos - 1) * 5 + subpos;
        }

        private async Task<string> ExecuteCommandAsync(string command, int timeoutMs)
        {
            string response = string.Empty;

            try
            {
                using var session = new VisaGpibSession();
                await session.OpenAsync(ProberResourceString, timeoutMs).ConfigureAwait(false);
                await session.WriteAsync(command + "\r").ConfigureAwait(false);
                Thread.Sleep(MoveReadDelayMs);
                response = await session.ReadAsync(ReadBufferChars).ConfigureAwait(false);

                if (QuietMode)
                {
                    await session.WriteAsync("EnableMotorQuiet 1\r").ConfigureAwait(false);
                    Thread.Sleep(QuietEnableDelayMs);
                    response = await session.ReadAsync(ReadBufferChars).ConfigureAwait(false);
                    Thread.Sleep(QuietPostDelayMs);
                }
            }
            catch
            {
                Thread.Sleep(ExceptionPauseMs);
                throw;
            }

            return response;
        }

        private async Task<string> ExecuteMoveAsync(string command, int timeoutMs, int readDelayMs, bool includePositionRead)
        {
            string response = string.Empty;

            try
            {
                using var session = new VisaGpibSession();
                await session.OpenAsync(ProberResourceString, timeoutMs).ConfigureAwait(false);
                await session.WriteAsync(command + "\r").ConfigureAwait(false);
                Thread.Sleep(readDelayMs);
                response = await session.ReadAsync(ReadBufferChars).ConfigureAwait(false);

                if (QuietMode)
                {
                    await session.WriteAsync("EnableMotorQuiet 1\r").ConfigureAwait(false);
                    Thread.Sleep(QuietEnableDelayMs);
                    response = await session.ReadAsync(ReadBufferChars).ConfigureAwait(false);
                    Thread.Sleep(QuietPostDelayMs);
                }

                if (includePositionRead)
                {
                    await session.WriteAsync("ReadChuckPosition Z\r").ConfigureAwait(false);
                    Thread.Sleep(PositionReadDelayMs);
                    var xyz = await session.ReadAsync(ReadBufferChars).ConfigureAwait(false);
                    response = xyz;
                }
            }
            catch
            {
                Thread.Sleep(ExceptionPauseMs);
                throw;
            }

            return response;
        }
    }
}
